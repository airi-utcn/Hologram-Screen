"""3D assets for the AIRI x UTCN story (StoryDirector.cs): extruded headline text, the pedestal and the rings.

    blender -b --factory-startup -P make_story_assets.py -- <output folder>

Every headline is one FBX whose letters are separate child objects (L00, L01, ... in reading order, each with
its origin in its middle), so Unity can animate them one by one. Letter faces are UTCN red, sides and bevels
graphite. Materials are named Graphite / UTCNRed; the Unity .meta files map them onto UTCN_Dark / UTCN_Red.
Same export as utcn_sigla.py: Y up, axes baked, front of everything toward the viewer.
"""
import math
import sys
from pathlib import Path

import bmesh
import bpy

OUT = Path(sys.argv[sys.argv.index("--") + 1])
# Liberation Sans (SIL Open Font License), in the project. Not Inter: it is a variable font whose letters are
# overlapping pieces, and thickening them left the bar of the A thinner than the rest of the letter.
FONT = Path(__file__).resolve().parents[2] / "Assets" / "TextMesh Pro" / "Fonts" / "LiberationSans.ttf"

PHRASES = {
    # The AIRI x UTCN spot
    "Phrase_Spark": "EVERY REVOLUTION\nSTARTS WITH A SPARK.",
    "Phrase_Learns": "IT LEARNS.",
    "Phrase_Connects": "IT CONNECTS\nTHE WORLD.",
    "Phrase_Heart": "BUT IT NEEDS\nA HUMAN HEART.",
    "Phrase_Meet": "AT UTCN,\nTHEY MEET.",
    "Phrase_Welcome": "WELCOME\nTO AIRI.",
    "Phrase_Institute": "THE AI RESEARCH\nINSTITUTE AT UTCN.",
    # its departments and centres (airi.utcluj.ro/research/departments)
    "Dept_Micro": "MICROELECTRONICS.",
    "Dept_Robotics": "ROBOTICS & IOT.",
    "Dept_Autonomous": "AUTONOMOUS\nSYSTEMS.",
    "Dept_Cyber": "CYBERSECURITY\n& SPACE.",
    "Dept_Health": "ROBOTICS FOR\nHEALTHCARE.",
    "Dept_Trust": "TRUSTWORTHY AI.",
    "Dept_Measure": "AI MEASUREMENT.",
    "Dept_Immersive": "IMMERSIVE\nEDUCATION.",
    "Dept_Labelling": "DATA LABELLING.",
    "Dept_HPC": "HPC-AI.",
    "Phrase_Future": "THE FUTURE\nSTARTS HERE.",
    "Phrase_JoinUs": "JOIN US.",
    # The health spot
    "Phrase_Health": "AI FOR\nHEALTH.",
    "Phrase_Sees": "IT SEES WHAT\nWE MISS.",
    "Phrase_Sight": "EARLY DETECTION.\nSAVED SIGHT.",
    "Phrase_AIRIxUTCN": "AIRI \u00d7 UTCN",
}


def material(name, rgb, roughness, metallic=0.0):
    mat = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.diffuse_color = (*rgb, 1.0)
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
    bsdf.inputs["Roughness"].default_value = roughness
    bsdf.inputs["Metallic"].default_value = metallic
    return mat


GRAPHITE = material("Graphite", (0.016, 0.014, 0.014), 0.35, 0.3)
RED = material("UTCNRed", (0.397, 0.019, 0.013), 0.3)
# Headlines: faces in the UTCN logo's grey, sides darker. Unity maps them to UTCN_Dark / AIRI_Body.
TEXT_FACE = material("TextFace", (0.017, 0.014, 0.014), 0.35)
TEXT_SIDE = material("TextSide", (0.004, 0.004, 0.004), 0.3, 0.3)
# The AIRI building and its campus. Unity maps them to Concrete / AIRIGlass (dark teal glass) / Leaf.
CONCRETE = material("Concrete", (0.18, 0.18, 0.19), 0.6)
GLASS = material("Glass", (0.02, 0.04, 0.05), 0.08, 0.4)
LEAF = material("Leaf", (0.013, 0.07, 0.02), 0.7)
FINS = material("Fins", (0.42, 0.42, 0.43), 0.5)          # the building's light fins and slab edges
SOLAR = material("Solar", (0.01, 0.02, 0.06), 0.15, 0.5)  # roof solar panels


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)


def export(name):
    bpy.ops.object.select_all(action="DESELECT") if bpy.ops.object.select_all.poll() else None
    for ob in bpy.context.view_layer.objects:
        ob.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUT / f"{name}.fbx"), use_selection=True,
                             object_types={"EMPTY", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                             bake_space_transform=True, axis_forward="-Z", axis_up="Y", add_leaf_bones=False)
    print(f"  {name}.fbx")


def link(ob):
    bpy.context.scene.collection.objects.link(ob)
    return ob


# ---------------------------------------------------------------- text

def text_mesh(body):
    """The whole headline as one mesh, upright: letters in the XZ plane, front faces toward -Y."""
    font = bpy.data.fonts.load(str(FONT), check_existing=True)
    curve = bpy.data.curves.new("text", "FONT")
    curve.body = body
    curve.font = font
    curve.size = 1.0
    curve.align_x = "CENTER"
    curve.align_y = "CENTER"
    curve.space_line = 1.05
    curve.space_character = 1.1   # air between the capitals (the thickening narrows the gaps)
    curve.offset = 0.022        # Regular thickened into a bold headline; clean on this font's single-contour letters
    curve.extrude = 0.09
    curve.bevel_depth = 0.01
    curve.bevel_resolution = 3
    curve.resolution_u = 14
    ob = link(bpy.data.objects.new("text", curve))
    bpy.context.view_layer.update()
    mesh = bpy.data.meshes.new_from_object(ob.evaluated_get(bpy.context.evaluated_depsgraph_get()))
    bpy.data.objects.remove(ob)
    bpy.data.curves.remove(curve)
    for v in mesh.vertices:
        x, y, z = v.co
        v.co = (x, -z, y)
    return mesh


