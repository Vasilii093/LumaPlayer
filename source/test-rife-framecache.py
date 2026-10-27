"""Check source-frame GPU cache, padded strides, eviction and concurrent requests.

Run using the bundled Python after stopping other GPU tests/playback.
The reference uses the same model with the source-frame cache disabled (existing pair cache).
"""
from pathlib import Path
import hashlib
import json
import os
import numpy as np
import vapoursynth as vs

root = Path(__file__).resolve().parents[1]
native = root / "engine/gpu/rife-memory"
dll_path = os.add_dll_directory(str(native))
core = vs.core
core.num_threads = 8
core.max_cache_size = 1024
core.std.LoadPlugin(path=str(native / "librife-native.dll"))
model = str(root / "engine/gpu/models/rife-v4.6_ensembleFalse")
results = []

def digest(frame):
    result = hashlib.sha256()
    for plane in range(3):
        values = np.asarray(frame[plane])
        assert np.isfinite(values).all(), "Non-finite output"
        result.update(values.tobytes())
    return result.hexdigest()

def check(width, height, count):
    base = core.std.BlankClip(width=width, height=height, format=vs.RGBS,
                              length=count, fpsnum=24000, fpsden=1001)
    y, x = np.mgrid[:height, :width]
    def make(n, f):
        out = f.copy()
        for plane in range(3):
            values = .2 + .1 * np.sin((x - n * 3) * .07 + plane)
            values += .06 * np.cos(y * .1 + plane)
            values[(y > height // 4) & (y < height * 3 // 4) &
                   (x > width // 4 + n * 3) & (x < width // 2 + n * 3)] = .7
            np.copyto(np.asarray(out[plane]), values.astype(np.float32))
        return out
    clip = core.std.ModifyFrame(base, clips=base, selector=make)
    kwargs = dict(model=23, model_path=model, fps_num=60, fps_den=1,
                  gpu_thread=3, batch=2, tta=False, uhd=True, sc=False)
    os.environ["LUMA_RIFE_MEMORY"]="1"
    os.environ.pop("LUMA_RIFE_FRAMECACHE", None)
    reference = core.rifebatch.RIFE(clip, **kwargs)
    expected = [digest(reference.get_frame(n)) for n in range(reference.num_frames)]
    os.environ["LUMA_RIFE_FRAMECACHE"] = "1"
    candidate = core.rifebatch.RIFE(clip, **kwargs)
    order = list(range(candidate.num_frames - 1, -1, -1))
    order += [1, 4, 2, 3, candidate.num_frames - 1, 1]
    for n in order:
        assert digest(candidate.get_frame(n)) == expected[n], (width, height, n)
    # A fresh node prevents its output cache from satisfying the concurrency check.
    parallel = core.rifebatch.RIFE(clip, **kwargs)
    order = list(range(1, parallel.num_frames, 2)) + list(range(0, parallel.num_frames, 2))
    for start in range(0, len(order), 6):
        batch = [(n, parallel.get_frame_async(n)) for n in order[start:start + 6]]
        for n, future in batch:
            assert digest(future.result()) == expected[n], ("parallel", width, height, n)
    results.append(dict(width=width, height=height, frames=len(expected),
                        identical=True, finite=True, parallel=True))

check(318, 178, 40)  # neither the source stride nor the network padding is tight
check(1920, 1080, 16)
print(json.dumps(results, indent=2))
