"""Scripted camera trajectories.

A reproducible viewpoint sequence is what makes policies comparable: every
policy is replayed against identical head motion, so differences in the metrics
come from the policy and not from how someone moved the mouse.

Each scenario owns its trajectories (see `scenarios.py`); `frames()` accepts
either a keyframe list or one of the legacy names below.
"""
from __future__ import annotations

import numpy as np

from .camera import Camera
from .scenarios import CLASSROOM_PATHS

#: legacy names kept so older commands and notebooks keep working
PATHS = {
    "lecture_walk": CLASSROOM_PATHS["walk"],
    "fast_look_around": CLASSROOM_PATHS["look_around"],
}


def _smoothstep(x: float) -> float:
    return x * x * (3.0 - 2.0 * x)


def resolve(path) -> list:
    """Accept a keyframe list directly, or look a legacy name up."""
    return PATHS[path] if isinstance(path, str) else path


def sample(keys, t: float, **cam_kwargs) -> Camera:
    """Camera pose at time `t`, smoothstep-interpolated between keyframes."""
    keys = resolve(keys)
    t = float(np.clip(t, keys[0][0], keys[-1][0]))
    for (t0, p0, l0), (t1, p1, l1) in zip(keys, keys[1:]):
        if t <= t1 or (t1, p1, l1) is keys[-1]:
            a = 0.0 if t1 <= t0 else _smoothstep((t - t0) / (t1 - t0))
            eye = np.array(p0) * (1 - a) + np.array(p1) * a
            look = np.array(l0) * (1 - a) + np.array(l1) * a
            return Camera(pos=eye, forward=look - eye, **cam_kwargs)
    raise RuntimeError("unreachable")


def duration(keys) -> float:
    return float(resolve(keys)[-1][0])


def frames(path="lecture_walk", fps: float = 30.0, **cam_kwargs):
    """Yield (t, dt, Camera) over the whole trajectory."""
    keys = resolve(path)
    dt = 1.0 / fps
    n = int(round(duration(keys) * fps))
    for i in range(n + 1):
        yield i * dt, (0.0 if i == 0 else dt), sample(keys, i * dt, **cam_kwargs)
