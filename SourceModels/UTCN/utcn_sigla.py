"""Build the UTCN sigla as a 3D model, from the university's own logo file.

    blender -b --factory-startup -P blender/utcn_sigla.py -- --render

Writes utcn_sigla.blend, .glb and .fbx into models/utcn_sigla/ (and preview.png
with --render). The outlines are read straight from logo/utcn-logo-color.svg, so
the shapes are the official ones; only the depth is invented here.

Run with plain `python3 blender/utcn_sigla.py` it only parses the SVG and prints
what it would build -- a check that needs no Blender.
"""
import argparse
import math
import re
import sys
from pathlib import Path

try:
    import bpy
except ImportError:  # plain Python: parse and report only
    bpy = None

REPO = Path(__file__).resolve().parent.parent
SVG = REPO / "logo" / "utcn-logo-color.svg"
OUT = REPO / "models" / "utcn_sigla"

WIDTH_M = 2.0          # finished width of the whole sigla, metres
TEXT_TOP = 31.0        # SVG y below which everything is lettering
SMALL_TEXT_TOP = 51.0  # ...and below this, the small third line

# Depths in SVG units (the logo is 86 x 55.5 of them). Every part shares one back
# plane; `extrude` and `bevel` are half-depth and edge rounding.
PARTS = {
    #  name       material  extrude bevel  curve resolution
    "frame":      ("dark",   1.60,  0.22,  12),   # the shield outline and its two stems
    "panels":     ("red",    1.05,  0.18,  12),   # the red halves, set back inside the frame
    "text":       ("dark",   0.55,  0.05,  16),
    "small_text": ("dark",   0.35,  0.00,  24),   # too fine for a bevel; 24 so C / O read round, not as polygons
}
COLOURS = {"dark": "#231F20", "red": "#A9261E"}


# ---------------------------------------------------------------- SVG outlines

def parse_subpaths(d):
    """Split a path's `d` into closed subpaths of cubic segments.

    Each subpath is a list of (c1, c2, end) triples following a start point;
    straight lines become cubics with their handles on the line. Only the
    absolute commands the logo uses are understood.
    """
    if re.search(r"[a-zA-Z]", re.sub(r"[MLHVCZ]|e-?\d", "", d)):
        raise ValueError("the SVG uses path commands this script does not read")
    toks = re.findall(r"[MLHVCZ]|-?\d*\.?\d+(?:e-?\d+)?", d)
    subs, i, cmd, x, y = [], 0, None, 0.0, 0.0

    def line_to(nx, ny):
        seg = ((x + (nx - x) / 3, y + (ny - y) / 3), (x + 2 * (nx - x) / 3, y + 2 * (ny - y) / 3), (nx, ny))
        subs[-1]["segs"].append(seg)

    while i < len(toks):
        if toks[i] in "MLHVCZ":
            cmd = toks[i]
            i += 1
            if cmd == "Z":
                x, y = subs[-1]["start"]
            continue
        if cmd == "M":
            x, y = float(toks[i]), float(toks[i + 1])
            i += 2
            subs.append({"start": (x, y), "segs": []})
            cmd = "L"
        elif cmd == "L":
            nx, ny = float(toks[i]), float(toks[i + 1])
            i += 2
            line_to(nx, ny)
            x, y = nx, ny
        elif cmd == "H":
            nx = float(toks[i])
            i += 1
            line_to(nx, y)
            x = nx
        elif cmd == "V":
            ny = float(toks[i])
            i += 1
            line_to(x, ny)
            y = ny
        elif cmd == "C":
            p = [float(v) for v in toks[i:i + 6]]
            i += 6
            subs[-1]["segs"].append(((p[0], p[1]), (p[2], p[3]), (p[4], p[5])))
            x, y = p[4], p[5]
    return [s for s in subs if s["segs"]]


def bezier_points(sub):
    """A closed subpath as (left handle, point, right handle) per Bezier point."""
    start, segs = sub["start"], list(sub["segs"])
    if math.dist(segs[-1][2], start) > 1e-6:   # Z closed it with an implied line
        (lx, ly), (sx, sy) = segs[-1][2], start
        segs.append(((lx + (sx - lx) / 3, ly + (sy - ly) / 3), (lx + 2 * (sx - lx) / 3, ly + 2 * (sy - ly) / 3), start))
    pts = []
    for k, seg in enumerate(segs):
        co = start if k == 0 else segs[k - 1][2]
        left = segs[k - 1][1]                  # k == 0 wraps to the closing segment
        pts.append((left, co, seg[0]))
    return pts


