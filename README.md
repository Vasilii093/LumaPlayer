# LumaPlayer — 0.3 exp

This repository is the source snapshot of the portable **0.3 exp** build.
It is the baseline for subsequent work. The executable and installed runtime
remain in the local `LumaPlayer-0.3-exp` folder; generated caches, logs, video,
runtime binaries and model weights are not stored in this source repository.
`BASELINE-HASHES.json` identifies the baseline executable and key dependencies.

## Active implementation

- `source/LumaPlayer.cs`: Windows UI, playback and cache coordination.
- `source/PreRenderSession.cs`: mpv producer, cache limits and stream delivery.
- `source/CacheMaintenance.cs`: cache ownership and manifest handling.
- `engine/gpu/interpolate-rife-resident.vpy`: 720p and 1080p RIFE path.
  The 1080p VapourSynth frame-cache limit is 1024 MiB.
- `engine/gpu/interpolate-rife-throughput.vpy`: optimized 900p path.
- `source/native-rife/` and `source/native-rife-memory/`: matching native RIFE
  source and build instructions for the shipped 720p/1080p and 900p backends.

The producer pauses at 240 seconds of prepared video and resumes below 240
seconds, subject to the separate disk-space limit. GPU-direct Vulkan to NVENC,
the later per-source-frame GPU cache, and the fully asynchronous pipeline are
not included in 0.3 exp. Earlier experiments remain in Git history and local
research folders; do not treat them as part of this baseline.

## Build and validation

On Windows, `source/build.ps1` compiles the player in an existing portable
package containing the required runtime and model files. Native build scripts
specify the pinned ncnn revision and require a compatible compiler and
VapourSynth headers. They also refer to an `engine/runtime-trt` CMake installation,
which is not present in the portable 0.3 exp package; supply or adapt that build
dependency separately. This source-only repository is not a standalone portable
distribution. Preserve the separately installed 0.3 exp package and verify its
component hashes before using it as the build environment.

Do not publish a new version when the measured preparation speed is below 1×.
One fast short run does not establish stable 1080p60; validate on difficult
scenes with real playback, audio, cache, seeking and the physical display.

Previously unsuccessful FP16 arithmetic, TensorRT, lite/hybrid, MEMC and
batch=1 experiments should not be repeated without new concrete evidence.
