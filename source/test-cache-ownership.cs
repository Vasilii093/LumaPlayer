using System;
using System.IO;
using System.Diagnostics;
using System.Threading;
using Luma;

class CacheOwnershipTest {
    static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
    static string Orphan(string root,bool withLease){
        string folder=Path.Combine(root,"cache","session-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"000000.ts"),new byte[188]);
        File.WriteAllText(Path.Combine(folder,"segments.csv"),"");
        if(withLease)File.WriteAllText(Path.Combine(folder,"session.lock"),"");
        return folder;
    }
    static void CleanInOtherProcess(string root){
        var start=new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
            "--clean "+PreRenderSession.Quote(root)){UseShellExecute=false,CreateNoWindow=true};
        using(var child=Process.Start(start)){
            if(!child.WaitForExit(10000)){child.Kill();throw new Exception("Cleaner timeout");}
            Check(child.ExitCode==0,"Cleaner failed");
        }
    }
    static int Main(string[] args){
        if(args.Length==2&&args[0]=="--clean"){PreRenderSession.CleanAbandoned(args[1]);return 0;}
        string root=Path.GetFullPath(Path.Combine("work","cache-ownership-"+Guid.NewGuid().ToString("N")));
        string active;
        using(var session=new PreRenderSession(root,"unused",0,60,60,900,1,false,"auto")){
            active=session.Folder;
            string chunk=Path.Combine(active,"000000.ts");File.WriteAllBytes(chunk,new byte[188]);
            string orphan=Orphan(root,true),legacy=Orphan(root,false);
            string unknown=Orphan(root,true);File.WriteAllText(Path.Combine(unknown,"keep.txt"),"preserve");
            CleanInOtherProcess(root);
            Check(File.Exists(chunk)&&File.Exists(Path.Combine(active,"session.lock")),"Active cache deleted");
            Check(!Directory.Exists(orphan)&&!Directory.Exists(legacy),"Abandoned cache retained");
            Check(File.ReadAllText(Path.Combine(unknown,"keep.txt"))=="preserve","Unknown file deleted");
            File.Delete(Path.Combine(unknown,"keep.txt"));Directory.Delete(unknown,false);
        }
        for(int i=0;i<50&&Directory.Exists(active);i++)Thread.Sleep(100);
        Check(!Directory.Exists(active),"Owner did not release and remove its cache");
        Directory.Delete(Path.Combine(root,"cache"),false);Directory.Delete(root,false);
        Console.WriteLine("PASS: another process preserves active cache, removes abandoned cache, retains unknown files; dispose releases lease");
        return 0;
    }
}
