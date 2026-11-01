using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Globalization;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.IO.Pipes;
using System.Text.RegularExpressions;

namespace Luma {
    public sealed class PreparedChunk {
        public string Path;
        public double Start, Length;
    }

    // A disk-backed producer. Only atomically published, closed files can play.
    // The limit includes the in-progress file, not just completed segments.
    public sealed class PreRenderSession : IDisposable {
#if LUMA_RIFE_NATIVE
        public const bool NativeRife = true;
#else
        public const bool NativeRife = false;
#endif
#if LUMA_RIFE_THROUGHPUT
        public const bool RifeThroughput = true;
#else
        public const bool RifeThroughput = false;
#endif
        public static bool Fast900(int model,int height){return NativeRife && RifeThroughput && model==23 && height==900;}
        public static string RifeScript(int model, int height) {
            if(model!=23 || (height!=720 && height!=900 && height!=1020 && height!=1080)) throw new ArgumentException("Unsupported RIFE profile");
            if(Fast900(model,height))return "interpolate-rife-throughput.vpy";
            return "interpolate-rife-resident.vpy";
        }
        public const long DefaultLimit = 10L * 1024 * 1024 * 1024;
        // IPC represents a disabled track as a boolean; command-line options do not.
        public static string NormalizeAudioTrack(object value) {
            string text=Convert.ToString(value,CultureInfo.InvariantCulture).Trim().ToLowerInvariant();
            if(text=="no" || text=="false") return "no";
            int id;
            if(Int32.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out id) && id>0)
                return id.ToString(CultureInfo.InvariantCulture);
            return "auto";
        }
        readonly object gate = new object();
        readonly string root, source, folder, audio;
        readonly int fps, height, protection, model;
        readonly double start, end, chunkSeconds;
        readonly long limit;
        readonly bool persistent;
        readonly bool transport;
        public int EncoderStarts { get; private set; }
        readonly List<PreparedChunk> chunks = new List<PreparedChunk>();
        readonly HashSet<string> consumed = new HashSet<string>();
        Process worker;
        FileStream lease;
        CacheUsageMonitor usage;
        public long ManifestBytesRead {get;private set;}
        public int CacheDirectoryScans {get {return usage==null ? 0 : usage.DirectoryScans;}}
        volatile bool cancelled;
        bool bufferSatisfied;
        // Keep preparing whenever the playback buffer is below the target.
        public void UpdatePlaybackBuffer(double seconds, bool playbackStarted) {
            if(Double.IsNaN(seconds) || Double.IsInfinity(seconds)) return;
            lock(gate) {
                if(!playbackStarted || model!=23 || (height!=720 && height!=900 && height!=1020 && height!=1080)) bufferSatisfied=false;
                else bufferSatisfied=seconds>=240;
            }
        }
        long measuredBytes;
        public volatile bool Completed;
        public double LastSpeed { get; private set; }
        public Action<double,double> Measured;
        public Action<PreparedChunk> Ready;
        public Action<string> Status;
        public Action<string> Failed;
        public Task Work { get; private set; }
        public string Folder { get { return folder; } }
        public long Bytes { get { return Interlocked.Read(ref measuredBytes); } }

