"""Procedural geometry with explicit LOD chains.

Every primitive is built from a parametric surface sampled on a (nu, nv) grid,
so a level-of-detail chain is obtained simply by halving the sampling
resolution.  This gives us honest LOD chains (real vertex/triangle counts,
real payload sizes) without depending on a mesh decimation library.

All meshes are returned as (positions, indices), optionally with colours
and part ids:
    positions : float32 (N, 3)
    indices   : uint32  (M, 3)
    colours   : uint8   (N, 3)   -- only on meshes built with paint()
    parts     : uint16  (N,)     -- only on meshes built with tag()

Per-vertex colour is what lets one object show several materials at once:
glass with coloured liquid inside, a board with writing on it.  Part ids name
the pieces the client animates -- a pendulum bob, a falling drop, each pen
stroke of the writing -- so continuous motion costs a few parameters instead
of a new mesh every frame.
"""
from __future__ import annotations

import numpy as np

Mesh = tuple[np.ndarray, np.ndarray]


# --------------------------------------------------------------------------
# core grid builder
# --------------------------------------------------------------------------
def parametric(fn, nu: int, nv: int, wrap_u: bool = False, wrap_v: bool = False) -> Mesh:
    """Sample fn(u, v) -> (x, y, z) on a grid and triangulate it.

    u, v run over [0, 1].  When an axis wraps, the last sample is omitted and
    the quads close back onto sample 0.
    """
    nu = max(2, int(nu))
    nv = max(2, int(nv))
    cu = nu if wrap_u else nu + 1
    cv = nv if wrap_v else nv + 1

    us = np.arange(cu, dtype=np.float64) / nu
    vs = np.arange(cv, dtype=np.float64) / nv
    uu, vv = np.meshgrid(us, vs, indexing="ij")
    flat_u = uu.ravel()
    comps = fn(flat_u, vv.ravel())
    pos = np.stack([np.broadcast_to(np.asarray(c, dtype=np.float64), flat_u.shape)
                    for c in comps], axis=1).astype(np.float32)

    tris = []
    for i in range(nu):
        i0 = i
        i1 = (i + 1) % cu
        for j in range(nv):
            j0 = j
            j1 = (j + 1) % cv
            a = i0 * cv + j0
            b = i1 * cv + j0
            c = i1 * cv + j1
            d = i0 * cv + j1
            tris.append((a, b, c))
            tris.append((a, c, d))
    idx = np.asarray(tris, dtype=np.uint32)
    return pos, idx


def merge(meshes: list) -> tuple:
    """Concatenate meshes, offsetting indices.

    If any part is painted, the result carries colours; unpainted parts
    default to light grey.
    """
    parts_p, parts_i, parts_c, parts_t, off = [], [], [], [], 0
    painted = any(len(m) > 2 for m in meshes)
    tagged = any(len(m) > 3 for m in meshes)
    for m in meshes:
        p, i = m[0], m[1]
        parts_p.append(p)
        parts_i.append(i + off)
        if painted:
            parts_c.append(m[2] if len(m) > 2
                           else np.full((len(p), 3), 200, dtype=np.uint8))
        if tagged:
            parts_t.append(m[3] if len(m) > 3 else np.zeros(len(p), dtype=np.uint16))
        off += len(p)
    out = (np.concatenate(parts_p, axis=0).astype(np.float32),
           np.concatenate(parts_i, axis=0).astype(np.uint32))
    if painted:
        out += (np.concatenate(parts_c, axis=0).astype(np.uint8),)
    if tagged:
        out += (np.concatenate(parts_t, axis=0).astype(np.uint16),)
    return out


def paint(mesh, colour) -> tuple:
    """Give every vertex of a mesh one colour (r, g, b in 0..1)."""
    p, i = mesh[0], mesh[1]
    rgb = np.tile(np.clip(np.asarray(colour) * 255.0, 0, 255).astype(np.uint8),
                  (len(p), 1))
    return (p, i, rgb, *mesh[3:4])


def tag(mesh, part: int) -> tuple:
    """Mark every vertex of a mesh as belonging to animatable part `part`."""
    if len(mesh) < 3:
        mesh = paint(mesh, (0.78, 0.78, 0.78))
    return (mesh[0], mesh[1], mesh[2], np.full(len(mesh[0]), part, dtype=np.uint16))


def transform(mesh, scale=(1, 1, 1), translate=(0, 0, 0), yaw: float = 0.0,
              roll: float = 0.0, pivot=(0, 0, 0)) -> tuple:
    """Scale, rotate (roll about z, then yaw about y, around `pivot`), translate."""
    p, i, *rest = mesh
    p = p * np.asarray(scale, dtype=np.float32)
    if roll:
        piv = np.asarray(pivot, dtype=np.float32)
        q = p - piv
        c, s = np.cos(roll), np.sin(roll)
        x, y = q[:, 0].copy(), q[:, 1].copy()
        q[:, 0] = c * x - s * y
        q[:, 1] = s * x + c * y
        p = q + piv
    if yaw:
        c, s = np.cos(yaw), np.sin(yaw)
        x, z = p[:, 0].copy(), p[:, 2].copy()
        p[:, 0] = c * x + s * z
        p[:, 2] = -s * x + c * z
    p = p + np.asarray(translate, dtype=np.float32)
    return (p.astype(np.float32), i, *rest)


