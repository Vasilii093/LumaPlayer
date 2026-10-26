# Audio, source changes and cached ranges — 2026-10-25

## Reproduced causes

1. `cycle audio` included a disabled-audio state. The observed `aid` JSON boolean became the C# string `False`, which was reused by the next source and passed as `--aid=False` to the producer. The saved producer log confirms the option parser rejected it.
2. Source track IDs were inherited by cached playback, although the cache carries only one audio track. Selecting source track 2 or 3 must not require a nonexistent track with that ID in the cached stream.
3. The user's local Reacher S04E01 file reports incomplete MKV metadata, invalid EBML length, corrupted clusters, H.264 decoding errors, and an audio PTS jump from 1.280000 to 2776.800000. These reproduced with ordinary mpv decoding, without RIFE or cache. Changing folders itself is not the cause. Missing media data cannot be repaired by interpolation.
4. A real UI shutdown reproduced `NullReferenceException` in `StreamWriter.Flush -> Write -> Player.Send -> Shutdown`. The read loop disposed its writer outside the send lock. Disposal now takes the same lock and clears the published writer before releasing its buffers.

## Changes

- Explicit audio-track menu, no hidden disabled entry; source selection is separate from cached playback.
- New file loads use `aid=auto`; cached streams also select their own audio automatically; returning to source restores its selected track.
- Producer validates audio IDs at its input boundary. Boolean false is serialized as the valid mpv value `no`; malformed IDs fall back to auto. Transient observed booleans no longer change source selection.
- Corrupt-container messages and audio timestamp jumps over two seconds stop preparation before affected segments are published. Smaller audio timing corrections are accepted. Initial preparation failures return to original playback with an explanation and detailed diagnostic log.
- Timeline paints closed, retained cache ranges in teal. Played progress has a purple lower stripe so cached history stays visible. Separate ranges preserve holes; adjacent ranges merge; cancellation and file changes clear the display. In-progress segments are not shown as ready.
- UI exceptions are logged before displaying the standard exception dialog.

## Verification

`test-player-ui.cs`: original/720/900/1080 routing, exact click seek and endpoints, cached seeks without producer replacement, source/cache audio mapping, new-source reset, boolean/malformed audio arguments, damaged-source detection with tolerance for small timing corrections, merged/clamped cache ranges and actual painted pixels, clear-on-cancel, overlay geometry.

`test-shutdown.cs`: 50 repeated shutdown cases, broken pipe, pending callback suppression; a blocking stream deterministically holds a send operation while the read-loop cleanup attempts disposal. Disposal must wait until the write completes.

`test-player-audio-flow.cs`: runs the real Player form and its normal handlers with the bundled playback engine, actual Vulkan output, GPU RIFE 900p producer and WASAPI output. It invokes the application handlers in-process to make the file-change sequence repeatable; it does not use null audio/video sinks. Sequence:

1. Open Arcane, verify default audio ID 1; select ID 2.
2. Prepare at 1900 seconds; wait for at least two ready parts; verify cache audio ID 1, active WASAPI output, unmuted state and decoded audio parameters.
3. Seek inside cached data without replacing its producer; verify audio output.
4. Return to source and verify restored audio ID 2.
5. Open the damaged Reacher file from another folder and start preparation at zero; verify the corruption message and detached failed producer.
6. Reopen Arcane; verify default audio ID 1; prepare again and verify recovered audio output after another segment boundary.
7. Close the form normally.

The automated checks establish audio routing and device output, not a subjective listening test. The RIFE model, native GPU library and throughput settings were unchanged. Previous 20-minute performance evidence remains in `validation-rife900`; this regression test is intentionally shorter.

Final desktop check on 2026-10-26: launched the released executable, opened and cancelled the file dialog, then closed with Alt+F4. Player and mpv exited; the earlier shutdown-error log was unchanged. Final regression logs, the timeline pixel preview, original failure evidence and build hashes are saved in `validation-audio-cache`. The complete audio/file/cache flow passed twice; the last run includes the synchronized writer disposal and timestamp tolerance.
