# LumaPlayer source repository

This private source snapshot corresponds to LumaPlayer-1080p-FrameCache.zip,
including the glass icon, equalizer presets and the 1080p throughput integration.

Main entry points:
- source/LumaPlayer.cs: WinForms UI, playback, equalizer and cache control.
- source/PreRenderSession.cs: persistent mpv producer and cache transport.
- source/CacheMaintenance.cs: manifest and storage accounting.
- engine/gpu/*.vpy: VapourSynth interpolation graphs.
- source/native-rife/: resident input implementation used by 720p.
- source/native-rife-memory/: optimized transfer implementation used by 900p/1080p.
- source/HD1080-OPTIMIZATION.md: measurements and remaining limitations.

Run source/build.ps1 on Windows to compile the C# application. Playback also
requires the engine binaries and model weights from the existing portable package;
they are not included in this source repository. Native dependencies and pinned
revisions are documented beside their build scripts. See licenses/ for bundled notices.

The 1080p goal is not yet met on all content: the heavy comparison measured 0.925x.
The 90s guarded null-output test measured 1.124x without cache starvation.
These measurements do not establish sustained physical-display 1080p60.

Update 2026-10-27: per-source-frame GPU cache, overlapped CPU readback and file-local display sync. See source/FRAMECACHE-VALIDATION.md for tests and measured limits.
