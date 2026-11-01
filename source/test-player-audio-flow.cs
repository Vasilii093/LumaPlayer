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

class AudioFlowTest {
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
 static async Task Flow(string arcane,string reacher,bool closeOnly) {
  try {
   await Until(()=> (bool)Get("ready"),20,"engine ready");
   if(closeOnly){Console.WriteLine("Closing idle actual player");return;}
   Call("LoadFile",arcane);
   await Until(()=> (bool)Get("sourceLoaded") && (double)Get("duration")>0,20,"Arcane loaded");
   await Task.Delay(1000);await Audio("1","Arcane default");
   Call("SelectAudioTrack","2");await Task.Delay(1200);await Audio("2","Arcane selected second");
   Call("BeginCache",1900.0);
   await Until(()=> (bool)Get("cacheFileLoaded") || ((string)Get("playbackNotice")).Length>0,100,"Arcane cache");
   Check((bool)Get("cacheFileLoaded"),"Arcane cache failed: "+Get("playbackNotice"));
   await Task.Delay(2000);await Audio("1","Cache remapped track");
   var first=Get("cache");var chunks=(Dictionary<string,PreparedChunk>)Get("scheduled");
   Check(chunks.Count>=2,"Missing initial chunks");
   Check(((double[][])typeof(ClickSeekBar).GetField("cached",flags).GetValue(Get("seek"))).Length>0,"Ready cache not displayed");
   Call("SeekTo",1910.0);await Until(()=> (bool)Get("cacheFileLoaded"),20,"cached seek");
   Check(Object.ReferenceEquals(first,Get("cache")),"Seek restarted producer");
   await Task.Delay(1000);await Audio("1","Cached seek");
   Call("ReturnToLive");await Until(()=> (bool)Get("sourceLoaded"),20,"source return");
   await Task.Delay(1000);await Audio("2","Restored original second track");
   if(File.Exists(reacher)) {
   Call("LoadFile",reacher);
   await Until(()=> (bool)Get("sourceLoaded") && (double)Get("duration")>0,20,"Reacher loaded");
   Check((string)Get("selectedAudio")=="auto" || (string)Get("selectedAudio")==Convert.ToString(await Property("aid")),"Other folder retained prior source id");
   Call("BeginCache",0.0);
   await Until(()=> ((string)Get("playbackNotice")).Length>0,30,"damaged file detected");
   Check(((string)Get("playbackNotice")).Contains("повреждён"),"Expected corrupt source diagnosis: "+Get("playbackNotice"));
   Check(Get("cache")==null,"Failed producer still attached");
   Console.WriteLine("DAMAGED FILE: rejected, returned to source; "+Get("playbackNotice"));
   } else {
    Call("LoadFile",Path.GetFullPath("outputs/cache-test-sample.mp4"));
    await Until(()=> (bool)Get("sourceLoaded") && (double)Get("duration")>0,20,"other-folder sample loaded");
    Check((string)Get("selectedAudio")=="auto" || (string)Get("selectedAudio")==Convert.ToString(await Property("aid")),"Other folder retained prior source id");
    await Task.Delay(500);await Audio("1","Other folder default track");
    Call("BeginCache",0.0);
    await Until(()=> (bool)Get("cacheFileLoaded") || ((string)Get("playbackNotice")).Length>0,90,"other-folder cache");
    Check((bool)Get("cacheFileLoaded"),"Other-folder cache failed");
    await Task.Delay(500);await Audio("1","Other-folder cache track");
    Console.WriteLine("SOURCE SWITCH: short file from another folder prepared successfully; damaged file no longer present");
   }
   Call("LoadFile",arcane);
   await Until(()=> (bool)Get("sourceLoaded") && (double)Get("duration")>0,20,"Arcane reopened");
   await Task.Delay(1000);await Audio("1","Arcane after other file");
   Call("BeginCache",1900.0);
   await Until(()=> (bool)Get("cacheFileLoaded") || ((string)Get("playbackNotice")).Length>0,100,"fresh cache after damaged source");
   Check((bool)Get("cacheFileLoaded"),"Failed to restart cache after bad source");
   await Task.Delay(16000);await Audio("1","Recovered cache after chunk boundary");
   Check(Get("cache")!=null,"Recovered cache lost");
   Console.WriteLine("PASS: actual Player + GPU 900p + WASAPI source/cache/folder switches, retained seek, timeline");
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
