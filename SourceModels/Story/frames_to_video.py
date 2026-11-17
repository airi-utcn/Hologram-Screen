"""Makes an MP4 for HLD Author from the JPEG frames that the Unity menu HLD -> Record ... spot writes.

    blender -b --factory-startup -P frames_to_video.py -- <frames folder> <output.mp4> [--top <px>] [--white <level>]

2160 x 3840 portrait, 30 fps, H.264 at high quality, Standard view transform (colours as the app renders them), except:
- --top: that many pixels at the top become white (the edge of the alcove's ceiling band; 26 px with StageShare 0.71),
- --black: lifted blacks back down (a level that becomes black; the stretch also brings the contrast back),
- --white: the app's near-white background (0.98-0.99 measured) and anything lighter become pure white, which is
  invisible on the HLD; any grey shows there as a dull wall. Default 0.97.
"""
import os
import sys

import bpy

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT = os.path.abspath(args[0]), os.path.abspath(args[1])   # Blender reads relative paths against the .blend, not the shell
TOP = int(args[args.index("--top") + 1]) if "--top" in args else 0
WHITE = float(args[args.index("--white") + 1]) if "--white" in args else 0.97
# --black: this level and anything darker become black (frames recorded under the alcove's lifted shadows: 0.2).
BLACK = float(args[args.index("--black") + 1]) if "--black" in args else 0.0
files = sorted(f for f in os.listdir(SRC) if f.lower().endswith((".jpg", ".jpeg", ".png")))
if "--frames" in args:                   # keep only the first N (drops a tail recorded after the spot ended)
    files = files[:int(args[args.index("--frames") + 1])]
assert files, f"no frames in {SRC}"

sc = bpy.context.scene
sc.render.resolution_x, sc.render.resolution_y, sc.render.resolution_percentage = 2160, 3840, 100
sc.render.fps = 30
sc.view_settings.view_transform = "Standard"
sc.view_settings.look = "None"
ed = sc.sequence_editor_create()
strips = ed.strips if hasattr(ed, "strips") else ed.sequences

white = strips.new_effect("White", "COLOR", 1, frame_start=1, length=len(files))
white.color = (1.0, 1.0, 1.0)

seq = strips.new_image("Frames", os.path.join(SRC, files[0]), 2, 1, fit_method="ORIGINAL")
for f in files[1:]:
    seq.elements.append(f)
seq.blend_type = "ALPHA_OVER"
seq.crop.max_y = TOP                     # cut from the top; the white strip shows through

curves = seq.modifiers.new("White point", "CURVES")
c = curves.curve_mapping.curves[3]       # the combined curve (the modifier ignores curve_mapping.white_level)
c.points[1].location = (1.0, 1.0)
c.points.new(WHITE, 1.0)
if BLACK > 0.0:
    c.points.new(BLACK, 0.0)
for p in c.points:
    p.handle_type = "VECTOR"             # straight segments: a smooth curve bows up and washes out the mid-tones
curves.curve_mapping.update()
sc.frame_start, sc.frame_end = 1, len(files)

if "--still" in args:                    # one graded frame, to check
    sc.render.image_settings.file_format = "PNG"
    sc.frame_set(int(args[args.index("--still") + 1]))
    sc.render.filepath = OUT
    bpy.ops.render.render(write_still=True)
    print("WROTE still", OUT)
    raise SystemExit

if hasattr(sc.render.image_settings, "media_type"):
    sc.render.image_settings.media_type = "VIDEO"   # Blender 5: pick video output before the FFmpeg format
sc.render.image_settings.file_format = "FFMPEG"
sc.render.ffmpeg.format = "MPEG4"
sc.render.ffmpeg.codec = "H264"
sc.render.ffmpeg.constant_rate_factor = "HIGH"
sc.render.ffmpeg.ffmpeg_preset = "GOOD"
sc.render.filepath = OUT
bpy.ops.render.render(animation=True)
print("WROTE", OUT, len(files), "frames")
