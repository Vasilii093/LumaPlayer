# RIFE 900p throughput investigation — 2026-10-25

Build: LumaPlayer-RIFE900.exe, using native RIFE4.6 with full 1600x900 processing for the 1080p test source. No lower-resolution motion model or nearest-frame replacement was introduced by these optimizations.

## Findings

A nearest-source-frame diagnostic control (no RIFE synthesis) completed the 10-second interval in 1.769 seconds (5.65x). This control was used only to estimate the surrounding pipeline's overhead, not as a player mode or an interpolation result.

Instrumented 900p RIFE, excluding the first 20 generated frames: mean GPU timestamps measured approximately 1.42 ms input preparation, 12.47 ms network, 0.04 ms postprocessing, and 1.43 ms download per generated frame. CPU-side input normalization and recording also took several milliseconds. CPU wait durations and GPU timings overlap across concurrent requests and must not be added as wall-clock totals. Short instrumented runs include startup and are not steady-state playback measurements.

Removing intermediate host copies preserves the existing arithmetic: normalized source planes are written directly into mapped upload staging memory; output is read directly from mapped download staging memory after completion and invalidation. Per-plane loops allow vectorization. Host-read barriers and staging lifetime are explicit. Four checked real-image-derived 1600x900 frames matched the same backend with the optimization disabled exactly (zero differing pixels; finite values).

The 256 MiB VapourSynth cache was also a significant bottleneck at 900p. With only that cache enlarged to 1024 MiB on top of the direct-memory path, elapsed time fell from 35.79 to 26.34 seconds for the same 30-second interval. Larger queues without the cache change had not helped materially.

## Sequential preparation benchmarks

Arcane S01E07, source 1920x1080/24fps, interval 1900–1930, target60, guard0, NVENC HEVC. Each row is a single run, including initialization.

| Variant | Seconds to prepare 30 s | Speed |
|---|---:|---:|
| Previous resident backend, earlier clean baseline | 39.805 | 0.754x |
| Direct staging, 256 MiB cache, 2 GPU workers | 35.790 | 0.838x |
| Direct staging, 256 MiB cache, 3 GPU workers | 35.898 | 0.836x |
| Direct staging, 1024 MiB cache, 2 GPU workers | 26.336 | 1.139x |
| Direct staging, 1024 MiB cache, 3 GPU workers | 25.595 | 1.172x |
| Above with 8 VapourSynth CPU workers | 25.425 | 1.180x |

Disabling Winograd did not improve speed (0.801x with the 256 MiB cache) and is not enabled. The prior FP16, lite-network, and hybrid experiments are also not enabled.

## Integration

Only the 900p profile uses interpolate-rife-throughput.vpy: 1024 MiB internal cache, 8 CPU workers, 3 GPU workers, 6 concurrent/buffered output requests. Original/720p/1080p profiles retain their existing routes. The disk-cache limit remains 10 GiB. Continuous audio transport and retained-cache seeking are preserved. Test-throughput-selection.cs passed.

Native sources and the pinned ncnn command overrides are in native-rife-memory/. The original shipped native DLL remains unchanged. Build the player using source/build.ps1 -OutputName LumaPlayer-RIFE900.exe -NativeRife -RifeThroughput.

## Playback acceptance

See the separately recorded soak results. Automated playback uses hardware decoding with null video/audio outputs and gain/limiter enabled. It tests producer/demux/decoder/filter continuity and cache starvation, not physical display cadence or listening quality. Defaults use scene/motion protection (guard1), unlike the guard0 comparative preparation benchmarks.

## Cache ownership

New sessions hold a session.lock file with write/delete sharing denied. Startup cleanup probes this lease before touching any segment; an active owner causes the directory to be skipped. An unlocked abandoned session can still be removed, and unknown files are retained. A separate-process regression test, test-cache-ownership.cs, passed. This prevents cleanup in another instance of the new build from destroying active cache; older executables do not know about the lease.

One earlier long test ended at about 210 seconds with EOF. Its IPC cleanup exception overwrote the first error, so the cause of that early EOF is not established. The test now preserves the first producer/stream error, writes final telemetry before asserting, and avoids flushing a StreamWriter after the IPC peer closes. The cache ownership issue was found during review, not proven to be the cause of that run.

Raw comparative benchmark results are copied to validation-rife900/. The native source snapshot and command overrides match the working source used to build the measured DLL.

## Completed 20-minute run

Arcane S01E07, source interval 900–2100 seconds, full 1600x900 RIFE output at 60fps, default scene/motion protection, initial buffer30 seconds. One resident producer and one continuous HTTP/MPEG-TS reader ran together, with hardware decoding and the audio gain/limiter enabled. Five pause/resume cycles released compute while retaining the loaded producer. Playback then drained the final prepared buffer.

- Requested1200 seconds; final position1200.004689; playback wall time1200.555733 seconds.
- 79 published chunks (the first chunk spanned about30 seconds, subsequent chunks about15 seconds).
- One encoder start, one playback start, one audio-output initialization, one video-output initialization.
- Zero observed paused-for-cache events after startup; no packet-corruption, missing-reference or decode-error log entries.
- Minimum ready buffer while the producer was active:19.60 seconds. Normal pause/resume control held the later buffer around60–120 seconds.
- Preparation active time819.076474 seconds, speed1.465070x, excluding intentional producer pauses. This guarded-content speed must not be compared directly with the guard0 benchmark table above.
- The remaining prepared video continued playing after preparation completed. Additional GPU pixel-regression checks ran only after the producer had finished, while the reader was draining this tail.

This is a headless transport/decoder/filter continuity test with null audio and video sinks. It does not measure physical display presentation or audible glitches. The long run used the final rendering/transport path before the independent cache-ownership lease was added; that lease change passed its own separate-process regression test. See validation-rife900/soak-20min.json and accompanying console/mpv logs.

## Additional final checks

- test-rife-memory.py passed: all40 frames at318x178 and15 frames at1600x900 matched the original transfer path exactly and were finite, at24000/1001 source FPS. Reversed/repeated requests and fresh-node concurrent requests also matched. These synthetic checks complement the four earlier real-image-derived pixel comparisons.
- test-cache900-seek.cs passed on an actual900p prepared segment through CacheStream. Requested6.4 seconds; playback restart at6.416667 seconds, with no decoding/reference errors. No re-interpolation or producer restart was involved.
- A frame decoded from the prepared segment around source32:06 was saved as validation-rife900/frame-1926.png and inspected. The severe block deformation in the earlier MEMC screenshot was not visible in this sample; a single frame cannot certify sequence-wide artifact freedom.
- Player compilation with NativeRife and RifeThroughput passed. Selection regression confirms only900p uses the optimized path, with720p/1080p retaining their prior implementations. Cache ownership regression passed.
