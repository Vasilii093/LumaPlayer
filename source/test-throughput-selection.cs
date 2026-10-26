using System;using System.IO;using System.Reflection;using Luma;
class ThroughputSelection {
 static void Main(string[] args){
  if(!PreRenderSession.NativeRife||!PreRenderSession.RifeThroughput)throw new Exception("Flags missing");
  foreach(int h in new int[]{720,900,1080}) {
   bool fast=h>=900;
   if(PreRenderSession.RifeScript(23,h)!=(fast?"interpolate-rife-throughput.vpy":"interpolate-rife-resident.vpy"))throw new Exception("Wrong route: "+h);
   using(var s=new PreRenderSession(Path.GetFullPath(args[0]),"unused",0,60,60,h,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true)){
    string graph=(string)typeof(PreRenderSession).GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{0.0,60.0});
    if(!graph.Contains("buffered-frames="+(fast?6:4)+":concurrent-frames="+(fast?6:4))||!graph.Contains("60|"+h+"|1|23|"+(fast?3:2)))throw new Exception("Incorrect pipeline settings");
    if(graph.Contains("scale="))throw new Exception("Unexpected down/upscale");
   }
  }
  bool rejected=false;try{PreRenderSession.RifeScript(70,1080);}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Experimental model allowed");
  Console.WriteLine("PASS: full-resolution 900p/1080p throughput; 720p resident; correct queues and workers; unsupported model rejected");
 }
}
