"""Articulated bodies for live participants.

A participant is a *capture*: a point cloud sampled on a body, re-sent every
capture frame.  Earlier versions deformed one frozen cloud with a twist and
rotated a single arm for a "gesture"; that could neither face anything, point
at anything, nor do a squat.  Here the cloud is skinned onto a skeleton:

    pelvis -> torso -> neck/head
           -> shoulders -> upper arms -> forearms -> hands
           -> hips      -> thighs     -> shins    -> feet

so every gesture is a real pose of the limbs.  Poses are built from joint
angles, or from targets (point *at* something, put the hand *on* the pen),
and blended smoothly when an activity changes.  The feet stay on the floor.

Conventions -- local frame: feet on y = 0, facing -z, right side +x.  A body
is placed in the room by (x, z, yaw) and the client applies that placement,
so one encoded capture can serve every participant in the same pose.
"""
from __future__ import annotations

import itertools
import math
from collections import OrderedDict
from dataclasses import dataclass, field, replace

import numpy as np

# --------------------------------------------------------------------------
# proportions (metres, scale 1)
# --------------------------------------------------------------------------
ROOT_Y = 0.95            # pelvis centre above the floor at rest
HIP_X, HIP_DY = 0.09, -0.03
THIGH, SHIN, ANKLE_H = 0.44, 0.42, 0.06
SPINE_DY, TORSO = 0.08, 0.45
SHOULDER_X, SHOULDER_DY = 0.19, -0.04
UPPER, FORE, HAND_R = 0.28, 0.26, 0.045
NECK, HEAD_DY, HEAD_R = 0.06, 0.13, 0.105
REACH = UPPER + FORE + HAND_R          # shoulder to hand centre, arm straight

DOWN = np.array([0.0, -1.0, 0.0])
UP = np.array([0.0, 1.0, 0.0])


def _unit(v) -> np.ndarray:
    v = np.asarray(v, dtype=np.float64)
    n = np.linalg.norm(v)
    return v / n if n > 1e-9 else DOWN.copy()


def rot_x(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])


def rot_y(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])


def rot_z(a):
    c, s = math.cos(a), math.sin(a)
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def align(a, b) -> np.ndarray:
    """The smallest rotation taking unit vector a onto unit vector b."""
    a, b = _unit(a), _unit(b)
    v = np.cross(a, b)
    c = float(a @ b)
    if c < -0.999999:                          # opposite: turn about any perpendicular
        axis = _unit(np.cross(a, [1, 0, 0] if abs(a[0]) < 0.9 else [0, 0, 1]))
        return 2 * np.outer(axis, axis) - np.eye(3)
    k = np.array([[0, -v[2], v[1]], [v[2], 0, -v[0]], [-v[1], v[0], 0]])
    return np.eye(3) + k + k @ k / (1 + c)


def limb(flex: float, abd: float, side: int = 1) -> np.ndarray:
    """Direction of a limb hanging from its joint, bent by angles.

    flex: forward (+) / backward (-) about the side axis; abd: out to the side
    (+ = away from the body), mirrored for the left (side = -1).  (0, 0) hangs
    straight down; (pi/2, 0) points forward; (0, pi/2) points sideways out;
    (0, pi) points up.
    """
    return rot_x(flex) @ rot_z(side * abd) @ DOWN


def facing_yaw(frm, to) -> float:
    """The yaw that makes a body at `frm` face `to` (both x, z)."""
    d = np.asarray(to, dtype=np.float64)[[0, -1]] - np.asarray(frm, dtype=np.float64)[[0, -1]]
    if np.hypot(*d) < 1e-6:
        return 0.0
    return math.atan2(-d[0], -d[1])