def bbox(sub):
    xs = [sub["start"][0]] + [p[0] for seg in sub["segs"] for p in seg]
    ys = [sub["start"][1]] + [p[1] for seg in sub["segs"] for p in seg]
    return min(xs), min(ys), max(xs), max(ys)


def read_logo(svg_path):
    """The logo's outlines sorted into the parts to extrude, plus its overall box."""
    text = Path(svg_path).read_text(encoding="utf-8")
    by_fill = {}
    for attrs in re.findall(r"<path([^>]*)>", text):
        fill = re.search(r'fill="(#[0-9A-Fa-f]{6})"', attrs).group(1).upper()
        by_fill.setdefault(fill, []).extend(parse_subpaths(re.search(r'\sd="([^"]+)"', attrs).group(1)))
    dark, red = by_fill[COLOURS["dark"]], by_fill[COLOURS["red"]]

    # The file draws the two shield halves in black and then again in red on top.
    # In 3D that would be two solids in the same place, so the black copies go.
    def same(a, b):
        return all(abs(p - q) < 0.01 for p, q in zip(bbox(a), bbox(b)))
    covered = [s for s in dark if any(same(s, r) for r in red)]
    if len(covered) != len(red):
        raise ValueError("expected each red panel to cover one black shape; the logo file has changed")
    dark = [s for s in dark if s not in covered]

    parts = {"frame": [], "panels": red, "text": [], "small_text": []}
    for s in dark:
        mid = (bbox(s)[1] + bbox(s)[3]) / 2
        parts["frame" if mid < TEXT_TOP else "text" if mid < SMALL_TEXT_TOP else "small_text"].append(s)
    boxes = [bbox(s) for subs in parts.values() for s in subs]
    overall = (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))
    return parts, overall


# ---------------------------------------------------------------------- Blender

def linear(hex_colour):
    """sRGB hex -> the linear RGBA Blender's materials expect."""
    def chan(v):
        v /= 255
        return v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return tuple(chan(int(hex_colour[i:i + 2], 16)) for i in (1, 3, 5)) + (1.0,)


def make_material(name, hex_colour, roughness):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = linear(hex_colour)
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = linear(hex_colour)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = 0.0
    return mat


def part_mesh(name, subs, overall, extrude, bevel, resolution):
    """Extrude one part's outlines and return it as a mesh standing upright.

    Upright means: logo in the XZ plane, front facing -Y, back plane at y = 0,
    centred on the origin and scaled to WIDTH_M.
    """
    x0, y0, x1, y1 = overall
    cx, cy, scale = (x0 + x1) / 2, (y0 + y1) / 2, WIDTH_M / (x1 - x0)

    curve = bpy.data.curves.new(name, "CURVE")
    curve.dimensions = "2D"
    curve.fill_mode = "BOTH"
    curve.resolution_u = resolution
    curve.extrude = extrude * scale
    curve.bevel_depth = bevel * scale
    curve.bevel_resolution = 3 if bevel else 0
    for sub in subs:
        pts = bezier_points(sub)
        spline = curve.splines.new("BEZIER")
        spline.bezier_points.add(len(pts) - 1)
        spline.use_cyclic_u = True
        for bp, (left, co, right) in zip(spline.bezier_points, pts):
            # SVG y runs down the page; flip it so the logo is the right way up.
            bp.co = ((co[0] - cx) * scale, (cy - co[1]) * scale, 0.0)
            bp.handle_left = ((left[0] - cx) * scale, (cy - left[1]) * scale, 0.0)
            bp.handle_right = ((right[0] - cx) * scale, (cy - right[1]) * scale, 0.0)
            bp.handle_left_type = bp.handle_right_type = "FREE"

    obj = bpy.data.objects.new(name, curve)
    bpy.context.scene.collection.objects.link(obj)
    bpy.context.view_layer.update()
    mesh = bpy.data.meshes.new_from_object(obj.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(obj)
    bpy.data.curves.remove(curve)

    # Curve space (x right, y up, z = depth, centred on its own plane) -> upright,
    # with the back of every part on the same plane whatever its depth.
    half = (extrude + bevel) * scale
    for v in mesh.vertices:
        x, y, z = v.co
        v.co = (x, -(z + half), y)
    return mesh


def build():
    import bmesh

    parts, overall = read_logo(SVG)
    materials = {
        "dark": make_material("UTCN_Dark", COLOURS["dark"], 0.45),
        "red": make_material("UTCN_Red", COLOURS["red"], 0.35),
    }
    slots = list(materials)

    bm = bmesh.new()
    for name, subs in parts.items():
        mat, extrude, bevel, resolution = PARTS[name]
        piece = part_mesh(name, subs, overall, extrude, bevel, resolution)
        before = len(bm.faces)
        bm.from_mesh(piece)
        bm.faces.ensure_lookup_table()
        for face in bm.faces[before:]:
            face.material_index = slots.index(mat)
        bpy.data.meshes.remove(piece)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for face in bm.faces:
        face.smooth = True
    mesh = bpy.data.meshes.new("UTCN_Sigla")
    bm.to_mesh(mesh)
    bm.free()
    for key in slots:
        mesh.materials.append(materials[key])
    if hasattr(mesh, "set_sharp_from_angle"):       # round the curves, keep the corners crisp
        mesh.set_sharp_from_angle(angle=math.radians(35))

    sigla = bpy.data.objects.new("UTCN_Sigla", mesh)
    bpy.context.scene.collection.objects.link(sigla)
    return sigla


def add_stage(sigla):
    """Camera, lights and a white world, for the .blend and the preview only."""
    from mathutils import Vector

    scene = bpy.context.scene
    world = scene.world or bpy.data.worlds.new("World")
    scene.world = world
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs["Color"].default_value = (1, 1, 1, 1)

    height = sigla.dimensions.z
    target = Vector((0.0, 0.0, 0.0))
    # Three-quarter view: 30 degrees round, 15 degrees up -- how the display shows a model.
    yaw, pitch, dist = math.radians(30), math.radians(15), 5.2
    eye = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch))) * dist
    cam = bpy.data.objects.new("Camera", bpy.data.cameras.new("Camera"))
    cam.data.lens = 50
    cam.location = eye
    cam.rotation_euler = (target - eye).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam

    key = bpy.data.objects.new("Key", bpy.data.lights.new("Key", "SUN"))
    key.data.energy = 3.0
    key.data.angle = math.radians(8)
    key.rotation_euler = (math.radians(60), 0.0, math.radians(-40))
    scene.collection.objects.link(key)

    floor_mesh = bpy.data.meshes.new("Floor")
    s, z = 20.0, -height / 2
    floor_mesh.from_pydata([(-s, -s, z), (s, -s, z), (s, s, z), (-s, s, z)], [], [(0, 1, 2, 3)])
    floor = bpy.data.objects.new("Floor", floor_mesh)
    floor_mesh.materials.append(make_material("Floor", "#FFFFFF", 0.6))
    scene.collection.objects.link(floor)
    return [cam, key, floor]


