#!/usr/bin/env python3
"""
plotly_to_hld.py - turn a Plotly 3D figure (OCT volume + segmentation meshes)
into a package the Looking Glass HLD setup can load.

Output folder  (one per scan):
    model.glb    meshes (tissue + fluid layers), glTF 2.0, Y-up, millimetres
    volume.u8    raw uint8 intensity grid, X fastest, then Y, then Z
    meta.json    grid size, spacing, origin, layer colours, stats text, title

Consumers:
    unity/  -> "HLD > Import OCT export..." builds a prefab for ObjectSwitcher
    viewer/ -> browser preview / direct full-screen playback on the HLD

Pure standard library (no numpy, no plotly needed) so it runs anywhere.

Usage
-----
CLI, from a saved Plotly HTML or a figure JSON (fig.write_json):
    python plotly_to_hld.py ../W.I._OS.html  -o ../out
    python plotly_to_hld.py scan.json -o ../out --name Patient01_OD

From the Python script that makes the figure (recommended):
    from plotly_to_hld import export_figure
    export_figure(fig, "out/Patient01_OD")

Axis convention
---------------
Plotly scene: x = lateral, y = B-scan index (en-face), z = depth.
Output (glTF, right-handed, Y up):
    X = lateral,  Y = up,  Z = B-scan direction
Plotly's "up" (z) stays up; the meshes in the source are vitreous-up.
--flip-depth turns everything over. --depth-scale rescales the depth axis
(the source figure is already exaggerated x3; 0.3333 = true proportions).

The source script writes the grayscale volume MIRRORED in depth relative
to the segmentation meshes (mesh_z = zmax - volume_z). The exporter
detects that (--volume-depth auto) and corrects the volume.
"""
from __future__ import annotations

import argparse
import base64
import html
import json
import math
import os
import re
import struct
import sys

# --------------------------------------------------------------------------
# Reading the Plotly figure
# --------------------------------------------------------------------------

_DTYPES = {"f4": "f", "f8": "d", "i1": "b", "u1": "B", "i2": "h",
           "u2": "H", "i4": "i", "u4": "I"}


def decode_array(v):
    """Plotly arrays come as plain lists, numpy arrays or {'dtype','bdata'}."""
    if v is None:
        return None
    if isinstance(v, dict) and "bdata" in v:
        code = _DTYPES[v["dtype"]]
        raw = base64.b64decode(v["bdata"])
        n = len(raw) // struct.calcsize(code)
        flat = list(struct.unpack("<%d%s" % (n, code), raw))
        return flat
    if hasattr(v, "tolist"):          # numpy
        v = v.tolist()
    if isinstance(v, (list, tuple)):
        out = []
        stack = [v]
        # flatten nested lists (plotly accepts 3-D lists for volume)
        def _flat(a):
            for e in a:
                if isinstance(e, (list, tuple)):
                    yield from _flat(e)
                else:
                    yield e
        out = list(_flat(v))
        return out
    raise TypeError("unsupported array encoding: %r" % type(v))


def figure_from_html(path):
    s = open(path, encoding="utf-8", errors="replace").read()
    i = s.find("Plotly.newPlot(")
    if i < 0:
        raise ValueError("no Plotly.newPlot(...) call found in %s" % path)
    dec = json.JSONDecoder()
    j = s.index("[", i)
    data, end = dec.raw_decode(s, j)
    rest = s[end:].lstrip()
    if rest.startswith(","):
        rest = rest[1:].lstrip()
    layout, _ = dec.raw_decode(rest)
    return {"data": data, "layout": layout}


def load_figure(obj):
    """Accept a path (.html/.json), a dict, or a plotly Figure."""
    if isinstance(obj, str):
        if obj.lower().endswith((".html", ".htm")):
            return figure_from_html(obj)
        with open(obj, encoding="utf-8") as f:
            return json.load(f)
    if isinstance(obj, dict):
        return obj
    if hasattr(obj, "to_json"):       # plotly.graph_objects.Figure
        return json.loads(obj.to_json())
    raise TypeError("cannot read figure from %r" % type(obj))


