using System;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Luma;

class Cache900Seek {
    static int Main(string[] args){
        string root=Path.GetFullPath(args[0]),sample=Path.GetFullPath(args[1]);
        string script=Path.GetFullPath("work/cache900-first-position.lua"),log=Path.GetFullPath("work/cache900-seek.log");
        File.WriteAllText(script,"mp.register_event('playback-restart', function() mp.msg.info('FIRST_POSITION=' .. tostring(mp.get_property_number('time-pos', -1))) end)");
        using(var stream=new CacheStream()){
            stream.Add(new PreparedChunk{Path=sample,Start=0,Length=15});stream.Finish();
            var psi=new ProcessStartInfo(Path.Combine(root,"engine/mpv.exe")){UseShellExecute=false,CreateNoWindow=true};
            psi.Arguments="--no-config --idle=no --keep-open=no --load-scripts=no --no-terminal --vo=null --ao=null --hwdec=auto-copy --untimed --force-seekable=yes --hr-seek=yes --start=6.4 --length=2 --demuxer-lavf-format=mpegts --msg-level=all=v "+
                PreRenderSession.Quote("--script="+script)+" "+PreRenderSession.Quote("--log-file="+log)+" "+PreRenderSession.Quote(stream.Url);
            using(var player=Process.Start(psi)){
                if(!player.WaitForExit(30000)){player.Kill();throw new Exception("Seek timeout");}
                if(player.ExitCode!=0)throw new Exception("Playback failed");
            }
        }
        string text=File.ReadAllText(log);
        var first=Regex.Match(text,@"FIRST_POSITION=([0-9.]+)");
        if(!first.Success||Math.Abs(Double.Parse(first.Groups[1].Value,CultureInfo.InvariantCulture)-6.4)>.05||
            text.Contains("Error decoding")||text.Contains("Could not find ref")||text.Contains("Packet corrupt")||!text.Contains("1600x900"))
            throw new Exception("900p cached seek regression failed; see "+log);
        Console.WriteLine("PASS: 900p cached seek starts at "+first.Groups[1].Value+" seconds without decoder corruption");
        return 0;
    }
}