def split_letters(mesh):
    """Connected parts = letters (all headlines are capitals, so no letter is in two pieces)."""
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.verts.ensure_lookup_table()
    parent = list(range(len(bm.verts)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for e in bm.edges:
        a, b = root(e.verts[0].index), root(e.verts[1].index)
        if a != b:
            parent[a] = b
    groups = {}
    for f in bm.faces:
        groups.setdefault(root(f.verts[0].index), []).append(f)

    letters = []
    for faces in groups.values():
        part = bmesh.new()
        index = {}
        for f in faces:
            vs = []
            for v in f.verts:
                if v.index not in index:
                    index[v.index] = part.verts.new(v.co)
                vs.append(index[v.index])
            nf = part.faces.new(vs)
            nf.smooth = True
            nf.material_index = 1 if f.normal.y < -0.9 else 0   # front faces grey, the rest darker
        letters.append(part)
    bm.free()
    return letters


def make_phrase(name, body):
    clear()
    root = link(bpy.data.objects.new(name, None))
    parts = []
    for part in split_letters(text_mesh(body)):
        xs = [v.co.x for v in part.verts]
        zs = [v.co.z for v in part.verts]
        mid = ((min(xs) + max(xs)) / 2, 0.0, (min(zs) + max(zs)) / 2)
        bmesh.ops.translate(part, verts=part.verts, vec=(-mid[0], 0.0, -mid[2]))
        parts.append((mid, part))
    # Reading order: lines from the top, then left to right (a line is about 1 unit tall).
    parts.sort(key=lambda p: (-round(p[0][2]), p[0][0]))
    for i, (mid, part) in enumerate(parts):
        mesh = bpy.data.meshes.new(f"L{i:02d}")
        part.to_mesh(mesh)
        part.free()
        mesh.materials.append(TEXT_SIDE)
        mesh.materials.append(TEXT_FACE)
        if hasattr(mesh, "set_sharp_from_angle"):
            mesh.set_sharp_from_angle(angle=math.radians(35))
        ob = link(bpy.data.objects.new(f"L{i:02d}", mesh))
        ob.location = mid
        ob.parent = root
    export(name)
    return len(parts)


# ---------------------------------------------------------------- set pieces

def torus(name, major, minor, segments=160, sides=14, mat=RED):
    """Lies flat (in the XY plane of Blender = the floor after export)."""
    bm = bmesh.new()
    rings = []
    for i in range(segments):
        a = 2 * math.pi * i / segments
        ring = []
        for j in range(sides):
            b = 2 * math.pi * j / sides
            r = major + minor * math.cos(b)
            ring.append(bm.verts.new((r * math.cos(a), r * math.sin(a), minor * math.sin(b))))
        rings.append(ring)
    for i in range(segments):
        for j in range(sides):
            f = bm.faces.new((rings[i][j], rings[(i + 1) % segments][j],
                              rings[(i + 1) % segments][(j + 1) % sides], rings[i][(j + 1) % sides]))
            f.smooth = True
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat)
    return link(bpy.data.objects.new(name, mesh))


def pedestal():
    """A low graphite drum (radius 1, 0.14 high, top at z = 0) with a red ring set into its top."""
    clear()
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=128, radius1=1.0, radius2=0.96, depth=0.14)
    bmesh.ops.translate(bm, verts=bm.verts, vec=(0, 0, -0.07))
    rims = [e for e in bm.edges if e.is_boundary is False and len(e.link_faces) == 2
            and abs(e.link_faces[0].normal.z) > 0.9 and abs(e.link_faces[1].normal.z) < 0.5]
    bmesh.ops.bevel(bm, geom=rims, offset=0.025, segments=4, profile=0.5, affect="EDGES")
    for f in bm.faces:
        f.smooth = True
    mesh = bpy.data.meshes.new("Pedestal")
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(GRAPHITE)
    if hasattr(mesh, "set_sharp_from_angle"):
        mesh.set_sharp_from_angle(angle=math.radians(40))
    root = link(bpy.data.objects.new("Pedestal", mesh))
    inlay = torus("Inlay", 0.86, 0.018)
    inlay.location = (0, 0, 0.0)
    inlay.parent = root
    export("Pedestal")


def ring():
    clear()
    torus("Ring", 1.0, 0.012)
    export("Ring")


# ---------------------------------------------------------------- the AIRI Lab and its campus