# --------------------------------------------------------------------------
# a pose
# --------------------------------------------------------------------------
@dataclass
class Pose:
    """Joint state of one body.  Limb directions: arms in the torso frame,
    legs in the (yaw-only) pelvis frame; see limb()."""
    pitch: float = 0.0        # pelvis tilt forward
    bend: float = 0.0         # torso bends forward
    side: float = 0.0         # torso bends sideways (+ = to its right)
    twist: float = 0.0        # torso turns (+ = to its left)
    head: float = 0.0         # nod forward
    air: float = 0.0          # off the floor (jumps)
    ru: np.ndarray = field(default_factory=lambda: limb(0.04, 0.10, 1))
    rf: np.ndarray = field(default_factory=lambda: limb(0.18, 0.06, 1))
    lu: np.ndarray = field(default_factory=lambda: limb(0.04, 0.10, -1))
    lf: np.ndarray = field(default_factory=lambda: limb(0.18, 0.06, -1))
    rt: np.ndarray = field(default_factory=lambda: limb(0.0, 0.03, 1))
    rs: np.ndarray = field(default_factory=lambda: limb(0.0, 0.02, 1))
    lt: np.ndarray = field(default_factory=lambda: limb(0.0, 0.03, -1))
    ls: np.ndarray = field(default_factory=lambda: limb(0.0, 0.02, -1))

    SCALARS = ("pitch", "bend", "side", "twist", "head", "air")
    VECTORS = ("ru", "rf", "lu", "lf", "rt", "rs", "lt", "ls")

    def key(self) -> tuple:
        """A quantised signature: equal keys look the same to the eye, so
        captures can be shared between participants and reused across cycles."""
        k = [round(getattr(self, n) / 0.015) for n in self.SCALARS]
        for n in self.VECTORS:
            k.extend(np.round(getattr(self, n) / 0.02).astype(int).tolist())
        return tuple(k)

    def with_arm(self, side: int, upper, fore) -> "Pose":
        if side > 0:
            return replace(self, ru=_unit(upper), rf=_unit(fore))
        return replace(self, lu=_unit(upper), lf=_unit(fore))


def blend(a: Pose, b: Pose, w: float) -> Pose:
    """a -> b by w in [0, 1]; limb directions interpolate as unit vectors."""
    if w <= 0:
        return a
    if w >= 1:
        return b
    out = {n: getattr(a, n) + (getattr(b, n) - getattr(a, n)) * w for n in Pose.SCALARS}
    for n in Pose.VECTORS:
        va, vb = getattr(a, n), getattr(b, n)
        mid = va + (vb - va) * w
        if np.linalg.norm(mid) < 0.2:             # nearly opposite: go round
            mid = va + (vb - va) * w + np.array([0.0, 0.0, -0.3])
        out[n] = _unit(mid)
    return Pose(**out)


def smooth(x: float) -> float:
    x = min(1.0, max(0.0, x))
    return x * x * (3 - 2 * x)


# --------------------------------------------------------------------------
# forward kinematics
# --------------------------------------------------------------------------
@dataclass
class Joints:
    root: np.ndarray
    R_pelvis: np.ndarray
    R_legs: np.ndarray
    R_torso: np.ndarray
    spine: np.ndarray
    neck: np.ndarray
    head: np.ndarray
    shoulder: dict
    elbow: dict
    wrist: dict
    hand: dict
    hip: dict
    knee: dict
    ankle: dict


def forward(pose: Pose, scale: float = 1.0, ground: bool = True) -> Joints:
    """Joint positions of `pose`, feet on the floor unless it is jumping."""
    s = scale
    R_legs = rot_y(0.0)
    R_pelvis = rot_x(-pose.pitch)
    R_torso = R_pelvis @ rot_x(-pose.bend) @ rot_z(-pose.side) @ rot_y(pose.twist)
    root = np.array([0.0, ROOT_Y * s, 0.0])
    spine = root + R_pelvis @ np.array([0.0, SPINE_DY * s, 0.0])
    neck = spine + R_torso @ np.array([0.0, TORSO * s, 0.0])
    R_head = R_torso @ rot_x(-pose.head)
    head = neck + R_head @ np.array([0.0, HEAD_DY * s, 0.0])

    shoulder, elbow, wrist, hand, hip, knee, ankle = {}, {}, {}, {}, {}, {}, {}
    for side, (u, f) in ((1, (pose.ru, pose.rf)), (-1, (pose.lu, pose.lf))):
        sh = spine + R_torso @ np.array([side * SHOULDER_X * s, (TORSO + SHOULDER_DY) * s, 0.0])
        el = sh + R_torso @ _unit(u) * UPPER * s
        wr = el + R_torso @ _unit(f) * FORE * s
        shoulder[side], elbow[side], wrist[side] = sh, el, wr
        hand[side] = wr + R_torso @ _unit(f) * HAND_R * s
    for side, (t, sh_) in ((1, (pose.rt, pose.rs)), (-1, (pose.lt, pose.ls))):
        hp = root + R_pelvis @ np.array([side * HIP_X * s, HIP_DY * s, 0.0])
        kn = hp + R_legs @ _unit(t) * THIGH * s
        an = kn + R_legs @ _unit(sh_) * SHIN * s
        hip[side], knee[side], ankle[side] = hp, kn, an

    lift = 0.0
    if ground:
        lift = ANKLE_H * s - min(ankle[1][1], ankle[-1][1]) + pose.air
    shift = np.array([0.0, lift, 0.0])
    moved = lambda d: {k: v + shift for k, v in d.items()}
    return Joints(root + shift, R_pelvis, R_legs, R_torso, spine + shift, neck + shift,
                  head + shift, moved(shoulder), moved(elbow), moved(wrist), moved(hand),
                  moved(hip), moved(knee), moved(ankle))


