# 1080p throughput integration — 2026-10-26

Base: LumaPlayer-Equalizer.zip (icon and 8 equalizer presets retained).

## Change

PreRenderSession.FastHD selects the existing memory-optimized native backend for
900p and 1080p, with 3 GPU workers and 6 concurrent/buffered requests. The wrapper
uses 8 CPU workers and 1536 MiB VS cache at 1080p; 900p retains 1024 MiB.
720p is unchanged. Native DLLs, weights, inference arithmetic, scene/motion
protection, HEVC/AAC settings and 10 GiB disk quota are unchanged.
The existing pair mutex remains necessary for mutable Vulkan barrier state.
Per-source-frame GPU caching and asynchronous GPU submission are NOT implemented
in this integration. Full GPU decode/inference/encode interop is also not implemented.

## Sequential real-video comparison

RTX 5070 Ti Laptop, Arcane S01E07 1900–1930 s, source 1920x1080/24,
target 60, guard=0 (scene protection; exact duplicate bypass still available),
HEVC NVENC p4/CQ20, AAC, initialization included. Single runs; not statistical confidence.
No other player/GPU benchmark was running during these three completed measurements.

| Path | Seconds for 30 s | Media speed |
|---|---:|---:|
| Previous resident 1080p, 256 MiB | 55.8692 | 0.53697x |
| Memory path, 1024 MiB | 33.1273 | 0.90560x |
| Memory path, 1536 MiB (selected) | 32.4341 | 0.92495x |

Selected path is about 72.3% faster throughput / 41.9% less elapsed time.
The 2.1% elapsed improvement from 1024 to 1536 MiB alone is within possible run variance;
it is not a separately established general speedup.
An initial benchmark launcher blocked on undrained process output and was terminated.
It was excluded; all recorded runs redirect/drain output correctly.

All three encoded MP4s were byte-identical:
SHA256 D117B036C7E0F183ABB31D83F271A042CDB5307D44C0B05D0B6F4C85BA3677DD.
The encoder log confirms 1920x1080 output. This is a sample, not a universal quality proof.

## Tests

- Build passed with native and throughput flags, retaining embedded icon.
- Routing regression: 720p resident, 900p/1080p memory backend, correct queues,
  full processing height, unsupported experimental model rejected.
- Pixel regression: same memory DLL with old vs optimized transfer path,
  40 output frames at 318x178 and 15 at 1920x1080, 24000/1001 source FPS.
  Pixel hashes match, all values finite, including reverse/repeated and concurrent requests.
- UI regression: HD selection, exact seek, overlay, source/cache audio IDs,
  normalization, new-file reset and cached timeline drawing passed.

## Limits

Heavy guard=0 material remains below realtime. A larger initial buffer alone cannot
sustain indefinitely when production stays below 1x. Stable full-quality 1080p60
with display/audio load has not been established by this change.

## Actual producer/transport/decoder smoke test

90 seconds from source 1900s, 1080p60, guard=1, initial buffer30, actual producer
and CacheStream with concurrent hardware-decoded playback to null video/audio sinks.
Completed: 90.004667s, playback wall 90.5101923s, 6 chunks, 1 encoder start,
1 playback start, zero cache stalls, minimum active buffer16.483333s.
Producer active time80.059107s; speed1.124228x. No recorded producer/decoder error.
This protected-content result includes frame holds and is not directly comparable
with guard=0 measurements. It does not certify physical display cadence or audible glitches,
and 90 seconds cannot establish long-duration performance. It is below the intended 20–30% margin.