# --------------------------------------------------------------------------
# Colours
# --------------------------------------------------------------------------

_NAMED = {
    "lightgray": (211, 211, 211), "lightgrey": (211, 211, 211),
    "gray": (128, 128, 128), "grey": (128, 128, 128),
    "darkgray": (169, 169, 169), "silver": (192, 192, 192),
    "white": (255, 255, 255), "black": (0, 0, 0), "red": (255, 0, 0),
    "green": (0, 128, 0), "blue": (0, 0, 255), "yellow": (255, 255, 0),
    "cyan": (0, 255, 255), "magenta": (255, 0, 255), "orange": (255, 165, 0),
    "purple": (128, 0, 128), "pink": (255, 192, 203), "brown": (165, 42, 42),
}


def parse_color(c, default=(200, 200, 200)):
    if not c or not isinstance(c, str):
        return default
    c = c.strip().lower()
    if c.startswith("#"):
        h = c[1:]
        if len(h) == 3:
            h = "".join(ch * 2 for ch in h)
        return tuple(int(h[k:k + 2], 16) for k in (0, 2, 4))
    m = re.match(r"rgba?\(([^)]*)\)", c)
    if m:
        parts = [float(p) for p in m.group(1).split(",")[:3]]
        return tuple(int(round(p)) for p in parts)
    return _NAMED.get(c, default)


def strip_html(s):
    s = re.sub(r"<br\s*/?>", "\n", s or "", flags=re.I)
    s = re.sub(r"<[^>]+>", "", s)
    return html.unescape(s).strip()


# --------------------------------------------------------------------------
# Geometry helpers
# --------------------------------------------------------------------------

class Frame:
    """Maps Plotly (x, y, z) to output glTF (X, Y, Z), then recentres."""

    def __init__(self, flip_depth=False, depth_scale=1.0):
        self.flip = flip_depth
        self.ds = depth_scale
        self.offset = (0.0, 0.0, 0.0)

    def raw(self, x, y, z):
        # proper rotations only, so triangle winding stays valid
        if self.flip:
            return (x, -z * self.ds, y)
        return (x, z * self.ds, -y)

    def __call__(self, x, y, z):
        X, Y, Z = self.raw(x, y, z)
        o = self.offset
        return (X - o[0], Y - o[1], Z - o[2])


def vertex_normals(pos, tris):
    n = [[0.0, 0.0, 0.0] for _ in range(len(pos))]
    for a, b, c in tris:
        ax, ay, az = pos[a]
        e1 = (pos[b][0] - ax, pos[b][1] - ay, pos[b][2] - az)
        e2 = (pos[c][0] - ax, pos[c][1] - ay, pos[c][2] - az)
        fx = e1[1] * e2[2] - e1[2] * e2[1]
        fy = e1[2] * e2[0] - e1[0] * e2[2]
        fz = e1[0] * e2[1] - e1[1] * e2[0]
        for v in (a, b, c):
            n[v][0] += fx; n[v][1] += fy; n[v][2] += fz
    out = []
    for x, y, z in n:
        L = math.sqrt(x * x + y * y + z * z) or 1.0
        out.append((x / L, y / L, z / L))
    return out


# --------------------------------------------------------------------------
# GLB writer (minimal glTF 2.0)
# --------------------------------------------------------------------------

