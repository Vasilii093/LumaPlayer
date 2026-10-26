using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Luma;
class MaintenanceTests {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Main() {
        string root=Path.GetFullPath(Path.Combine("work","maintenance-"+Guid.NewGuid().ToString("N")));
        string cache=Path.Combine(root,"cache"),session=Path.Combine(cache,"session-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(session);
        string csv=Path.Combine(session,"segments.csv");
        using(var reader=new SegmentManifestReader(csv)) {
            Check(new List<string>(reader.ReadNew()).Count==0,"Missing manifest must wait");
            File.WriteAllText(csv,"000000.partial.ts,0,15\n000001.partial.ts,15",Encoding.ASCII);
            var lines=new List<string>(reader.ReadNew());
            Check(lines.Count==1 && lines[0]=="000000.partial.ts,0,15","Partial segment was published");
            long first=reader.BytesRead;
            for(int i=0;i<100;i++)Check(new List<string>(reader.ReadNew()).Count==0,"Duplicate segment");
            Check(reader.BytesRead==first,"Old manifest data reread");
            File.AppendAllText(csv,",30\r\n000002.partial.ts,30,45\n",Encoding.ASCII);
            lines=new List<string>(reader.ReadNew());
            Check(lines.Count==2 && lines[0]=="000001.partial.ts,15,30","Split append not reassembled");
            Check(reader.BytesRead==new FileInfo(csv).Length,"Incremental byte accounting");
            using(var f=new FileStream(csv,FileMode.Open,FileAccess.Write,FileShare.ReadWrite|FileShare.Delete))f.SetLength(0);
            bool rejected=false;try{new List<string>(reader.ReadNew());}catch(IOException){rejected=true;}
            Check(rejected,"Truncated manifest silently accepted");
        }
        Console.WriteLine("PASS: append-only reads, split lines, no replay, truncation rejected");
        File.Delete(csv);
        string part=Path.Combine(session,"000001.partial.ts"),closed=Path.Combine(session,"000000.ts");
        File.WriteAllBytes(closed,new byte[100]);File.WriteAllBytes(part,new byte[20]);
        using(var usage=new CacheUsageMonitor(cache)) {
            Check(usage.Measure()==120,"Initial cache size");
            using(var f=new FileStream(part,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))f.Write(new byte[30],0,30);
            Check(usage.Measure()==150,"Growing unfinished file missed");
            for(int i=0;i<100;i++)Check(usage.Measure()==150,"Incorrect repeated size");
            Check(usage.DirectoryScans<=3,"Unchanged directory repeatedly rescanned");
            File.Move(part,Path.Combine(session,"000001.ts"));usage.Invalidate();
            Check(usage.Measure()==150,"Publication double-counted or lost bytes");
            File.Delete(closed);usage.Invalidate();Check(usage.Measure()==50,"Deletion did not release quota");
            string other=Path.Combine(cache,"session-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other,"000000.partial.ts"),new byte[37]);
            var watch=Stopwatch.StartNew();
            while(usage.Measure()!=87 && watch.ElapsedMilliseconds<2000)Thread.Sleep(20);
            Check(usage.Measure()==87,"Other player cache excluded from quota");
            File.Delete(Path.Combine(other,"000000.partial.ts"));Directory.Delete(other);
            Console.WriteLine("PASS: growing bytes, publish/delete, shared quota, directory scans="+usage.DirectoryScans);
        }
        foreach(string p in Directory.GetFiles(session))File.Delete(p);
        // A read-volume comparison, not an interpolation-speed benchmark.
        var content=new StringBuilder();for(int i=0;i<1000;i++)content.AppendLine(i.ToString("D6")+".partial.ts,"+(i*15)+","+(i*15+15));
        File.WriteAllText(csv,content.ToString(),Encoding.ASCII);
        long oldRead=0;var timer=Stopwatch.StartNew();
        for(int i=0;i<1000;i++)oldRead+=File.ReadAllText(csv).Length;
        double oldMs=timer.Elapsed.TotalMilliseconds;timer.Restart();
        using(var tail=new SegmentManifestReader(csv)) {
            int count=0;for(int i=0;i<1000;i++)foreach(string row in tail.ReadNew())count++;
            Check(count==1000,"Manifest replay in long playback");
            Console.WriteLine("MANIFEST_BENCH baseline_read_bytes="+oldRead+" optimized_read_bytes="+tail.BytesRead+" baseline_ms="+oldMs.ToString("0.0")+" optimized_ms="+timer.Elapsed.TotalMilliseconds.ToString("0.0"));
        }
        File.Delete(csv);Directory.Delete(session);Directory.Delete(cache);Directory.Delete(root);
    }
}
