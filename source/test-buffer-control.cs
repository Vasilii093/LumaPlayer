using System;
using System.IO;
using System.Threading;
using Luma;
class BufferTest {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static int Main(string[] args){
  PreRenderSession s=null;
  try {
   s=new PreRenderSession(Path.GetFullPath(args[0]),args[1],1922,1930,60,720,0,false,"auto",1);
   int count=0;string error=null;var paused=new ManualResetEvent(false);
   s.Ready=c=>{if(Interlocked.Increment(ref count)==1)s.UpdatePlaybackBuffer(240,true);};
   s.Status=m=>{if(m.Contains("Запас готов"))paused.Set();};
   s.Failed=e=>{error=e;paused.Set();};
   s.Start();Check(paused.WaitOne(45000),"Pause timeout");Check(error==null,error);
   Thread.Sleep(1000);int before=count;Thread.Sleep(1500);
   Check(count==before&&!s.Completed,"Producer did not stay paused");
   s.UpdatePlaybackBuffer(240.01,true);Thread.Sleep(1200);
   Check(count==before,"Producer resumed above target");
   s.UpdatePlaybackBuffer(239.99,true);
   Check(s.Work.Wait(45000),"Resume timeout");Check(error==null,error);
   Check(s.Completed&&count>before&&s.EncoderStarts==1,"Resume restarted model or failed");
   Console.WriteLine("PASS: encoder pauses at 240 s, remains paused above it and resumes below it without restart");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally {if(s!=null){s.Dispose();for(int i=0;i<40&&Directory.Exists(s.Folder);i++)Thread.Sleep(250);}}
 }
}