def write_glb(path, meshes):
    """meshes: list of dict(name, pos, nrm, tris, rgba, emissive)."""
    bin_ = bytearray()
    views, accs, gl_meshes, nodes, mats = [], [], [], [], []

    def add_view(data, target):
        while len(bin_) % 4:
            bin_.append(0)
        off = len(bin_)
        bin_.extend(data)
        views.append({"buffer": 0, "byteOffset": off,
                      "byteLength": len(data), "target": target})
        return len(views) - 1

    for m in meshes:
        pos, nrm, tris = m["pos"], m["nrm"], m["tris"]
        mn = [min(p[k] for p in pos) for k in range(3)]
        mx = [max(p[k] for p in pos) for k in range(3)]
        pv = add_view(struct.pack("<%df" % (3 * len(pos)),
                                  *[c for p in pos for c in p]), 34962)
        accs.append({"bufferView": pv, "componentType": 5126,
                     "count": len(pos), "type": "VEC3", "min": mn, "max": mx})
        pa = len(accs) - 1
        nv = add_view(struct.pack("<%df" % (3 * len(nrm)),
                                  *[c for p in nrm for c in p]), 34962)
        accs.append({"bufferView": nv, "componentType": 5126,
                     "count": len(nrm), "type": "VEC3"})
        na = len(accs) - 1
        flat = [v for t in tris for v in t]
        iv = add_view(struct.pack("<%dI" % len(flat), *flat), 34963)
        accs.append({"bufferView": iv, "componentType": 5125,
                     "count": len(flat), "type": "SCALAR"})
        ia = len(accs) - 1

        r, g, b, a = m["rgba"]
        mat = {"name": m["name"],
               "pbrMetallicRoughness": {"baseColorFactor": [r, g, b, a],
                                        "metallicFactor": 0.0,
                                        "roughnessFactor": m.get("roughness", 0.45)},
               "doubleSided": True}
        if a < 0.999:
            mat["alphaMode"] = "BLEND"
        if m.get("emissive"):
            mat["emissiveFactor"] = list(m["emissive"])
        mats.append(mat)
        gl_meshes.append({"name": m["name"], "primitives": [{
            "attributes": {"POSITION": pa, "NORMAL": na},
            "indices": ia, "material": len(mats) - 1}]})
        nodes.append({"name": m["name"], "mesh": len(gl_meshes) - 1})

    gltf = {"asset": {"version": "2.0", "generator": "plotly_to_hld.py"},
            "scene": 0, "scenes": [{"nodes": list(range(len(nodes)))}],
            "nodes": nodes, "meshes": gl_meshes, "materials": mats,
            "accessors": accs, "bufferViews": views,
            "buffers": [{"byteLength": len(bin_)}]}
    js = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
    js += b" " * ((4 - len(js) % 4) % 4)
    while len(bin_) % 4:
        bin_.append(0)
    total = 12 + 8 + len(js) + 8 + len(bin_)
    with open(path, "wb") as f:
        f.write(struct.pack("<III", 0x46546C67, 2, total))
        f.write(struct.pack("<II", len(js), 0x4E4F534A)); f.write(js)
        f.write(struct.pack("<II", len(bin_), 0x004E4942)); f.write(bin_)


# --------------------------------------------------------------------------
# Volume -> regular grid in output axes
# --------------------------------------------------------------------------

