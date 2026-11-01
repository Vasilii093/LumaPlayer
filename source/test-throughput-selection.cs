using System;using System.IO;using System.Reflection;using Luma;
class ThroughputSelection {
 static void Main(string[] args){
  if(!PreRenderSession.NativeRife||!PreRenderSession.RifeThroughput)throw new Exception("Flags missing");
  if(PreRenderSession.RifeScript(23,720)!="interpolate-rife-resident.vpy"||PreRenderSession.RifeScript(23,1080)!="interpolate-rife-resident.vpy")throw new Exception("Other modes changed");
  if(PreRenderSession.RifeScript(23,900)!="interpolate-rife-throughput.vpy")throw new Exception("900p not selected");
  if(PreRenderSession.RifeScript(23,1020)!="interpolate-rife-resident.vpy")throw new Exception("1020p high-resolution profile missing");
  using(var s=new PreRenderSession(Path.GetFullPath(args[0]),"unused",0,60,60,1020,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true)){
   string graph=(string)typeof(PreRenderSession).GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{0.0,60.0});
   if(!graph.Contains("60|1020|1|23|2")||!graph.Contains("buffered-frames=4:concurrent-frames=4"))throw new Exception("1020p profile settings lost");
   var satisfied=typeof(PreRenderSession).GetField("bufferSatisfied",BindingFlags.NonPublic|BindingFlags.Instance);
   s.UpdatePlaybackBuffer(240,true);if(!(bool)satisfied.GetValue(s))throw new Exception("1020p ignores target");
   s.UpdatePlaybackBuffer(239.99,true);if((bool)satisfied.GetValue(s))throw new Exception("1020p does not resume below target");
  }
  using(var s=new PreRenderSession(Path.GetFullPath(args[0]),"unused",0,60,60,900,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true)){
   string graph=(string)typeof(PreRenderSession).GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{0.0,60.0});
   if(!graph.Contains("buffered-frames=6:concurrent-frames=6")||!graph.Contains("60|900|1|23|3"))throw new Exception("Incorrect pipeline settings");
  }
  Console.WriteLine("PASS: 1020p cache profile and 240s target; 720p/900p/1080p paths preserved");
 }
}
