"""Turn a simple binary FBX (static meshes, no skeleton) into a .glb the viewer can load.

    python3 exporter/fbx_to_glb.py AIRI.fbx out/airi_sign/model.glb --color "#050505" --metallic 0.3 --roughness 0.35

Needs nothing but Python. It reads each mesh, applies the mesh's own position,
rotation and scale, converts the file's units to metres and writes one glTF
mesh per FBX mesh, all with the one material given on the command line.
Textures, animation and parented meshes are not handled; a parented mesh stops
the run rather than coming out in the wrong place.
"""
import argparse
import json
import math
import struct
import zlib

SCALARS = {"Y": ("<h", 2), "C": ("<?", 1), "I": ("<i", 4), "F": ("<f", 4), "D": ("<d", 8), "L": ("<q", 8)}
ARRAYS = {"f": "f", "d": "d", "l": "q", "i": "i", "b": "b"}


def read_fbx(path):
    """Return the top-level nodes as (name, properties, children) tuples."""
    with open(path, "rb") as f:
        d = f.read()
    if not d.startswith(b"Kaydara FBX Binary"):
        raise SystemExit(f"{path}: not a binary FBX file")
    wide = struct.unpack_from("<I", d, 23)[0] >= 7500
    head = "<QQQ" if wide else "<III"

    def node(o):
        end, count, _ = struct.unpack_from(head, d, o)
        o += struct.calcsize(head)
        name_len = d[o]
        o += 1
        if end == 0:
            return None, o
        name = d[o:o + name_len].decode()
        o += name_len
        props = []
        for _ in range(count):
            t = chr(d[o])
            o += 1
            if t in SCALARS:
                fmt, size = SCALARS[t]
                props.append(struct.unpack_from(fmt, d, o)[0])
                o += size
            elif t in ARRAYS:
                n, packed, size = struct.unpack_from("<III", d, o)
                o += 12
                raw = d[o:o + size]
                o += size
                props.append(struct.unpack(f"<{n}{ARRAYS[t]}", zlib.decompress(raw) if packed else raw))
            else:  # S string, R raw bytes
                n = struct.unpack_from("<I", d, o)[0]
                o += 4
                props.append(d[o:o + n].decode("utf8", "replace") if t == "S" else d[o:o + n])
                o += n
        kids = []
        while o < end:
            kid, o = node(o)
            if kid:
                kids.append(kid)
        return (name, props, kids), end

    nodes, o = [], 27
    while o < len(d):
        n, o = node(o)
        if n is None:
            break
        nodes.append(n)
    return nodes


def child(n, name):
    return next((k for k in n[2] if k[0] == name), None)


def prop70(n, name, default):
    for p in (child(n, "Properties70") or (0, 0, []))[2]:
        if p[1][0] == name:
            return p[1][4:]
    return default


def rotation(rx, ry, rz):
    """FBX default rotation order: X first, then Y, then Z."""
    (cx, sx), (cy, sy), (cz, sz) = [(math.cos(math.radians(a)), math.sin(math.radians(a))) for a in (rx, ry, rz)]
    return [[cy * cz, sx * sy * cz - cx * sz, cx * sy * cz + sx * sz],
            [cy * sz, sx * sy * sz + cx * cz, cx * sy * sz - sx * cz],
            [-sy, sx * cy, cx * cy]]


def mesh_arrays(geometry, model, to_metres):
    """Flat lists of positions, normals and triangle indices, in metres, Y up."""
    t = prop70(model, "Lcl Translation", (0, 0, 0))
    r = rotation(*prop70(model, "Lcl Rotation", (0, 0, 0)))
    s = prop70(model, "Lcl Scaling", (1, 1, 1))
    verts = child(geometry, "Vertices")[1][0]
    index = child(geometry, "PolygonVertexIndex")[1][0]
    layer = child(geometry, "LayerElementNormal")
    by_corner = layer and child(layer, "MappingInformationType")[1][0] == "ByPolygonVertex"
    normals = child(layer, "Normals")[1][0] if by_corner else None
    normal_index = child(layer, "NormalsIndex")[1][0] if by_corner and child(layer, "NormalsIndex") else None

    def place(v, scale, offset):
        v = [v[i] * scale[i] for i in range(3)]
        return [sum(r[row][i] * v[i] for i in range(3)) + offset[row] for row in range(3)]

    pos, nor, tris, polygon = [], [], [], []
    for corner, vi in enumerate(index):
        last = vi < 0
        vi = ~vi if last else vi
        pos += [c * to_metres for c in place(verts[vi * 3:vi * 3 + 3], s, t)]
        if normals:
            ni = normal_index[corner] if normal_index else corner
            # a normal under an uneven scale is divided by it, not multiplied
            n = place(normals[ni * 3:ni * 3 + 3], [1 / c for c in s], (0, 0, 0))
            length = math.hypot(*n) or 1
            nor += [c / length for c in n]
        polygon.append(corner)
        if last:
            tris += [i for k in range(1, len(polygon) - 1) for i in (polygon[0], polygon[k], polygon[k + 1])]
            polygon = []
    return pos, nor, tris


