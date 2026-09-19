"""A single-stroke font, for writing on the boards.

Board writing is streamed as geometry like everything else, so it has to be
geometry: every letter is a few pen strokes, every stroke a thin bar.  Strokes
are numbered in the order a hand would draw them, so the viewer can reveal a
line of text stroke by stroke -- the board fills up while the teacher writes,
instead of a finished paragraph popping into existence.

Glyphs sit on a 4 x 6 grid (width x cap height), baseline at y = 0.
"""
from __future__ import annotations

import numpy as np

from . import geometry as g

_O = [(1, 0), (0, 1), (0, 5), (1, 6), (3, 6), (4, 5), (4, 1), (3, 0), (1, 0)]
_P = [(0, 0), (0, 6), (3, 6), (4, 5), (4, 4), (3, 3), (0, 3)]

GLYPHS: dict[str, list[list[tuple[float, float]]]] = {
    "A": [[(0, 0), (2, 6), (4, 0)], [(1, 3), (3, 3)]],
    "B": [[(0, 0), (0, 6), (3, 6), (4, 5), (4, 4), (3, 3), (0, 3)],
          [(3, 3), (4, 2), (4, 1), (3, 0), (0, 0)]],
    "C": [[(4, 5), (3, 6), (1, 6), (0, 5), (0, 1), (1, 0), (3, 0), (4, 1)]],
    "D": [[(0, 0), (0, 6), (3, 6), (4, 5), (4, 1), (3, 0), (0, 0)]],
    "E": [[(4, 6), (0, 6), (0, 0), (4, 0)], [(0, 3), (3, 3)]],
    "F": [[(4, 6), (0, 6), (0, 0)], [(0, 3), (3, 3)]],
    "G": [[(4, 5), (3, 6), (1, 6), (0, 5), (0, 1), (1, 0), (3, 0), (4, 1), (4, 3), (2, 3)]],
    "H": [[(0, 0), (0, 6)], [(4, 0), (4, 6)], [(0, 3), (4, 3)]],
    "I": [[(1, 6), (3, 6)], [(2, 6), (2, 0)], [(1, 0), (3, 0)]],
    "J": [[(4, 6), (4, 1), (3, 0), (1, 0), (0, 1)]],
    "K": [[(0, 0), (0, 6)], [(4, 6), (0, 2)], [(1, 3), (4, 0)]],
    "L": [[(0, 6), (0, 0), (4, 0)]],
    "M": [[(0, 0), (0, 6), (2, 3), (4, 6), (4, 0)]],
    "N": [[(0, 0), (0, 6), (4, 0), (4, 6)]],
    "O": [_O],
    "P": [_P],
    "Q": [_O, [(2, 2), (4, 0)]],
    "R": [_P, [(2, 3), (4, 0)]],
    "S": [[(4, 5), (3, 6), (1, 6), (0, 5), (0, 4), (1, 3), (3, 3), (4, 2), (4, 1),
           (3, 0), (1, 0), (0, 1)]],
    "T": [[(0, 6), (4, 6)], [(2, 6), (2, 0)]],
    "U": [[(0, 6), (0, 1), (1, 0), (3, 0), (4, 1), (4, 6)]],
    "V": [[(0, 6), (2, 0), (4, 6)]],
    "W": [[(0, 6), (1, 0), (2, 3), (3, 0), (4, 6)]],
    "X": [[(0, 0), (4, 6)], [(0, 6), (4, 0)]],
    "Y": [[(0, 6), (2, 3), (4, 6)], [(2, 3), (2, 0)]],
    "Z": [[(0, 6), (4, 6), (0, 0), (4, 0)]],
    "0": [_O, [(0.6, 1), (3.4, 5)]],
    "1": [[(1, 5), (2, 6), (2, 0)], [(1, 0), (3, 0)]],
    "2": [[(0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (0, 0), (4, 0)]],
    "3": [[(0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (3, 3), (2, 3)],
          [(3, 3), (4, 2), (4, 1), (3, 0), (1, 0), (0, 1)]],
    "4": [[(3, 0), (3, 6), (0, 2), (4, 2)]],
    "5": [[(4, 6), (0, 6), (0, 3), (3, 3), (4, 2), (4, 1), (3, 0), (1, 0), (0, 1)]],
    "6": [[(4, 5), (3, 6), (1, 6), (0, 5), (0, 1), (1, 0), (3, 0), (4, 1), (4, 2),
           (3, 3), (0, 3)]],
    "7": [[(0, 6), (4, 6), (1, 0)]],
    "8": [[(1, 3), (0, 4), (0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (3, 3), (1, 3),
           (0, 2), (0, 1), (1, 0), (3, 0), (4, 1), (4, 2), (3, 3)]],
    "9": [[(4, 3), (1, 3), (0, 4), (0, 5), (1, 6), (3, 6), (4, 5), (4, 1), (3, 0),
           (1, 0), (0, 1)]],
    "+": [[(2, 1), (2, 5)], [(0, 3), (4, 3)]],
    "-": [[(0.5, 3), (3.5, 3)]],
    "=": [[(0, 2), (4, 2)], [(0, 4), (4, 4)]],
    "(": [[(3, 6.5), (2, 5.5), (1.5, 3), (2, 0.5), (3, -0.5)]],
    ")": [[(1, 6.5), (2, 5.5), (2.5, 3), (2, 0.5), (1, -0.5)]],
    ".": [[(1.7, 0), (2.3, 0)]],
    ",": [[(2.2, 0.5), (1.5, -1)]],
    ":": [[(1.7, 1), (2.3, 1)], [(1.7, 4), (2.3, 4)]],
    "/": [[(0, 0), (4, 6)]],
    ">": [[(0, 1), (4, 3), (0, 5)]],
    "<": [[(4, 1), (0, 3), (4, 5)]],
    "*": [[(2, 1), (2, 5)], [(0.5, 2), (3.5, 4)], [(0.5, 4), (3.5, 2)]],
    "?": [[(0, 5), (1, 6), (3, 6), (4, 5), (4, 4), (2, 3), (2, 1.8)], [(1.7, 0), (2.3, 0)]],
    "%": [[(0, 0), (4, 6)], [(0.5, 5.5), (1, 5.5)], [(3, 0.5), (3.5, 0.5)]],
    "'": [[(2, 6), (2, 4.5)]],
    "^": [[(1, 4.5), (2, 6), (3, 4.5)]],
    "!": [[(2, 6), (2, 1.8)], [(1.7, 0), (2.3, 0)]],
}


def text_strokes(text: str) -> list[list[tuple[float, float]]]:
    """Polylines for a line of text, in grid units, laid out left to right."""
    out, x = [], 0.0
    for ch in text.upper():
        if ch == " ":
            x += 4.0
            continue
        for stroke in GLYPHS.get(ch, GLYPHS["?"]):
            out.append([(x + px, py) for px, py in stroke])
        x += 5.6
    return out


def text_width(text: str, height: float) -> float:
    """Width of a line of text in metres at a given cap height."""
    n = len(text)
    return max(0.0, (n * 5.6 - 1.6) * height / 6.0)


def _bar(a, b, z0, thickness, colour, part):
    d = b - a
    length = float(np.hypot(*d)) + thickness
    bar = g.transform(g.box(length, thickness, 0.01), roll=float(np.arctan2(d[1], d[0])))
    bar = g.transform(bar, translate=((a[0] + b[0]) / 2, (a[1] + b[1]) / 2, z0))
    return g.tag(g.paint(bar, colour), part)


def draw(polylines, z0: float, colour=(0.12, 0.16, 0.34), thickness: float = 0.018,
         first_part: int = 1) -> tuple[tuple, int]:
    """Drawings on a board (a triangle, an arrow): polylines of (x, y) points
    in room coordinates, one part per pen segment, in drawing order."""
    bars, part = [], first_part
    for line in polylines:
        pts = [np.asarray(p, dtype=np.float64) for p in line]
        for a, b in zip(pts, pts[1:]):
            bars.append(_bar(a, b, z0, thickness, colour, part))
            part += 1
    return g.merge(bars), part


def write(lines: list[str], origin, height: float = 0.14, line_gap: float = 1.7,
          colour=(0.12, 0.16, 0.34), thickness: float = 0.016,
          first_part: int = 1) -> tuple[tuple, int]:
    """Lay out lines of text on a board (the xy plane at z = origin[2]).

    Returns (mesh, next_part).  Every pen segment is its own part, numbered in
    writing order from `first_part`, so the client can reveal them one by one.
    """
    x0, y0, z0 = origin
    unit = height / 6.0
    bars, part = [], first_part
    for row, text in enumerate(lines):
        base_y = y0 - row * height * line_gap
        for stroke in text_strokes(text):
            for (ax, ay), (bx, by) in zip(stroke, stroke[1:]):
                a = np.array([x0 + ax * unit, base_y + ay * unit])
                b = np.array([x0 + bx * unit, base_y + by * unit])
                d = b - a
                length = float(np.hypot(*d)) + thickness
                bar = g.transform(g.box(length, thickness, 0.01),
                                  roll=float(np.arctan2(d[1], d[0])))
                bar = g.transform(bar, translate=((a[0] + b[0]) / 2,
                                                  (a[1] + b[1]) / 2, z0))
                bars.append(g.tag(g.paint(bar, colour), part))
                part += 1
    if not bars:
        return g.tag(g.paint(g.box(0.001, 0.001, 0.001), colour), first_part), first_part
    return g.merge(bars), part
