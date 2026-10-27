Update 2026-10-27: LUMA_RIFE_FRAMECACHE=1 enables a bounded per-source-frame GPU cache in batch=2. It is enabled by the 1080p wrapper by default; =0 retains the pair cache. Both modes release the GPU worker permit after fence completion, before copying private readback to the VS output. Per-command Vulkan metadata isolates access/stage tracking; source owners remain alive until all command references are gone. See ../FRAMECACHE-VALIDATION.md for measurements and limits. These changes are not a fully asynchronous submission queue.

Update 2026-10-26: the latest player also routes 1080p through this unchanged DLL (1536 MiB VS cache). Historical implementation notes follow.

# Native RIFE memory path

This snapshot builds the DLL in engine/gpu/rife-memory. The original backend in rife-native is unchanged.

Changes: direct normalization into Vulkan staging buffers, direct readback from mapped staging memory, per-plane vectorizable copies, optional timestamp profiling. Network weights and network arithmetic are unchanged when experimental environment flags are absent. Enable the memory path with LUMA_RIFE_MEMORY=1 before creating the RIFE instance. LUMA_PROFILE_PATH enables CSV timing output; it is normally unset.

Dependency: ncnn revision in ncnn-revision.txt, with the matching glslang revision documented in RIFE-NATIVE-VALIDATION.md. Apply the two files in ncnn-overrides to the src directory of a private build copy of that pinned dependency. They expose timestamp queries independently of per-layer benchmarking and add record_download_mapped with an explicit host-read barrier. The caller retains staging buffers through submission completion and invalidates mapped output before reading.

Run build-native.ps1 with -Toolchain, -NcnnSource and optionally -BuildDirectory. It uses this folder as the CMake source and verifies that the supplied private ncnn copy contains the exact command overrides. It does not alter that dependency or install the resulting DLL. Runtime dependencies and licenses are in engine/gpu/rife-memory.

The player uses this path only when built with -NativeRife -RifeThroughput, and only for the 900p profile. The corresponding VapourSynth wrapper reserves up to 1024 MiB for its cache and uses 8 CPU workers. The cache producer uses 3 GPU workers and 6 concurrent/buffered output frames. Disk cache remains limited to 10 GiB.

Run source/test-rife-memory.py with the bundled Python to compare mapped transfers against the same backend's original transfers, including fractional source FPS, padded strides, reversed/repeated requests and concurrent output requests. The default path does not enable experimental FP16 arithmetic, pack8 or Winograd changes.
