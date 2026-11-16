"""Turns a landscape screen recording of the hologram app into a portrait video ready for HLD Author.

    blender -b --factory-startup -P hld_video.py -- <input.mp4> <output.mp4> [white_level] [--start <frame>] [--frames <folder>]

- crops the recording to the app's picture (CROP_X0..CROP_X1 of the source width; the rest is black bars),
- puts it on a 1080 x 1920 portrait canvas (9:16, what HLD Author works in) on pure white: white is invisible on the
  HLD, any grey shows as a dull wall,
- grades it: the app's light grey background (white_level, measured) becomes pure white, a gentle S-curve deepens
  the shadows and the models' darks, saturation goes up a little so the UTCN red and the greens read,
- keeps the sound, and writes H.264 MP4 (HLD Author's General / BrightSign input). --frames writes a few graded
  stills instead, to check the grade.
"""
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = args[0], args[1]
WHITE = float(args[2]) if len(args) > 2 and not args[2].startswith("--") else 0.97    # app background measured at 0.992; a little lower so it lands on pure white
FRAMES = args[args.index("--frames") + 1] if "--frames" in args else None
# First frame to keep: a recording that opens on black would show a black box on the HLD (Video Project 1: frame 82).
START = int(args[args.index("--start") + 1]) if "--start" in args else 1
CROP_X0, CROP_X1, SRC_W, SRC_H = 352, 1568, 1920, 1080
W, H = 1080, 1920

sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = W, H, 100
sc.render.fps = 30
sc.view_settings.view_transform = "Standard"     # no filmic look: the video's colours go through unchanged
sc.view_settings.look = "None"
ed = sc.sequence_editor_create()
strips = ed.strips if hasattr(ed, "strips") else ed.sequences

white = strips.new_effect("White", "COLOR", 1, frame_start=1, length=1)
white.color = (1.0, 1.0, 1.0)

movie = strips.new_movie("Recording", SRC, 2, 1, fit_method="ORIGINAL")
length = movie.frame_final_duration
white.frame_final_end = 1 + length
sc.frame_start, sc.frame_end = START, length      # sound and picture share the timeline, so they stay in sync
# The picture (CROP_X0..CROP_X1 of the source) fills the canvas width; the black bars fall off the canvas.
# shortcut: Blender 5 showed this 1920x1080 clip at 0.5 x 0.7 of its size at scale 1 (measured: the 1216 x 1080 picture
# came out 608 x 756), so the scales below are measured, not derived; re-measure for a recording of another size.
SHOWN_W, SHOWN_H = 608, 756
EDGE = 1.03   # 3% larger, evenly: the window's grey edge line and the mouse cursor fall off the canvas sides
movie.transform.scale_x = W / SHOWN_W * EDGE
movie.transform.scale_y = (W * SRC_H / (CROP_X1 - CROP_X0)) / SHOWN_H * EDGE
movie.crop.min_y, movie.crop.max_y = 162, 170     # the recording's own bars above and below the picture, and its edge line
movie.blend_type = "ALPHA_OVER"

# Grade: white point at the app's background grey, a gentle S-curve for depth, a little more colour.
curves = movie.modifiers.new("Levels", "CURVES")
cm = curves.curve_mapping
c = cm.curves[3]                                   # the combined curve (the modifier ignores cm.white_level)
c.points[0].location = (0.0, 0.0)
c.points[1].location = (1.0, 1.0)
c.points.new(WHITE, 1.0)                           # white point: the app's background and anything lighter -> pure white
c.points.new(0.25 * WHITE, 0.20)                   # darker darks: shadows and model shading read stronger
c.points.new(0.75 * WHITE, 0.78)
cm.update()
movie.color_saturation = 1.2

try:
    sound = strips.new_sound("Sound", SRC, 3, 1)
except Exception as e:                             # a recording without sound is fine
    print("no sound:", e)

if FRAMES:
    sc.render.image_settings.file_format = "PNG"
    for f in (300, 1200, 2200):
        sc.frame_set(f)
        sc.render.filepath = f"{FRAMES}/graded_{f:04d}.png"
        bpy.ops.render.render(write_still=True)
else:
    if hasattr(sc.render.image_settings, "media_type"):
        sc.render.image_settings.media_type = "VIDEO"   # Blender 5: pick video output before the FFmpeg format
    sc.render.image_settings.file_format = "FFMPEG"
    sc.render.ffmpeg.format = "MPEG4"
    sc.render.ffmpeg.codec = "H264"
    sc.render.ffmpeg.constant_rate_factor = "HIGH"
    sc.render.ffmpeg.ffmpeg_preset = "GOOD"
    sc.render.ffmpeg.audio_codec = "AAC"
    sc.render.ffmpeg.audio_bitrate = 192
    sc.render.filepath = OUT
    bpy.ops.render.render(animation=True)
    print("WROTE", OUT, length, "frames")