# --------------------------------------------------------------------------
# primitives -- each takes a single resolution knob so LODs are trivial
# --------------------------------------------------------------------------
def sphere(res: int = 16, radius: float = 1.0) -> Mesh:
    def fn(u, v):
        th = 2 * np.pi * u
        ph = np.pi * v
        return (radius * np.sin(ph) * np.cos(th),
                radius * np.cos(ph),
                radius * np.sin(ph) * np.sin(th))
    return parametric(fn, res * 2, res, wrap_u=True)


def torus(res: int = 16, major: float = 1.0, minor: float = 0.3) -> Mesh:
    def fn(u, v):
        a = 2 * np.pi * u
        b = 2 * np.pi * v
        r = major + minor * np.cos(b)
        return (r * np.cos(a), minor * np.sin(b), r * np.sin(a))
    return parametric(fn, res * 2, res, wrap_u=True, wrap_v=True)


def cylinder(res: int = 16, radius: float = 1.0, height: float = 1.0, caps: bool = True) -> Mesh:
    def side(u, v):
        a = 2 * np.pi * u
        return (radius * np.cos(a), height * (v - 0.5), radius * np.sin(a))

    parts = [parametric(side, res * 2, max(2, res // 2), wrap_u=True)]
    if caps:
        for sign in (1.0, -1.0):
            def cap(u, v, sign=sign):
                a = 2 * np.pi * u
                return (radius * v * np.cos(a), sign * height * 0.5, radius * v * np.sin(a))
            parts.append(parametric(cap, res * 2, 2, wrap_u=True))
    return merge(parts)


def plane(res: int = 4, width: float = 1.0, depth: float = 1.0) -> Mesh:
    def fn(u, v):
        return (width * (u - 0.5), np.zeros_like(u), depth * (v - 0.5))
    return parametric(fn, res, res)


def wall(res: int = 4, width: float = 1.0, height: float = 1.0) -> Mesh:
    def fn(u, v):
        return (width * (u - 0.5), height * v, np.zeros_like(u))
    return parametric(fn, res, res)


def box(w: float = 1.0, h: float = 1.0, d: float = 1.0) -> Mesh:
    """Fixed-resolution box: 8 verts, 12 triangles.  Has no meaningful LOD."""
    hx, hy, hz = w / 2, h / 2, d / 2
    p = np.array([
        [-hx, -hy, -hz], [hx, -hy, -hz], [hx, hy, -hz], [-hx, hy, -hz],
        [-hx, -hy, hz], [hx, -hy, hz], [hx, hy, hz], [-hx, hy, hz],
    ], dtype=np.float32)
    i = np.array([
        [0, 2, 1], [0, 3, 2], [4, 5, 6], [4, 6, 7],
        [0, 1, 5], [0, 5, 4], [3, 7, 6], [3, 6, 2],
        [0, 4, 7], [0, 7, 3], [1, 2, 6], [1, 6, 5],
    ], dtype=np.uint32)
    return p, i


def writing(lines: int, width: float, origin, seed: int = 0,
            line_gap: float = 0.28, colour=(0.12, 0.16, 0.30)) -> tuple:
    """Handwriting on a board: rows of word-sized dark strokes.

    `origin` is the top-left corner (x, y, z) of the text block, on the board.
    """
    rng = np.random.default_rng(seed)
    x0, y0, z0 = origin
    strokes = []
    for row in range(lines):
        x = x0
        limit = x0 + width * (0.55 + 0.45 * rng.random())
        while x < limit:
            w = 0.12 + 0.35 * rng.random()
            strokes.append(transform(box(w, 0.045, 0.012),
                                     translate=(x + w / 2, y0 - row * line_gap, z0)))
            x += w + 0.08
    return paint(merge(strokes), colour)


# --------------------------------------------------------------------------
# point clouds (volumetric capture stand-in, cf. SOTA 1.5)
# --------------------------------------------------------------------------
def sample_surface(mesh: Mesh, n_points: int, rng: np.random.Generator):
    """Area-weighted point sampling of a mesh -> (positions, barycentric normals)."""
    p, idx = mesh
    a, b, c = p[idx[:, 0]], p[idx[:, 1]], p[idx[:, 2]]
    cross = np.cross(b - a, c - a)
    area = 0.5 * np.linalg.norm(cross, axis=1)
    total = area.sum()
    if total <= 0:
        raise ValueError("degenerate mesh")
    pick = rng.choice(len(area), size=n_points, p=area / total)
    r1 = np.sqrt(rng.random(n_points))[:, None]
    r2 = rng.random(n_points)[:, None]
    pts = (1 - r1) * a[pick] + r1 * (1 - r2) * b[pick] + r1 * r2 * c[pick]
    nrm = cross[pick]
    nrm /= np.maximum(np.linalg.norm(nrm, axis=1, keepdims=True), 1e-9)
    return pts.astype(np.float32), nrm.astype(np.float32)


def humanoid(res: int = 14) -> Mesh:
    """Crude articulated figure -- enough to stand in for a captured participant."""
    parts = [
        transform(sphere(res, 0.11), translate=(0, 1.62, 0)),                   # head
        transform(cylinder(res, 0.17, 0.60), translate=(0, 1.20, 0)),           # torso
        transform(cylinder(max(4, res // 2), 0.055, 0.55), translate=(-0.23, 1.22, 0)),
        transform(cylinder(max(4, res // 2), 0.055, 0.55), translate=(0.23, 1.22, 0)),
        transform(cylinder(max(4, res // 2), 0.075, 0.85), translate=(-0.09, 0.45, 0)),
        transform(cylinder(max(4, res // 2), 0.075, 0.85), translate=(0.09, 0.45, 0)),
    ]
    return merge(parts)
