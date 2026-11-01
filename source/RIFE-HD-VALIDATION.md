# RIFE HD validation — 2026-10-24

Build: LumaPlayer-RIFE-HD.exe (native RIFE enabled).

Added 900p and 1080p processing profiles for cache preparation. Both use the resident-input native RIFE backend, model 23. The original and tested 720p modes remain. Selecting HD profiles shows the original until Prepare cache is pressed. The producer receives the selected processing height; no additional CPU upscale is used. Native input cache and buffer pause/resume are enabled for all three heights. Continuous audio transport and retained-cache seeking are preserved.

Sequential clean benchmark: Arcane S01E07, source 1920x1080/24fps, interval 1900–1930 seconds, RIFE target60, native resident inputs, two GPU workers, four concurrent frames, NVENC output. The user closed their player before these measurements.

| Processing resolution | Time for 30 seconds | Speed |
|---|---:|---:|
| 1280x720 | 20.578 s | 1.458x |
| 1600x900 | 39.805 s | 0.754x |
| 1920x1080 | 55.701 s | 0.539x |

These are single offline preparation runs including initialization, with scene protection disabled for comparison. They are not sustained playback or GPU-only inference measurements. Earlier concurrent-load results were excluded. HD cannot be recommended for continuous preparation on this interval; initial buffering cannot compensate indefinitely for throughput below 1x.

UI tests passed: profile selection, native HD backend routing, no slow live inference for HD, existing seek/overlay/screen-fit tests, retained-cache forward/backward seeking. Saved output frames were decoded for inspection. Full-sequence artifact-free output is not established.

To test: choose RIFE 900p (cache) or RIFE 1080p (cache), then Prepare cache. Existing modes must exit cache playback using Ordinary mode before selecting a different profile. A new cache is necessary for a different resolution.
