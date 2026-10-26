using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Luma;

class ShutdownTests {
    class BrokenStream : MemoryStream {
        public override void Flush() { throw new IOException("Engine closed its pipe"); }
    }
    class BusyStream : MemoryStream {
        public readonly ManualResetEvent entered=new ManualResetEvent(false), release=new ManualResetEvent(false);
        public volatile bool disposed;
        public override void Write(byte[] bytes,int offset,int count) { entered.Set();if(!release.WaitOne(5000))throw new Exception("Write not released");base.Write(bytes,offset,count); }
        protected override void Dispose(bool disposing) {disposed=true;base.Dispose(disposing);}
    }
    [STAThread] static int Main() {
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        for(int i=0;i<50;i++) {
            using(var player=new Player(null)) {
                var handle=player.Handle;
                bool callback=false;
                typeof(Player).GetMethod("UI",flags).Invoke(player,new object[]{(Action)(()=>callback=true)});
                var stream=new BrokenStream();
                var writer=new StreamWriter(stream);
                writer.Write("pending");
                typeof(Player).GetField("writer",flags).SetValue(player,writer);
                typeof(Player).GetMethod("Shutdown",flags).Invoke(player,null);
                typeof(Player).GetMethod("Shutdown",flags).Invoke(player,null);
                Application.DoEvents();
                if(callback) throw new Exception("Queued callback ran after shutdown");
                if(typeof(Player).GetField("writer",flags).GetValue(player)!=null)
                    throw new Exception("Writer remains accessible after shutdown");
                try { writer.Dispose(); } catch(IOException) {} // read-loop owner
                stream.Dispose();
            }
        }
        using(var player=new Player(null)) using(var stream=new BusyStream()) {
            var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false)){AutoFlush=true};
            typeof(Player).GetField("writer",flags).SetValue(player,writer);
            var sending=Task.Run(()=>typeof(Player).GetMethod("Send",flags).Invoke(player,new object[]{new object[]{"quit"}}));
            if(!stream.entered.WaitOne(5000))throw new Exception("Send did not enter stream");
            var disposing=Task.Run(()=>typeof(Player).GetMethod("CloseWriter",flags).Invoke(player,new object[]{writer}));
            Thread.Sleep(100);
            if(stream.disposed || disposing.IsCompleted)throw new Exception("Read loop disposed writer during Send");
            stream.release.Set();Task.WaitAll(sending,disposing);
            if(!stream.disposed || typeof(Player).GetField("writer",flags).GetValue(player)!=null)throw new Exception("Writer cleanup incomplete");
        }
        Console.WriteLine("PASS: 50 shutdown races, broken pipe, queued callback suppression, repeated shutdown, concurrent write/dispose serialization");
        return 0;
    }
}
