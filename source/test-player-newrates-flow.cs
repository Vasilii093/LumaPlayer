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
 static async Task Flow(string arcane,string unused,bool closeOnly) {
  try {
   await Until(()=> (bool)Get("ready"),20,"engine");
   Call("LoadFile",arcane);
   await Until(()=> (bool)Get("sourceLoaded") && (double)Get("duration")>0,20,"source");
   ((ComboBox)Get("buffer")).SelectedIndex=0;
   foreach(int mode in new int[]{2,4}) {
    ((ComboBox)Get("motion")).SelectedIndex=mode;
    Call("BeginCache",1900.0);
    await Until(()=> (bool)Get("cacheFileLoaded") || ((string)Get("playbackNotice")).Length>0,160,"cache");
    Check((bool)Get("cacheFileLoaded"),"cache failed: "+Get("playbackNotice"));
    await Task.Delay(2500);
    int expected=mode==2?90:72;
    double fps=Convert.ToDouble(await Property("container-fps"));
    Check(Math.Abs(fps-expected)<.05,"Wrong playback fps "+fps+" expected "+expected);
    await Audio("1","cache "+expected+" fps");
    Check(Convert.ToString(await Property("video-params/w"))==(mode==2?"1280":"1600"),"Wrong cached width");
    Console.WriteLine("PASS actual player "+(mode==2?"720p90":"900p72")+" container-fps="+fps);
    Call("ReturnToLive");
    await Until(()=> (bool)Get("sourceLoaded"),20,"return");
    await Task.Delay(800);
   }
  } catch(Exception e) {result=1;Console.WriteLine("FAIL "+e);}
  finally {player.Close();}
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