# --------------------------------------------------------------------------
# inverse kinematics: reach a point, point at a thing
# --------------------------------------------------------------------------
def reach(pose: Pose, side: int, target_local, scale: float = 1.0,
          pole=(0.35, -1.0, 0.45)) -> Pose:
    """Put the hand (centre) on `target_local` (body-local coordinates) with a
    two-bone solve; the elbow bends towards `pole` (down, out and back)."""
    j = forward(pose, scale, ground=True)
    R = j.R_torso
    sh = j.shoulder[side]
    t = R.T @ (np.asarray(target_local, dtype=np.float64) - sh)   # torso frame
    L1, L2 = UPPER * scale, (FORE + HAND_R) * scale
    d = float(np.linalg.norm(t))
    u = _unit(t)
    d = min(max(d, abs(L1 - L2) + 1e-3), L1 + L2 - 1e-4)
    a = math.acos(max(-1.0, min(1.0, (L1 * L1 + d * d - L2 * L2) / (2 * L1 * d))))
    p = np.array(pole, dtype=np.float64) * np.array([side, 1, 1])
    p = _unit(p - (p @ u) * u)
    upper = math.cos(a) * u + math.sin(a) * p
    elbow = upper * L1
    fore = _unit(u * d - elbow)
    return pose.with_arm(side, upper, fore)


def point_at(pose: Pose, side: int, target_local, scale: float = 1.0) -> Pose:
    """Arm straight out towards a point (body-local coordinates)."""
    j = forward(pose, scale, ground=True)
    d = j.R_torso.T @ _unit(np.asarray(target_local, dtype=np.float64) - j.shoulder[side])
    return pose.with_arm(side, d, d)


# --------------------------------------------------------------------------
# activities: whole-body motion as a function of time
# --------------------------------------------------------------------------
def _arms(p: Pose, side: int, flex, abd, elbow) -> Pose:
    return p.with_arm(side, limb(flex, abd, side), limb(flex + elbow, abd, side))


def _legs(p: Pose, side: int, flex, abd, knee) -> Pose:
    t, s = limb(flex, abd, side), limb(flex - knee, abd * 0.7, side)
    return replace(p, rt=t, rs=s) if side > 0 else replace(p, lt=t, ls=s)


def _cyc(t: float, period: float) -> float:
    return (t / period) % 1.0


def _hump(phase: float) -> float:
    """0 -> 1 -> 0 over one cycle, smooth at both ends."""
    return 0.5 - 0.5 * math.cos(2 * math.pi * phase)


def idle(t: float, seed: float = 0.0) -> Pose:
    """Standing: breathing, a little sway, arms at rest."""
    b = math.sin(2 * math.pi * (t / 3.6 + seed))
    p = Pose(bend=0.015 * b, twist=0.03 * math.sin(2 * math.pi * (t / 7.3 + seed)),
             head=0.04 * math.sin(2 * math.pi * (t / 5.1 + seed)))
    p = _arms(p, 1, 0.04 + 0.03 * b, 0.10, 0.16)
    return _arms(p, -1, 0.04 - 0.03 * b, 0.10, 0.16)


