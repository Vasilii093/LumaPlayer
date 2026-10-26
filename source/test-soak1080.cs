using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.IO.Pipes;using System.Web.Script.Serialization;using Luma;
class Soak1080 {
 static int Main(string[] args){
  double length=args.Length>2?Double.Parse(args[2]):180;double origin=length>400?900:1900;
  string root=Path.GetFullPath(args[0]),log=Path.GetFullPath("work/optimized-soak1080-mpv.log"),pipe="luma-soak-"+Guid.NewGuid().ToString("N");
  using(var s=new PreRenderSession(root,args[1],origin,origin+length,60,1080,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true))
  using(var stream=new CacheStream()){
   string error=null;int count=0,stalls=0;double pos=0,produced=0,minAhead=99999,measuredSeconds=0,renderSeconds=0,lastReport=-30;bool started=false;
   var chunks=new List<PreparedChunk>();var gate=new object();var ready=new ManualResetEvent(false);
   Action<string> fail=e=>{lock(gate){if(error==null)error=e;Console.WriteLine("ERROR "+e);}};
   s.Measured=(seconds,renderElapsed)=>{lock(gate){measuredSeconds+=seconds;renderSeconds+=renderElapsed;}};
   s.Ready=c=>{lock(gate){chunks.Add(c);produced=c.Start+c.Length-origin;stream.Add(c);Console.WriteLine("READY "+(++count)+" speed="+s.LastSpeed.ToString("0.00")+" ahead="+(produced-pos).ToString("0.0"));if(produced>=30)ready.Set();}};
   s.Failed=e=>{fail("Producer: "+e);ready.Set();stream.Finish();};stream.Failed=e=>fail("Stream: "+e);
   string lastStatus=null;
   s.Status=m=>{if(m!=lastStatus){Console.WriteLine("STATE "+m);lastStatus=m;}if(s.Completed)stream.Finish();};
   s.Start();if(!ready.WaitOne(90000)||error!=null)throw new Exception(error??"No initial buffer");
   var psi=new ProcessStartInfo(Path.Combine(root,"engine/mpv.exe")){UseShellExecute=false,CreateNoWindow=true};
   psi.Arguments="--no-config --idle=no --keep-open=no --load-scripts=no --vo=null --ao=null --hwdec=auto-copy --no-terminal --cache=yes --cache-secs=5 --demuxer-max-bytes=32MiB --demuxer-max-back-bytes=0 --demuxer-lavf-format=mpegts --msg-level=all=v --af=lavfi=[volume=6dB,alimiter=limit=0.95:level=false:latency=true] "+PreRenderSession.Quote("--input-ipc-server="+pipe)+" "+PreRenderSession.Quote("--log-file="+log)+" "+PreRenderSession.Quote(stream.Url);
   var elapsed=Stopwatch.StartNew();
   using(var player=Process.Start(psi)){
    var reader=Task.Run(()=>{try{using(var ipc=new NamedPipeClientStream(".",pipe,PipeDirection.InOut)){ipc.Connect(15000);
     byte[] observe=System.Text.Encoding.UTF8.GetBytes("{\"command\":[\"observe_property\",1,\"time-pos\"]}\n{\"command\":[\"observe_property\",2,\"paused-for-cache\"]}\n");
     ipc.Write(observe,0,observe.Length);ipc.Flush();
     using(var r=new StreamReader(ipc,System.Text.Encoding.UTF8,false,1024,true)){
     var json=new JavaScriptSerializer();string line;while((line=r.ReadLine())!=null){var o=json.Deserialize<Dictionary<string,object>>(line);if(!o.ContainsKey("name")||!o.ContainsKey("data")||o["data"]==null)continue;
      if((string)o["name"]=="time-pos"){lock(gate){pos=Convert.ToDouble(o["data"]);started=pos>1;double ahead=produced-pos;if(started&&!s.Completed)minAhead=Math.Min(minAhead,ahead);s.UpdatePlaybackBuffer(ahead,started);foreach(var c in new List<PreparedChunk>(chunks))if(origin+pos>=c.Start+c.Length+30){chunks.Remove(c);s.Consume(c);}if(pos-lastReport>=30){lastReport=pos;Console.WriteLine("PLAY "+pos.ToString("0.0")+" ahead="+ahead.ToString("0.0")+" bytes="+s.Bytes);}}}
      if((string)o["name"]=="paused-for-cache"&&Convert.ToBoolean(o["data"])&&started)stalls++;
     }
    }}}catch(Exception e){if(!player.HasExited)fail("IPC: "+e);}});
    if(!player.WaitForExit((int)((length+60)*1000))){player.Kill();throw new Exception("Playback timeout");}reader.Wait(5000);
    elapsed.Stop();if(player.ExitCode!=0)fail("Playback exit code "+player.ExitCode);
   }
   string text=File.ReadAllText(log);int starts=text.Split(new[]{"Starting playback..."},StringSplitOptions.None).Length-1;
   var result=new {seconds=length,playback_wall_seconds=elapsed.Elapsed.TotalSeconds,final_position=pos,chunks=count,manifest_read_bytes=s.ManifestBytesRead,cache_directory_scans=s.CacheDirectoryScans,encoder_starts=s.EncoderStarts,playback_starts=starts,cache_stalls=stalls,min_ahead_seconds=minAhead,completed=s.Completed,rendered_seconds=measuredSeconds,render_active_seconds=renderSeconds,render_speed=measuredSeconds/Math.Max(.001,renderSeconds),error=error};
   string output=new JavaScriptSerializer().Serialize(result);Console.WriteLine(output);File.WriteAllText("work/optimized-soak1080-result.json",output);
   if(error!=null||starts!=1||!s.Completed||stalls!=0||pos<length-2||elapsed.Elapsed.TotalSeconds>length+8||text.Contains("Packet corrupt")||text.Contains("Error decoding"))throw new Exception("Soak acceptance failed: "+error);
   Console.WriteLine("PASS: continuous 1080p cache playback with no starvation");
  }return 0;
 }
}
