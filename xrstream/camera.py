"""Camera model: frustum culling and screen-space size estimation.

These two quantities are what every viewport-dependent streaming policy in the
SOTA (1.4.3 region-of-interest, 1.5.5 viewport-dependent / tile-based) is
ultimately built on, so they live in one place.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np


def _norm(v):
    v = np.asarray(v, dtype=np.float64)
    n = np.linalg.norm(v)
    return v / n if n > 1e-12 else np.array([0.0, 0.0, -1.0])


@dataclass
class Camera:
    pos: np.ndarray = field(default_factory=lambda: np.zeros(3))
    forward: np.ndarray = field(default_factory=lambda: np.array([0.0, 0.0, -1.0]))
    up: np.ndarray = field(default_factory=lambda: np.array([0.0, 1.0, 0.0]))
    fov_y_deg: float = 60.0
    aspect: float = 16 / 9
    near: float = 0.1
    far: float = 80.0
    screen_h: int = 1080

    def __post_init__(self):
        self.pos = np.asarray(self.pos, dtype=np.float64)
        self.forward = _norm(self.forward)
        self.up = _norm(self.up)

    # -- derived ----------------------------------------------------------
    @property
    def tan_half(self) -> float:
        return math.tan(math.radians(self.fov_y_deg) / 2.0)

    def planes(self) -> np.ndarray:
        """Six inward-facing frustum planes as rows of (nx, ny, nz, d).

        Computed once per camera: a camera is a snapshot of one viewpoint, and
        rebuilding the planes for every object was most of a tick's cost.
        """
        cached = self.__dict__.get("_planes")
        if cached is not None:
            return cached
        self._planes = self._build_planes()
        return self._planes

    def _build_planes(self) -> np.ndarray:
        f = self.forward
        r = _norm(np.cross(f, self.up))
        u = np.cross(r, f)
        h = self.tan_half
        w = h * self.aspect

        corners = [f + r * w + u * h, f - r * w + u * h,
                   f - r * w - u * h, f + r * w - u * h]
        inside = self.pos + f * (self.near + self.far) * 0.5

        rows = []
        for a, b in zip(corners, corners[1:] + corners[:1]):
            n = _norm(np.cross(a, b))
            rows.append((n, self.pos))
        rows.append((f, self.pos + f * self.near))
        rows.append((-f, self.pos + f * self.far))

        out = np.empty((6, 4))
        for k, (n, p) in enumerate(rows):
            d = -float(np.dot(n, p))
            if float(np.dot(n, inside)) + d < 0:      # flip to face inward
                n, d = -np.asarray(n), -d
            out[k, :3] = n
            out[k, 3] = d
        return out

    # -- queries ----------------------------------------------------------
    def sees_sphere(self, centre: np.ndarray, radius: float, planes=None) -> bool:
        pl = self.planes() if planes is None else planes
        dist = pl[:, :3] @ np.asarray(centre, dtype=np.float64) + pl[:, 3]
        return bool(np.all(dist >= -radius))

    def distance_to(self, centre: np.ndarray, radius: float) -> float:
        return max(1e-3, float(np.linalg.norm(np.asarray(centre) - self.pos)) - radius)

    def screen_radius_px(self, centre: np.ndarray, radius: float) -> float:
        """Approximate projected radius of a bounding sphere, in pixels."""
        d = self.distance_to(centre, radius)
        return radius * (self.screen_h * 0.5) / (d * self.tan_half)

    def screen_fraction(self, centre: np.ndarray, radius: float,
                        cap: float = 0.25) -> float:
        """Share of the viewport the object's bounding sphere covers.

        Capped, so that one enormous nearby surface (the floor) cannot drown
        out everything else in the quality metric.
        """
        r_px = self.screen_radius_px(centre, radius)
        area = math.pi * r_px * r_px
        return min(cap, area / max(1.0, self.screen_h * self.screen_h * self.aspect))

    def attention(self, centre: np.ndarray, radius: float,
                  floor: float = 0.3, cap: float = 0.25) -> float:
        """How much of the viewer's attention this object commands, in [floor, 1].

        Pure screen coverage would make a distant participant almost worthless,
        which lets a quality-maximising policy drop them for free.  The floor
        says: anything on screen at all still matters somewhat.
        """
        a = self.screen_fraction(centre, radius, cap) / cap
        return floor + (1.0 - floor) * min(1.0, a)

    def to_dict(self) -> dict:
        return {
            "pos": list(map(float, self.pos)),
            "forward": list(map(float, self.forward)),
            "fov_y_deg": self.fov_y_deg,
            "aspect": self.aspect,
            "screen_h": self.screen_h,
        }

    @staticmethod
    def from_dict(d: dict) -> "Camera":
        return Camera(
            pos=np.asarray(d.get("pos", [0, 1.6, 6]), dtype=np.float64),
            forward=np.asarray(d.get("forward", [0, 0, -1]), dtype=np.float64),
            fov_y_deg=float(d.get("fov_y_deg", 60.0)),
            aspect=float(d.get("aspect", 16 / 9)),
            screen_h=int(d.get("screen_h", 1080)),
        )
