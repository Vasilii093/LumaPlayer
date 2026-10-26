using System;
using System.IO;
using System.Threading;
using Luma;

// Compile against LumaPlayer.exe; takes app folder and a short video with audio.
// Uses a small test quota to exercise backpressure without allocating 10 GiB.
class CacheTests {
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); }
    static int Main(string[] args) {
        try {
            string root=Path.GetFullPath(args[0]), source=Path.GetFullPath(args[1]);
            PreRenderSession.CleanAbandoned(root);
            var first=new ManualResetEvent(false); var second=new ManualResetEvent(false);
            PreparedChunk one=null, two=null; string error=null; int count=0;
            var session=new PreRenderSession(root,source,0,4,60,720,0,false,"auto",2,4*1024*1024);
            session.Ready=delegate(PreparedChunk c) {
                if(Interlocked.Increment(ref count)==1) { one=c; first.Set(); }
                else { two=c; second.Set(); }
            };
            session.Failed=delegate(string e) { error=e; first.Set(); second.Set(); };
            session.Start();
            Check(first.WaitOne(60000),"First segment timeout"); Check(error==null,error);
            Check(one!=null && File.Exists(one.Path),"No completed file");
            Check(!one.Path.Contains("partial"),"Published an unfinished file");
            Check(!second.WaitOne(1500),"Quota did not stop producer");
            Check(session.Bytes<=4*1024*1024,"Quota exceeded");
            string sample=Path.Combine(root,"..","cache-test-sample.mp4");
            File.Copy(one.Path,sample,true);
            session.Consume(one);
            Check(second.WaitOne(60000),"Producer did not resume after deletion"); Check(error==null,error);
            Check(!File.Exists(one.Path),"Consumed file retained");
            Check(two.Start==2 && two.Length==2,"Segment timeline incorrect");
            Check(session.Work.Wait(10000),"Producer completion timeout");
            Check(session.Completed,"Completion missing");
            string directory=session.Folder; session.Dispose();
            for(int i=0;i<40 && Directory.Exists(directory);i++) Thread.Sleep(250);
            Check(!Directory.Exists(directory),"Session cleanup failed");
            Console.WriteLine("PASS: publish, quota, deletion, resume, timeline, completion and cleanup");

            var cancel=new PreRenderSession(root,source,0,4,120,720,0,false,"auto",2);
            cancel.Start(); Thread.Sleep(500); cancel.Dispose();
            Check(cancel.Work.Wait(15000),"Cancellation hung");
            Check(!Directory.Exists(cancel.Folder),"Cancelled partial retained");
            Console.WriteLine("PASS: cancellation during GPU preparation");
            return 0;
        } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