def box(name, size, center, mat, parent=None, bevel=0.0):
    """An axis-aligned box: size (x, y depth, z height), centre in Blender space (front is -Y)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bmesh.ops.translate(bm, vec=center, verts=bm.verts)
    if bevel:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=2, affect="EDGES")
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat)
    ob = link(bpy.data.objects.new(name, mesh))
    if parent:
        ob.parent = parent
    return ob


def cylinder(name, radius, height, base, mat, parent=None, segments=24, radius_top=None):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                          radius2=radius if radius_top is None else radius_top, depth=height)
    bmesh.ops.translate(bm, vec=(base[0], base[1], base[2] + height / 2), verts=bm.verts)
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    mesh.materials.append(mat)
    ob = link(bpy.data.objects.new(name, mesh))
    if parent:
        ob.parent = parent
    return ob


def sign(name, body, height, center, parent):
    """Standing 3D lettering in one piece (grey face, dark sides)."""
    mesh = text_mesh(body)
    xs = [v.co.x for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    k = height / (max(zs) - min(zs))
    mx, mz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    for v in mesh.vertices:
        v.co = ((v.co.x - mx) * k + center[0], v.co.y * k + center[1], (v.co.z - mz) * k + center[2])
    for poly in mesh.polygons:
        poly.material_index = 1 if poly.normal.y < -0.9 else 0
        poly.use_smooth = True
    mesh.materials.append(TEXT_SIDE)
    mesh.materials.append(TEXT_FACE)
    ob = link(bpy.data.objects.new(name, mesh))
    ob.parent = parent
    return ob


def lab():
    """The AIRI building at UTCN, after the architects' render: a long seven-storey block wrapped in dense light vertical
    fins, its front curving inward in the middle (it bends around the old house in front of it), a glazed ground floor
    set back behind the fins, and on the roof solar panels, a green terrace and a recessed plant floor.
    Every storey is its own object, F0..F6, and the roof is "Roof", so Unity can assemble the building floor by floor;
    the entrance is "Door". Front toward -Y, base at z = 0, about 3.2 wide."""
    from mathutils import Matrix, Vector

    clear()
    root = link(bpy.data.objects.new("AIRI_Lab", None))
    W, D, H, floors = 3.2, 0.95, 0.2, 7
    mats = [FINS, GLASS, CONCRETE, SOLAR, LEAF, GRAPHITE]
    FIN, GLS, CON, SOL, GRN, DRK = range(6)

    def front_y(x):  # the front curves in, in the middle, around the old house
        return -D / 2 + 0.3 * math.exp(-((x - 0.15) / 0.42) ** 2)

    n = 64
    outline = [(-W / 2 + W * i / n, front_y(-W / 2 + W * i / n)) for i in range(n + 1)] + [(W / 2, D / 2), (-W / 2, D / 2)]

    def offset(poly, d):
        """Moves every point of a counter-clockwise outline inward by d."""
        out = []
        for i, (x, y) in enumerate(poly):
            px, py = poly[i - 1]
            nx, ny = poly[(i + 1) % len(poly)]
            a = Vector((-(y - py), x - px)).normalized() if (x, y) != (px, py) else Vector((0, 0))
            b = Vector((-(ny - y), nx - x)).normalized() if (x, y) != (nx, ny) else Vector((0, 0))
            m = (a + b).normalized()
            out.append((x + m.x * d, y + m.y * d))
        return out

    def prism(bm, poly, z0, z1, mat):
        bottom = [bm.verts.new((x, y, z0)) for x, y in poly]
        top = [bm.verts.new((x, y, z1)) for x, y in poly]
        for f in (bm.faces.new(top), bm.faces.new(list(reversed(bottom)))):
            f.material_index = mat
        for i in range(len(poly)):
            j = (i + 1) % len(poly)
            bm.faces.new((bottom[i], bottom[j], top[j], top[i])).material_index = mat

    def block(bm, center, x_axis, y_axis, size, mat, tilt=0.0):
        """A box with its sides along x_axis / y_axis (on the ground plane), tilted about x_axis by `tilt` radians."""
        xa, ya = Vector((*x_axis, 0)).normalized(), Vector((*y_axis, 0)).normalized()
        za = Vector((0, 0, 1))
        rot = Matrix.Rotation(tilt, 3, xa)
        ya, za = rot @ ya, rot @ za
        m = Matrix((xa * size[0], ya * size[1], za * size[2])).transposed().to_4x4()
        m.translation = Vector(center)
        res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
        for v in res["verts"]:
            for f in v.link_faces:
                f.material_index = mat

    def fins_along(bm, pts, step, z0, z1, depth):
        """Vertical fins every `step` along a polyline, standing out `depth` from it, square to it."""
        total, acc = 0.0, step / 2
        for i in range(len(pts) - 1):
            a, b = Vector(pts[i]), Vector(pts[i + 1])
            seg = (b - a).length
            t = (b - a) / seg
            nrm = Vector((t.y, -t.x))  # outward for a counter-clockwise outline
            while acc <= total + seg:
                p = a + t * (acc - total)
                block(bm, (p.x + nrm.x * depth / 2, p.y + nrm.y * depth / 2, (z0 + z1) / 2), t, nrm, (0.011, depth, z1 - z0), FIN)
                acc += step
            total += seg

    def finish(name, bm):
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        mesh = bpy.data.meshes.new(name)
        bm.to_mesh(mesh)
        bm.free()
        for m in mats:
            mesh.materials.append(m)
        ob = link(bpy.data.objects.new(name, mesh))
        ob.parent = root
        return ob

    front = outline[: n + 1]
    sides = [(W / 2, front_y(W / 2)), (W / 2, D / 2), (-W / 2, D / 2), (-W / 2, front_y(-W / 2))]
    for f in range(floors):
        bm = bmesh.new()
        z0 = f * H
        prism(bm, outline, z0, z0 + 0.022, FIN)                       # the light slab edge
        if f == 0:                                                     # ground floor: glass, set back
            prism(bm, offset(outline, 0.07), z0 + 0.022, z0 + H, GLS)
        else:
            prism(bm, offset(outline, 0.035), z0 + 0.022, z0 + H, GLS)
            fins_along(bm, front, 0.05, z0 + 0.022, z0 + H, 0.045)
            fins_along(bm, sides, 0.07, z0 + 0.022, z0 + H, 0.045)
        finish(f"F{f}", bm)

    bm = bmesh.new()
    top = floors * H
    prism(bm, outline, top, top + 0.03, FIN)                           # roof slab
    prism(bm, offset(outline, 0.12), top + 0.03, top + 0.045, CON)     # roof deck
    for r in range(3):                                                 # solar arrays, left part of the roof
        for c in range(7):
            block(bm, (-1.35 + c * 0.17, -0.18 + r * 0.17, top + 0.075), (1, 0), (0, 1), (0.15, 0.11, 0.01), SOL, math.radians(-22))
    block(bm, (0.1, 0.05, top + 0.05), (1, 0), (0, 1), (0.62, 0.42, 0.012), GRN)         # green terrace
    for x, y, sx, sy in ((0.1, -0.165, 0.62, 0.025), (0.1, 0.265, 0.62, 0.025), (-0.21, 0.05, 0.025, 0.42), (0.41, 0.05, 0.025, 0.42)):
        block(bm, (x, y, top + 0.07), (1, 0), (0, 1), (sx, sy, 0.04), GRN)               # hedges around it
    block(bm, (1.05, 0.12, top + 0.13), (1, 0), (0, 1), (0.8, 0.5, 0.17), GLS)          # recessed plant floor
    block(bm, (1.05, 0.12, top + 0.225), (1, 0), (0, 1), (0.84, 0.54, 0.02), FIN)
    pen = [(0.65, -0.13), (1.45, -0.13), (1.45, 0.37), (0.65, 0.37), (0.65, -0.13)]
    fins_along(bm, pen, 0.06, top + 0.045, top + 0.215, 0.03)
    for c in range(4):
        block(bm, (0.8 + c * 0.17, 0.12, top + 0.25), (1, 0), (0, 1), (0.15, 0.3, 0.01), SOL, math.radians(-22))
    finish("Roof", bm)

    bm = bmesh.new()
    block(bm, (0.15, front_y(0.15) + 0.065, 0.1), (1, 0), (0, 1), (0.32, 0.02, 0.16), DRK)
    finish("Door", bm)
    export("AIRI_Lab")


def tree():
    """A stylised tree: graphite trunk, three stacked green cones. Base at z = 0, about 0.8 tall."""
    clear()
    root = link(bpy.data.objects.new("Tree", None))
    cylinder("Trunk", 0.03, 0.22, (0, 0, 0), GRAPHITE, root, 10)
    for i, (r, z, h) in enumerate(((0.22, 0.16, 0.32), (0.17, 0.33, 0.28), (0.11, 0.5, 0.26))):
        cylinder(f"Leaves{i}", r, h, (0, 0, z), LEAF, root, 9, 0.0)
    export("Tree")


def lamp():
    """A street lamp: graphite pole and arm, red head. Base at z = 0, about 0.6 tall."""
    clear()
    root = link(bpy.data.objects.new("Lamp", None))
    cylinder("Pole", 0.012, 0.58, (0, 0, 0), GRAPHITE, root, 10)
    box("Arm", (0.12, 0.02, 0.02), (0.05, 0, 0.58), GRAPHITE, root)
    box("Head", (0.07, 0.05, 0.03), (0.1, 0, 0.56), RED, root, 0.005)
    export("Lamp")


def plaza():
    """The campus ground, after the real one: a graphite rim, a green lawn, a concrete path to the entrance and the
    curved concrete wall that sweeps across the lawn. Radius 1, top at z = 0."""
    clear()
    root = link(bpy.data.objects.new("Plaza", None))
    cylinder("Ground", 1.0, 0.05, (0, 0, -0.05), GRAPHITE, root, 96)
    cylinder("Lawn", 0.96, 0.01, (0, 0, -0.004), LEAF, root, 96)
    box("Path", (0.3, 1.0, 0.012), (0, -0.5, 0.004), CONCRETE, root)
    for i in range(18):  # the sweeping wall: short segments along an arc on the right of the path
        a0, a1 = math.radians(-80 + i * 5), math.radians(-80 + (i + 1) * 5)
        r = 0.7
        x0, y0, x1, y1 = r * math.cos(a0), -0.15 + r * math.sin(a0), r * math.cos(a1), -0.15 + r * math.sin(a1)
        length = math.hypot(x1 - x0, y1 - y0)
        seg = box(f"Wall{i}", (length + 0.004, 0.025, 0.05 + 0.04 * math.sin(math.pi * i / 17)), (0, 0, 0), CONCRETE, root)
        seg.location = ((x0 + x1) / 2, (y0 + y1) / 2, 0.035)
        seg.rotation_euler = (0, 0, math.atan2(y1 - y0, x1 - x0))
    export("Plaza")


# ---------------------------------------------------------------- the research areas

def part(name, parent, build, origin=(0, 0, 0)):
    """An object whose geometry is made by build(bm) around the world origin, then placed at `origin` (its pivot)."""
    from mathutils import Vector
    bm = bmesh.new()
    build(bm)
    bmesh.ops.translate(bm, vec=-Vector(origin), verts=bm.verts)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    mesh = bpy.data.meshes.new(name)
    bm.to_mesh(mesh)
    bm.free()
    for m in (GRAPHITE, RED, CONCRETE, LEAF, GLASS, FINS):
        mesh.materials.append(m)
    ob = link(bpy.data.objects.new(name, mesh))
    ob.location = origin
    ob.parent = parent   # always the file's root: the FBX exporter's axis baking breaks chains of placed meshes
    return ob


G, R, C, L, GL, F = range(6)   # material slots of part(): graphite, red, concrete, green, glass, light metal


def cube(bm, size, center, mat, bevel=0.0):
    from mathutils import Matrix
    m = Matrix.Diagonal((*size, 1.0))
    m.translation = center
    res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
    if bevel:
        edges = list({e for v in res["verts"] for e in v.link_edges})
        res = {"verts": list({v for e in edges for v in e.verts} | set(v for v in res["verts"] if v.is_valid))}
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mat


def tube(bm, radius, depth, center, mat, axis="Z", segments=24, radius_top=None):
    from mathutils import Matrix
    rot = {"Z": Matrix.Identity(3), "X": Matrix.Rotation(math.pi / 2, 3, "Y"), "Y": Matrix.Rotation(math.pi / 2, 3, "X")}[axis].to_4x4()
    rot.translation = center
    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=segments, radius1=radius,
                                radius2=radius if radius_top is None else radius_top, depth=depth, matrix=rot)
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mat


def ball(bm, radius, center, mat):
    from mathutils import Matrix
    m = Matrix.Translation(center)
    res = bmesh.ops.create_uvsphere(bm, u_segments=16, v_segments=10, radius=radius, matrix=m)
    for v in res["verts"]:
        for f in v.link_faces:
            f.material_index = mat
            f.smooth = True


def flat_text(body, height, center, mat):
    """Lettering lying flat (printed on a board), reading toward the front."""
    mesh = text_mesh(body)
    xs = [v.co.x for v in mesh.vertices]
    zs = [v.co.z for v in mesh.vertices]
    k = height / (max(zs) - min(zs))
    mx, mz = (min(xs) + max(xs)) / 2, (min(zs) + max(zs)) / 2
    for v in mesh.vertices:
        x, y, z = (v.co.x - mx) * k, v.co.y * k, (v.co.z - mz) * k
        v.co = (x + center[0], z + center[1], center[2] - y * 0.15)
    mesh.materials.append(mat)
    return mesh


def chip():
    """An AI board, close to a real one: a dark-green PCB with mounting holes, the main processor under a finned
    heatsink, four memory chips, an AI accelerator with a red die, capacitors and inductors round the processor,
    gold edge contacts, two ports, a pin header, a red status LED, AIRI@UTCN printed on it, and copper traces in
    buses between the parts (each its own object, Trace<nn>, for signals). Lies flat, about 1.6 x 1.0."""
    clear()
    root = link(bpy.data.objects.new("Chip", None))
    W, D, T = 1.6, 1.0, 0.03

    def board(bm):
        cube(bm, (W, D, T), (0, 0, T / 2), L, 0.01)
        for x, y in ((-0.74, -0.44), (0.74, -0.44), (0.74, 0.44), (-0.74, 0.44)):   # mounting holes (metal rings)
            tube(bm, 0.03, 0.004, (x, y, T + 0.002), F, segments=20)
            tube(bm, 0.016, 0.006, (x, y, T + 0.003), G, segments=16)
        # main processor + heatsink with fins
        cube(bm, (0.36, 0.36, 0.03), (-0.3, 0.06, T + 0.015), G, 0.006)
        cube(bm, (0.32, 0.32, 0.02), (-0.3, 0.06, T + 0.04), F, 0.004)
        for i in range(9):
            cube(bm, (0.32, 0.012, 0.11), (-0.3, 0.06 - 0.14 + i * 0.035, T + 0.1), F)
        # memory chips
        for i in range(4):
            cube(bm, (0.09, 0.13, 0.014), (0.02 + (i % 2) * 0.11, 0.2 - (i // 2) * 0.16, T + 0.007), G, 0.003)
        # AI accelerator with a red die
        cube(bm, (0.22, 0.22, 0.025), (0.42, -0.18, T + 0.0125), G, 0.005)
        cube(bm, (0.11, 0.11, 0.008), (0.42, -0.18, T + 0.029), R, 0.002)
        # capacitors and inductors round the processor
        for i in range(8):
            a = i / 8 * 2 * math.pi
            tube(bm, 0.022, 0.05, (-0.3 + 0.27 * math.cos(a), 0.06 + 0.27 * math.sin(a), T + 0.025), F, segments=14)
            tube(bm, 0.023, 0.006, (-0.3 + 0.27 * math.cos(a), 0.06 + 0.27 * math.sin(a), T + 0.053), G, segments=14)
        for i in range(4):
            cube(bm, (0.05, 0.05, 0.035), (-0.62 + i * 0.07, -0.25, T + 0.0175), G, 0.006)
        # gold edge contacts along the front edge
        for i in range(22):
            cube(bm, (0.018, 0.06, 0.003), (-0.4 + i * 0.038, -D / 2 + 0.03, T + 0.0015), F)
        # ports at the back edge, pin header at the right edge
        cube(bm, (0.14, 0.1, 0.07), (-0.6, D / 2 - 0.05, T + 0.035), F, 0.005)
        cube(bm, (0.16, 0.12, 0.08), (-0.4, D / 2 - 0.06, T + 0.04), F, 0.005)
        cube(bm, (0.05, 0.4, 0.025), (W / 2 - 0.06, 0.12, T + 0.0125), G)
        for i in range(10):
            for j in range(2):
                cube(bm, (0.008, 0.008, 0.05), (W / 2 - 0.075 + j * 0.03, -0.06 + i * 0.04, T + 0.045), F)
        cube(bm, (0.025, 0.015, 0.012), (0.66, -0.4, T + 0.006), R)                      # status LED
    part("Board", root, board)
    print_ = flat_text("AIRI@UTCN", 0.045, (0.42, 0.02, T + 0.002), FINS)
    link(bpy.data.objects.new("Silkscreen", print_)).parent = root
    # Buses of traces: processor -> memory, processor -> accelerator, processor -> edge contacts.
    n = 0

    def trace(x0, y0, x1, y1):
        nonlocal n
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        sx, sy = max(abs(x1 - x0), 0.008), max(abs(y1 - y0), 0.008)
        part(f"Trace{n:02d}", root, lambda bm: cube(bm, (sx, sy, 0.003), (cx, cy, T + 0.0015), F))
        n += 1
    for i in range(5):
        trace(-0.12, 0.0 + i * 0.03, -0.035, 0.0 + i * 0.03)       # to the memory
    for i in range(5):
        trace(-0.22 + i * 0.03, -0.12, -0.22 + i * 0.03, -0.42)    # down to the edge contacts
    for i in range(5):
        trace(-0.12, -0.04 - i * 0.025, 0.31, -0.04 - i * 0.025)   # across to the accelerator
    for i in range(5):
        trace(0.53, -0.12 - i * 0.025, 0.7, -0.12 - i * 0.025)     # accelerator to the header side
    export("Chip")


def robot_arm():
    """An industrial robot arm in UTCN red on a graphite base. Joints are separate objects with their pivots at the
    joint: Base, Turret (turns about the vertical), UpperArm (shoulder), Forearm (elbow), Wrist, FingerL / FingerR,
    plus the Crate it lifts. All sit under the file's root; Unity chains them (Turret > UpperArm > ...) when it plays.
    Reaches toward -Y (the viewer). Base at z = 0, about 1.1 tall."""
    clear()
    root = link(bpy.data.objects.new("RobotArm", None))
    base = part("Base", root, lambda bm: (tube(bm, 0.24, 0.08, (0, 0, 0.04), G, segments=40), tube(bm, 0.2, 0.04, (0, 0, 0.1), C, segments=40)))
    turret = part("Turret", root, lambda bm: (tube(bm, 0.15, 0.14, (0, 0, 0.19), R, segments=32),
                                               cube(bm, (0.2, 0.16, 0.12), (0, 0, 0.3), R, 0.02),
                                               tube(bm, 0.07, 0.24, (0, 0, 0.33), G, axis="X")), (0, 0, 0.12))
    upper = part("UpperArm", root, lambda bm: (cube(bm, (0.1, 0.1, 0.46), (0, 0, 0.56), R, 0.02),
                                                  tube(bm, 0.06, 0.18, (0, 0, 0.79), G, axis="X")), (0, 0, 0.33))
    fore = part("Forearm", root, lambda bm: (cube(bm, (0.08, 0.4, 0.08), (0, -0.2, 0.79), R, 0.018),
                                               tube(bm, 0.045, 0.12, (0, -0.42, 0.79), G, axis="X")), (0, 0, 0.79))
    wrist = part("Wrist", root, lambda bm: (tube(bm, 0.04, 0.08, (0, -0.42, 0.72), G),
                                             cube(bm, (0.12, 0.05, 0.03), (0, -0.42, 0.665), C, 0.006)), (0, -0.42, 0.79))
    part("FingerL", root, lambda bm: cube(bm, (0.02, 0.04, 0.09), (-0.04, -0.42, 0.61), C, 0.005), (-0.04, -0.42, 0.65))
    part("FingerR", root, lambda bm: cube(bm, (0.02, 0.04, 0.09), (0.04, -0.42, 0.61), C, 0.005), (0.04, -0.42, 0.65))
    part("Crate", root, lambda bm: cube(bm, (0.1, 0.1, 0.1), (0, -0.42, 0.05), G, 0.01))   # what it picks up
    export("RobotArm")


def drone():
    """A quadcopter: graphite body with a red stripe, four arms, motors and two-blade rotors (Rotor0..3, pivots on the
    motor axes, so they can spin), landing legs and a camera ball underneath."""
    clear()
    root = link(bpy.data.objects.new("Drone", None))

    def body(bm):
        cube(bm, (0.26, 0.34, 0.09), (0, 0, 0.2), G, 0.03)
        cube(bm, (0.27, 0.06, 0.02), (0, -0.05, 0.25), R, 0.005)
        ball(bm, 0.05, (0, -0.12, 0.13), GL)
        for sx in (-1, 1):
            cube(bm, (0.012, 0.2, 0.012), (sx * 0.1, 0, 0.08), G)
            cube(bm, (0.012, 0.012, 0.1), (sx * 0.1, 0.07, 0.13), G)
            cube(bm, (0.012, 0.012, 0.1), (sx * 0.1, -0.07, 0.13), G)
        for i, (x, y) in enumerate(((-0.3, -0.3), (0.3, -0.3), (0.3, 0.3), (-0.3, 0.3))):
            arm_len = math.hypot(x, y)
            ang = math.atan2(y, x)
            from mathutils import Matrix
            m = Matrix.Rotation(ang, 4, "Z") @ Matrix.Diagonal((arm_len, 0.035, 0.03, 1.0))
            m.translation = (x / 2, y / 2, 0.22)
            res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = G
            tube(bm, 0.04, 0.06, (x, y, 0.25), R)
    part("Body", root, body)
    for i, (x, y) in enumerate(((-0.3, -0.3), (0.3, -0.3), (0.3, 0.3), (-0.3, 0.3))):
        def rotor(bm, x=x, y=y, i=i):
            from mathutils import Matrix
            for blade in (0, 1):
                m = Matrix.Rotation(math.radians(30 + i * 20 + blade * 180), 4, "Z") @ Matrix.Translation((0.075, 0, 0)) @ Matrix.Diagonal((0.15, 0.025, 0.006, 1.0))
                m = Matrix.Translation((x, y, 0.285)) @ m
                res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
                for v in res["verts"]:
                    for f in v.link_faces:
                        f.material_index = C
            tube(bm, 0.015, 0.02, (x, y, 0.285), G)
        part(f"Rotor{i}", root, rotor, (x, y, 0.285))
    export("Drone")


def car():
    """An autonomous car in UTCN red: rounded body, dark glass cabin, wheels (Wheel0..3, pivots on their axles, so they
    can turn) and a lidar on the roof (Lidar, can spin). Front toward -Y, on the ground, about 1.2 long."""
    clear()
    root = link(bpy.data.objects.new("Car", None))

    def body(bm):
        cube(bm, (0.5, 1.2, 0.2), (0, 0, 0.2), R, 0.06)
        cube(bm, (0.44, 0.62, 0.17), (0, 0.06, 0.37), GL, 0.05)
        cube(bm, (0.08, 0.02, 0.03), (-0.17, -0.6, 0.23), F, 0.008)
        cube(bm, (0.08, 0.02, 0.03), (0.17, -0.6, 0.23), F, 0.008)
        cube(bm, (0.36, 0.02, 0.04), (0, -0.6, 0.15), G, 0.01)
        tube(bm, 0.06, 0.03, (0, 0.06, 0.47), G)
    part("Body", root, body)
    part("Lidar", root, lambda bm: (tube(bm, 0.045, 0.06, (0, 0.06, 0.515), G, segments=24), cube(bm, (0.05, 0.02, 0.02), (0, 0.02, 0.52), R)), (0, 0.06, 0.515))
    for i, (x, y) in enumerate(((-0.26, -0.37), (0.26, -0.37), (-0.26, 0.37), (0.26, 0.37))):
        part(f"Wheel{i}", root, lambda bm, x=x, y=y: (tube(bm, 0.11, 0.08, (x, y, 0.11), G, axis="X", segments=28),
                                                       tube(bm, 0.06, 0.085, (x, y, 0.11), F, axis="X", segments=6)), (x, y, 0.11))
    export("Car")


def dna():
    """A DNA double helix: two strands of beads (red and graphite) joined by light rungs, about 1.3 tall."""
    clear()
    root = link(bpy.data.objects.new("DNA", None))

    def helix(bm):
        from mathutils import Vector, Matrix
        pairs, height, radius, turns = 22, 1.3, 0.22, 2.0
        for i in range(pairs):
            t = i / (pairs - 1)
            a = t * turns * 2 * math.pi
            z = 0.05 + t * height
            p = Vector((math.cos(a) * radius, math.sin(a) * radius, z))
            q = Vector((-p.x, -p.y, z))
            ball(bm, 0.045, p, R)
            ball(bm, 0.045, q, G)
            d = q - p
            m = Matrix.Translation((p + q) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.013, radius2=0.013, depth=d.length * 0.8, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = C
            if i < pairs - 1:  # the backbones between neighbouring beads
                for sign, mat in ((1, R), (-1, G)):
                    t2 = (i + 1) / (pairs - 1)
                    a2 = t2 * turns * 2 * math.pi
                    p2 = Vector((sign * math.cos(a2) * radius, sign * math.sin(a2) * radius, 0.05 + t2 * height))
                    p1 = Vector((sign * math.cos(a) * radius, sign * math.sin(a) * radius, z))
                    e = p2 - p1
                    m = Matrix.Translation((p1 + p2) / 2) @ e.to_track_quat("Z", "Y").to_matrix().to_4x4()
                    res = bmesh.ops.create_cone(bm, cap_ends=True, segments=8, radius1=0.016, radius2=0.016, depth=e.length, matrix=m)
                    for v in res["verts"]:
                        for f in v.link_faces:
                            f.material_index = mat
    part("Helix", root, helix)
    export("DNA")


def padlock():
    """Cybersecurity: a graphite padlock with a red shackle and a keyhole. Stands upright, front toward -Y."""
    clear()
    root = link(bpy.data.objects.new("Padlock", None))

    def lock(bm):
        cube(bm, (0.5, 0.2, 0.42), (0, 0, 0.21), G, 0.05)
        tube(bm, 0.035, 0.03, (0, -0.105, 0.24), F, axis="Y", segments=20)
        cube(bm, (0.03, 0.03, 0.08), (0, -0.105, 0.17), F)
    part("Body", root, lock)

    def shackle(bm):
        from mathutils import Matrix
        segs = 20
        for i in range(segs):
            a0, a1 = math.pi * i / segs, math.pi * (i + 1) / segs
            p0 = (0.17 * math.cos(a0), 0, 0.55 + 0.17 * math.sin(a0))
            p1 = (0.17 * math.cos(a1), 0, 0.55 + 0.17 * math.sin(a1))
            from mathutils import Vector
            v0, v1 = Vector(p0), Vector(p1)
            d = v1 - v0
            m = Matrix.Translation((v0 + v1) / 2) @ d.to_track_quat("Z", "Y").to_matrix().to_4x4()
            res = bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.04, radius2=0.04, depth=d.length * 1.15, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = R
        tube(bm, 0.04, 0.16, (-0.17, 0, 0.47), R, segments=12)
        tube(bm, 0.04, 0.16, (0.17, 0, 0.47), R, segments=12)
    part("Shackle", root, shackle)
    export("Padlock")


def satellite():
    """Space: a satellite with a graphite body, two navy solar wings and a dish. About 1 wide."""
    clear()
    root = link(bpy.data.objects.new("Satellite", None))

    def sat(bm):
        cube(bm, (0.22, 0.22, 0.28), (0, 0, 0), G, 0.02)
        cube(bm, (0.23, 0.05, 0.05), (0, -0.115, 0.08), R)
        for sx in (-1, 1):
            cube(bm, (0.12, 0.012, 0.012), (sx * 0.17, 0, 0), F)
            for k in range(3):
                cube(bm, (0.12, 0.2, 0.008), (sx * (0.28 + k * 0.125), 0, 0), GL)
        tube(bm, 0.11, 0.04, (0, 0, 0.2), F, radius_top=0.03)
        tube(bm, 0.006, 0.12, (0, 0, 0.24), G)
    part("Body", root, sat)
    export("Satellite")


def scale():
    """Trustworthy AI: a balance. A graphite column on a base; the beam and its two pans hang from the top (Beam, with
    its pivot at the top of the column, so it can tip and settle level)."""
    clear()
    root = link(bpy.data.objects.new("Scale", None))
    part("Stand", root, lambda bm: (tube(bm, 0.22, 0.05, (0, 0, 0.025), G, segments=40),
                                     tube(bm, 0.03, 0.85, (0, 0, 0.475), F, segments=16),   # tube() takes the middle
                                     ball(bm, 0.05, (0, 0, 0.92), R)))

    def beam(bm):
        cube(bm, (0.9, 0.03, 0.03), (0, 0, 0.9), G, 0.008)
        for sx in (-1, 1):
            for dy in (-0.05, 0.05):
                cube(bm, (0.006, 0.006, 0.3), (sx * 0.42, dy, 0.75), F)
            tube(bm, 0.14, 0.02, (sx * 0.42, 0, 0.59), R if sx < 0 else G, segments=32, radius_top=0.16)
        ball(bm, 0.05, (-0.42, 0, 0.64), F)     # on one pan: a small sphere (the data)...
        cube(bm, (0.07, 0.07, 0.07), (0.42, 0, 0.64), F, 0.01)   # ...on the other a cube (the model): in balance
    part("Beam", root, beam, (0, 0, 0.9))
    export("Scale")


def gauge():
    """AI measurement: a round dial gauge (graphite rim, light face, tick marks, red needle with its pivot at the
    centre: Needle) on a stand, beside five bars of a chart (Bar0..4, pivots at their base, so they can grow)."""
    clear()
    root = link(bpy.data.objects.new("Gauge", None))

    def dial(bm):
        tube(bm, 0.32, 0.06, (-0.25, 0, 0.62), G, axis="Y", segments=48)
        tube(bm, 0.28, 0.065, (-0.25, 0, 0.62), F, axis="Y", segments=48)
        for i in range(11):
            a = math.radians(-135 + 27 * i)
            x, z = -0.25 + 0.23 * math.sin(a), 0.62 + 0.23 * math.cos(a)
            from mathutils import Matrix
            m = Matrix.Translation((x, -0.035, z)) @ Matrix.Rotation(-a, 4, "Y") @ Matrix.Diagonal((0.012, 0.004, 0.05, 1.0))
            res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = R if i >= 8 else G
        tube(bm, 0.03, 0.26, (-0.25, 0.02, 0.17), G)   # stand post from the foot up to the dial
        cube(bm, (0.3, 0.2, 0.04), (-0.25, 0.02, 0.02), G, 0.01)
    part("Dial", root, dial)
    part("Needle", root, lambda bm: (cube(bm, (0.012, 0.006, 0.2), (-0.25, -0.04, 0.62 + 0.09), R),
                                      tube(bm, 0.025, 0.012, (-0.25, -0.042, 0.62), G, axis="Y")), (-0.25, -0.04, 0.62))
    for i, h in enumerate((0.25, 0.4, 0.33, 0.55, 0.7)):
        part(f"Bar{i}", root, lambda bm, i=i, h=h: cube(bm, (0.075, 0.075, h), (0.17 + i * 0.1, 0, h / 2), R if i == 4 else G, 0.006), (0.17 + i * 0.1, 0, 0))
    export("Gauge")


def vr_headset():
    """Immersive education: a VR headset (dark glass visor, graphite body, red strap) and three virtual windows
    (Panel0..2) to float round it."""
    clear()
    root = link(bpy.data.objects.new("VR", None))

    def headset(bm):
        cube(bm, (0.5, 0.26, 0.28), (0, 0, 0.0), G, 0.07)
        cube(bm, (0.46, 0.04, 0.24), (0, -0.13, 0.0), GL, 0.05)
        cube(bm, (0.14, 0.08, 0.05), (0, 0.13, -0.06), G, 0.02)
        segs = 24
        from mathutils import Matrix, Vector
        for i in range(segs):   # the strap: a flat band round the back
            a0, a1 = math.pi * i / segs, math.pi * (i + 1) / segs
            p0 = Vector((0.26 * math.cos(a0), 0.1 + 0.3 * math.sin(a0), 0.0))
            p1 = Vector((0.26 * math.cos(a1), 0.1 + 0.3 * math.sin(a1), 0.0))
            d = p1 - p0
            m = Matrix.Translation((p0 + p1) / 2) @ Matrix.Rotation(math.atan2(d.y, d.x), 4, "Z") @ Matrix.Diagonal((d.length * 1.1, 0.015, 0.06, 1.0))
            res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
            for v in res["verts"]:
                for f in v.link_faces:
                    f.material_index = R
    part("Headset", root, headset)
    for i, (x, z, ang) in enumerate(((-0.62, 0.18, 25), (0.62, 0.22, -25), (0.0, 0.5, 0))):
        def panel(bm, x=x, z=z, ang=ang):
            from mathutils import Matrix
            for (sx, sz, dx, dz, mat) in ((0.36, 0.24, 0, 0, GL), (0.37, 0.01, 0, 0.12, R), (0.37, 0.01, 0, -0.12, F),
                                          (0.01, 0.24, -0.18, 0, F), (0.01, 0.24, 0.18, 0, F), (0.2, 0.012, -0.06, 0.06, F), (0.26, 0.012, -0.03, 0.02, F)):
                m = Matrix.Translation((x, 0, z)) @ Matrix.Rotation(math.radians(ang), 4, "Z") @ Matrix.Translation((dx, -0.006, dz)) @ Matrix.Diagonal((sx, 0.008, sz, 1.0))
                res = bmesh.ops.create_cube(bm, size=1.0, matrix=m)
                for v in res["verts"]:
                    for f in v.link_faces:
                        f.material_index = mat
        part(f"Panel{i}", root, panel, (x, 0, z))
    export("VR")


def labelling():
    """Data labelling: three objects (a sphere, a cube, a cone) on the ground, each with a red bounding box (Box0..2)
    and a label tag above it (Tag0..2), pivots at their middles so they can snap on."""
    clear()
    root = link(bpy.data.objects.new("Labelling", None))
    items = ((-0.45, 0.13, "ball"), (0.0, 0.11, "cube"), (0.45, 0.16, "cone"))

    def things(bm):
        ball(bm, 0.13, (-0.45, 0, 0.13), G)
        cube(bm, (0.22, 0.22, 0.22), (0.0, 0, 0.11), F, 0.015)
        tube(bm, 0.14, 0.32, (0.45, 0, 0.0), G, radius_top=0.0, segments=24)
    part("Things", root, things)
    for i, (x, h, kind) in enumerate(items):
        half, top = (0.17, 0.3) if kind != "cone" else (0.18, 0.36)

        def frame(bm, x=x, half=half, top=top):
            e = 0.008
            for (sx, sy, sz, cx, cy, cz) in ((2 * half, e, e, 0, -half, 0), (2 * half, e, e, 0, half, 0), (e, 2 * half, e, -half, 0, 0), (e, 2 * half, e, half, 0, 0)):
                for z in (-top / 2, top / 2):
                    cube(bm, (sx, sy, sz), (x + cx, cy, top / 2 + cz + z), R)
            for cx in (-half, half):
                for cy in (-half, half):
                    cube(bm, (e, e, top), (x + cx, cy, top / 2), R)
        part(f"Box{i}", root, frame, (x, 0, top / 2))
        part(f"Tag{i}", root, lambda bm, x=x, top=top: (cube(bm, (0.2, 0.012, 0.07), (x - 0.07, -0.17, top + 0.05), R, 0.01),
                                                        cube(bm, (0.12, 0.014, 0.012), (x - 0.09, -0.177, top + 0.05), F)), (x - 0.07, -0.17, top + 0.05))
    export("Labelling")


def rack():
    """HPC-AI: a graphite server rack, eight server units with vents, and a column of status LEDs on each
    (Led<unit><n>, so Unity can blink them)."""
    clear()
    root = link(bpy.data.objects.new("Rack", None))

    def cabinet(bm):
        cube(bm, (0.6, 0.55, 1.3), (0, 0, 0.65), G, 0.02)
        for u in range(8):
            z = 0.12 + u * 0.14
            cube(bm, (0.52, 0.02, 0.11), (0, -0.28, z), F, 0.006)
            for k in range(6):
                cube(bm, (0.04, 0.022, 0.06), (-0.2 + k * 0.05, -0.285, z), G)
        cube(bm, (0.62, 0.57, 0.03), (0, 0, 1.31), F)
    part("Cabinet", root, cabinet)
    for u in range(8):
        for n in range(3):
            part(f"Led{u}{n}", root, lambda bm, u=u, n=n: cube(bm, (0.025, 0.015, 0.025), (0.12 + n * 0.045, -0.297, 0.12 + u * 0.14), G),
                 (0.12 + n * 0.045, -0.297, 0.12 + u * 0.14))
    export("Rack")


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    print(f"Story assets -> {OUT}")
    for name, body in PHRASES.items():
        n = make_phrase(name, body)
        expected = len(body.replace(" ", "").replace("\n", ""))  # the multiplication sign is one piece too
        assert n == expected, f"{name}: {n} pieces for {expected} characters (a letter split or merged)"
    ring()
    lab()
    chip()
    robot_arm()
    drone()
    car()
    padlock()
    satellite()
    scale()
    gauge()
    vr_headset()
    labelling()
    rack()
    tree()
    lamp()
    plaza()