        public PreRenderSession(string appRoot, string input, double position, double duration,
            int target, int processingHeight, int guard, bool scale, string audioTrack,
            double segmentSeconds = 15, long byteLimit = DefaultLimit, int interpolationModel = 23, bool persistentEncoder = true, bool continuousTransport = false) {
            if(target!=60 && target!=120) throw new ArgumentException("FPS");
            if(duration<=position || position<0 || segmentSeconds<=0 || byteLimit<1024*1024)
                throw new ArgumentException("Invalid cache bounds");
            root=appRoot; source=input; start=position; end=duration; fps=target;
            if(interpolationModel!=23) throw new ArgumentException("Interpolation model");
            model=interpolationModel;
            height=processingHeight; protection=guard; audio=NormalizeAudioTrack(audioTrack);
            chunkSeconds=segmentSeconds; limit=byteLimit;
            // Small synthetic test quotas retain the bounded one-file path.
            persistent=persistentEncoder && byteLimit>=256L*1024*1024;
            transport=continuousTransport;
            if(transport && !persistent) throw new ArgumentException("Continuous transport requires persistent encoder");
            folder=System.IO.Path.Combine(root,"cache","session-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            // A new player may clean abandoned sessions while this one is playing.
            // Keep an exclusive-write lease until the producer and its cache are closed.
            lease=new FileStream(System.IO.Path.Combine(folder,"session.lock"),FileMode.CreateNew,FileAccess.ReadWrite,FileShare.Read);
        }
        static bool IsFilterFailure(string line) {
            return line.Contains("Invalid MEMC") || line.Contains("Invalid TensorRT pixels") || line.Contains("Disabling filter") || line.Contains("Script evaluation failed") ||
                line.Contains("failed to configure") || line.Contains("Failed to initialize VapourSynth") ||
                line.Contains("Failed to load VapourSynth") ||
                (line.Contains("Creating filter") && line.Contains("failed")) ||
                line.Contains("video: encoded 0 bytes");
        }
        static bool IsSourceFailure(string line) {
            // Do not turn missing/corrupt source packets into minutes of invented frames.
            if(line.Contains("Corrupt file detected") || line.Contains("Invalid EBML length") || line.Contains("metadata beyond end of file"))return true;
            var pts=Regex.Match(line,@"Invalid audio PTS:\s*([-+0-9.]+)\s*->\s*([-+0-9.]+)");
            double before,after;
            return pts.Success && Double.TryParse(pts.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out before) &&
                Double.TryParse(pts.Groups[2].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out after) && Math.Abs(after-before)>2;
        }
        public void Start() { Work=Task.Run((Action)Produce); }
        public void Consume(PreparedChunk chunk) {
            lock(gate) consumed.Add(chunk.Path);
            Task.Run(delegate {
                Sweep();
                for(int n=0;n<20 && !cancelled;n++) {
                    lock(gate) { if(!consumed.Contains(chunk.Path)) return; }
                    Thread.Sleep(250); Sweep();
                }
            });
        }
        long UsedBytes() {
            if(usage==null)usage=new CacheUsageMonitor(System.IO.Path.Combine(root,"cache"));
            long size=usage.Measure();
            Interlocked.Exchange(ref measuredBytes,size);
            return size;
        }
        public static void CleanAbandoned(string appRoot) {
            string basePath=System.IO.Path.Combine(appRoot,"cache");
            if(!Directory.Exists(basePath)) return;
            foreach(string directory in Directory.GetDirectories(basePath,"session-*")) {
                Guid id;
                if(!Guid.TryParseExact(System.IO.Path.GetFileName(directory).Substring(8),"N",out id)) continue;
                if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0) continue;
                FileStream abandonedLease=null;
                string leasePath=System.IO.Path.Combine(directory,"session.lock");
                try {
                    // Sharing violation means an active session owns the entire directory.
                    if(File.Exists(leasePath))abandonedLease=new FileStream(leasePath,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
                    foreach(string p in Directory.GetFiles(directory)) {
                        string name=System.IO.Path.GetFileName(p);
                        if(name=="segments.csv" || System.Text.RegularExpressions.Regex.IsMatch(name,@"^\d{6}(\.partial)?\.(mp4|ts)$")) File.Delete(p);
                    }
                    if(abandonedLease!=null){abandonedLease.Dispose();abandonedLease=null;File.Delete(leasePath);}
                    if(Directory.GetFileSystemEntries(directory).Length==0) Directory.Delete(directory,false);
                } catch(IOException) {} catch(UnauthorizedAccessException) {}
                  finally {if(abandonedLease!=null)abandonedLease.Dispose();}
            }
        }
        void Sweep() {
            lock(gate) {
                if(consumed.Count==0)return;
                foreach(string p in new List<string>(consumed)) {
                    try { File.Delete(p); consumed.Remove(p); if(usage!=null)usage.Invalidate(); } catch(IOException) {} catch(UnauthorizedAccessException) {}
                }
            }
        }
        void Report(string message) { if(!cancelled && Status!=null) Status(message); }
        static string Number(double value) { return value.ToString("0.########",CultureInfo.InvariantCulture); }
        public static string Quote(string s) {
            var b=new StringBuilder("\""); int slashes=0;
            foreach(char c in s) {
                if(c=='\\') { slashes++; continue; }
                if(c=='\"') { b.Append('\\',slashes*2+1); b.Append(c); }
                else { b.Append('\\',slashes); b.Append(c); }
                slashes=0;
            }
            b.Append('\\',slashes*2); b.Append('"'); return b.ToString();
        }
        string Filter(double position, double length) {
            string path=System.IO.Path.Combine(root,"engine","gpu",RifeScript(model,height)).Replace('\\','/');
            // Avoid neural inference over the entire keyframe seek preroll;
            // retain a small context at both edges for correct interpolation.
            string graph="lavfi=[trim=start="+Number(Math.Max(0,position-0.15))+":end="+Number(position+length+0.5)+"],"+
                "vapoursynth=file=%"+Encoding.UTF8.GetByteCount(path)+"%"+path+
                ":buffered-frames="+(Fast900(model,height) ? 6 : 4)+":concurrent-frames="+(Fast900(model,height) ? 6 : 4)+":user-data="+fps+"|"+height+"|"+protection+"|"+model+"|"+(Fast900(model,height) ? 3 : 2)+
                ",lavfi=[fps=fps="+fps+":round=near,trim=start="+Number(position)+":end="+Number(position+length)+"]";
            // The playback GPU scales to the window; do not upscale on CPU
            // and encode the larger frames into the disk cache.
            return graph;
        }
        void Produce() {
            if(persistent) { ProduceContinuous(); return; }
            try {
                int index=0;
                for(double position=start; position<end-0.001 && !cancelled;) {
                    Sweep();
                    // 30 Mbit/s maximum + VBV burst + audio + mux overhead, plus margin.
                    double length=Math.Min(chunkSeconds,end-position);
                    long reserve=(long)((length*30e6/8+60e6/8)*1.5)+8*1024*1024;
                    reserve=Math.Min(reserve,limit);
                    var drive=new DriveInfo(System.IO.Path.GetPathRoot(folder));
                    while(!cancelled && (UsedBytes()+reserve>limit || drive.AvailableFreeSpace<reserve+64*1024*1024)) {
                        Report("Кэш заполнен или мало места · ожидание освобождения");
                        Thread.Sleep(250); Sweep();
                    }
                    if(cancelled) break;
                    string partial=System.IO.Path.Combine(folder,index.ToString("D6")+".partial.mp4");
                    string finished=System.IO.Path.Combine(folder,index.ToString("D6")+".mp4");
                    var si=new ProcessStartInfo(System.IO.Path.Combine(root,"engine","mpv.exe"));
                    si.UseShellExecute=false; si.CreateNoWindow=true;
                    si.RedirectStandardError=true; si.RedirectStandardOutput=true;
                    string runtime=System.IO.Path.Combine(root,"engine","runtime");
                    string vs=System.IO.Path.Combine(runtime,"Lib","site-packages","vapoursynth");
                    si.EnvironmentVariables["VSSCRIPT_PATH"]=System.IO.Path.Combine(vs,"vsscript.dll");
                    si.EnvironmentVariables["PYTHONHOME"]=runtime;
                    si.EnvironmentVariables["PATH"]=runtime+";"+vs+";"+Environment.GetEnvironmentVariable("PATH");
                    si.Arguments="--no-config --untimed --terminal=yes --msg-level=all=warn --hr-seek=yes --hwdec=auto-copy --sid=no --audio-channels=auto"+
                        " --start="+Number(position)+" --length="+Number(length)+" --aid="+audio+
                        " --of=mp4 --ovc=hevc_nvenc --ovcopts=preset=p4,rc=vbr,cq=20,b=12000000,maxrate=30000000,bufsize=60000000"+
                        " --oac=aac --oacopts=b=192000 "+Quote("--o="+partial)+" "+Quote("--vf="+Filter(position,length))+" -- "+Quote(source);
                    var tail=new StringBuilder(); bool filterFailed=false, sourceFailed=false, exceeded=false;
                    var segmentTimer=Stopwatch.StartNew();
                    using(var process=new Process { StartInfo=si }) {
                        DataReceivedEventHandler receive=delegate(object sender,DataReceivedEventArgs e) {
                            if(e.Data==null) return;
                            lock(tail) {
                                if(IsFilterFailure(e.Data)) filterFailed=true;
                                if(IsSourceFailure(e.Data)) sourceFailed=true;
                                tail.AppendLine(e.Data); if(tail.Length>6000) tail.Remove(0,tail.Length-6000);
                            }
                        };
                        process.OutputDataReceived+=receive; process.ErrorDataReceived+=receive;
                        lock(gate) { if(cancelled) break; process.Start(); worker=process; EncoderStarts++; }
                        // Keep decoder/audio scheduling ahead of background setup.
                        try { process.PriorityClass=ProcessPriorityClass.BelowNormal; }
                        catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
                        process.BeginOutputReadLine(); process.BeginErrorReadLine();
                        var timer=Stopwatch.StartNew();
                        while(!process.WaitForExit(500)) {
                            lock(tail) { if(sourceFailed || filterFailed) {try {process.Kill();}catch(InvalidOperationException){} break;} }
                            if(cancelled || UsedBytes()>limit) {
                                exceeded=!cancelled;
                                try { process.Kill(); } catch(InvalidOperationException) {}
                                break;
                            }
                            Report("Подготовка "+Number(position)+"–"+Number(position+length)+" с · прошло "+(int)timer.Elapsed.TotalSeconds+" с");
                        }
                        process.WaitForExit();
                        lock(gate) worker=null;
                        if(cancelled) break;
                        if(exceeded || UsedBytes()>limit) throw new IOException("Достигнут лимит кэша. Незавершённая часть удалена.");
                        lock(tail) {
                            if(sourceFailed) throw new IOException("Исходный файл повреждён или загружен не полностью. Проверьте завершение загрузки.\n"+tail);
                            if(process.ExitCode!=0 || filterFailed || !File.Exists(partial) || new FileInfo(partial).Length<1024)
                                throw new IOException("Не удалось подготовить видео. "+tail.ToString());
                        }
                    }
                    File.Move(partial,finished);
                    if(usage!=null)usage.Invalidate();
                    LastSpeed=length/segmentTimer.Elapsed.TotalSeconds;
                    if(!cancelled && Measured!=null) Measured(length,segmentTimer.Elapsed.TotalSeconds);
                    var chunk=new PreparedChunk { Path=finished,Start=position,Length=length };
                    lock(gate) chunks.Add(chunk);
                    if(!cancelled && Ready!=null) Ready(chunk);
                    position+=length; index++;
                }
                if(!cancelled) { Completed=true; Report("Подготовка завершена"); }
            } catch(Exception ex) {
                try { foreach(string p in Directory.GetFiles(folder,"*.partial.mp4")) File.Delete(p); } catch(IOException) {}
                if(!cancelled && Failed!=null) Failed(ex.Message);
            } finally { if(cancelled) Cleanup(); }
        }
        void ProduceContinuous() {
            NamedPipeClientStream control=null; StreamWriter commands=null;
            SegmentManifestReader segments=null;
            try {
                string pipe="luma-render-"+Guid.NewGuid().ToString("N");
                string manifest=System.IO.Path.Combine(folder,"segments.csv");
                segments=new SegmentManifestReader(manifest);
                string ext=transport ? "ts" : "mp4";
                var si=new ProcessStartInfo(System.IO.Path.Combine(root,"engine","mpv.exe"));
                si.UseShellExecute=false; si.CreateNoWindow=true; si.WorkingDirectory=folder;
                si.RedirectStandardError=true; si.RedirectStandardOutput=true;
                string runtime=System.IO.Path.Combine(root,"engine","runtime");
                string vs=System.IO.Path.Combine(runtime,"Lib","site-packages","vapoursynth");
                si.EnvironmentVariables["VSSCRIPT_PATH"]=System.IO.Path.Combine(vs,"vsscript.dll");
                si.EnvironmentVariables["PYTHONHOME"]=runtime;
                si.EnvironmentVariables["PATH"]=runtime+";"+vs+";"+Environment.GetEnvironmentVariable("PATH");
                si.Arguments="--no-config --untimed --terminal=yes --msg-level=all=warn --hr-seek=yes --hwdec=auto-copy --sid=no --audio-channels=auto"+
                    " --input-ipc-server="+pipe+" --start="+Number(start)+" --length="+Number(end-start)+" --aid="+audio+
                    " --of=segment --ofopts=segment_time="+Number(chunkSeconds)+",segment_format="+(transport ? "mpegts,reset_timestamps=0,segment_format_options=mpegts_copyts=1" : "mp4,reset_timestamps=1")+",segment_list=segments.csv,segment_list_type=csv"+
                    " --ovc=hevc_nvenc --ovcopts=preset=p4,rc=vbr,cq=20,b=12000000,maxrate=30000000,bufsize=60000000,bf=0,g="+(int)Math.Round(chunkSeconds*fps)+
                    " --oac=aac --oacopts=b=192000 --o=%06d.partial."+ext+" "+Quote("--vf="+Filter(start,end-start))+" -- "+Quote(source);
                var tail=new StringBuilder(); bool filterFailed=false, sourceFailed=false;
                var published=new HashSet<string>();
                var entryPattern=new Regex(@"^(\d{6}\.partial\."+ext+@"),([0-9.]+),([0-9.]+)$",RegexOptions.CultureInvariant);
                var measure=Stopwatch.StartNew();
                long reserve=(long)((chunkSeconds*30e6/8+60e6/8)*1.5)+32*1024*1024;
                var drive=new DriveInfo(System.IO.Path.GetPathRoot(folder));
                while(!cancelled && (UsedBytes()+reserve*2>limit || drive.AvailableFreeSpace<reserve*2)) {
                    Report("Ожидание места для постоянного обработчика"); Sweep(); Thread.Sleep(250);
                }
                if(cancelled) return;
                using(var process=new Process { StartInfo=si }) {
                    DataReceivedEventHandler receive=delegate(object sender,DataReceivedEventArgs e) {
                        if(e.Data==null) return;
                        lock(tail) {
                            if(IsFilterFailure(e.Data)) filterFailed=true;
                            if(IsSourceFailure(e.Data)) sourceFailed=true;
                            tail.AppendLine(e.Data); if(tail.Length>8000) tail.Remove(0,tail.Length-8000);
                        }
                    };
                    process.OutputDataReceived+=receive; process.ErrorDataReceived+=receive;
                    lock(gate) { if(cancelled) return; process.Start(); worker=process; EncoderStarts++; }
                    try { process.PriorityClass=ProcessPriorityClass.BelowNormal; }
                    catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
                    process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    bool paused=false; int ticks=0;
                    try {
                    while(true) {
                        bool exited=process.WaitForExit(100);
                        if(exited) process.WaitForExit(); // drain stderr before publication
                        if(cancelled) break;
                        lock(tail) {
                            if(sourceFailed) throw new IOException("Исходный файл повреждён или загружен не полностью. Проверьте завершение загрузки.\n"+tail);
                            if(filterFailed) throw new IOException("Ошибка фильтра: "+tail);
                        }
                        // CSV is appended only when the muxer has closed a segment.
                        foreach(string line in segments.ReadNew()) {
                                Match entry=entryPattern.Match(line);
                                if(!entry.Success)throw new IOException("Повреждён список готовых отрезков");
                                string name=entry.Groups[1].Value;
                                if(published.Contains(name)) continue;
                                double a=Double.Parse(entry.Groups[2].Value,CultureInfo.InvariantCulture);
                                double b=Double.Parse(entry.Groups[3].Value,CultureInfo.InvariantCulture);
                                string part=System.IO.Path.Combine(folder,name), path=part.Replace(".partial."+ext,"."+ext);
                                if(!File.Exists(part) || new FileInfo(part).Length<1024 || b<=a) throw new IOException("Повреждённый отрезок кэша");
                                File.Move(part,path); published.Add(name);
                                if(usage!=null)usage.Invalidate();
                                double seconds=b-a, elapsed=measure.Elapsed.TotalSeconds;
                                LastSpeed=seconds/Math.Max(0.001,elapsed);
                                if(Measured!=null) Measured(seconds,elapsed);
                                measure.Restart();
                                var chunk=new PreparedChunk {Path=path,Start=start+a,Length=Math.Min(seconds,end-start-a)};
                                lock(gate) chunks.Add(chunk);
                                if(Ready!=null && !cancelled) Ready(chunk);
                        }
                        ManifestBytesRead=segments.BytesRead;
                        if(exited) {
                            lock(gate) worker=null;
                            if(process.ExitCode!=0) throw new IOException("Обработчик остановлен: "+tail);
                            if(published.Count==0) throw new IOException("Обработчик не создал готовых отрезков: "+tail);
                            if(UsedBytes()>limit)throw new IOException("Достигнут лимит кэша");
                            Completed=true; Report("Подготовка завершена"); break;
                        }
                        Sweep(); long bytes=UsedBytes();
                        if(bytes>limit) throw new IOException("Достигнут лимит кэша");
                        bool shouldPause=bytes+reserve>limit || drive.AvailableFreeSpace<reserve;
                        // Hysteresis avoids pause/resume oscillation near the limit.
                        if(paused && bytes+reserve*2>limit) shouldPause=true;
                        bool aheadEnough;
                        lock(gate) aheadEnough=bufferSatisfied;
                        shouldPause=shouldPause || aheadEnough;
                        if(control==null) {
                            var candidate=new NamedPipeClientStream(".",pipe,PipeDirection.Out);
                            try { candidate.Connect(10); control=candidate; commands=new StreamWriter(control,new UTF8Encoding(false)){AutoFlush=true}; }
                            catch(TimeoutException) {candidate.Dispose();}
                        }
                        if(shouldPause!=paused) {
                            if(commands==null) throw new IOException("Нет связи для ограничения кэша");
                            commands.WriteLine("{\"command\":[\"set_property\",\"pause\","+(shouldPause ? "true" : "false")+"]}");
                            paused=shouldPause;
                            if(paused) measure.Stop(); else measure.Start();
                        }
                        if(++ticks%10==0) Report(paused ? (aheadEnough ? "Запас готов · GPU освобождён для просмотра" : "Кэш заполнен · обработчик приостановлен") : "Непрерывная подготовка · модель остаётся в памяти");
                    }
                    } finally {
                        try { if(!process.HasExited) {process.Kill(); process.WaitForExit(5000);} }
                        catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {}
                        lock(gate) worker=null;
                    }
                }
            } catch(Exception ex) {
                if(!cancelled && Failed!=null) Failed(ex.Message);
            } finally {
                lock(gate) { try { if(worker!=null && !worker.HasExited) { worker.Kill(); worker.WaitForExit(5000); } } catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {} worker=null; }
                if(segments!=null)segments.Dispose();
                if(commands!=null) {try {commands.Dispose();} catch(IOException) {}}
                if(control!=null) control.Dispose();
                if(cancelled) Cleanup();
            }
        }
        void Cleanup() {
            if(usage!=null)usage.Dispose();
            var ownedLease=Interlocked.Exchange(ref lease,null);
            if(ownedLease!=null)ownedLease.Dispose();
            // folder is a newly created private GUID directory; never recurse.
            for(int retry=0;retry<20;retry++) {
                try {
                    foreach(string path in Directory.GetFiles(folder)) File.Delete(path);
                    Directory.Delete(folder,false); return;
                } catch(DirectoryNotFoundException) { return; }
                  catch(IOException) {} catch(UnauthorizedAccessException) {}
                Thread.Sleep(250);
            }
        }
        public void Dispose() {
            cancelled=true;
            lock(gate) { try { if(worker!=null && !worker.HasExited) worker.Kill(); } catch(InvalidOperationException) {} catch(System.ComponentModel.Win32Exception) {} }
            if(Work==null || Work.IsCompleted) Task.Run((Action)Cleanup);
        }
    }
}

