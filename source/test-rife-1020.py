"""Exercise the actual player vpy, including backend/model selection and resize."""
from pathlib import Path
import gc
import hashlib
import json
import os
import runpy
import sys
import types
import numpy as np
import vapoursynth as vs

root = Path(sys.argv[1]).resolve()
height = int(sys.argv[2])
output = Path(sys.argv[3])
script = root / 'engine/gpu' / ('interpolate-rife-throughput.vpy' if height == 900 else 'interpolate-rife-resident.vpy')
workers = 3 if height == 900 else 2
inflight = 6 if height == 900 else 4
core = vs.core
records = []


def digest(frame):
    h = hashlib.sha256()
    for plane in range(frame.format.num_planes):
        pixels = np.asarray(frame[plane])
        assert np.isfinite(pixels).all()
        h.update(pixels.tobytes())
    return h.hexdigest()


def check(w, h):
    states = []
    blank = core.std.BlankClip(width=w, height=h, format=vs.RGBS, length=6, fpsnum=24, fpsden=1)
    y, x = np.mgrid[:h, :w]
    def supply(n, f):
        result = f.copy()
        for p in range(3):
            values = .3 + .14*np.sin((x-n*5)*.037+p) + .1*np.cos(y*.021+n*.11)
            np.copyto(np.asarray(result[p]), values.astype(np.float32))
        return result
    source = core.std.ModifyFrame(blank, clips=blank, selector=supply)
    def graph():
        state = {
            '__file__': str(script),
            'video_in': source, 'container_fps': 24.0,
            'user_data': f'60|{height}|2|23|{workers}',
        }
        text = script.read_text(encoding='utf-8-sig')
        if len(sys.argv) > 4 and sys.argv[4] == 'original':
            text = text.replace('rife-v4.6_ensembleFalse_residual', 'rife-v4.6_ensembleFalse')
        exec(compile(text, str(script), 'exec'), state)
        node = state['clip']
        states.append(state)
        vs.clear_outputs()
        return node
    serial = graph()
    expected = [digest(serial.get_frame(n)) for n in range(serial.num_frames)]
    parallel = graph()
    order = list(range(parallel.num_frames-1, -1, -1)) + [1, 4, 2, 3, 1]
    for start in range(0, len(order), inflight):
        group = [(n, parallel.get_frame_async(n)) for n in order[start:start+inflight]]
        for n, future in group:
            assert digest(future.result()) == expected[n], (w,h,n,'concurrent mismatch')
    records.append(dict(source=[w,h], output=[serial.width,serial.height], frames=len(expected),
                        hashes=expected, parallel=True, finite=True))
    group = future = parallel = serial = source = blank = None
    # runpy leaves callbacks with their own module globals, including VideoNodes.
    # Release those graphs before Python interpreter finalization.
    scopes = []
    for state in states:
        for value in state.values():
            if isinstance(value, types.FunctionType) and value.__globals__.get('__file__') == str(script):
                scopes.append(value.__globals__)
    value = None
    for scope in scopes:
        scope.clear()
    for state in states:
        state.clear()
    scopes.clear()
    states.clear()
    gc.collect()


check(1920, 1080)  # Resize path used with the user's control video.
check(round(height * 16 / 9 / 2) * 2 - 2, height-2)  # Non-tight input/output stride.
output.write_text(json.dumps(records, indent=2), encoding='utf-8')
print(json.dumps([dict((k,v) for k,v in r.items() if k!='hashes') for r in records]))
gc.collect()
