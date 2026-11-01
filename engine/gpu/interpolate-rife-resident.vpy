"""Luma's streaming RIFE filter. All dependencies and weights are local."""
import os
import numpy as np
from fractions import Fraction
import vapoursynth as vs

core = vs.core
core.num_threads = 4
core.max_cache_size = 1024
here = os.path.dirname(os.path.abspath(__file__))
# mpv passes fps|processing-height|protection. 0 keeps source resolution.
settings = str(user_data).split("|")
target_fps = int(settings[0])
processing_height = int(settings[1]) if len(settings) > 1 else 0
# File controls the 1080p experimental preset in the prerender process.
if processing_height == 1080:
    profile_path = os.path.join(here, "io1080.profile")
    with open(profile_path, encoding="ascii") as profile:
        preset = profile.read().strip()
    for letter, option in (("O", "OUTPUT"), ("N", "UNIT"), ("I", "INPUT")):
        if letter in preset:
            os.environ["LUMA_IO_" + option] = "1"

# High-resolution profiles share direct staging to remove redundant host copies.
if processing_height in (1020, 1080):
    os.environ["LUMA_RIFE_MEMORY"] = "1"
    native_dir = os.path.join(here, "rife-memory")
else:
    native_dir = os.path.join(here, "rife-native")
_native_dll_path = os.add_dll_directory(native_dir)
if not hasattr(core, "rifebatch"):
    core.std.LoadPlugin(path=os.path.join(native_dir, "librife-native.dll"))
protection = int(settings[2]) if len(settings) > 2 else 0
model = int(settings[3]) if len(settings) > 3 else 23
gpu_threads = int(settings[4]) if len(settings) > 4 else 2
if gpu_threads not in (1, 2, 3, 4):
    raise ValueError("Invalid GPU concurrency")
if model not in (23, 70, 71):
    raise ValueError("Unsupported bundled RIFE model")
if target_fps not in (60, 72, 90, 120):
    raise ValueError("Luma: target FPS must be 60, 72, 90 or 120")

clip = video_in
rate = Fraction(float(container_fps)).limit_denominator(100000)
if rate <= 0 or rate > 1000:
    raise ValueError("Luma: cannot interpolate a source with unknown frame rate")
clip = core.std.AssumeFPS(clip, fpsnum=rate.numerator, fpsden=rate.denominator)

if float(rate) < target_fps:
    if processing_height and clip.height > processing_height:
        w = max(2, round(clip.width * processing_height / clip.height / 2) * 2)
        clip = core.resize.Bicubic(clip, width=w, height=processing_height)
    # Preserve frame colour metadata when present, use Rec.709 only as fallback.
    depth = clip.format.bits_per_sample
    if clip.format.color_family == vs.RGB:
        rgb = core.resize.Bicubic(clip, format=vs.RGBS)
    else:
        rgb = core.resize.Bicubic(clip, format=vs.RGBS, matrix_in_s="709")
    # Mark hard cuts, so RIFE repeats the source rather than inventing a transition.
    if protection != 2 or model in (23, 71):
        small = core.resize.Bilinear(rgb, width=64, height=36)
        next_small = small[1:] + small[-1]
        diffs = [core.std.PlaneStats(small, next_small, plane=p) for p in range(3)]
        # Coverage catches localized rapid motion and dark cuts that a global
        # mean misses. Conservative heuristic, not a confidence guarantee.
        difference = core.std.Expr([small, next_small], "x y - abs")
        changed = core.std.Expr(difference, "x 0.078431 > 1 0 ?")
        coverage = [core.std.PlaneStats(changed, plane=p) for p in range(3)]
        threshold = 12 / 255.0
        def mark_cut(n, f):
            result = f[0].copy()
            cut = protection != 2 and max(frame.props["PlaneStatsDiff"] for frame in f[1:4]) >= threshold
            risky = protection == 1 and max(frame.props["PlaneStatsAverage"] for frame in f[4:7]) >= 18 / 255.0
            # Never infer equality from the thumbnail alone: fine moving details
            # can disappear during reduction. Only scan full pixels on candidates.
            duplicate = model in (23,71) and all(frame.props['PlaneStatsDiff']==0 for frame in f[1:4]) and all(
                np.array_equal(np.asarray(f[0][p]),np.asarray(f[7][p])) for p in range(3))
            result.props["_SceneChangeNext"] = int(cut or risky or duplicate)
            return result
        rgb = core.std.ModifyFrame(rgb, clips=[rgb] + diffs + coverage + ([rgb[1:]+rgb[-1]] if model in (23,71) else []), selector=mark_cut)
    duplicate_rgb=rgb  # Pre-inference node, also used by regression tests.
    original_width, original_height = rgb.width, rgb.height
    if model == 71:
        # The half-size network needs multiples of 32; retain full-size RGB
        # for the final GPU warps and remove only the explicit padding later.
        rgb = core.std.AddBorders(rgb, right=(-rgb.width) % 64, bottom=(-rgb.height) % 64)
    rgb = core.rifebatch.RIFE(
        rgb, batch=2, model=23 if model == 71 else model, fps_num=target_fps, fps_den=1,
        gpu_thread=gpu_threads, tta=False, uhd=True, sc=(protection != 2 or model in (23, 71)),
        model_path=os.path.join(here, "models", "rife-v4.6-luma-hybrid" if model == 71 else "rife-v4.25-lite_ensembleFalse" if model == 70 else "rife-v4.6_ensembleFalse_residual" if processing_height in (720, 900, 1020, 1080) else "rife-v4.6_ensembleFalse"),
    )
    if model == 71:
        rgb = core.std.CropAbs(rgb, original_width, original_height)
    clip = core.resize.Bicubic(rgb, format=vs.YUV420P10 if depth > 8 else vs.YUV420P8, matrix_s="709")

clip.set_output()