def export(sigla, out_dir, render):
    out_dir.mkdir(parents=True, exist_ok=True)
    scene = bpy.context.scene
    for obj in scene.objects:
        obj.select_set(obj is sigla)
    bpy.context.view_layer.objects.active = sigla

    bpy.ops.export_scene.gltf(filepath=str(out_dir / "utcn_sigla.glb"), export_format="GLB",
                              use_selection=True, export_yup=True)
    bpy.ops.export_scene.fbx(filepath=str(out_dir / "utcn_sigla.fbx"), use_selection=True,
                             object_types={"MESH"}, mesh_smooth_type="FACE",
                             apply_scale_options="FBX_SCALE_ALL", bake_space_transform=True,
                             axis_forward="-Z", axis_up="Y")
    if render:
        scene.render.engine = "CYCLES"
        scene.cycles.device = "CPU"
        scene.cycles.samples = 96
        scene.view_settings.view_transform = "Standard"   # keep white white, as on the display
        scene.render.resolution_x, scene.render.resolution_y = 1600, 1000
        scene.render.filepath = str(out_dir / "preview.png")
        bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out_dir / "utcn_sigla.blend"))


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else ([] if bpy else sys.argv[1:])
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", type=Path, default=OUT)
    ap.add_argument("--render", action="store_true", help="also render preview.png (Cycles, CPU)")
    args = ap.parse_args(argv)

    if bpy is None:
        parts, overall = read_logo(SVG)
        print(f"{SVG.name}: {overall[2] - overall[0]:.1f} x {overall[3] - overall[1]:.1f} units")
        for name, subs in parts.items():
            print(f"  {name:<11}{len(subs):>3} outlines, {sum(len(bezier_points(s)) for s in subs):>4} points")
        print("Parsed only. Run this file inside Blender to build the model.")
        return

    for obj in list(bpy.data.objects):          # --factory-startup leaves a cube, a lamp, a camera
        bpy.data.objects.remove(obj)
    sigla = build()
    bpy.context.view_layer.update()
    add_stage(sigla)
    export(sigla, args.out, args.render)
    d = sigla.dimensions
    print(f"UTCN sigla: {len(sigla.data.vertices)} vertices, {len(sigla.data.polygons)} faces, "
          f"{d.x:.2f} x {d.z:.2f} x {d.y:.2f} m -> {args.out}")


if __name__ == "__main__":
    main()