namespace Luma {
    // One demuxer and audio filter lifetime across all cache chunks. Loopback only.
    public sealed class CacheStream : IDisposable {
        readonly System.Net.Sockets.TcpListener listener;
        readonly System.Collections.Concurrent.BlockingCollection<PreparedChunk> pending=new System.Collections.Concurrent.BlockingCollection<PreparedChunk>();
        readonly string token=Guid.NewGuid().ToString("N");
        readonly object sync=new object();
        System.Net.Sockets.TcpClient client;
        volatile bool stopped;
        public Action<string> Failed;
        public string Url {get;private set;}
        public CacheStream() {
            listener=new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback,0); listener.Start();
            Url="http://127.0.0.1:"+((System.Net.IPEndPoint)listener.LocalEndpoint).Port+"/"+token+".ts";
            Task.Run((Action)Serve);
        }
        public void Add(PreparedChunk chunk) {if(!stopped && !pending.IsAddingCompleted)pending.Add(chunk);}
        public void Finish() {if(!pending.IsAddingCompleted)pending.CompleteAdding();}
        void Serve() {
            try {
                while(!stopped) {
                    var accepted=listener.AcceptTcpClient();
                    lock(sync){if(stopped){accepted.Close();return;}client=accepted;}
                    accepted.ReceiveTimeout=5000;
                    using(accepted) using(var stream=accepted.GetStream()) {
                        // Bound the request, leave the response unbuffered and non-seekable.
                        var request=new StringBuilder(); int b;
                        while(request.Length<8192 && (b=stream.ReadByte())>=0){request.Append((char)b);if(request.ToString().EndsWith("\r\n\r\n"))break;}
                        if(!request.ToString().StartsWith("GET /"+token+".ts HTTP/"))continue;
                        byte[] header=Encoding.ASCII.GetBytes("HTTP/1.0 200 OK\r\nContent-Type: video/mp2t\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
                        stream.Write(header,0,header.Length);
                        byte[] buffer=new byte[188*256];
                        int[] counters=new int[8192]; for(int i=0;i<counters.Length;i++)counters[i]=-1;
                        foreach(var chunk in pending.GetConsumingEnumerable()) {
                            if(stopped)break;
                            using(var file=new FileStream(chunk.Path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete)) {
                                while(!stopped) {
                                    int count=0,n;while(count<buffer.Length && (n=file.Read(buffer,count,buffer.Length-count))>0)count+=n;
                                    if(count==0)break;
                                    if(count%188!=0)throw new IOException("Incomplete transport packet");
                                    for(int offset=0;offset<count;offset+=188) {
                                        if(buffer[offset]!=0x47)throw new IOException("Invalid transport sync");
                                        int pid=((buffer[offset+1]&31)<<8)|buffer[offset+2];
                                        bool payload=(buffer[offset+3]&16)!=0;
                                        // Each segment muxer restarts TS continuity counters. Stitch them
                                        // without touching payload, timestamps or encoded audio samples.
                                        if(counters[pid]<0)counters[pid]=0;
                                        else if(payload)counters[pid]=(counters[pid]+1)&15;
                                        buffer[offset+3]=(byte)((buffer[offset+3]&240)|counters[pid]);
                                    }
                                    stream.Write(buffer,0,count);
                                }
                            }
                        }
                        return;
                    }
                }
            } catch(Exception ex) {if(!stopped && Failed!=null)Failed(ex.Message);}
            finally {listener.Stop();}
        }
        public void Dispose(){stopped=true;Finish();listener.Stop();lock(sync){if(client!=null)client.Close();}}
    }
}
