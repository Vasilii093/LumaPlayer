# RIFE native GPU input reuse — 2026-10-24

## Build and scope

Run `LumaPlayer-RIFE-Native.exe` and select **60 FPS · RIFE 720p**. Prepare a new cache to evaluate the new implementation. The native filter is selected only for model 23 at processing height 720. Other modes and the original executable retain their existing implementation.

The new native plugin retains prepared source-pair tensors on the GPU in a bounded four-entry cache. Multiple generated frames reuse those inputs. Waiting for another calculation on the same pair occurs before taking a GPU worker slot, so waiting callbacks do not consume worker capacity. RIFE weights and processing resolution are unchanged.

Pair batching with one submission is implemented as `batch=1`, but is disabled because measured performance was worse. The player uses resident inputs (`batch=2`). Output still returns through VapourSynth host frames; this is not an end-to-end GPU-only decoder/inference/encoder pipeline. Experimental GPU normalization was reverted after numerical differences were detected.

## Validation

- Synthetic sequential, repeated, out-of-order and concurrent frame requests: resident cache versus this native backend without cache produced identical pixels and finite outputs.
- Real frames from Arcane S01E07 at 32:06: resident cache versus the same native backend without cache produced identical pixels. Visual inspection of the saved sample did not show the severe block artifacts seen with MEMC.
- The rebuilt native backend is not bit-identical to the previously shipped plugin. Across four inspected real generated frames, mean absolute differences were approximately 0.008–0.011 on an 8-bit scale; largest observed difference was 3.55 levels. This is a limited sample, not a general quality guarantee.
- Native mode selection test passed: only model 23 / height 720 selects the resident filter.
- UI regression test passed.
- Actual encoder buffer-control test passed: pauses at sufficient buffer, holds hysteresis, resumes without encoder restart.

## Preparation speed

Same 60-second source interval starting at 1900 seconds, 720p RIFE processing, target 60 FPS:

| Implementation | Elapsed | Preparation speed |
| --- | ---: | ---: |
| Existing shipped RIFE | 46.798 s | 1.282× |
| Final native resident-input build | 42.096 s | 1.425× |

This is approximately 10% less elapsed time (11% higher throughput), from single runs on the available laptop. It measures offline preparation, not display frame pacing or audio continuity. Longer playback and diverse scenes remain to be verified by the user.

## Rebuild

Native sources and build script are in `source/native-rife`. The script accepts the LLVM-MinGW toolchain and ncnn source directory. ncnn revision: `305837fd4a722ebc47c5d72e72d8ec9ae970e932`; glslang revision: `a9ac7d5f307e5db5b8c4fbf904bdba8fca6283bc`. Runtime DLLs and licenses are in `engine/gpu/rife-native`.

Build the player with `source/build.ps1 -OutputName LumaPlayer-RIFE-Native.exe -NativeRife`. Source tests include `test-rife-native.py`, `test-native-selection.cs`, and `test-buffer-control.cs`.