def grid_from_volume_trace(t, frame, crop=True, volume_depth="auto",
                           mesh_pts=None, log=print):
    xs, ys, zs = (decode_array(t[k]) for k in ("x", "y", "z"))
    vals = decode_array(t["value"])
    n = len(vals)

    def axis(a):
        u = sorted(set(round(v, 9) for v in a))
        stride = next((k for k in range(1, n) if a[k] != a[0]), n)
        return u, stride

    ux, sx = axis(xs); uy, sy = axis(ys); uz, sz = axis(zs)
    nx, ny, nz = len(ux), len(uy), len(uz)
    if nx * ny * nz != n:
        raise ValueError("volume is not a regular grid (%d*%d*%d != %d)"
                         % (nx, ny, nz, n))
    dx = (ux[-1] - ux[0]) / max(nx - 1, 1)
    dy = (uy[-1] - uy[0]) / max(ny - 1, 1)
    dz = (uz[-1] - uz[0]) / max(nz - 1, 1)

    def at(ix, iy, iz):
        return vals[ix * sx + iy * sy + iz * sz]

    vmin = t.get("cmin", min(vals)); vmax = t.get("cmax", max(vals))
    span = (vmax - vmin) or 1.0

    # Is the volume mirrored in depth relative to the meshes? (Seen in
    # practice: B-scan rows used as z directly while the segmentation
    # meshes were written vitreous-up.) Test: the top surface of the
    # largest mesh must sit on the top edge of the volume's signal.
    mirror = volume_depth == "mirror"
    if volume_depth == "auto" and mesh_pts:
        mxs, mys, mzs = mesh_pts
        dx_ = (ux[-1] - ux[0]) / max(nx - 1, 1) or 1.0
        dy_ = (uy[-1] - uy[0]) / max(ny - 1, 1) or 1.0
        top = {}
        for a_, b_, c_ in zip(mxs, mys, mzs):
            key = (int(round((a_ - ux[0]) / dx_)), int(round((b_ - uy[0]) / dy_)))
            if c_ > top.get(key, -1e30):
                top[key] = c_
        cut = vmin + 0.25 * span
        e_same, e_mirror = [], []
        for (ix, iy), zt in top.items():
            if not (0 <= ix < nx and 0 <= iy < ny):
                continue
            col = [vals[ix * sx + iy * sy + iz * sz] for iz in range(nz)]
            hi = next((iz for iz in reversed(range(nz)) if col[iz] > cut), None)
            lo = next((iz for iz in range(nz) if col[iz] > cut), None)
            if hi is None:
                continue
            e_same.append(abs(zt - uz[hi]))
            e_mirror.append(abs(zt - (uz[0] + uz[-1] - uz[lo])))
        if e_same:
            ms = sorted(e_same)[len(e_same) // 2]
            mm = sorted(e_mirror)[len(e_mirror) // 2]
            mirror = mm < ms
            log("  volume/mesh depth check: median surface error same=%.3f "
                "mirrored=%.3f -> %s" % (ms, mm, "volume is MIRRORED in the "
                "source, correcting" if mirror else "aligned"))

    # Output axes: X <- plotly x, Y <- plotly z (depth), Z <- plotly y
    NX, NY, NZ = nx, nz, ny
    flip = frame.flip
    out = bytearray(NX * NY * NZ)
    p = 0
    for k in range(NZ):                     # output Z  (B-scan)
        iy = k if flip else (ny - 1 - k)
        for j in range(NY):                 # output Y  (up)
            iz = j if flip == mirror else (nz - 1 - j)
            for i in range(NX):             # output X  (lateral)
                v = (at(i, iy, iz) - vmin) / span
                out[p] = 0 if v <= 0 else 255 if v >= 1 else int(v * 255 + .5)
                p += 1
    # world-space corner of voxel (0,0,0) in raw output coords
    corner = list(frame.raw(ux[0], uy[0] if flip else uy[-1],
                            uz[-1] if flip else uz[0]))
    spacing = (dx, dz * frame.ds, dy)
    iso = t.get("isomin", vmin)
    threshold = max(0.0, min(1.0, (iso - vmin) / span))
    # opacity ramp start (plotly opacityscale), if present
    osc = t.get("opacityscale")
    if isinstance(osc, list):
        zero = [s for s, o in osc if o == 0]
        if zero:
            threshold = max(threshold, max(zero))

    # Crop empty depth layers (dark vitreous / choroid noise). The HLD
    # alcove is a fixed box, so every empty millimetre makes the retina
    # smaller on screen.
    if crop:
        cut = threshold * 255
        layer = NX * NY
        keep = []
        for j in range(NY):
            hits = 0
            for k in range(NZ):
                row = k * layer + j * NX
                hits += sum(1 for v in out[row:row + NX] if v > cut)
            keep.append(hits / float(NX * NZ))
        rows = [j for j, f in enumerate(keep) if f > 0.05]
        if rows:
            j0 = max(0, rows[0] - 2); j1 = min(NY - 1, rows[-1] + 2)
            if j1 - j0 + 1 < NY:
                cropped = bytearray()
                for k in range(NZ):
                    base = k * layer
                    cropped += out[base + j0 * NX: base + (j1 + 1) * NX]
                out, NY = cropped, j1 - j0 + 1
                corner[1] += j0 * spacing[1]
    return {"dims": [NX, NY, NZ], "spacing": spacing, "corner": tuple(corner),
            "mirrored": mirror,
            "data": out, "threshold": threshold,
            "opacity": float(t.get("opacity", 0.15))}


# --------------------------------------------------------------------------
# Main export
# --------------------------------------------------------------------------

def _slug(s):
    s = re.sub(r"[^A-Za-z0-9-]+", "_", s).strip("_")
    return s or "Scan"


def export_figure(fig, out_dir, name=None, flip_depth=False, depth_scale=1.0,
                  crop=True, volume_depth="auto", quiet=False):
    fig = load_figure(fig)
    data = fig.get("data", [])
    layout = fig.get("layout", {})
    os.makedirs(out_dir, exist_ok=True)
    name = name or _slug(os.path.basename(os.path.normpath(out_dir)))
    frame = Frame(flip_depth, depth_scale)

    # 1) collect raw geometry so we can find the bounds
    meshes_raw, vol_trace = [], None
    for t in data:
        typ = t.get("type")
        if typ in ("volume", "isosurface") and vol_trace is None:
            vol_trace = t
        elif typ == "mesh3d":
            xs, ys, zs = (decode_array(t[k]) for k in ("x", "y", "z"))
            I, J, K = (decode_array(t.get(k)) for k in ("i", "j", "k"))
            if not xs or I is None:
                continue
            pos = [frame.raw(xs[n], ys[n], zs[n]) for n in range(len(xs))]
            tris = list(zip(I, J, K))
            meshes_raw.append((t, pos, tris))

    log = (lambda *a: None) if quiet else print
    pts = [p for _, pos, _ in meshes_raw for p in pos]
    mesh_pts = None
    if meshes_raw:   # plotly coords of the largest mesh (the tissue shell)
        big = max(meshes_raw, key=lambda m: len(m[1]))[0]
        mesh_pts = tuple(decode_array(big[k]) for k in ("x", "y", "z"))
    vol = (grid_from_volume_trace(vol_trace, frame, crop, volume_depth,
                                  mesh_pts, log) if vol_trace else None)
    if vol:
        c = vol["corner"]; d = vol["dims"]; s = vol["spacing"]
        pts += [c, (c[0] + (d[0] - 1) * s[0], c[1] + (d[1] - 1) * s[1],
                    c[2] + (d[2] - 1) * s[2])]
    if not pts:
        raise ValueError("figure has no mesh3d or volume traces")
    mn = [min(p[k] for p in pts) for k in range(3)]
    mx = [max(p[k] for p in pts) for k in range(3)]
    # centre X/Z, sit on Y = 0 (what GesturePlayer expects of a model)
    frame.offset = ((mn[0] + mx[0]) / 2, mn[1], (mn[2] + mx[2]) / 2)
    o = frame.offset
    size = [mx[k] - mn[k] for k in range(3)]

    # 2) meshes -> GLB
    layers, glb_meshes = [], []
    for t, pos, tris in meshes_raw:
        pos = [(p[0] - o[0], p[1] - o[1], p[2] - o[2]) for p in pos]
        rgb = parse_color(t.get("color"))
        alpha = float(t.get("opacity", 1.0))
        lname = re.sub(r"\s*\(.*?\)\s*$", "", t.get("name") or "mesh").strip()
        kind = "tissue" if alpha < 0.999 else "fluid"
        col = tuple(c / 255.0 for c in rgb)
        glb_meshes.append({
            "name": lname, "pos": pos, "nrm": vertex_normals(pos, tris),
            "tris": tris, "rgba": (*col, alpha),
            "emissive": tuple(c * 0.25 for c in col) if kind == "fluid" else None,
            "roughness": 0.6 if kind == "tissue" else 0.35})
        layers.append({"name": lname, "label": t.get("name") or lname,
                       "kind": kind, "color": "#%02x%02x%02x" % rgb,
                       "opacity": alpha, "vertices": len(pos),
                       "triangles": len(tris)})
    if glb_meshes:
        write_glb(os.path.join(out_dir, "model.glb"), glb_meshes)

    # 3) volume -> raw bytes
    vol_meta = None
    if vol:
        with open(os.path.join(out_dir, "volume.u8"), "wb") as f:
            f.write(vol["data"])
        c = vol["corner"]
        vol_meta = {"file": "volume.u8", "format": "uint8",
                    "order": "x-fastest, then y, then z",
                    "dims": vol["dims"],
                    "spacing": [round(v, 6) for v in vol["spacing"]],
                    "origin": [round(c[k] - o[k], 6) for k in range(3)],
                    "threshold": round(vol["threshold"], 4),
                    "sourceOpacity": vol["opacity"],
                    "mirroredInSource": vol["mirrored"],
                    "name": vol_trace.get("name") or "volume"}

    title = layout.get("title", {})
    title = title.get("text", "") if isinstance(title, dict) else str(title)
    title_lines = strip_html(title).split("\n")
    notes = [strip_html(a.get("text", "")) for a in layout.get("annotations", [])]
    scene = layout.get("scene", {})
    axis_titles = {}
    for ax in ("xaxis", "yaxis", "zaxis"):
        at = scene.get(ax, {}).get("title", {})
        axis_titles[ax] = at.get("text") if isinstance(at, dict) else at

    meta = {
        "name": name, "title": title_lines[0] if title_lines else name,
        "subtitle": " ".join(title_lines[1:]).strip(),
        "notes": notes, "units": "mm",
        "axes": {"X": "lateral", "Y": "up" + (" (flipped vs plotly)" if flip_depth else ""),
                 "Z": "B-scan direction", "source": axis_titles,
                 "depthScale": depth_scale, "flipDepth": flip_depth},
        "size": [round(v, 5) for v in size],
        "layers": layers, "volume": vol_meta,
        "model": "model.glb" if glb_meshes else None,
    }
    with open(os.path.join(out_dir, "meta.json"), "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2, ensure_ascii=False)

    # keep a list of all scans next to them, for the browser viewer
    parent = os.path.dirname(os.path.abspath(out_dir))
    scans = sorted(d for d in os.listdir(parent)
                   if os.path.isfile(os.path.join(parent, d, "meta.json")))
    with open(os.path.join(parent, "index.json"), "w", encoding="utf-8") as f:
        json.dump({"scans": scans}, f, indent=1)

    if not quiet:
        print("exported %s -> %s" % (name, out_dir))
        print("  size (mm): %.2f x %.2f x %.2f" % tuple(size))
        for L in layers:
            print("  mesh  %-14s %6d tris  %s  a=%.2f" % (
                L["name"], L["triangles"], L["color"], L["opacity"]))
        if vol_meta:
            print("  volume %s  threshold %.2f" % (vol_meta["dims"],
                                                    vol_meta["threshold"]))
    return meta


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("source", help="Plotly .html or figure .json")
    ap.add_argument("-o", "--out", default="out",
                    help="parent output folder (default: out)")
    ap.add_argument("--name", help="scan name (default: from file name)")
    ap.add_argument("--flip-depth", action="store_true",
                    help="turn the whole scan upside down")
    ap.add_argument("--volume-depth", choices=("auto", "same", "mirror"),
                    default="auto",
                    help="is the volume mirrored in depth vs the meshes? "
                         "(default: detect)")
    ap.add_argument("--depth-scale", type=float, default=1.0,
                    help="extra scale on the depth axis (e.g. 0.3333)")
    ap.add_argument("--no-crop", action="store_true",
                    help="keep empty depth layers of the volume")
    a = ap.parse_args(argv)
    name = a.name or _slug(os.path.splitext(os.path.basename(a.source))[0])
    export_figure(a.source, os.path.join(a.out, name), name=name,
                  flip_depth=a.flip_depth, depth_scale=a.depth_scale,
                  crop=not a.no_crop, volume_depth=a.volume_depth)


if __name__ == "__main__":
    sys.exit(main())