def walk(phase: float, speed: float = 1.0) -> Pose:
    """One stride per cycle; `speed` scales the swing."""
    w = 2 * math.pi * phase
    p = Pose(bend=0.04, twist=0.06 * math.sin(w))
    for side, ph in ((1, 0.0), (-1, math.pi)):
        sw = math.sin(w + ph)
        p = _legs(p, side, 0.38 * sw * speed, 0.03, 0.1 + 0.55 * max(0.0, math.cos(w + ph)) * speed)
        p = _arms(p, side, -0.32 * sw * speed, 0.08, 0.3)
    return p


def run(phase: float) -> Pose:
    w = 2 * math.pi * phase
    p = Pose(pitch=0.10, bend=0.10, twist=0.10 * math.sin(w),
             air=0.05 * abs(math.sin(2 * w)))
    for side, ph in ((1, 0.0), (-1, math.pi)):
        sw = math.sin(w + ph)
        p = _legs(p, side, 0.78 * sw, 0.02, 0.35 + 1.05 * max(0.0, math.cos(w + ph - 0.4)))
        p = _arms(p, side, -0.65 * sw, 0.10, 1.45)
    return p


def jumping_jacks(phase: float) -> Pose:
    s = _hump(phase)
    p = Pose(air=0.07 * abs(math.sin(2 * math.pi * phase)))
    for side in (1, -1):
        p = _arms(p, side, 0.08, 0.18 + 2.72 * s, 0.10)
        p = _legs(p, side, 0.0, 0.04 + 0.30 * s, 0.14 * (1 - abs(math.cos(2 * math.pi * phase))))
    return p


def arm_circles(phase: float) -> Pose:
    w = 2 * math.pi * phase
    p = Pose()
    for side in (1, -1):
        p = _arms(p, side, 0.30 * math.cos(w), math.pi / 2 + 0.30 * math.sin(w), 0.02)
    return p


def squats(phase: float, depth: float = 1.0) -> Pose:
    s = _hump(phase) * depth
    p = Pose(pitch=0.35 * s, bend=0.20 * s, head=-0.25 * s)
    for side in (1, -1):
        p = _legs(p, side, 1.35 * s, 0.10, 2.10 * s)
        p = _arms(p, side, 1.50 * _hump(phase), 0.12, 0.0)     # arms forward, for balance
    return p


def lunges(phase: float) -> Pose:
    # right leg forward in the first half of the cycle, left in the second
    front = 1 if phase < 0.5 else -1
    s = _hump((phase * 2) % 1.0)
    p = Pose(bend=-0.05 * s)
    p = _legs(p, front, 1.15 * s, 0.05, 1.15 * s)               # front shin upright
    p = _legs(p, -front, -0.40 * s, 0.05, 1.40 * s)             # back knee drops
    return hands_on_hips(p)


def high_knees(phase: float) -> Pose:
    w = 2 * math.pi * phase
    p = Pose(air=0.04 * abs(math.sin(w)))
    for side, ph in ((1, 0.0), (-1, math.pi)):
        up = max(0.0, math.sin(w + ph))
        p = _legs(p, side, 1.45 * up, 0.04, 1.60 * up)
        p = _arms(p, side, -0.8 * math.sin(w + ph), 0.10, 1.5)
    return p


def toe_touch(phase: float) -> Pose:
    s = _hump(phase)
    p = Pose(pitch=0.55 * s, bend=0.95 * s, head=0.35 * s)
    total = 0.55 * s + 0.95 * s
    for side in (1, -1):
        p = _arms(p, side, total, 0.06, 0.05)                  # hanging towards the toes
        p = _legs(p, side, 0.0, 0.05, 0.0)
    return p


def side_stretch(phase: float) -> Pose:
    # lean right with the left arm over the head, then the other way
    lean = math.sin(2 * math.pi * phase)
    p = Pose(side=0.42 * lean)
    over = -1 if lean > 0 else 1
    a = abs(lean)
    p = _arms(p, over, 0.10, 0.25 + 2.55 * a, 0.25 * a)
    p = hands_on_hips(p, sides=(-over,))
    for side in (1, -1):
        p = _legs(p, side, 0.0, 0.14, 0.0)
    return p