def write_glb(path, meshes, color, metallic, roughness):
    blob, views, accessors, gl_meshes = bytearray(), [], [], []

    def accessor(values, fmt, kind, target, bounds=False):
        data = struct.pack(f"<{len(values)}{fmt}", *values)
        views.append({"buffer": 0, "byteOffset": len(blob), "byteLength": len(data), "target": target})
        blob.extend(data + b"\0" * (-len(data) % 4))
        width = 3 if kind == "VEC3" else 1
        acc = {"bufferView": len(views) - 1, "componentType": 5126 if fmt == "f" else 5125,
               "count": len(values) // width, "type": kind}
        if bounds:
            acc["min"] = [min(values[i::3]) for i in range(3)]
            acc["max"] = [max(values[i::3]) for i in range(3)]
        accessors.append(acc)
        return len(accessors) - 1

    for name, pos, nor, tris in meshes:
        attributes = {"POSITION": accessor(pos, "f", "VEC3", 34962, bounds=True)}
        if nor:
            attributes["NORMAL"] = accessor(nor, "f", "VEC3", 34962)
        gl_meshes.append({"name": name, "primitives": [
            {"attributes": attributes, "indices": accessor(tris, "I", "SCALAR", 34963), "material": 0}]})

    doc = {
        "asset": {"version": "2.0", "generator": "fbx_to_glb.py"},
        "scene": 0,
        "scenes": [{"nodes": list(range(len(gl_meshes)))}],
        "nodes": [{"name": m["name"], "mesh": i} for i, m in enumerate(gl_meshes)],
        "meshes": gl_meshes,
        "materials": [{"name": "Body", "doubleSided": True, "pbrMetallicRoughness": {
            "baseColorFactor": color + [1.0], "metallicFactor": metallic, "roughnessFactor": roughness}}],
        "accessors": accessors, "bufferViews": views, "buffers": [{"byteLength": len(blob)}],
    }
    text = json.dumps(doc, separators=(",", ":")).encode()
    text += b" " * (-len(text) % 4)
    with open(path, "wb") as f:
        f.write(struct.pack("<4sII", b"glTF", 2, 28 + len(text) + len(blob)))
        f.write(struct.pack("<I4s", len(text), b"JSON") + text)
        f.write(struct.pack("<I4s", len(blob), b"BIN\0") + bytes(blob))


def srgb_to_linear(hex_color):
    channels = [int(hex_color.lstrip("#")[i:i + 2], 16) / 255 for i in (0, 2, 4)]
    return [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in channels]


def convert(src, dst, color="#808080", metallic=0.0, roughness=0.5):
    nodes = {n[0]: n for n in read_fbx(src)}
    settings = nodes.get("GlobalSettings")
    if settings and prop70(settings, "UpAxis", (1,))[0] != 1:
        raise SystemExit(f"{src}: only Y-up files are handled")
    to_metres = (prop70(settings, "UnitScaleFactor", (1.0,))[0] if settings else 1.0) / 100
    objects = {o[1][0]: o for o in nodes["Objects"][2]}
    links = [c[1][1:3] for c in nodes["Connections"][2] if c[1][0] == "OO"]

    meshes = []
    for kid, parent in links:
        geometry, model = objects.get(kid), objects.get(parent)
        if not (geometry and model and geometry[0] == "Geometry" and model[0] == "Model"):
            continue
        if any(a == parent and b in objects and objects[b][0] == "Model" for a, b in links):
            raise SystemExit(f"{src}: mesh {model[1][1]!r} has a parent, which this converter does not handle")
        name = model[1][1].split("\0")[0]
        meshes.append((name, *mesh_arrays(geometry, model, to_metres)))
    if not meshes:
        raise SystemExit(f"{src}: no meshes found")
    write_glb(dst, meshes, srgb_to_linear(color), metallic, roughness)
    return meshes


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("fbx")
    ap.add_argument("glb")
    ap.add_argument("--color", default="#808080", help="surface colour as #rrggbb")
    ap.add_argument("--metallic", type=float, default=0.0)
    ap.add_argument("--roughness", type=float, default=0.5)
    a = ap.parse_args()
    for name, pos, _, tris in convert(a.fbx, a.glb, a.color, a.metallic, a.roughness):
        size = [max(pos[i::3]) - min(pos[i::3]) for i in range(3)]
        print(f"{name}: {len(tris) // 3} triangles, {size[0]:.2f} x {size[1]:.2f} x {size[2]:.2f} m")
    print("wrote", a.glb)
