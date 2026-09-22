"""Concrete holographic lessons.

Four scenarios, chosen because they stress the streaming pipeline in different
ways.  A policy that only works on one of them has not solved the problem.

  classroom       one instructor at the board, students seated and still.
                  Attention is concentrated on a single participant.
  physics_lab     students working in pairs at benches with detailed apparatus.
                  Attention is spread across many mid-importance objects, and
                  the static geometry is unusually heavy.
  chemistry_lab   everyone crowded around one small demonstration.  A tiny
                  region of the scene needs extreme detail while a crowd of
                  participants competes for the same link.
  gym             a PE class: almost nothing static, many fast-moving people
                  spread over a large hall, and rapid camera sweeps.  This is
                  the worst case for viewport-dependent streaming.

Each scenario also ships its own camera trajectories, so every policy is
replayed against identical motion.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Callable

import numpy as np

from . import font, geometry as g
from .scene import Scene, SceneBuilder


@dataclass
class Scenario:
    name: str
    title: str
    blurb: str
    build: Callable[..., Scene]
    paths: dict[str, list] = field(default_factory=dict)
    default_path: str = "walk"
    # Where a person is put when the room opens. A benchmark path starts where
    # it has to start for the measurement, which is not always somewhere you
    # would want to stand -- the sports hall path begins outside the court.
    home: tuple[float, float, float] | None = None
    look: tuple[float, float, float] | None = None

    def path(self, name: str | None = None):
        return self.paths[name or self.default_path]

    def viewpoint(self):
        """(eye, look) for a viewer arriving in this room."""
        _t, eye, look = self.path()[0]
        return (list(self.home or eye), list(self.look or look))


# --------------------------------------------------------------------------
# shared furniture
# --------------------------------------------------------------------------
def _room(b: SceneBuilder, width=16.0, depth=12.0, height=4.0,
          floor_res=12, wall_res=10, floor_colour=(0.32, 0.34, 0.38)):
    b.mesh("floor", "structure", lambda r: g.plane(r, width, depth),
           floor_res, floor_colour)
    b.mesh("back_wall", "structure",
           lambda r: g.transform(g.wall(r, width, height), translate=(0, 0, -depth / 2)),
           wall_res, (0.26, 0.27, 0.31))
    b.mesh("side_wall", "structure",
           lambda r: g.transform(g.wall(r, depth, height),
                                 translate=(-width / 2, 0, 0), yaw=np.pi / 2),
           wall_res, (0.24, 0.25, 0.29))


def _bench(b: SceneBuilder, name, x, z, w=2.0, d=0.9, h=0.9,
           colour=(0.45, 0.47, 0.52)):
    b.solid(name, "furniture", g.merge([
        g.transform(g.box(w, 0.08, d), translate=(x, h, z)),
        g.transform(g.box(0.08, h, 0.08), translate=(x - w / 2 + 0.1, h / 2, z)),
        g.transform(g.box(0.08, h, 0.08), translate=(x + w / 2 - 0.1, h / 2, z)),
    ]), colour)


def _stool(b: SceneBuilder, name, x, z, colour=(0.30, 0.33, 0.40)):
    b.solid(name, "furniture", g.merge([
        g.transform(g.box(0.36, 0.06, 0.36), translate=(x, 0.55, z)),
        g.transform(g.box(0.06, 0.55, 0.06), translate=(x, 0.28, z)),
    ]), colour)


INK = (0.12, 0.16, 0.34)
RED_INK = (0.78, 0.16, 0.18)
GLASS = (0.74, 0.86, 0.94)


#: seconds of "handwriting" per pen stroke when the board fills up, and the
#: longest any one block of text may take (a lesson has a pace to keep)
STROKE_S = 0.055
MAX_WRITE_S = 7.0


def _reveal(mesh, first: int, last: int) -> dict:
    """The reveal animation for strokes first..last of a board mesh.

    Each stroke gets its own start time: STROKE_S apiece, plus time to carry
    the pen to the next stroke when it is far away (a new line, the other side
    of a drawing).  The server moves the writer along this same timetable, so
    the hand is on each stroke as it appears.
    """
    pos, part = mesh[0], mesh[3]
    times, t, prev = [], 0.0, None
    for k in range(first, last + 1):
        pts = pos[part == k]
        c = pts.mean(axis=0) if len(pts) else prev
        if prev is not None and c is not None:
            jump = float(np.linalg.norm(c[:2] - prev[:2]))
            if jump > 0.25:
                t += 0.20 + 0.30 * jump                 # step across to it
        times.append(t)
        t += STROKE_S
        prev = c
    total = max(1.5, t)
    squeeze = min(1.0, MAX_WRITE_S / total)             # keep the lesson's pace
    return {"kind": "reveal", "from": first, "to": last,
            "duration": round(total * squeeze, 3),
            "times": [round(x * squeeze, 3) for x in times]}


def _whiteboard(b: SceneBuilder, z=-5.9, width=6.0, pages: list | None = None,
                height: float = 0.14, top: float = 2.0, columns=None, gap: float = 1.7):
    """A board, and what is written on it during the lesson.

    `pages` is an ordered list of (state, block).  A block is a list of lines
    (written in column 0), a dict {"lines": [...], "column": i, "height": h},
    or a callable (first_part) -> (mesh, next_part) for a drawing.  Each state
    holds everything written so far; its new strokes carry a "reveal"
    animation, so they appear one by one as the teacher writes them.

    Text starts at `top` = 2 m: that is as high as a teacher's hand reaches,
    and the teacher is seen writing each line where it appears.
    """
    b.mesh("whiteboard", "content",
           lambda r: g.transform(g.wall(r, width, 2.4), translate=(0, 0.9, z)),
           16, (0.92, 0.93, 0.95))
    zz = z + 0.03
    columns = columns or [-width / 2 + 0.35]
    rows = [0.0] * len(columns)
    pages = pages or [("default", ["NOTES"])]
    written, part = [], 1
    variants, default = {}, None
    for state, block in pages:
        first = part
        if callable(block):
            mesh, part = block(part)
        else:
            if isinstance(block, list):
                block = {"lines": block}
            col = block.get("column", 0)
            h = block.get("height", height)
            y = top - rows[col]
            mesh, part = font.write(block["lines"], (columns[col], y, zz), height=h,
                                    line_gap=gap, first_part=part)
            rows[col] += len(block["lines"]) * h * gap
        written.append(mesh)
        whole = g.merge(written)
        anims = [_reveal(whole, first, part - 1)]
        if state == "default":
            default = whole                       # already on the board at the start
        else:
            variants[state] = (whole, None, anims)
    board = b.solid("annotation_layer", "content", default, INK, variants=variants)
    # remembered so a lesson can bring its own pages for this board
    board.board_cfg = {"z": zz, "width": width, "height": height, "top": top,
                       "gap": gap, "columns": list(columns)}


INK_COLOURS = {"ink": INK, "red": RED_INK, "green": (0.10, 0.50, 0.22)}


def add_board_pages(board, pages: list, prefix: str = "") -> None:
    """Add a lesson's own pages to a board as new states.

    A page is {"state", "lines" [, "at": [x, y], "height"]} for text, or
    {"state", "draw": [[[x, y], ...], ...], "labels": [[text, x, y], ...]} for
    a drawing, with an optional "colour" (ink, red, green).  Pages accumulate:
    each state shows everything written so far, the new strokes revealed as
    they are written.
    """
    from .scene import LODLevel, SceneBuilder
    cfg = board.board_cfg
    zz, h0, gap = cfg["z"], cfg["height"], cfg["gap"]
    written, part, row = [], 1, 0.0
    variants = {}
    for page in pages:
        first = part
        colour = INK_COLOURS.get(page.get("colour", "ink"), INK)
        parts = []
        if "lines" in page:
            h = float(page.get("height", h0))
            at = page.get("at") or [cfg["columns"][0], cfg["top"] - row]
            mesh, part = font.write(page["lines"], (at[0], at[1], zz), height=h,
                                    line_gap=gap, colour=colour, first_part=part)
            parts.append(mesh)
            if "at" not in page:
                row += len(page["lines"]) * h * gap
        if "draw" in page:
            mesh, part = font.draw(page["draw"], zz + 0.002, colour, first_part=part)
            parts.append(mesh)
        for text, x, y in page.get("labels", []):
            mesh, part = font.write([text], (x, y, zz + 0.004), height=0.11,
                                    colour=colour, first_part=part)
            parts.append(mesh)
        written.extend(parts)
        whole = g.merge(written)
        variants[prefix + page["state"]] = (whole, None, [_reveal(whole, first, part - 1)])
    SceneBuilder._add_variants(board, variants, lambda m: [LODLevel(0, *m)])


def _heart_sketch(z, first_part):
    """The four chambers drawn in red, then labelled -- stroke by stroke."""
    bars, part = [], first_part
    w, h = 0.55, 0.38
    for cx, cy in ((-0.33, 1.62), (0.33, 1.62), (-0.33, 1.18), (0.33, 1.18)):
        for dx, dy, bw, bh in ((0, h / 2, w, 0.035), (w / 2, 0, 0.035, h),
                               (0, -h / 2, w, 0.035), (-w / 2, 0, 0.035, h)):
            bars.append(g.tag(g.paint(g.transform(
                g.box(bw, bh, 0.012), translate=(cx + dx, cy + dy, z + 0.03)), RED_INK), part))
            part += 1
    for label, (cx, cy) in (("RA", (-0.33, 1.62)), ("LA", (0.33, 1.62)),
                            ("RV", (-0.33, 1.18)), ("LV", (0.33, 1.18))):
        mesh, part = font.write([label], (cx - 0.09, cy - 0.055, z + 0.035), height=0.11,
                                colour=RED_INK, first_part=part)
        bars.append(mesh)
    return g.merge(bars), part


# what people wear: a few palettes, so bodies (and captures) can be shared
SKIN = [(0.93, 0.76, 0.62), (0.78, 0.57, 0.42), (0.55, 0.38, 0.26)]
HAIR = [(0.30, 0.20, 0.11), (0.12, 0.10, 0.09), (0.62, 0.45, 0.24)]


def _palette(shirt, trousers=(0.20, 0.24, 0.38), k=0, shoes=(0.12, 0.11, 0.10)):
    return {"shirt": shirt, "trousers": trousers, "skin": SKIN[k % 3],
            "hair": HAIR[k % 3], "shoes": shoes}


TEACHER = _palette((0.62, 0.24, 0.26), (0.22, 0.22, 0.28), 0)
LAB_COAT = lambda k: _palette((0.93, 0.93, 0.95), (0.22, 0.26, 0.40), k)
STUDENT = lambda k: _palette([(0.30, 0.50, 0.85), (0.35, 0.62, 0.44),
                              (0.85, 0.62, 0.25)][k % 3], (0.20, 0.25, 0.40), k)
COACH = _palette((0.15, 0.17, 0.22), (0.15, 0.17, 0.22), 1, (0.9, 0.9, 0.9))
KIT = lambda k: _palette([(0.85, 0.22, 0.22), (0.22, 0.40, 0.85)][k % 2],
                         (0.10, 0.10, 0.12), k, (0.92, 0.92, 0.92))


def _person(b: SceneBuilder, name, semantic, x, z, points, palette, face, **kw):
    return b.participant(name, semantic, x, z, points, palette, face=face, **kw)


# --------------------------------------------------------------------------
# 1. lecture classroom
# --------------------------------------------------------------------------
def build_classroom(seed: int = 7, students: int = 6,
                    instructor_points: int = 60_000,
                    student_points: int = 24_000) -> Scene:
    b = SceneBuilder("classroom", "Lecture classroom",
                     "One instructor at the board; attention concentrated on a "
                     "single participant.", seed=seed)
    _room(b)
    _whiteboard(b, height=0.16, pages=[
        ("default", ["THE HEART AS A PUMP"]),
        ("sketch", lambda part: _heart_sketch(-5.9, part)),
        ("summary", lambda part: font.write(
            ["4 CHAMBERS", "VALVES OPEN ONE WAY", "ABOUT 70 BEATS / MIN"],
            (0.95, 1.78, -5.87), height=0.10, first_part=part)),
    ])

    for i in range(4):
        for j in range(3):
            x, z = -4.5 + i * 3.0, -2.5 + j * 2.2
            b.solid(f"desk_{i}{j}", "furniture",
                    g.transform(g.box(1.6, 0.08, 0.8), translate=(x, 0.75, z)),
                    (0.55, 0.42, 0.30))
            b.solid(f"chair_{i}{j}", "furniture", g.merge([
                g.transform(g.box(0.45, 0.06, 0.45), translate=(x, 0.45, z + 0.7)),
                g.transform(g.box(0.45, 0.50, 0.06), translate=(x, 0.72, z + 0.9)),
            ]), (0.30, 0.33, 0.40))

    def instrument(r, x, z, valve=False):
        parts = [
            g.paint(g.transform(g.torus(r, 0.22, 0.07), translate=(x, 1.15, z)),
                    (0.75, 0.78, 0.85)),
            g.paint(g.transform(g.cylinder(r, 0.10, 0.30), translate=(x, 0.94, z)),
                    (0.62, 0.66, 0.74)),
            g.paint(g.transform(g.sphere(r, 0.09), translate=(x, 1.38, z)),
                    (0.75, 0.78, 0.85)),
        ]
        if valve:                     # the valve flap, shown during the answer
            parts.append(g.tag(g.paint(g.transform(g.cylinder(r, 0.2, 0.025),
                                                   translate=(x, 1.15, z)), RED_INK), 8))
        return g.merge(parts)

    for k, (x, z) in enumerate([(-4.5, -2.5), (-1.5, -2.5), (1.5, -2.5), (4.5, -2.5)]):
        # the flap opens and shuts like a real valve: one way only
        flap = [{"kind": "swing", "part": 8, "pivot": [x - 0.2, 1.15, z],
                 "amp": 0.45, "bias": 0.45, "period": 0.9}]
        b.mesh(f"instrument_{k}", "equipment",
               lambda r, x=x, z=z: instrument(r, x, z), 40, (0.75, 0.78, 0.85),
               variants={"valve": (lambda r, x=x, z=z: instrument(r, x, z, True),
                                   None, flap)})

    b.mesh("anatomical_model", "equipment", lambda r: g.merge([
        g.transform(g.sphere(r, 0.30), translate=(-6.2, 1.35, -3.0)),
        g.transform(g.cylinder(r, 0.16, 0.70), translate=(-6.2, 0.85, -3.0)),
    ]), 64, (0.85, 0.62, 0.55))

    b.mesh("cad_assembly", "equipment", lambda r: g.merge([
        g.transform(g.torus(r, 0.45, 0.12), translate=(6.0, 1.30, -3.0)),
        g.transform(g.torus(r, 0.28, 0.08), translate=(6.0, 1.30, -3.0), yaw=1.0),
        g.transform(g.cylinder(r, 0.09, 0.60), translate=(6.0, 0.95, -3.0)),
    ]), 72, (0.60, 0.68, 0.78))

    _person(b, "instructor", "instructor", 0.0, -4.6, instructor_points, TEACHER,
            face=(0.0, 5.0), scale=1.1)
    for s in range(students):
        x, z = -4.0 + (s % 4) * 2.6, -1.4 + (s // 4) * 2.2
        _person(b, f"student_{s}", "student", x, z, student_points, STUDENT(s),
                face=(x, -6.0))
    return b.finish()


CLASSROOM_PATHS = {
    "walk": [
        (0.0,  (0.0, 1.65, 7.0),   (0.0, 1.40, -5.0)),
        (4.0,  (0.0, 1.65, 3.0),   (0.0, 1.40, -5.9)),
        (8.0,  (2.0, 1.65, 1.0),   (6.0, 1.30, -3.0)),
        (12.0, (1.5, 1.50, -1.0),  (1.5, 1.15, -2.5)),
        (16.0, (-2.0, 1.60, -0.5), (-6.2, 1.35, -3.0)),
        (20.0, (-1.0, 1.65, 1.5),  (0.0, 1.30, -4.6)),
    ],
    "look_around": [
        (0.0,  (0.0, 1.65, 2.0), (0.0, 1.40, -5.9)),
        (2.0,  (0.0, 1.65, 2.0), (7.0, 1.40, -2.0)),
        (4.0,  (0.0, 1.65, 2.0), (0.0, 1.40, 8.0)),
        (6.0,  (0.0, 1.65, 2.0), (-7.0, 1.40, -2.0)),
        (8.0,  (0.0, 1.65, 2.0), (0.0, 1.40, -5.9)),
        (10.0, (0.0, 1.65, 2.0), (6.0, 1.30, -3.0)),
    ],
}


# --------------------------------------------------------------------------
# 2. physics laboratory
# --------------------------------------------------------------------------
PENDULUM_LENGTH = 0.65
G = 9.81


def pendulum_period(length: float, amplitude: float = 0.0) -> float:
    """2 pi sqrt(L / g), with the first large-angle correction (1 + a^2 / 16)."""
    return round(2 * np.pi * np.sqrt(length / G) * (1 + amplitude ** 2 / 16), 3)


#: small-angle period of the standard pendulum -- the thing the students measure
PENDULUM_PERIOD = pendulum_period(PENDULUM_LENGTH)

#: what each bench changes about its pendulum; every bench finds something new
BENCHES = [
    {"what": "reference",   "length": 0.65, "bob": 0.075, "colour": (0.85, 0.66, 0.30), "amp": 0.30},
    {"what": "short",       "length": 0.40, "bob": 0.075, "colour": (0.85, 0.66, 0.30), "amp": 0.30},
    {"what": "heavy bob",   "length": 0.65, "bob": 0.105, "colour": (0.30, 0.31, 0.34), "amp": 0.30},
    {"what": "wide swing",  "length": 0.65, "bob": 0.075, "colour": (0.85, 0.66, 0.30), "amp": 1.00},
]


def _pendulum(r, x, z, angle=0.0, length=PENDULUM_LENGTH, bob=0.075,
              bob_colour=(0.85, 0.66, 0.30)):
    """A pendulum on a stand.  The rod and bob are part 1, so a released
    pendulum is the same geometry plus a swing animation."""
    pivot = (x, 1.73, z)
    swing = [g.transform(g.cylinder(max(4, r // 3), 0.012, length),
                         translate=(x, 1.73 - length / 2, z)),
             g.transform(g.sphere(r, bob), translate=(x, 1.73 - length, z))]
    if angle:
        swing = [g.transform(m, roll=angle, pivot=pivot) for m in swing]
    return g.merge([
        g.tag(g.paint(g.transform(g.box(0.9, 0.05, 0.05), translate=(x, 1.75, z)),
                      (0.55, 0.57, 0.62)), 0),
        g.tag(g.paint(g.transform(g.cylinder(max(4, r // 3), 0.02, 0.05),
                                  translate=(x - 0.4, 1.5, z)), (0.55, 0.57, 0.62)), 0),
        g.tag(g.paint(swing[0], (0.70, 0.72, 0.78)), 1),
        g.tag(g.paint(swing[1], bob_colour), 1),            # brass, or heavy iron
    ])


def _oscilloscope(r, x, z, trace=False):
    parts = [
        g.paint(g.transform(g.box(0.42, 0.30, 0.34), translate=(x, 1.10, z)), (0.30, 0.44, 0.42)),
        g.paint(g.transform(g.wall(max(4, r // 2), 0.30, 0.20), translate=(x, 1.02, z + 0.175)),
                (0.05, 0.08, 0.07)),
        g.paint(g.transform(g.cylinder(r, 0.025, 0.06), translate=(x - 0.14, 0.97, z + 0.18)),
                (0.75, 0.75, 0.75)),
        g.paint(g.transform(g.cylinder(r, 0.025, 0.06), translate=(x - 0.06, 0.97, z + 0.18)),
                (0.75, 0.75, 0.75)),
    ]
    if trace:                         # a green waveform on the screen (parts 10..35)
        n = 26
        for i in range(n):
            u = i / (n - 1)
            parts.append(g.tag(g.paint(g.transform(
                g.box(0.28 / n + 0.004, 0.012, 0.006),
                translate=(x - 0.14 + 0.28 * u, 1.12 + 0.06 * np.sin(u * 4 * np.pi),
                           z + 0.182)), (0.30, 1.0, 0.45)), 10 + i))
    return g.merge(parts)


def _optical_bench(r, x, z, beam=False):
    parts = [g.paint(g.transform(g.box(1.3, 0.05, 0.12), translate=(x, 0.98, z)),
                     (0.45, 0.47, 0.52))]
    for dx in (-0.42, 0.0, 0.42):
        parts.append(g.paint(g.transform(g.torus(r, 0.11, 0.022),
                                         translate=(x + dx, 1.16, z), yaw=np.pi / 2),
                             (0.66, 0.70, 0.78)))
        parts.append(g.paint(g.transform(g.cylinder(max(4, r // 3), 0.02, 0.16),
                                         translate=(x + dx, 1.06, z)), (0.55, 0.57, 0.62)))
    if beam:                          # the light gate is on: a red laser line (part 7)
        parts.append(g.tag(g.paint(g.transform(g.box(1.2, 0.012, 0.012),
                                               translate=(x, 1.16, z)), (1.0, 0.15, 0.12)), 7))
    return g.merge(parts)


def _vacuum_rig(r, running=False):
    chamber = (0.55, 0.82, 1.0) if running else (0.46, 0.52, 0.62)
    parts = [
        g.tag(g.paint(g.transform(g.sphere(r, 0.42), translate=(0.0, 1.55, -5.2)), chamber), 9),
        g.paint(g.transform(g.cylinder(r, 0.20, 0.95), translate=(0.0, 0.90, -5.2)),
                (0.50, 0.54, 0.62)),
        g.paint(g.transform(g.torus(r, 0.52, 0.06), translate=(0.0, 1.55, -5.2)),
                (0.62, 0.66, 0.74)),
        # a pressure gauge on the front; its needle swings when the pump runs
        g.paint(g.transform(g.cylinder(r, 0.11, 0.03), translate=(0.0, 1.0, -4.95)),
                (0.92, 0.92, 0.9)),
        g.tag(g.paint(g.transform(g.box(0.012, 0.09, 0.012), translate=(0.0, 1.04, -4.93),
                                  roll=(-1.1 if running else 0.9), pivot=(0.0, 1.0, -4.93)),
                      (0.8, 0.1, 0.1)), 6),
    ]
    return g.merge(parts)


def build_physics_lab(seed: int = 11, pairs: int = 4,
                      instructor_points: int = 55_000,
                      student_points: int = 26_000) -> Scene:
    b = SceneBuilder(
        "physics_lab", "Physics laboratory",
        "Students working in pairs on apparatus; attention is spread across "
        "many mid-importance objects and the static geometry is heavy.",
        seed=seed,
        # the apparatus being manipulated matters nearly as much as the tutor,
        # and the students doing the experiment matter more than in a lecture
        weights={"apparatus": 2.6, "student": 2.0, "instructor": 2.8})
    _room(b, width=17.0, depth=13.0)
    periods = [pendulum_period(bn["length"], bn["amp"] if bn["amp"] > 0.5 else 0.0)
               for bn in BENCHES]
    _whiteboard(b, z=-6.4, width=5.0, height=0.12, gap=1.6, top=2.0,
                columns=[-2.15, 0.25], pages=[
        ("default", ["THE SIMPLE PENDULUM"]),
        ("formula", ["T = 2 PI SQRT(L/G)"]),
        ("variables", ["L = LENGTH", "G = 9.81 M/S2"]),
        ("predict", ["SMALL ANGLES ONLY", f"PREDICT: T = {periods[0]:.2f} S"]),
        ("results", {"column": 1, "height": 0.1, "lines": [
            "RESULTS (10 SWINGS)",
            f"1  L=0.65  T={periods[0]:.2f} S",
            f"2  L=0.40  T={periods[1]:.2f} S",
            f"3  HEAVY   T={periods[2]:.2f} S",
            f"4  WIDE    T={periods[3]:.2f} S"]}),
        ("conclusion", {"column": 1, "height": 0.1, "lines": [
            "T DEPENDS ON L", "NOT ON THE MASS"]}),
    ])

    xs = [-5.4, -1.8, 1.8, 5.4][:pairs]
    for k, x in enumerate(xs):
        z = -2.0 if k % 2 == 0 else 1.4
        _bench(b, f"bench_{k}", x, z, w=2.6, d=1.0)
        _stool(b, f"stool_{k}a", x - 0.7, z + 0.85)
        _stool(b, f"stool_{k}b", x + 0.7, z + 0.85)
        # each bench changes one thing about its pendulum; released, it swings
        # (slowly damped) at the period that change produces
        bn = BENCHES[k % len(BENCHES)]
        period = pendulum_period(bn["length"], bn["amp"] if bn["amp"] > 0.5 else 0.0)
        pend = dict(length=bn["length"], bob=bn["bob"], bob_colour=bn["colour"])
        swing = [{"kind": "swing", "part": 1, "pivot": [x, 1.73, z - 0.25],
                  "amp": bn["amp"], "period": period, "decay": 0.015}]
        b.mesh(f"pendulum_{k}", "apparatus",
               lambda r, x=x, z=z, pend=pend: _pendulum(r, x, z - 0.25, **pend),
               44, (0.78, 0.80, 0.86),
               variants={"released": (lambda r, x=x, z=z, pend=pend:
                                      _pendulum(r, x, z - 0.25, **pend), None, swing)})
        b.mesh(f"scope_{k}", "apparatus",
               lambda r, x=x, z=z: _oscilloscope(r, x + 0.85, z), 36, (0.35, 0.60, 0.55),
               variants={"trace": (lambda r, x=x, z=z: _oscilloscope(r, x + 0.85, z, True),
                                   None, [{"kind": "sweep", "from": 10, "to": 35,
                                           "period": period / 2}])})
        b.mesh(f"optics_{k}", "apparatus",
               lambda r, x=x, z=z: _optical_bench(r, x - 0.45, z + 0.2),
               52, (0.66, 0.70, 0.78),
               variants={"measuring": (lambda r, x=x, z=z:
                                       _optical_bench(r, x - 0.45, z + 0.2, True), None,
                                       [{"kind": "pulse", "part": 7,
                                         "from": [1.0, 0.15, 0.12], "to": [0.35, 0.04, 0.04],
                                         "period": period / 2}])})

    # a heavy shared rig at the front of the room: switching it on, the
    # chamber lights up and the gauge needle falls as the air is pumped out
    b.mesh("vacuum_rig", "equipment", _vacuum_rig, 80, (0.58, 0.64, 0.74),
           variants={"running": (lambda r: _vacuum_rig(r, True), None, [
               {"kind": "turn", "part": 6, "pivot": [0.0, 1.0, -4.93],
                "from": 2.0, "to": 0.0, "duration": 4.0},
               {"kind": "colour", "part": 9, "from": [0.46, 0.52, 0.62],
                "to": [0.55, 0.82, 1.0], "duration": 3.0}])})

    _person(b, "instructor", "instructor", -1.6, -4.4, instructor_points, TEACHER,
            face=(-1.6, 5.0), scale=1.1)
    for k, x in enumerate(xs):
        z = -2.0 if k % 2 == 0 else 1.4
        for side, dx in (("a", -0.7), ("b", 0.7)):
            _person(b, f"student_{k}{side}", "student", x + dx, z + 0.95,
                    student_points, STUDENT(2 * k + (side == "b")), face=(x + dx, z))
    return b.finish()


PHYSICS_PATHS = {
    "walk": [
        (0.0,  (0.0, 1.65, 6.4),   (0.0, 1.35, -5.2)),
        (4.0,  (-3.4, 1.60, 3.2),  (-5.4, 1.20, -2.0)),
        (8.0,  (-4.8, 1.45, 0.4),  (-5.4, 1.10, -2.2)),
        (12.0, (-0.6, 1.55, 2.6),  (1.8, 1.15, 1.4)),
        (16.0, (2.4, 1.45, 1.0),   (5.4, 1.10, -2.0)),
        (20.0, (0.6, 1.62, 0.0),   (0.0, 1.45, -5.2)),
    ],
    "bench_inspection": [
        (0.0, (-5.4, 1.45, 0.2), (-5.4, 1.10, -2.0)),
        (3.0, (-5.4, 1.30, -0.9), (-5.4, 1.08, -2.2)),
        (6.0, (-4.0, 1.40, -0.6), (-1.8, 1.15, 1.4)),
        (9.0, (-1.8, 1.35, 2.8), (-1.8, 1.10, 1.4)),
        (12.0, (0.4, 1.55, 1.6), (0.0, 1.45, -5.2)),
    ],
}


# --------------------------------------------------------------------------
# 3. chemistry laboratory
# --------------------------------------------------------------------------
def _flask(r, x, y, z, s=1.0):
    return g.merge([
        g.transform(g.sphere(r, 0.11 * s), translate=(x, y + 0.11 * s, z)),
        g.transform(g.cylinder(max(4, r // 2), 0.032 * s, 0.14 * s),
                    translate=(x, y + 0.28 * s, z)),
        g.transform(g.torus(max(6, r // 2), 0.036 * s, 0.008 * s),
                    translate=(x, y + 0.35 * s, z)),
    ])


def _condenser(r, x, y, z):
    parts = [g.transform(g.cylinder(r, 0.030, 0.58), translate=(x, y + 0.29, z))]
    for k in range(6):
        parts.append(g.transform(g.torus(r, 0.055, 0.013),
                                 translate=(x, y + 0.08 + k * 0.085, z)))
    return g.merge(parts)


def _burette(r, x, y, z):
    return g.merge([
        g.transform(g.cylinder(r, 0.018, 0.70), translate=(x, y + 0.45, z)),
        g.transform(g.sphere(max(6, r // 2), 0.028), translate=(x, y + 0.08, z)),
        g.transform(g.box(0.05, 0.9, 0.05), translate=(x + 0.10, y + 0.45, z)),
    ])


def build_chemistry_lab(seed: int = 13, students: int = 8,
                        instructor_points: int = 60_000,
                        student_points: int = 26_000) -> Scene:
    b = SceneBuilder(
        "chemistry_lab", "Chemistry laboratory",
        "Everyone crowds around one small demonstration: a tiny region needs "
        "extreme detail while a crowd competes for the same link.",
        seed=seed,
        weights={"demo": 3.6, "apparatus": 2.4, "student": 1.8})
    _room(b, width=15.0, depth=12.0)
    _whiteboard(b, z=-5.9, width=4.0, height=0.13, top=2.0, pages=[
        ("default", ["ACID-BASE TITRATION"]),
        ("intro", ["HCL + NAOH -> NACL + H2O", "INDICATOR: PHENOLPHTHALEIN"]),
        ("result", ["END POINT: 24.5 ML NAOH", "C(HCL) = 0.098 MOL/L"]),
    ])

    # fume hood along the back wall
    b.solid("fume_hood", "furniture", g.merge([
        g.transform(g.box(3.2, 0.10, 1.0), translate=(-4.4, 0.95, -5.0)),
        g.transform(g.box(3.2, 1.30, 0.06), translate=(-4.4, 1.70, -5.45)),
        g.transform(g.box(0.06, 1.30, 1.0), translate=(-6.0, 1.70, -5.0)),
        g.transform(g.box(0.06, 1.30, 1.0), translate=(-2.8, 1.70, -5.0)),
    ]), (0.40, 0.44, 0.50))

    # the demonstration bench: small, central, and the reason everyone is here
    _bench(b, "demo_bench", 0.0, -3.2, w=2.8, d=1.1, h=0.95,
           colour=(0.38, 0.40, 0.46))
    # The demonstration.  Its states are the story of the titration: an empty
    # flask, acid with indicator, turning faintly pink as base drips in, the
    # sudden end point, then heating.  Each is a new look to deliver.
    def reaction(r, liquid=None, heated=False):
        x, y, z, sc = 0.0, 1.00, -3.2, 1.35
        bulb = g.transform(g.sphere(r, 0.11 * sc), translate=(x, y + 0.11 * sc, z))
        neck = g.merge([g.transform(g.cylinder(max(4, r // 2), 0.032 * sc, 0.14 * sc),
                                    translate=(x, y + 0.28 * sc, z)),
                        g.transform(g.torus(max(6, r // 2), 0.036 * sc, 0.008 * sc),
                                    translate=(x, y + 0.35 * sc, z))])
        parts = [g.tag(g.paint(bulb, liquid or GLASS), 5), g.paint(neck, GLASS),
                 g.paint(g.transform(g.torus(r, 0.20, 0.035), translate=(x, 1.02, z)),
                         (0.45, 0.47, 0.52))]
        if heated:
            parts.append(g.tag(g.paint(g.transform(g.sphere(max(6, r // 2), 0.06),
                                                   scale=(1, 1.8, 1), translate=(x, 0.93, z)),
                                       (1.0, 0.55, 0.12)), 4))                 # flame
            for k, (dx, dy, rad) in enumerate(((0.0, 0.62, 0.035), (0.05, 0.72, 0.028),
                                               (-0.04, 0.8, 0.022), (0.02, 0.88, 0.018))):
                parts.append(g.tag(g.paint(g.transform(g.sphere(max(6, r // 2), rad),
                                                       translate=(x + dx, y + dy, z)),
                                           (0.95, 0.95, 0.98)), 20 + k))       # steam
        return g.merge(parts)

    # the liquid (part 5) changes colour gradually, as a real titration does:
    # it creeps towards pink while base drips in, then flips at the end point
    CLEAR, PALE, FAINT, PINK = GLASS, [0.86, 0.90, 0.96], [0.96, 0.78, 0.86], [0.92, 0.22, 0.58]
    colour = lambda a, b, d: [{"kind": "colour", "part": 5, "from": list(a),
                               "to": list(b), "duration": d}]
    b.mesh("reaction_demo", "demo", lambda r: g.merge([reaction(r)]), 88, GLASS, variants={
        "indicator": (lambda r: reaction(r, PALE), None, colour(CLEAR, PALE, 2.5)),
        "pinkish":   (lambda r: reaction(r, FAINT), None, colour(PALE, FAINT, 6.5)),
        "endpoint":  (lambda r: reaction(r, PINK), None, colour(FAINT, PINK, 1.2)),
        "heated":    (lambda r: reaction(r, PINK, heated=True), None, [
            {"kind": "flicker", "part": 4, "pivot": [0.0, 0.88, -3.2],
             "amount": 0.3, "freq": 6.0},
            {"kind": "rise", "from": 20, "to": 23, "height": 0.35, "period": 1.8}]),
    })

    def condenser(r, vapour=False):
        parts = [g.paint(_condenser(r, -0.62, 0.99, -3.2), GLASS)]
        if vapour:                    # vapour bubbles, parts 40..46
            for k in range(7):
                parts.append(g.tag(g.paint(g.transform(g.sphere(max(6, r // 3), 0.022),
                                                       translate=(-0.62, 1.1, -3.2)),
                                           (0.97, 0.97, 1.0)), 40 + k))
        return g.merge(parts)

    def burette(r, dripping=False):
        parts = [g.paint(_burette(r, 0.62, 0.99, -3.2), GLASS)]
        if dripping:                  # drops at the tip, parts 30..32
            for k in range(3):
                parts.append(g.tag(g.paint(g.transform(g.sphere(max(6, r // 3), 0.013),
                                                       translate=(0.62, 1.06, -3.2)),
                                           (0.45, 0.62, 0.95)), 30 + k))
        return g.merge(parts)

    b.mesh("condenser_demo", "apparatus", condenser, 60, GLASS,
           variants={"vapour": (lambda r: condenser(r, True), None, [
               {"kind": "rise", "from": 40, "to": 46, "height": 0.45, "period": 2.4}])})
    b.mesh("burette_demo", "apparatus", burette, 52, GLASS,
           variants={"dripping": (lambda r: burette(r, True), None, [
               {"kind": "fall", "from": 30, "to": 32, "dist": 0.13, "period": 0.9}])})

    # student benches with their own glassware
    for k, (x, z) in enumerate([(-4.6, 0.4), (-1.6, 2.4), (1.6, 2.4), (4.6, 0.4)]):
        _bench(b, f"bench_{k}", x, z, w=2.4, d=1.0)
        b.mesh(f"glassware_{k}", "apparatus", lambda r, x=x, z=z: g.merge([
            _flask(r, x - 0.55, 0.94, z),
            _flask(r, x - 0.15, 0.94, z, s=0.8),
            _burette(r, x + 0.55, 0.94, z),
        ]), 46, (0.74, 0.82, 0.88))

    _person(b, "instructor", "instructor", 0.0, -4.3, instructor_points, LAB_COAT(0),
            face=(0.0, 3.0), scale=1.1)

    # the crowd: a ring of students around the demonstration bench
    for s in range(students):
        a = np.pi * (0.15 + 0.7 * s / max(1, students - 1))
        x = 0.0 + 2.3 * np.cos(a)
        z = -3.2 + 2.3 * np.sin(a)
        _person(b, f"student_{s}", "student", float(x), float(z), student_points,
                LAB_COAT(s + 1), face=(0.0, -3.2))
    return b.finish()


CHEMISTRY_PATHS = {
    "walk": [
        (0.0,  (0.0, 1.65, 4.6),   (0.0, 1.30, -3.2)),
        (4.0,  (0.0, 1.62, 0.4),   (0.0, 1.25, -3.2)),
        (8.0,  (0.2, 1.50, -1.4),  (0.0, 1.15, -3.2)),   # lean into the demo
        (12.0, (0.2, 1.45, -1.9),  (0.0, 1.10, -3.2)),
        (16.0, (-2.2, 1.55, -0.6), (-4.4, 1.30, -5.0)),  # glance at the hood
        (20.0, (0.0, 1.62, 0.8),   (0.0, 1.25, -3.2)),
    ],
    "close_demo": [
        (0.0, (0.0, 1.55, -1.2), (0.0, 1.15, -3.2)),
        (4.0, (0.45, 1.42, -2.0), (0.0, 1.12, -3.2)),
        (8.0, (-0.45, 1.42, -2.0), (0.0, 1.12, -3.2)),
        (12.0, (0.0, 1.58, -0.8), (0.0, 1.20, -3.2)),
    ],
}


# --------------------------------------------------------------------------
# 4. PE class in a sports hall
# --------------------------------------------------------------------------
#: where the coach stands, and the class's places in three rows of four
GYM_COACH = (0.0, -4.6)
GYM_FORMATION = [(x, z) for z in (-1.6, 0.7, 3.0) for x in (-4.2, -1.4, 1.4, 4.2)]


def build_gym(seed: int = 17, athletes: int = 12,
              coach_points: int = 55_000,
              athlete_points: int = 22_000) -> Scene:
    b = SceneBuilder(
        "gym", "PE class (sports hall)",
        "Almost nothing static, many fast-moving people spread over a large "
        "hall: the worst case for viewport-dependent streaming.",
        seed=seed,
        # in a PE lesson the moving bodies *are* the content
        weights={"athlete": 2.4, "instructor": 2.8, "structure": 0.3,
                 "furniture": 0.5})
    b.mesh("court", "structure", lambda r: g.plane(r, 30, 18), 14, (0.55, 0.42, 0.28))
    b.mesh("centre_circle", "structure",
           lambda r: g.transform(g.torus(r, 1.8, 0.03), translate=(0, 0.02, 0)),
           24, (0.90, 0.90, 0.92))
    b.mesh("end_wall", "structure",
           lambda r: g.transform(g.wall(r, 30, 6), translate=(0, 0, -9)),
           10, (0.30, 0.32, 0.36))
    b.mesh("side_wall_gym", "structure",
           lambda r: g.transform(g.wall(r, 18, 6), translate=(-15, 0, 0), yaw=np.pi / 2),
           10, (0.28, 0.30, 0.34))

    for side in (-1, 1):
        b.mesh(f"goal_{'n' if side < 0 else 's'}", "furniture",
               lambda r, side=side: g.merge([
                   g.transform(g.cylinder(r, 0.06, 2.4), translate=(-1.8, 1.2, side * 8.2)),
                   g.transform(g.cylinder(r, 0.06, 2.4), translate=(1.8, 1.2, side * 8.2)),
                   g.transform(g.box(3.72, 0.11, 0.11), translate=(0.0, 2.4, side * 8.2)),
               ]), 20, (0.85, 0.86, 0.90))

    # wall bars and a spectator bench: the only other static geometry
    b.solid("wall_bars", "furniture", g.merge([
        # left of the logos on the end wall, which a demo puts up there
        g.transform(g.box(3.0, 0.06, 0.06), translate=(-13.0, 0.5 + k * 0.35, -8.7))
        for k in range(9)
    ]), (0.62, 0.48, 0.32))
    b.solid("bench", "furniture", g.merge([
        g.transform(g.box(4.0, 0.08, 0.35), translate=(9.0, 0.45, -8.4)),
        g.transform(g.box(0.08, 0.45, 0.35), translate=(7.2, 0.22, -8.4)),
        g.transform(g.box(0.08, 0.45, 0.35), translate=(10.8, 0.22, -8.4)),
    ]), (0.50, 0.52, 0.58))

    # the coach stands in front of the class; the class stands in three rows
    _person(b, "instructor", "instructor", *GYM_COACH, coach_points, COACH,
            face=(0.0, 3.0), scale=1.05)
    for k in range(athletes):
        x, z = GYM_FORMATION[k % len(GYM_FORMATION)]
        _person(b, f"athlete_{k}", "athlete", x, z, athlete_points, KIT(k),
                face=GYM_COACH,
                default=("program", ["jumping_jacks", "squats", "arm_circles",
                                     "high_knees", "lunges", "run"], 6.0))
    return b.finish()


GYM_PATHS = {
    "walk": [
        (0.0,  (0.0, 1.90, 11.0),  (0.0, 1.10, 0.7)),
        (5.0,  (6.5, 1.80, 6.0),   (-1.0, 1.00, 0.0)),
        (10.0, (7.5, 1.70, 0.5),   (0.0, 1.20, 0.5)),
        (15.0, (3.0, 1.70, -1.5),  (0.0, 1.40, -4.6)),
        (20.0, (0.0, 1.90, 8.0),   (0.0, 1.10, 0.7)),
    ],
    "fast_sweep": [
        (0.0,  (0.0, 1.70, 6.0), (0.0, 1.10, 0.7)),
        (2.0,  (0.0, 1.70, 6.0), (12.0, 1.40, -4.0)),
        (4.0,  (0.0, 1.70, 6.0), (0.0, 1.40, 14.0)),
        (6.0,  (0.0, 1.70, 6.0), (-12.0, 1.40, -4.0)),
        (8.0,  (0.0, 1.70, 6.0), (0.0, 1.40, -9.0)),
        (10.0, (0.0, 1.70, 6.0), (0.0, 1.10, 0.7)),
    ],
}


# --------------------------------------------------------------------------
# 5. Hazard lab: experiments no school would allow, safe because it is virtual
# --------------------------------------------------------------------------
#: the three demonstration benches (x, z); bench tops are at BENCH_TOP
ALKALI, THERMITE, BALLOON = (-3.6, -3.0), (0.0, -3.0), (3.6, -3.0)
BENCH_TOP = 0.99
BENCH_W, BENCH_D = 2.2, 1.0

HOT_WHITE, FLAME = [1.0, 0.97, 0.80], [1.0, 0.62, 0.16]
EMBER, SMOKE = [0.85, 0.22, 0.06], [0.30, 0.29, 0.29]
WATER = (0.30, 0.52, 0.80)
LILAC = (0.80, 0.52, 1.0)


def _ball(r, rad, at, colour, part=None, scale=(1, 1, 1)):
    m = g.paint(g.transform(g.sphere(max(3, r), rad), scale=scale, translate=at), colour)
    return g.tag(m, part) if part is not None else m


def _cloud(r, first, n, at, rad, colour, jitter=0.0, seed=0, lumps=1):
    """n small particles (parts first..first+n-1) sitting at `at`; the viewer
    throws them about (sparks, spray, smoke).  With `lumps` > 1 each one is a
    clump of blobs in slightly different greys -- a puff of smoke."""
    rng = np.random.default_rng(seed)
    out = []
    for k in range(n):
        d = rng.normal(size=3) * jitter
        c = (at[0] + d[0], at[1] + abs(d[1]), at[2] + d[2])
        if lumps == 1:
            out.append(_ball(r, rad, c, colour, first + k))
            continue
        blobs = []
        for _ in range(lumps):
            o = rng.normal(size=3) * rad * 0.55
            shade = np.clip(np.asarray(colour) * (0.8 + 0.35 * rng.random()), 0, 1)
            blobs.append(_ball(max(3, r // 2), rad * (0.55 + 0.45 * rng.random()),
                               (c[0] + o[0], c[1] + o[1], c[2] + o[2]), shade))
        out.append(g.tag(g.merge(blobs), first + k))
    return out


def _smoke(first, last, opacity=0.5):
    """Smoke is drawn see-through."""
    return {"kind": "soft", "parts": list(range(first, last + 1)), "opacity": opacity}


def _fireball(r, part, centre, radius, seed=0):
    """A fireball as a cluster of overlapping, lumpy puffs (one part), so it
    billows instead of looking like a ball; the viewer grows and burns it."""
    rng = np.random.default_rng(seed)
    puffs = []
    for k in range(18):
        d = rng.normal(size=3) * radius * 0.38
        d[1] = abs(d[1]) * 0.8
        rad = radius * (0.28 + 0.3 * rng.random())
        puffs.append(g.transform(g.sphere(max(4, r // 3), rad),
                                 scale=(1, 0.8 + 0.4 * rng.random(), 1),
                                 translate=(centre[0] + d[0], centre[1] + d[1], centre[2] + d[2])))
    return g.tag(g.paint(g.merge(puffs), FLAME), part)


def _flame(r, part, base, height, width, bottom=HOT_WHITE, top=(1.0, 0.35, 0.05), seed=0):
    """A flame as tongues of fire: white-hot and wide at the base, narrower,
    wispier and redder towards the tip (one part, flickered by the viewer)."""
    rng = np.random.default_rng(seed)
    tongues, n = [], 14
    for k in range(n):
        f = k / (n - 1)
        w = width * (1.0 - 0.7 * f) * (0.7 + 0.5 * rng.random())
        d = rng.normal(size=2) * width * 0.35 * (0.3 + f)
        colour = np.asarray(bottom) * (1 - f) + np.asarray(top) * f
        tongues.append(g.paint(g.transform(
            g.sphere(max(4, r // 3), w), scale=(1, 1.6 + rng.random(), 1),
            translate=(base[0] + d[0], base[1] + f * height, base[2] + d[1])), colour))
    return g.tag(g.merge(tongues), part)


def _shards(first, n, at, colour, size=0.05, seed=1):
    """Broken glass or rubber: thin flakes at random angles."""
    rng = np.random.default_rng(seed)
    out = []
    for k in range(n):
        s = size * (0.5 + rng.random())
        flake = g.transform(g.box(s, 0.004, s * 0.7), roll=rng.random() * 3.1,
                            yaw=rng.random() * 6.3, translate=at)
        out.append(g.tag(g.paint(flake, colour), first + k))
    return out


def _table(x, z):
    """Where debris lands on a bench top instead of falling to the floor."""
    return [x - BENCH_W / 2, x + BENCH_W / 2, z - BENCH_D / 2, z + BENCH_D / 2, BENCH_TOP]


def _light(at, colour, intensity, flicker=0.0, decay=0.0):
    """A light the viewer adds at a fire or a blast (flickering, or a flash
    that dies away over `decay` seconds)."""
    return {"kind": "light", "at": list(at), "colour": list(colour),
            "intensity": intensity, "flicker": flicker, "decay": decay}


def _burst(first, last, centre, speed, spread=1.0, up=0.8, period=None, table=None,
           hot=None, cool=None, cool_s=1.0):
    """Particles thrown out of `centre` under gravity: once (debris, spray)
    or over and over (a fountain of sparks), landing on a bench or the floor."""
    a = {"kind": "burst", "from": first, "to": last, "centre": list(centre),
         "speed": speed, "spread": spread, "up": up, "gravity": 9.8, "floor": 0.0}
    if period:
        a["period"] = period
    if table:
        a["table"] = table
    if hot and cool:
        a.update(hot=list(hot), cool=list(cool), cool_s=cool_s)
    return a


def build_hazard_lab(seed: int = 23, students: int = 8,
                     instructor_points: int = 60_000,
                     student_points: int = 24_000) -> Scene:
    b = SceneBuilder(
        "hazard_lab", "Hazard lab (virtual only)",
        "Experiments far too dangerous for a real classroom -- explosions, "
        "molten iron, fireballs -- watched from arm's length.",
        seed=seed,
        weights={"demo": 3.6, "apparatus": 2.2, "student": 1.6})
    _room(b, width=15.0, depth=12.0, floor_colour=(0.30, 0.31, 0.33))
    _whiteboard(b, z=-5.9, width=6.0, height=0.13, top=2.0,
                pages=[("default", ["HAZARD LAB"])])
    # hazard stripes along the back wall: this is the dangerous room
    b.solid("hazard_stripes", "furniture", g.merge([
        g.paint(g.transform(g.box(0.35, 0.12, 0.02), roll=0.6,
                            translate=(-7.2 + 0.45 * k, 0.35, -5.95)),
                (0.95, 0.78, 0.10) if k % 2 == 0 else (0.08, 0.08, 0.08))
        for k in range(33)]), (0.95, 0.78, 0.1))

    for name, (x, z) in (("bench_alkali", ALKALI), ("bench_thermite", THERMITE),
                         ("bench_balloon", BALLOON)):
        _bench(b, name, x, z, w=BENCH_W, d=BENCH_D, h=0.95, colour=(0.30, 0.32, 0.36))

    # ---- 1. alkali metals in water -------------------------------------
    xa, za = ALKALI
    lump = (xa - 0.12, 1.125, za)

    def trough(r, metal=None):
        glass = [g.transform(g.box(0.62, 0.01, 0.36), translate=(xa, BENCH_TOP + 0.005, za))]
        if metal != "caesium":
            glass += [g.transform(g.box(0.62, 0.16, 0.008),
                                  translate=(xa, BENCH_TOP + 0.08, za + s * 0.176))
                      for s in (1, -1)]
            glass += [g.transform(g.box(0.008, 0.16, 0.36),
                                  translate=(xa + s * 0.306, BENCH_TOP + 0.08, za))
                      for s in (1, -1)]
        parts = [g.paint(g.merge(glass), GLASS)]
        if metal != "caesium":
            parts.append(g.paint(g.transform(g.box(0.60, 0.125, 0.34),
                                             translate=(xa, BENCH_TOP + 0.067, za)), WATER))
        # the metals wait under oil in jars at the side of the bench
        for k in range(4):
            jar = (xa + 0.55, BENCH_TOP + 0.06, za - 0.3 + 0.2 * k)
            parts.append(g.paint(g.transform(g.cylinder(max(4, r // 2), 0.045, 0.12),
                                             translate=jar), (0.86, 0.80, 0.55)))
        if metal in ("lithium", "sodium", "potassium"):
            parts.append(_ball(r // 2, {"lithium": 0.02, "sodium": 0.022,
                                        "potassium": 0.024}[metal], lump,
                               (0.80, 0.80, 0.76), 5))
            parts += _cloud(r // 4, 20, 8, lump, 0.008, (0.92, 0.95, 1.0), jitter=0.02, seed=3)
        if metal == "sodium":
            parts.append(_flame(r, 6, (lump[0], lump[1] + 0.02, lump[2]), 0.09, 0.025,
                                bottom=(1.0, 0.95, 0.6), top=(1.0, 0.5, 0.05), seed=31))
            parts += _cloud(r // 4, 40, 12, lump, 0.007, (1.0, 0.85, 0.35), seed=4)
        if metal == "potassium":
            parts.append(_flame(r, 6, (lump[0], lump[1] + 0.02, lump[2]), 0.26, 0.05,
                                bottom=(0.72, 0.45, 0.95), top=(0.55, 0.2, 0.85), seed=32))
            parts += _cloud(r // 4, 40, 16, lump, 0.008, (0.95, 0.80, 1.0), seed=5)
        if metal == "caesium":
            centre = (xa, 1.10, za)
            parts.append(g.paint(g.transform(g.cylinder(max(6, r), 0.5, 0.004),
                                             translate=(xa, BENCH_TOP + 0.003, za)), WATER))
            parts.append(_fireball(r, 7, (xa, 1.25, za), 0.55, seed=21))
            parts += _shards(60, 24, centre, GLASS)                              # the trough
            parts += _cloud(r // 4, 90, 30, centre, 0.02, WATER, seed=6)          # spray
            parts += _cloud(r // 3, 130, 10, (xa, 1.2, za), 0.08, (0.72, 0.72, 0.72),
                            jitter=0.12, seed=7, lumps=4)
        return g.merge(parts)

    flame_at = list(lump)
    table_a = _table(xa, za)
    b.mesh("alkali_demo", "demo", trough, 40, GLASS, variants={
        "lithium": (lambda r: trough(r, "lithium"), None, [
            {"kind": "rise", "from": 20, "to": 27, "height": 0.12, "period": 1.6}]),
        "sodium": (lambda r: trough(r, "sodium"), None, [
            {"kind": "rise", "from": 20, "to": 27, "height": 0.14, "period": 0.8},
            {"kind": "flicker", "part": 6, "pivot": flame_at, "amount": 0.4, "freq": 9.0},
            _burst(40, 51, lump, 0.9, spread=0.8, up=0.9, period=0.45,
                   table=[xa - 0.3, xa + 0.3, za - 0.17, za + 0.17, 1.115],
                   hot=[1, 0.9, 0.5], cool=EMBER, cool_s=0.4),
            {"kind": "hot", "parts": [6] + list(range(40, 52))},
            _light((lump[0], 1.25, lump[2]), (1.0, 0.7, 0.3), 1.6, flicker=0.4)]),
        "potassium": (lambda r: trough(r, "potassium"), None, [
            {"kind": "rise", "from": 20, "to": 27, "height": 0.16, "period": 0.6},
            {"kind": "flicker", "part": 6, "pivot": flame_at, "amount": 0.5, "freq": 11.0},
            _burst(40, 55, lump, 1.4, spread=1.0, up=0.9, period=0.35, table=table_a,
                   hot=[1, 0.95, 1.0], cool=[0.55, 0.25, 0.8], cool_s=0.5),
            {"kind": "hot", "parts": [6] + list(range(40, 56))},
            _light((lump[0], 1.3, lump[2]), (0.8, 0.55, 1.0), 2.4, flicker=0.5)]),
        "caesium": (lambda r: trough(r, "caesium"), None, [
            {"kind": "blast", "part": 7, "centre": [xa, 1.25, za], "grow": 0.18,
             "duration": 1.6},
            _burst(60, 83, (xa, 1.10, za), 3.4, spread=1.0, up=0.7, table=table_a),
            _burst(90, 119, (xa, 1.10, za), 4.0, spread=1.0, up=0.9, table=table_a),
            _smoke(130, 139),
            {"kind": "rise", "from": 130, "to": 139, "height": 1.6, "period": 3.0,
             "grow": 2.5, "drift": 0.15},
            {"kind": "hot", "parts": [7], "unlit": True},
            _light((xa, 1.4, za), (1.0, 0.75, 0.45), 14.0, decay=0.45)]),
    })

    # ---- 2. thermite ---------------------------------------------------
    xt, zt = THERMITE
    POT, STEEL, SAND = (0.72, 0.36, 0.22), (0.55, 0.57, 0.60), (0.78, 0.68, 0.48)

    def thermite(r, stage=None):
        stand = [
            g.transform(g.cylinder(max(4, r // 2), 0.012, 0.75), translate=(xt + 0.31, 1.39, zt)),
            g.transform(g.box(0.22, 0.012, 0.012), translate=(xt + 0.2, 1.53, zt)),
            g.transform(g.torus(max(6, r // 2), 0.09, 0.008), translate=(xt, 1.53, zt))]
        stand += [g.transform(g.cylinder(4, 0.008, 0.2),
                              translate=(xt + sx * 0.15, 1.12, zt + sz * 0.15))
                  for sx in (1, -1) for sz in (1, -1)]
        parts = [
            g.paint(g.transform(g.box(0.8, 0.05, 0.55),
                                translate=(xt, BENCH_TOP + 0.025, zt)), SAND),
            g.paint(g.merge(stand), STEEL),       # a ring stand over a steel plate
        ]
        if stage == "cooled":                     # burned through: a hole in the plate
            ring = [g.transform(g.box(0.34, 0.012, 0.11), translate=(xt, 1.22, zt + s * 0.115))
                    for s in (1, -1)]
            ring += [g.transform(g.box(0.11, 0.012, 0.12), translate=(xt + s * 0.115, 1.22, zt))
                     for s in (1, -1)]
            parts.append(g.paint(g.merge(ring), (0.35, 0.30, 0.28)))
        else:
            parts.append(g.paint(g.transform(g.box(0.34, 0.012, 0.34),
                                             translate=(xt, 1.22, zt)), STEEL))
        # the clay pot, wider at the top
        parts.append(g.paint(g.merge([
            g.transform(g.cylinder(max(6, r), 0.065, 0.08), translate=(xt, 1.49, zt)),
            g.transform(g.cylinder(max(6, r), 0.088, 0.09), translate=(xt, 1.575, zt))]),
            (0.25, 0.14, 0.10) if stage == "cooled" else POT))
        if stage is None:                         # grey powder and a magnesium fuse
            parts.append(g.paint(g.transform(g.cylinder(max(6, r), 0.084, 0.01),
                                             translate=(xt, 1.615, zt)), (0.45, 0.45, 0.47)))
            parts.append(g.paint(g.transform(g.box(0.008, 0.14, 0.012),
                                             translate=(xt, 1.69, zt)), (0.9, 0.9, 0.92)))
        if stage in ("burning", "molten"):
            big = stage == "burning"
            parts.append(_ball(r, 0.085, (xt, 1.62, zt), HOT_WHITE, 5, scale=(1, 0.55, 1)))
            parts.append(_flame(r, 6, (xt, 1.64, zt), 0.5 if big else 0.22,
                                0.09 if big else 0.06, seed=33))
            parts += _cloud(r // 4, 20, 60 if big else 30,
                            (xt, 1.64, zt) if big else (xt, 1.235, zt), 0.016,
                            (1.0, 0.9, 0.5), seed=8)
            parts += _cloud(r // 3, 100, 8, (xt, 1.9, zt), 0.1, (0.86, 0.86, 0.86),
                            jitter=0.08, seed=9, lumps=4)
        if stage == "molten":
            parts += _cloud(r // 4, 120, 8, (xt, 1.43, zt), 0.018, (1.0, 0.7, 0.2), seed=10)
            parts.append(g.tag(g.paint(g.transform(g.cylinder(max(6, r), 0.1, 0.01),
                                                   translate=(xt, 1.232, zt)),
                                       (1.0, 0.6, 0.15)), 8))
        if stage == "cooled":
            parts.append(_ball(r, 0.06, (xt, 1.06, zt), (1.0, 0.55, 0.15), 9,
                               scale=(1.4, 0.6, 1.1)))
            parts += _cloud(r // 3, 100, 4, (xt, 1.15, zt), 0.07, (0.8, 0.8, 0.8),
                            jitter=0.05, seed=11, lumps=3)
        return g.merge(parts)

    table_t = _table(xt, zt)
    b.mesh("thermite_demo", "demo", thermite, 40, POT, variants={
        "burning": (lambda r: thermite(r, "burning"), None, [
            {"kind": "pulse", "part": 5, "from": HOT_WHITE, "to": [1.0, 0.8, 0.35],
             "period": 0.13},
            {"kind": "flicker", "part": 6, "pivot": [xt, 1.64, zt], "amount": 0.45,
             "freq": 8.0},
            _burst(20, 79, (xt, 1.64, zt), 3.2, spread=0.9, up=0.9, period=1.1,
                   table=table_t, hot=[1.0, 0.95, 0.6], cool=EMBER, cool_s=0.8),
            _smoke(100, 107),
            {"kind": "rise", "from": 100, "to": 107, "height": 1.6, "period": 3.2,
             "grow": 3.0, "drift": 0.2},
            {"kind": "hot", "parts": [5, 6] + list(range(20, 80))},
            _light((xt, 1.85, zt), (1.0, 0.78, 0.5), 5.0, flicker=0.35)]),
        "molten": (lambda r: thermite(r, "molten"), None, [
            {"kind": "pulse", "part": 5, "from": [1.0, 0.85, 0.4], "to": [1.0, 0.6, 0.2],
             "period": 0.2},
            {"kind": "flicker", "part": 6, "pivot": [xt, 1.64, zt], "amount": 0.35,
             "freq": 7.0},
            {"kind": "fall", "from": 120, "to": 127, "dist": 0.2, "period": 0.5},
            {"kind": "pulse", "part": 8, "from": [1.0, 0.85, 0.3], "to": [1.0, 0.45, 0.1],
             "period": 0.3},
            _burst(20, 49, (xt, 1.235, zt), 1.8, spread=1.0, up=0.6, period=0.8,
                   table=table_t, hot=[1.0, 0.9, 0.5], cool=EMBER, cool_s=0.6),
            _smoke(100, 107),
            {"kind": "rise", "from": 100, "to": 107, "height": 1.4, "period": 3.5,
             "grow": 2.5, "drift": 0.15},
            {"kind": "hot", "parts": [5, 6, 8] + list(range(20, 50)) + list(range(120, 128))},
            _light((xt, 1.5, zt), (1.0, 0.7, 0.4), 4.0, flicker=0.25)]),
        "cooled": (lambda r: thermite(r, "cooled"), None, [
            {"kind": "colour", "part": 9, "from": [1.0, 0.6, 0.2], "to": [0.3, 0.27, 0.26],
             "duration": 14.0},
            _smoke(100, 103),
            {"kind": "rise", "from": 100, "to": 103, "height": 0.9, "period": 3.0,
             "grow": 1.5, "drift": 0.08},
            {"kind": "hot", "parts": [9]},
            _light((xt, 1.15, zt), (1.0, 0.55, 0.3), 2.0, decay=6.0)]),
    })

    # ---- 3. a hydrogen balloon -----------------------------------------
    xb, zb = BALLOON
    tip = (xb - 0.2, 1.93, zb)

    def balloon(r, stage=None):
        pole = [g.transform(g.box(0.2, 0.02, 0.2), translate=(xb - 0.7, BENCH_TOP + 0.01, zb)),
                g.transform(g.cylinder(4, 0.01, 0.8), translate=(xb - 0.7, 1.39, zb)),
                g.transform(g.box(0.012, 0.62, 0.012), roll=-0.95,
                            translate=((xb - 0.7 + tip[0]) / 2, (1.62 + tip[1]) / 2, zb))]
        parts = [g.paint(g.transform(g.box(0.08, 0.06, 0.08),
                                     translate=(xb, BENCH_TOP + 0.03, zb)), (0.3, 0.3, 0.32)),
                 g.paint(g.merge(pole), (0.45, 0.35, 0.25))]    # a taper on a long pole
        if stage == "boom":
            parts.append(g.paint(g.transform(g.cylinder(4, 0.002, 0.35),
                                             translate=(xb, 1.2, zb)), (0.9, 0.9, 0.9)))
            parts.append(_fireball(r, 7, (xb, 2.2, zb), 0.6, seed=22))
            parts += _shards(20, 12, (xb, 2.2, zb), (0.85, 0.12, 0.12), size=0.06, seed=12)
            parts += _cloud(r // 3, 40, 8, (xb, 2.2, zb), 0.09, (0.9, 0.9, 0.92),
                            jitter=0.2, seed=13, lumps=4)
        else:
            parts.append(g.paint(g.transform(g.cylinder(4, 0.002, 1.0),
                                             translate=(xb, 1.52, zb)), (0.9, 0.9, 0.9)))
            parts.append(_ball(r, 0.22, (xb, 2.25, zb), (0.85, 0.12, 0.12),
                               scale=(1, 1.18, 1)))
        if stage == "lit":
            parts.append(_flame(r, 6, (tip[0], tip[1] + 0.01, tip[2]), 0.06, 0.014,
                                bottom=(1.0, 0.9, 0.55), seed=34))
        return g.merge(parts)

    b.mesh("balloon_demo", "demo", balloon, 32, (0.85, 0.12, 0.12), variants={
        "lit": (lambda r: balloon(r, "lit"), None, [
            {"kind": "flicker", "part": 6, "pivot": list(tip), "amount": 0.3, "freq": 6.0},
            {"kind": "hot", "parts": [6]},
            _light((tip[0], tip[1] + 0.1, tip[2]), (1.0, 0.7, 0.35), 0.8, flicker=0.3)]),
        "boom": (lambda r: balloon(r, "boom"), None, [
            {"kind": "blast", "part": 7, "centre": [xb, 2.2, zb], "grow": 0.12,
             "duration": 1.3},
            _burst(20, 31, (xb, 2.2, zb), 2.8, spread=1.0, up=0.5, table=_table(xb, zb)),
            _smoke(40, 47),
            {"kind": "rise", "from": 40, "to": 47, "height": 1.0, "period": 2.6,
             "grow": 2.5, "drift": 0.2},
            {"kind": "hot", "parts": [7], "unlit": True},
            _light((xb, 2.2, zb), (1.0, 0.8, 0.5), 16.0, decay=0.4)]),
    })

    # ---- people: the class stands right at the benches ------------------
    _person(b, "instructor", "instructor", 0.0, -4.4, instructor_points, LAB_COAT(0),
            face=(0.0, 3.0), scale=1.1)
    for s in range(students):
        x = -4.6 + s * (9.2 / (students - 1))
        z = -1.55 + 0.35 * abs(np.sin(s * 1.3))
        _person(b, f"student_{s}", "student", float(x), float(z), student_points,
                LAB_COAT(s + 1), face=(float(x) * 0.8, -3.0))
    return b.finish()


HAZARD_PATHS = {
    "walk": [
        (0.0,  (0.0, 1.80, 3.2),   (0.0, 1.30, -3.0)),
        (5.0,  (-2.8, 1.65, -0.4), (-3.6, 1.15, -3.0)),
        (10.0, (0.5, 1.65, -0.3),  (0.0, 1.45, -3.0)),
        (15.0, (3.0, 1.70, -0.4),  (3.6, 1.90, -3.0)),
        (20.0, (0.0, 1.80, 3.2),   (0.0, 1.30, -3.0)),
    ],
}


# --------------------------------------------------------------------------
REGISTRY: dict[str, Scenario] = {
    s.name: s for s in (
        Scenario("classroom", "Lecture classroom",
                 "One instructor at the board, students seated and still.",
                 build_classroom, CLASSROOM_PATHS),
        Scenario("physics_lab", "Physics laboratory",
                 "Students in pairs at benches with detailed apparatus.",
                 build_physics_lab, PHYSICS_PATHS),
        Scenario("chemistry_lab", "Chemistry laboratory",
                 "A crowd around one small, highly detailed demonstration.",
                 build_chemistry_lab, CHEMISTRY_PATHS),
        Scenario("gym", "PE class (sports hall)",
                 "Many fast-moving people over a large hall; rapid camera sweeps.",
                 build_gym, GYM_PATHS,
                 # inside the hall, at the edge of the court, rather than the
                 # path's own start, which is behind the south goal
                 home=(0.0, 1.85, 7.6), look=(0.0, 1.3, 0.2)),
        Scenario("hazard_lab", "Hazard lab (virtual only)",
                 "Dangerous experiments up close: explosions, molten iron, fireballs.",
                 build_hazard_lab, HAZARD_PATHS),
    )
}

NAMES = tuple(REGISTRY)


def get(name: str) -> Scenario:
    if name not in REGISTRY:
        raise KeyError(f"unknown scenario {name!r}; have {sorted(REGISTRY)}")
    return REGISTRY[name]


def build(name: str, **kwargs) -> Scene:
    return get(name).build(**kwargs)
