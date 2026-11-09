using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Reflection;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Luma;

class NewRatesFlowTest {
 static BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
 static Player player; static int result; static string pipe;
 static object Get(string name){return typeof(Player).GetField(name,flags).GetValue(player);}
 static void Set(string name,object value){typeof(Player).GetField(name,flags).SetValue(player,value);}
 static object Call(string name,params object[] args){return typeof(Player).GetMethod(name,flags).Invoke(player,args);}
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static async Task Until(Func<bool> condition,int seconds,string description) {
  var timer=Stopwatch.StartNew();
  while(!condition()){if(timer.Elapsed.TotalSeconds>seconds)throw new Exception("Timeout: "+description+"; "+((Label)Get("info")).Text);await Task.Delay(100);}
 }
 static async Task<object> Property(string name) {
  using(var ipc=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)) {
   ipc.Connect(5000);byte[] request=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new {command=new object[]{"get_property",name},request_id=1})+"\n");
   await ipc.WriteAsync(request,0,request.Length);await ipc.FlushAsync();
   using(var reader=new StreamReader(ipc)) {
    while(true){var pending=reader.ReadLineAsync();if(await Task.WhenAny(pending,Task.Delay(5000))!=pending)throw new Exception("IPC timeout "+name);
     var line=await pending;if(line==null)throw new Exception("IPC EOF");var data=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
     if(data.ContainsKey("request_id")){Check((string)data["error"]=="success","Unavailable "+name);return data["data"];}
    }
   }
  }
 }
 static async Task Audio(string expected,string stage) {
  string aid=Convert.ToString(await Property("aid"));
  Check(aid==expected,stage+" audio id "+aid+" != "+expected);
  Check(Convert.ToString(await Property("current-ao"))=="wasapi",stage+" is not real WASAPI output");
  Check(!Convert.ToBoolean(await Property("mute")),stage+" muted");
  Check(await Property("audio-out-params")!=null,stage+" has no decoded audio output");
  Console.WriteLine("AUDIO "+stage+": aid="+aid+", WASAPI, unmuted");
 }
 static System.Windows.Forms.ContextMenuStrip Menu(bool subtitles) {return (ContextMenuStrip)Call("CreateTrackMenu",subtitles);}
 static void MenuCheck(bool subtitles,int tracks,string selected) {
  using(var menu=Menu(subtitles)) {
   int count=0,checks=0;
   foreach(ToolStripItem item in menu.Items) {
    var option=item as ToolStripMenuItem;if(option==null || !option.Enabled)continue;
    if(option.Text!="Выключены")count++;
    if(option.Checked){checks++;Check(selected=="no" ? option.Text=="Выключены" : option.Text.StartsWith(selected+"."),"Wrong menu selection: "+option.Text);}
   }
   Check(count==tracks && checks==1,"Menu track/check count "+count+"/"+checks);
   Check(menu.ShowCheckMargin,"Check marks invisible");
  }
 }
 static async Task CheckDialog(bool picture) {
  var done=new TaskCompletionSource<bool>();var timer=new System.Windows.Forms.Timer{Interval=200};
  timer.Tick+=async delegate {timer.Stop();Form sheet=null;try {
   foreach(Form f in Application.OpenForms)if(f.Text.StartsWith(picture?"Изображение":"Звук"))sheet=f;
   Check(sheet!=null,"dialog missing");
   var bars=new List<TrackBar>();foreach(Control c in sheet.Controls)if(c is TrackBar)bars.Add((TrackBar)c);bars.Sort((x,y)=>x.Top.CompareTo(y.Top));
   Check(bars.Count==(picture?5:6),"slider count");
   foreach(Control c in sheet.Controls)Check(c.Text!="Применить","obsolete apply button");
   int[] values=picture?new int[]{12,15,20,4,35}:new int[]{3,2,-1,3,1,-2};
   for(int i=0;i<bars.Count;i++)bars[i].Value=values[i];await Task.Delay(500);
   if(picture) {Check(Convert.ToInt32(await Property("brightness"))==12,"live brightness");Check(Math.Abs(Convert.ToDouble(await Property("sharpen"))-.35)<.001,"live sharpen");}
   else {string af=new JavaScriptSerializer().Serialize(await Property("af"));Check(af.Contains("volume=3dB")&&af.Contains("g=-2"),"live sound");}
   var slot=(ComboBox)sheet.Controls["saveSlot"];slot.SelectedIndex=2;
   foreach(Control c in sheet.Controls)if(c is Button&&c.Text=="Сохранить")((Button)c).PerformClick();
   foreach(Control c in sheet.Controls)if(c is Button&&c.Text=="Сбросить")((Button)c).PerformClick();await Task.Delay(200);
   if(picture)Check(Convert.ToInt32(await Property("brightness"))==0,"picture reset");else Check(new JavaScriptSerializer().Serialize(await Property("af"))=="[]","sound reset");
   ((ComboBox)sheet.Controls["preset"]).SelectedIndex=6;await Task.Delay(200);
   for(int i=0;i<bars.Count;i++)Check(bars[i].Value==values[i],"custom preset restore");
   using(var bitmap=new System.Drawing.Bitmap(sheet.Width,sheet.Height)){sheet.DrawToBitmap(bitmap,new System.Drawing.Rectangle(0,0,bitmap.Width,bitmap.Height));bitmap.Save(picture?"picture.png":"sound.png");}
   Console.WriteLine("PASS "+(picture?"picture":"sound")+" slider events, reset and custom slot 3");done.SetResult(true);
  }catch(Exception e){done.SetException(e);}finally{if(sheet!=null)sheet.Close();timer.Dispose();}};
  timer.Start();Call(picture?"ShowPicture":"ShowSound");await done.Task;
 }
 static async Task Flow(string video,string unused,bool closeOnly) {
  try {
   await Until(()=> (bool)Get("ready"),20,"engine");((ComboBox)Get("motion")).SelectedIndex=0;Call("LoadFile",video);await Until(()=> (bool)Get("sourceLoaded"),20,"source");
   var panel=(Control)Get("controls");bool image=false,sound=false;foreach(Control c in panel.Controls){image|=c.Text=="Изображение";sound|=c.Text=="Звук";}Check(image&&sound,"main buttons missing");
   await CheckDialog(true);await CheckDialog(false);
   using(var fresh=new Player(null)) {
    typeof(Player).GetField("root",flags).SetValue(fresh,Get("root"));typeof(Player).GetMethod("LoadAdjustmentSettings",flags).Invoke(fresh,null);
    Check((int)typeof(Player).GetField("pictureBrightness",flags).GetValue(fresh)==12,"persistent picture");
    Check((int)typeof(Player).GetField("soundGain",flags).GetValue(fresh)==3,"persistent sound");
    var config=typeof(Player).GetField("adjustmentSettings",flags).GetValue(fresh);
    var images=(int[][])config.GetType().GetField("ImagePresets").GetValue(config);var sounds=(int[][])config.GetType().GetField("SoundPresets").GetValue(config);
    Check(images.Length==3&&images[2][4]==35&&sounds.Length==3&&sounds[2][5]==-2,"persistent presets");
    ((System.Windows.Forms.Timer)typeof(Player).GetField("chromeTimer",flags).GetValue(fresh)).Dispose();typeof(Player).GetMethod("Shutdown",flags).Invoke(fresh,null);
   }
   Console.WriteLine("PASS settings and custom presets loaded by fresh Player");
  }catch(Exception e){result=1;Console.WriteLine("FAIL "+e);}finally{player.Close();}
 }
 [STAThread] static int Main(string[] args) {
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
  Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
  Application.ThreadException+=delegate(object sender,System.Threading.ThreadExceptionEventArgs e){result=1;Console.WriteLine("UI EXCEPTION "+e.Exception);Application.ExitThread();};
  pipe="luma-audio-test-"+Guid.NewGuid().ToString("N");
  using(player=new Player(null)) {
   Set("root",Path.GetFullPath(args[0]));Set("pipeName",pipe);
   ((ComboBox)Get("motion")).SelectedIndex=3;
   player.Shown+=async delegate {await Flow(args.Length>1?args[1]:null,args.Length>2?args[2]:null,args.Length==1);};
   Application.Run(player);
  }
  Console.WriteLine("EXIT "+result);return result;
 }
}