def reach_up(phase: float) -> Pose:
    """Cool-down breathing: arms rise overhead and come down."""
    s = _hump(phase)
    p = Pose(head=-0.2 * s, bend=-0.05 * s)
    for side in (1, -1):
        p = _arms(p, side, 0.2 * s, 0.15 + 2.8 * s, 0.05)
    return p


EXERCISES = {
    "idle": (idle, 3.6), "walk": (walk, 1.1), "run": (run, 0.7),
    "jumping_jacks": (jumping_jacks, 1.0), "arm_circles": (arm_circles, 1.3),
    "squats": (squats, 2.4), "lunges": (lunges, 3.2), "high_knees": (high_knees, 0.7),
    "toe_touch": (toe_touch, 3.4), "side_stretch": (side_stretch, 4.4),
    "reach_up": (reach_up, 4.5),
}


def activity(name: str, t: float, depth: float = 1.0, seed: float = 0.0) -> Pose:
    fn, period = EXERCISES[name]
    if name == "idle":
        return idle(t, seed)
    if name == "squats":
        return squats(_cyc(t, period), depth)
    return fn(_cyc(t, period))


# --------------------------------------------------------------------------
# gestures: override the arms of whatever the body is doing
# --------------------------------------------------------------------------
def hands_on_hips(p: Pose, sides=(1, -1), scale: float = 1.0) -> Pose:
    j = forward(p, scale)
    for side in sides:
        target = j.hip[side] + j.R_pelvis @ np.array([side * 0.10, 0.06, 0.02]) * scale
        p = reach(p, side, target, scale, pole=(0.9, -0.2, 0.6))
    return p


def hand_up(p: Pose, side: int = 1) -> Pose:
    return p.with_arm(side, limb(0.05, 2.95, side), limb(0.05, 3.0, side))


def wave(p: Pose, t: float, side: int = 1) -> Pose:
    sway = 0.35 * math.sin(2 * math.pi * t / 0.8)
    return p.with_arm(side, limb(0.25, 1.25, side), limb(0.25, 2.55 + sway, side))


def clap(p: Pose, t: float, scale: float = 1.0) -> Pose:
    s = _hump(_cyc(t, 0.55))
    j = forward(p, scale)
    front = j.spine + j.R_torso @ np.array([0.0, 0.30, -0.33]) * scale
    for side in (1, -1):
        target = front + j.R_torso @ np.array([side * (0.03 + 0.20 * s), 0, 0]) * scale
        p = reach(p, side, target, scale)
    return p


def whistle(p: Pose, scale: float = 1.0) -> Pose:
    j = forward(p, scale)
    mouth = j.head + j.R_torso @ np.array([0.02, -0.05, -0.12]) * scale
    return reach(p, 1, mouth, scale, pole=(0.9, -0.6, 0.2))


def shield(p: Pose, scale: float = 1.0) -> Pose:
    """Flinching from a blast: lean back, duck the head, forearms up in front
    of the face."""
    p = replace(p, pitch=p.pitch - 0.10, head=p.head + 0.25)
    j = forward(p, scale)
    for side in (1, -1):
        target = j.head + j.R_torso @ np.array([side * 0.09, 0.0, -0.24]) * scale
        p = reach(p, side, target, scale, pole=(side * 0.9, -0.8, 0.1))
    return p


# --------------------------------------------------------------------------
# the capture: a point cloud skinned onto the skeleton
# --------------------------------------------------------------------------
@dataclass
class Segment:
    name: str
    kind: str            # "limb" (along the bone), "torso", "sphere", "box", "band"
    role: str            # colour role: shirt, trousers, skin, shoes, hair, headset...
    radius: float = 0.05
    length: float = 0.1
    size: tuple = (0.1, 0.1, 0.1)
    rz: float = 0.0      # torso: front-back radius
    offset: tuple = (0.0, 0.0, 0.0)   # box / band: centre relative to the joint


