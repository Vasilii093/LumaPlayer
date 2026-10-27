using System;using System.IO;using System.Reflection;using System.Text;using System.Collections.Generic;using Luma;
class PlaybackChecks {
 static BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
 static void Set(Player p,string n,object v){typeof(Player).GetField(n,F).SetValue(p,v);}
 static object Get(Player p,string n){return typeof(Player).GetField(n,F).GetValue(p);}
 static object Call(Player p,string n,params object[] args){return typeof(Player).GetMethod(n,F).Invoke(p,args);}
 static void Check(bool x,string message){if(!x)throw new Exception(message);}
 [STAThread] static void Main(){
  using(var p=new Player(null)) using(var bytes=new MemoryStream()) {
   Set(p,"writer",new StreamWriter(bytes,new UTF8Encoding(false)){AutoFlush=true});
   using(var s=new PreRenderSession(Path.GetFullPath("work/framecache/ui-fixture"),"unused",0,60,60,1080,1,false,"auto",15,PreRenderSession.DefaultLimit,23,true,true)) {
    Set(p,"cache",s);Set(p,"cacheStream",new CacheStream());Set(p,"cacheFileLoaded",true);Set(p,"cacheOrigin",0.0);Set(p,"position",20.0);
    Call(p,"UpdateMetric","paused-for-cache",true);Call(p,"UpdateMetric","paused-for-cache",true);
    Check((int)Get(p,"cacheStalls")==1,"Counted duplicate cache state as another stall");
    Call(p,"UpdateMetric","paused-for-cache",false);Call(p,"UpdateMetric","paused-for-cache",true);
    Check((int)Get(p,"cacheStalls")==2,"Missed new stall");
    Call(p,"UpdateMetric","display-fps",60.0);Call(p,"UpdateMetric","display-fps",(object)null);
    Check((string)Call(p,"Metric","display-fps")=="н/д","Stale display metric retained");
    Call(p,"LoadCacheStream",0.0);
    var commands=Encoding.UTF8.GetString(bytes.ToArray());
    Check(commands.Contains("display-resample")&&commands.Contains("video-sync-max-video-change")&&commands.Contains("0.2"),"Display sync not scoped to cache file");
    ((CacheStream)Get(p,"cacheStream")).Dispose();Set(p,"cacheStream",null);Set(p,"cache",null);
    Call(p,"UpdateMetric","paused-for-cache",false);Call(p,"UpdateMetric","paused-for-cache",true);
    Check((int)Get(p,"cacheStalls")==2,"Source playback counted as cache stall");
   }
   Set(p,"writer",null);
  }
  Console.WriteLine("PASS: file-local display sync; cache stall transitions; unavailable metrics; source/cache distinction");
 }
}
