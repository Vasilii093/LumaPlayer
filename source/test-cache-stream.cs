using System;using System.IO;using System.Diagnostics;using System.Threading;using Luma;
class StreamTest {
 static int Main(string[] args){
  string root=Path.GetFullPath(args[0]), log=Path.GetFullPath("work/continuous-audio-test.log");
  using(var s=new PreRenderSession(root,args[1],1922,1934,60,720,1,false,"auto",2,PreRenderSession.DefaultLimit,23,true,true))
  using(var stream=new CacheStream()){
   string error=null;int count=0;var ready=new ManualResetEvent(false);
   s.Ready=c=>{if(count>=2)stream.Add(c);Console.WriteLine("CHUNK "+c.Start+" "+c.Length);if(Interlocked.Increment(ref count)>=4)ready.Set();};
   s.Failed=e=>{error=e;ready.Set();stream.Finish();};stream.Failed=e=>error=e;
   s.Start();if(!ready.WaitOne(60000)||error!=null)throw new Exception(error??"No chunks");
   var psi=new ProcessStartInfo(Path.Combine(root,"engine/mpv.exe"));psi.UseShellExecute=false;psi.CreateNoWindow=true;
   psi.Arguments=PreRenderSession.Quote("--script="+Path.GetFullPath("work/first-position.lua"))+" --force-seekable=yes --start=1 --no-config --idle=no --keep-open=no --load-scripts=no --vo=null --ao=null --untimed --no-terminal --demuxer-lavf-format=mpegts --msg-level=all=v --af=lavfi=[volume=6dB,alimiter=limit=0.95:level=false:latency=true] "+PreRenderSession.Quote("--log-file="+log)+" "+PreRenderSession.Quote(stream.Url);
   using(var player=Process.Start(psi)){
    if(!s.Work.Wait(60000))throw new Exception("Producer timeout");stream.Finish();
    if(!player.WaitForExit(30000)){player.Kill();throw new Exception("Player timeout");}
    if(error!=null||player.ExitCode!=0)throw new Exception(error??"Playback failed");
   }
   string text=File.ReadAllText(log);
   int starts=text.Split(new[]{"Starting playback..."},StringSplitOptions.None).Length-1;
   if(starts!=1||count<5||text.Contains("Error decoding")||text.Contains("Packet corrupt"))throw new Exception("Not a single continuous playback");
   Console.WriteLine("PASS: "+count+" chunks, one playback and audio-filter lifetime, encoder complete");
  } return 0;
 }
}