SEGMENTS = [
    Segment("pelvis", "torso", "trousers", 0.155, 0.20, rz=0.105),
    Segment("torso", "torso", "shirt", 0.170, TORSO, rz=0.115),
    Segment("neck", "limb_up", "skin", 0.050, NECK),
    Segment("head", "sphere", "skin", HEAD_R),
    Segment("hair", "cap", "hair", HEAD_R * 1.04),
    # everyone in the class wears a VR headset: a visor over the eyes, a dark
    # front plate and a strap round the back of the head
    Segment("headset", "box", "headset", size=(0.20, 0.09, 0.075), offset=(0.0, 0.012, -0.128)),
    Segment("visor", "box", "visor", size=(0.175, 0.068, 0.008), offset=(0.0, 0.012, -0.168)),
    Segment("strap", "band", "strap", HEAD_R * 1.03, 0.028, offset=(0.0, 0.02, 0.0)),
    Segment("upper_r", "limb", "shirt", 0.052, UPPER),
    Segment("upper_l", "limb", "shirt", 0.052, UPPER),
    Segment("fore_r", "limb", "skin", 0.042, FORE),
    Segment("fore_l", "limb", "skin", 0.042, FORE),
    Segment("hand_r", "sphere", "skin", HAND_R),
    Segment("hand_l", "sphere", "skin", HAND_R),
    Segment("thigh_r", "limb", "trousers", 0.078, THIGH),
    Segment("thigh_l", "limb", "trousers", 0.078, THIGH),
    Segment("shin_r", "limb", "trousers", 0.058, SHIN),
    Segment("shin_l", "limb", "trousers", 0.058, SHIN),
    Segment("foot_r", "box", "shoes", size=(0.10, 0.07, 0.25), offset=(0.0, -0.025, -0.06)),
    Segment("foot_l", "box", "shoes", size=(0.10, 0.07, 0.25), offset=(0.0, -0.025, -0.06)),
]

#: at coarser levels every segment keeps at least this many points, so a hand
#: or a forearm never dissolves: a gesture must survive a slow connection.
#: The points come out of the big segments (torso, thighs): a level costs the
#: same as a uniform subsample, only spent where the gestures are.
LIMB_FLOOR = (None, 96, 40, 16)

LIGHT = _unit([0.35, 0.8, 0.5])

#: the headset's colours (a palette may override them)
HEADSET = {"headset": (0.93, 0.93, 0.95), "visor": (0.05, 0.05, 0.07),
           "strap": (0.16, 0.16, 0.19)}


def _sample(seg: Segment, n: int, rng) -> tuple[np.ndarray, np.ndarray]:
    """Points and normals on a canonical segment (joint at the origin)."""
    if seg.kind in ("limb", "limb_up"):
        a = rng.random(n) * 2 * math.pi
        y = -rng.random(n) * seg.length if seg.kind == "limb" else rng.random(n) * seg.length
        pts = np.stack([seg.radius * np.cos(a), y, seg.radius * np.sin(a)], 1)
        nrm = np.stack([np.cos(a), np.zeros(n), np.sin(a)], 1)
    elif seg.kind == "torso":
        a = rng.random(n) * 2 * math.pi
        y = rng.random(n) * seg.length
        taper = 1.0 - 0.12 * np.sin(np.pi * y / seg.length)            # a waist
        pts = np.stack([seg.radius * taper * np.cos(a), y, seg.rz * taper * np.sin(a)], 1)
        nrm = np.stack([np.cos(a) / seg.radius, np.zeros(n), np.sin(a) / seg.rz], 1)
        # close the top and bottom with shoulders / seat
        cap = rng.random(n) < 0.14
        r = np.sqrt(rng.random(cap.sum()))
        b = rng.random(cap.sum()) * 2 * math.pi
        top = rng.random(cap.sum()) < 0.5
        pts[cap] = np.stack([seg.radius * r * np.cos(b),
                             np.where(top, seg.length, 0.0), seg.rz * r * np.sin(b)], 1)
        nrm[cap] = np.stack([np.zeros(cap.sum()), np.where(top, 1.0, -1.0),
                             np.zeros(cap.sum())], 1)
    elif seg.kind in ("sphere", "cap"):
        v = rng.normal(size=(n, 3))
        v /= np.linalg.norm(v, axis=1, keepdims=True)
        if seg.kind == "cap":                  # hair: top and back of the head
            v[:, 1] = np.abs(v[:, 1]) * 0.9 + 0.1
            v[:, 2] = np.abs(v[:, 2]) * 0.6
            v /= np.linalg.norm(v, axis=1, keepdims=True)
        pts, nrm = v * seg.radius, v
        if seg.name == "head":
            pts = pts * np.array([0.92, 1.12, 1.0])
    elif seg.kind == "band":                   # a strap round the sides and back
        # angle 0 is the right side, pi/2 the back of the head (+z), pi the left;
        # the front (-z) is left open for the headset
        a = rng.uniform(-0.12 * math.pi, 1.12 * math.pi, n)
        y = (rng.random(n) - 0.5) * seg.length
        pts = np.stack([seg.radius * 0.93 * np.cos(a), y, seg.radius * np.sin(a)], 1)
        nrm = np.stack([np.cos(a), np.zeros(n), np.sin(a)], 1)
        pts = pts + np.asarray(seg.offset)
    else:                                      # box (feet), slightly forward
        w, h, d = seg.size
        face = rng.integers(0, 6, n)
        u, v = rng.random(n) - 0.5, rng.random(n) - 0.5
        pts = np.zeros((n, 3))
        nrm = np.zeros((n, 3))
        for f, (ax, sgn) in enumerate(((0, 1), (0, -1), (1, 1), (1, -1), (2, 1), (2, -1))):
            m = face == f
            dims = np.array([w, h, d])
            q = np.zeros((m.sum(), 3))
            others = [i for i in range(3) if i != ax]
            q[:, ax] = sgn * dims[ax] / 2
            q[:, others[0]] = u[m] * dims[others[0]]
            q[:, others[1]] = v[m] * dims[others[1]]
            pts[m] = q
            nrm[m, ax] = sgn
        pts = pts + np.asarray(seg.offset)
    return pts.astype(np.float64), _unit_rows(nrm)


