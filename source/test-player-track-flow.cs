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

class TrackFlowTest {
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
   await Until(()=> (bool)Get("ready"),20,"engine ready");
   using(var menu=Menu(false))Check(!menu.Items[0].Enabled && menu.Items[0].Text.Contains("откройте"),"Empty-state menu");
   Call("LoadFile",arcane);
   await Until(()=> (bool)Get("sourceLoaded") && ((List<Dictionary<string,object>>)Get("subtitleTracks")).Count==7,20,"Arcane tracks");
   await Audio("1","source default"); MenuCheck(false,3,"1"); MenuCheck(true,7,"1");
   Call("SelectAudioTrack","3");Call("SelectSubtitleTrack","2");
   Call("Send",new object[]{new object[]{"set_property","pause",true}});
   Call("SeekTo",1905.0);await Task.Delay(1500);
   await Audio("3","source English"); MenuCheck(false,3,"3");MenuCheck(true,7,"2");
   string sourceText=Convert.ToString(await Property("sub-text"));Check(sourceText.Length>0,"Source subtitle missing at reference time");
   Console.WriteLine("REFERENCE subtitle: "+sourceText.Replace("\n"," / "));
   ((ComboBox)Get("buffer")).SelectedIndex=0;
   Call("BeginCache",1900.0);
   await Until(()=> (bool)Get("cacheFileLoaded") && ((Dictionary<string,string>)Get("cachedSubtitleIds")).Count==7,100,"cache and subtitle mapping");
   Call("Send",new object[]{new object[]{"set_property","pause",true}});
   await Task.Delay(500);await Audio("1","cache encoded track");
   var first=Get("cache");Check((string)typeof(PreRenderSession).GetField("audio",flags).GetValue(first)=="3","Wrong encoded source audio");
   Check(((Button)Get("audioButton")).Enabled && ((Button)Get("subButton")).Enabled,"Cache buttons disabled");
   MenuCheck(false,3,"3");MenuCheck(true,7,"2");
   Call("SeekTo",1905.0);
   await Until(()=> (bool)Get("cacheFileLoaded") && ((Dictionary<string,string>)Get("cachedSubtitleIds")).Count==7,25,"cached seek subtitles");
   Call("Send",new object[]{new object[]{"set_property","pause",true}});await Task.Delay(1200);
   Check(Object.ReferenceEquals(first,Get("cache")),"Subtitle/seek restarted producer");
   Check(Convert.ToString(await Property("sub-text"))==sourceText,"Subtitle timing changed in cache: "+await Property("sub-text"));
   Check(Convert.ToDouble(await Property("sub-delay"))==-(double)Get("cacheOrigin"),"Wrong subtitle offset");
   Console.WriteLine("CACHE subtitle text matches source at 31:45; external offset correct");
   Call("SelectSubtitleTrack","3");await Task.Delay(700);
   Check(Convert.ToString(await Property("sid"))==((Dictionary<string,string>)Get("cachedSubtitleIds"))["3"],"Cache subtitle selection failed");MenuCheck(true,7,"3");
   Call("SelectSubtitleTrack","no");await Task.Delay(500);
   Check(PreRenderSession.NormalizeAudioTrack(await Property("sid"))=="no","Subtitles not off");MenuCheck(true,7,"no");
   Call("SelectSubtitleTrack","2");
   double before=(double)Get("position");Call("SelectAudioTrack","2");
   Check(!Object.ReferenceEquals(first,Get("cache")),"Cache audio switch did not rebuild");
   Check(Math.Abs((double)Get("cacheOrigin")-before)<.1,"Audio switch lost position");
   await Until(()=> (bool)Get("cacheFileLoaded") && ((Dictionary<string,string>)Get("cachedSubtitleIds")).Count==7,100,"cache after audio switch");
   await Task.Delay(1500);await Audio("1","cache switched audio");
   Check((string)typeof(PreRenderSession).GetField("audio",flags).GetValue(Get("cache"))=="2","Producer kept old audio");MenuCheck(false,3,"2");MenuCheck(true,7,"2");
   Call("ReturnToLive");await Until(()=> (bool)Get("sourceLoaded"),20,"source return");await Task.Delay(1200);
   await Audio("2","restored source");Check(Convert.ToString(await Property("sid"))=="2","Return lost subtitles");Check(Convert.ToDouble(await Property("sub-delay"))==0,"Return kept cache offset");
   Call("LoadFile",arcane);await Until(()=> (bool)Get("sourceLoaded") && ((List<Dictionary<string,object>>)Get("subtitleTracks")).Count==7,20,"new source reset");
   await Task.Delay(1000);await Audio("1","new file default");Check(Convert.ToString(await Property("sid"))=="1","New file kept previous subtitle");
   MenuCheck(false,3,"1");MenuCheck(true,7,"1");
   Console.WriteLine("PASS: visible 3 audio / 7 subtitle choices; source/cache selection, matching timed text, cached seek, off, audio restart, return, new file reset; real Vulkan/WASAPI");
  }catch(Exception e){result=1;Console.WriteLine("FAIL "+e);}
  finally{player.Close();}
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
