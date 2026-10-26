using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace Luma {
    // The muxer appends a line only after closing its segment. Keep partial
    // lines across polls; never treat an incomplete append as a ready file.
    public sealed class SegmentManifestReader : IDisposable {
        readonly string path;
        FileStream input;
        readonly byte[] buffer=new byte[4096];
        readonly StringBuilder pending=new StringBuilder();
        public long BytesRead { get; private set; }
        public SegmentManifestReader(string file) { path=file; }
        public IEnumerable<string> ReadNew() {
            if(input==null) {
                try { input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete); }
                catch(FileNotFoundException) { yield break; }
            }
            long length=input.Length;
            if(length<input.Position) throw new IOException("Список сегментов неожиданно усечён");
            while(input.Position<length) {
                int n=input.Read(buffer,0,(int)Math.Min(buffer.Length,length-input.Position));
                if(n==0) yield break;
                BytesRead+=n;
                for(int i=0;i<n;i++) {
                    char c=(char)buffer[i];
                    if(c=='\n') {
                        string line=pending.ToString().TrimEnd('\r'); pending.Clear();
                        if(line.Length>0) yield return line;
                    } else {
                        pending.Append(c);
                        if(pending.Length>4096) throw new IOException("Повреждён список сегментов");
                    }
                }
            }
        }
        public void Dispose() { if(input!=null){input.Dispose();input=null;} }
    }

    // Published segments are immutable. Poll only growing files between
    // changes; periodically reconcile all sessions, including other players.
    public sealed class CacheUsageMonitor : IDisposable {
        readonly string root;
        readonly object sync=new object();
        readonly List<string> growing=new List<string>();
        readonly Stopwatch reconciliation=Stopwatch.StartNew();
        FileSystemWatcher watcher;
        long settledBytes;
        int dirty=1;
        public int DirectoryScans {get;private set;}
        public int GrowingFileStats {get;private set;}
        public CacheUsageMonitor(string cacheRoot) {
            root=cacheRoot;
            try {
                watcher=new FileSystemWatcher(root) {IncludeSubdirectories=true,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName};
                watcher.Created+=Changed; watcher.Deleted+=Changed; watcher.Renamed+=Changed;
                watcher.Error+=delegate { Invalidate(); };
                watcher.EnableRaisingEvents=true;
            } catch(IOException) { if(watcher!=null)watcher.Dispose();watcher=null; }
        }
        void Changed(object sender,FileSystemEventArgs args) { Invalidate(); }
        public void Invalidate() { Interlocked.Exchange(ref dirty,1); }
        public long Measure() {
            lock(sync) {
                if(Interlocked.Exchange(ref dirty,0)!=0 || reconciliation.ElapsedMilliseconds>=1000) Reconcile();
                long size=settledBytes;
                foreach(string file in growing) {
                    try { GrowingFileStats++; size+=new FileInfo(file).Length; }
                    catch(IOException) { Invalidate(); }
                }
                return size;
            }
        }
        void Reconcile() {
            DirectoryScans++; settledBytes=0; growing.Clear();
            try {
                foreach(string directory in Directory.GetDirectories(root,"session-*")) {
                    try {
                        if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)continue;
                        foreach(string path in Directory.GetFiles(directory)) {
                            string name=Path.GetFileName(path);
                            bool complete=(name.EndsWith(".ts",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".mp4",StringComparison.OrdinalIgnoreCase)) && name.IndexOf(".partial.",StringComparison.OrdinalIgnoreCase)<0;
                            if(complete) {try {settledBytes+=new FileInfo(path).Length;} catch(IOException){Invalidate();}}
                            else growing.Add(path);
                        }
                    } catch(IOException) {Invalidate();}
                }
            } catch(DirectoryNotFoundException) {}
            reconciliation.Restart();
        }
        public void Dispose() { var owned=Interlocked.Exchange(ref watcher,null);if(owned!=null)owned.Dispose(); }
    }
}