def _floored_split(sizes: np.ndarray, stride: int, floor: int) -> np.ndarray:
    """Points per segment for one LOD level: the same total as keeping every
    `stride`-th point, but no segment below `floor`; big segments pay for it."""
    budget = int(sizes.sum()) // stride
    prop = sizes / stride
    low = np.minimum(sizes, floor).astype(float)
    flex = prop > low
    spare = budget - low[~flex].sum() - low[flex].sum()
    extra = (prop - low)[flex]
    alpha = min(1.0, max(0.0, spare / extra.sum())) if extra.sum() > 0 else 0.0
    want = low.copy()
    want[flex] += alpha * extra
    return np.maximum(want.astype(int), 1)


def _unit_rows(v):
    return v / np.maximum(np.linalg.norm(v, axis=1, keepdims=True), 1e-9)


class Body:
    """A participant's capture template: points sampled once on each segment,
    skinned onto any pose on demand, with a limb-preserving LOD chain."""

    #: a number of its own for every body ever built, because the capture cache
    #: has to tell two people apart for longer than either of them lives
    _next_uid = itertools.count(1)

    def __init__(self, n_points: int, palette: dict, scale: float = 1.0,
                 seed: int = 0, n_levels: int = 4):
        self.uid = next(Body._next_uid)
        self.scale = scale
        self.palette = {k: np.asarray(v, dtype=np.float64)
                        for k, v in {**HEADSET, **palette}.items()}
        rng = np.random.default_rng(seed)
        areas = []
        for seg in SEGMENTS:
            if seg.kind in ("limb", "limb_up"):
                areas.append(2 * math.pi * seg.radius * seg.length)
            elif seg.kind == "torso":
                areas.append(2 * math.pi * seg.radius * seg.length * 1.1)
            elif seg.kind in ("sphere", "cap"):
                areas.append(4 * math.pi * seg.radius ** 2 * (0.6 if seg.kind == "cap" else 1))
            elif seg.kind == "band":
                areas.append(2 * math.pi * seg.radius * seg.length * 0.62)
            else:
                w, h, d = seg.size
                areas.append(2 * (w * h + w * d + h * d))
        areas = np.array(areas)
        share = np.maximum(areas / areas.sum(), 0.025)          # small parts get enough
        counts = np.maximum((share / share.sum() * n_points).astype(int), 40)
        counts = np.maximum((counts * n_points / counts.sum()).astype(int), 40)
        self.slices, pts, nrm, col = {}, [], [], []
        start = 0
        for seg, n in zip(SEGMENTS, counts):
            p, q = _sample(seg, int(n), rng)
            pts.append(p * scale)
            nrm.append(q)
            base = self.palette[seg.role]
            jitter = 1.0 + 0.06 * rng.normal(size=(len(p), 1))       # fabric, skin texture
            col.append(np.clip(base * jitter, 0, 1))
            self.slices[seg.name] = slice(start, start + len(p))
            start += len(p)
        self.canon = np.concatenate(pts)
        self.normals = np.concatenate(nrm)
        self.albedo = np.concatenate(col)
        self.n = start

        # limb-preserving LOD: every level keeps a floor of points per segment
        self.level_idx = [np.arange(self.n)]
        sizes = np.array([self.slices[seg.name].stop - self.slices[seg.name].start
                          for seg in SEGMENTS])
        for k in range(1, n_levels):
            want = _floored_split(sizes, 4 ** k, LIMB_FLOOR[min(k, len(LIMB_FLOOR) - 1)])
            keep = [self.slices[seg.name].start + np.linspace(0, size - 1, w).astype(int)
                    for seg, size, w in zip(SEGMENTS, sizes, want)]
            self.level_idx.append(np.concatenate(keep))

    # -- skinning ----------------------------------------------------------
    def skin(self, pose: Pose) -> tuple[np.ndarray, np.ndarray]:
        """World-local positions and lit colours for a pose."""
        j = forward(pose, self.scale)
        out = np.empty_like(self.canon)
        nrm = np.empty_like(self.normals)

        def place(name, R, origin):
            sl = self.slices[name]
            out[sl] = self.canon[sl] @ R.T + origin
            nrm[sl] = self.normals[sl] @ R.T

        place("pelvis", j.R_pelvis, j.root - j.R_pelvis @ np.array([0, 0.10, 0]) * self.scale)
        place("torso", j.R_torso, j.spine)
        place("neck", j.R_torso, j.neck)
        R_head = j.R_torso @ rot_x(-pose.head)
        place("head", R_head, j.head)
        place("hair", R_head, j.head)
        for part in ("headset", "visor", "strap"):
            place(part, R_head, j.head)
        for side, sfx in ((1, "r"), (-1, "l")):
            place(f"upper_{sfx}", align(DOWN, j.elbow[side] - j.shoulder[side]), j.shoulder[side])
            place(f"fore_{sfx}", align(DOWN, j.wrist[side] - j.elbow[side]), j.elbow[side])
            place(f"hand_{sfx}", np.eye(3), j.hand[side])
            place(f"thigh_{sfx}", align(DOWN, j.knee[side] - j.hip[side]), j.hip[side])
            place(f"shin_{sfx}", align(DOWN, j.ankle[side] - j.knee[side]), j.knee[side])
            place(f"foot_{sfx}", np.eye(3), j.ankle[side])

        light = np.clip(nrm @ LIGHT, 0.0, 1.0)
        rgb = self.albedo * (0.50 + 0.50 * light)[:, None]
        # 32 levels per channel: invisible on a point cloud, and the capture's
        # colour compresses to about what a flat-shaded one would
        rgb8 = np.clip(rgb * 255, 0, 255).astype(np.uint8)
        return out.astype(np.float32), (rgb8 // 8 * 8 + 4).astype(np.uint8)


# --------------------------------------------------------------------------
# a cache of captures, shared by everyone with the same body and pose
# --------------------------------------------------------------------------
class CaptureCache:
    def __init__(self, capacity: int = 4000):
        self.capacity = capacity
        self.store: OrderedDict = OrderedDict()
        self.hits = self.misses = 0

    def get(self, body: Body, pose: Pose, level: int, make):
        # Keyed by the body's own number, not by id(): Python reuses the
        # address of a body that has been collected, and a cache that keys on
        # it hands the next person the previous person's points -- which ends
        # as an index error when the two were sampled at different densities.
        key = (body.uid, pose.key(), level)
        if key in self.store:
            self.store.move_to_end(key)
            self.hits += 1
            return self.store[key]
        self.misses += 1
        value = make()
        self.store[key] = value
        if len(self.store) > self.capacity:
            self.store.popitem(last=False)
        return value


CACHE = CaptureCache()
