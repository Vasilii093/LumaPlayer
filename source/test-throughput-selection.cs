using System;using System.IO;using System.Reflection;using Luma;
class ThroughputSelection {
 static void Main(string[] args){
  if(!PreRenderSession.NativeRife||!PreRenderSession.RifeThroughput)throw new Exception("Flags missing");
  if(PreRenderSession.RifeScript(23,720)!="interpolate-rife-resident.vpy"||PreRenderSession.RifeScript(23,1080)!="interpolate-rife-resident.vpy")throw new Exception("Other modes changed");
  if(PreRenderSession.RifeScript(23,900)!="interpolate-rife-throughput.vpy")throw new Exception("900p not selected");
  using(var s=new PreRenderSession(Path.GetFullPath(args[0]),"unused",0,60,60,900,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true)){
   string graph=(string)typeof(PreRenderSession).GetMethod("Filter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(s,new object[]{0.0,60.0});
   if(!graph.Contains("buffered-frames=6:concurrent-frames=6")||!graph.Contains("60|900|1|23|3"))throw new Exception("Incorrect pipeline settings");
  }
  Console.WriteLine("PASS: 900p throughput selected; 720p/1080p preserved; full 900p and 3 GPU workers");
 }
}
