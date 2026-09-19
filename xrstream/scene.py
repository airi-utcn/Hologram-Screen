"""Scene graph for holographic lessons (SOTA UC7, sections 1.3-1.5).

A scene is deliberately *heterogeneous* and *mixed-rate*:

  static objects   polygonal meshes streamed once and cached -- room structure,
                   furniture, laboratory apparatus, educational content (1.4)
  dynamic objects  live volumetric point clouds that must be re-transmitted
                   every capture frame -- the instructor and the students (1.5)

The dynamic half is what makes bandwidth the binding constraint: a roomful of
participants at full capture rate needs hundreds of Mbit/s, far beyond any
realistic link, so the streaming policy is forced to choose who gets detail.

Every object carries a semantic class, because semantic prioritisation is the
research gap this testbed targets (1.5.7).  The concrete lessons -- a lecture,
a physics lab, a chemistry lab, a PE class -- live in `scenarios.py`.

Objects can also *change* during a lesson, which is what lets a lesson plan
actually play out on screen:

  states     a static object has named variants ("empty", "pink", ...); a new
             state is new geometry the client must receive before it can show it
  people     are articulated bodies (body.py): a skeleton the capture is
             skinned onto, placed in the room by (x, z, yaw).  Every gesture
             and exercise is a real pose of the limbs.
  anims      a state can carry *motion parameters* (a pendulum's swing, drops
             falling, writing appearing stroke by stroke, a colour slowly
             changing).  The client plays them on the geometry it already has,
             so continuous motion costs a few numbers, not a mesh per frame.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from . import body as bodies, codec, geometry as g

#: capture rate of the volumetric rig, in frames per second
VOLUMETRIC_HZ = 15.0


# Relative importance of each semantic class for educational quality of
# experience.  These are hand-set weights; they are exactly the knob an
# orchestrator / learned policy would replace.  A scenario may override them:
# in a PE class the students *are* the lesson, in a lecture the instructor is.
DEFAULT_WEIGHTS = {
    "instructor": 3.0,
    "demo": 3.5,        # the thing everyone is crowding around to see
    "content": 2.5,     # whiteboard, annotations, projected slides
    "apparatus": 2.2,   # experiment rigs being actively manipulated
    "equipment": 2.0,   # instruments, CAD, anatomical models
    "athlete": 2.0,     # participants whose motion is the subject
    "student": 1.5,
    "furniture": 0.7,
    "structure": 0.4,   # floor, walls
}

#: backwards-compatible alias
SEMANTIC_WEIGHT = DEFAULT_WEIGHTS


# --------------------------------------------------------------------------
@dataclass
class LODLevel:
    level: int
    pos: np.ndarray
    idx: np.ndarray | None = None
    rgb: np.ndarray | None = None
    part: np.ndarray | None = None
    _cache: dict = field(default_factory=dict, repr=False)

    @property
    def n_vertices(self) -> int:
        return int(len(self.pos))

    @property
    def n_triangles(self) -> int:
        return 0 if self.idx is None else int(len(self.idx))

    @property
    def n_primitives(self) -> int:
        return self.n_triangles or self.n_vertices

    def encoded(self, quantise: bool = True, deflate: bool = True):
        key = (quantise, deflate)
        if key not in self._cache:
            if self.idx is None:
                self._cache[key] = codec.encode_points(self.pos, self.rgb, quantise, deflate)
            else:
                self._cache[key] = codec.encode_mesh(self.pos, self.idx, quantise,
                                                     deflate, self.rgb, self.part)
        return self._cache[key]

    def nbytes(self, quantise: bool = True, deflate: bool = True) -> int:
        return self.encoded(quantise, deflate)[1]["bytes"]


@dataclass
class SceneObject:
    oid: int
    name: str
    semantic: str
    kind: str                                  # 'mesh' | 'points'
    poses: list[list[LODLevel]]                # poses[frame][level]
    dynamic: bool = False
    weight: float = 1.0
    colour: tuple[float, float, float] = (0.8, 0.8, 0.8)
    centre: np.ndarray = field(default_factory=lambda: np.zeros(3))
    radius: float = 1.0
    # -- lesson-driven changes ---------------------------------------------
    variants: dict = field(default_factory=dict, repr=False)        # state -> levels
    variant_colour: dict = field(default_factory=dict, repr=False)  # state -> rgb
    anims: dict = field(default_factory=dict, repr=False)           # state -> [spec]
    state: str = "default"
    offset: np.ndarray = field(default_factory=lambda: np.zeros(3))
    gesture: str = "none"
    base_centre: np.ndarray | None = None
    # -- people ------------------------------------------------------------
    body: object | None = field(default=None, repr=False)          # body.Body
    pose: object | None = field(default=None, repr=False)          # body.Pose now
    yaw: float = 0.0                                                # facing, radians
    base_yaw: float = 0.0
    default_activity: tuple = ("idle",)                             # without a lesson
    seed: float = 0.0                                               # desynchronises idling

    def __post_init__(self):
        if self.base_centre is None:
            self.base_centre = np.asarray(self.centre, dtype=np.float64).copy()

    # -- lesson-driven changes ---------------------------------------------
    @property
    def states(self) -> list[str]:
        return ["default", *self.variants]

    def colour_for(self, state: str | None = None) -> tuple:
        return tuple(self.variant_colour.get(state or self.state, self.colour))

    def move(self, offset) -> None:
        """Place a participant `offset` metres from their home position."""
        self.offset = np.asarray(offset, dtype=np.float64)
        self.centre = self.base_centre + self.offset

    @property
    def place(self) -> list:
        """Where the client puts this person's capture: x, y, z, yaw."""
        x, z = self.base_centre[0] + self.offset[0], self.base_centre[2] + self.offset[2]
        return [round(float(x), 3), 0.0, round(float(z), 3), round(float(self.yaw), 4)]

    def reset_dynamics(self) -> None:
        self.state = "default"
        self.gesture = "none"
        self.move(np.zeros(3))
        if self.body is not None:
            self.yaw = self.base_yaw
            self.pose = bodies.idle(0.0, self.seed)

    def _frame(self, lvl: int) -> "LODLevel":
        """The capture of this person's current pose, at one level of detail.

        Shared by everyone with the same body and pose (a class doing jumping
        jacks in step), and reused across repeating cycles.
        """
        body, pose = self.body, self.pose
        full = bodies.CACHE.get(body, pose, -1, lambda: body.skin(pose))
        idx = body.level_idx[lvl]
        return bodies.CACHE.get(body, pose, lvl,
                                lambda: LODLevel(lvl, full[0][idx], None, full[1][idx]))

    # -- shape ------------------------------------------------------------
    @property
    def levels(self) -> list[LODLevel]:
        return self.poses[0]

    @property
    def n_levels(self) -> int:
        return len(self.poses[0])

    @property
    def n_poses(self) -> int:
        return len(self.poses)

    @property
    def coarsest(self) -> int:
        return self.n_levels - 1

    def level(self, lvl: int, pose: int = 0, state: str | None = None) -> LODLevel:
        if self.dynamic:
            return self._frame(lvl)
        state = state or self.state
        levels = self.variants.get(state, self.poses[0]) if state != "default" \
            else self.poses[0]
        return levels[min(lvl, len(levels) - 1)]

    def nbytes(self, lvl: int, quantise=True, deflate=True, pose: int = 0,
               state: str | None = None) -> int:
        if self.dynamic:
            # a capture's size barely depends on the pose: plan with the size of
            # the resting capture, pay for the real one when it is sent
            return self.poses[0][lvl].nbytes(quantise, deflate)
        return self.level(lvl, pose, state).nbytes(quantise, deflate)

    # -- viewpoint --------------------------------------------------------
    def required_level(self, cam, px_per_primitive: float = 24.0) -> int:
        """Finest level actually worth sending for the current viewpoint.

        Targets roughly `px_per_primitive` screen pixels per primitive and
        returns the coarsest level that still meets it (0 == finest).
        """
        r_px = cam.screen_radius_px(self.centre, self.radius)
        want = max(8.0, np.pi * r_px * r_px / px_per_primitive)
        for k in range(self.coarsest, -1, -1):
            if self.levels[k].n_primitives >= want:
                return k
        return 0

    def meta(self) -> dict:
        return {
            "oid": self.oid,
            "name": self.name,
            "semantic": self.semantic,
            "kind": self.kind,
            "dynamic": self.dynamic,
            "weight": self.weight,
            "colour": list(self.colour),
            "centre": [float(v) for v in self.base_centre],
            "yaw": float(self.base_yaw),
            "states": self.states,
            "radius": float(self.radius),
            "levels": [
                {"level": l.level, "nv": l.n_vertices, "nt": l.n_triangles}
                for l in self.levels
            ],
        }


@dataclass
class Scene:
    objects: list[SceneObject]
    volumetric_hz: float = VOLUMETRIC_HZ
    name: str = "scene"
    title: str = ""
    blurb: str = ""

    def __iter__(self):
        return iter(self.objects)

    def __len__(self):
        return len(self.objects)

    def by_id(self, oid: int) -> SceneObject:
        return self.objects[oid]

    def reset_dynamics(self) -> None:
        """Put every object back in its initial state, pose and place."""
        for o in self.objects:
            o.reset_dynamics()

    def animate(self, t: float) -> None:
        """Without a lesson, everyone does their default activity."""
        for o in self.objects:
            if o.body is None:
                continue
            name, *rest = o.default_activity
            if name == "program":                 # cycle through exercises together
                names, dwell = rest
                name = names[int(t // dwell) % len(names)]
            o.pose = bodies.activity(name, t, seed=o.seed)

    @property
    def static(self):
        return [o for o in self.objects if not o.dynamic]

    @property
    def dynamic(self):
        return [o for o in self.objects if o.dynamic]

    def static_bytes(self, quantise=True, deflate=True) -> int:
        """One-off cost of the static half at finest LOD."""
        return sum(o.nbytes(0, quantise, deflate) for o in self.static)

    def uncompressed_stream_bps(self, quantise=True, deflate=True) -> float:
        """Bits/s needed to carry every participant at full capture quality."""
        per_frame = sum(o.nbytes(0, quantise, deflate) for o in self.dynamic)
        return per_frame * 8 * self.volumetric_hz

    def stats(self, quantise=True, deflate=True) -> dict:
        return {
            "scenario": self.name,
            "title": self.title,
            "objects": len(self.objects),
            "static_objects": len(self.static),
            "dynamic_objects": len(self.dynamic),
            "triangles_l0": sum(o.levels[0].n_triangles for o in self.objects),
            "points_l0": sum(o.levels[0].n_vertices for o in self.objects
                             if o.kind == "points"),
            "static_bytes": self.static_bytes(quantise, deflate),
            "volumetric_hz": self.volumetric_hz,
            "full_stream_mbps": round(
                self.uncompressed_stream_bps(quantise, deflate) / 1e6, 1),
        }


# --------------------------------------------------------------------------
# builder
# --------------------------------------------------------------------------
def _bounds(pos: np.ndarray):
    lo, hi = pos.min(axis=0), pos.max(axis=0)
    centre = (lo + hi) / 2.0
    return centre, max(float(np.linalg.norm(hi - centre)), 1e-3)


def _resolutions(base: int, n: int = 4, floor: int = 3) -> list[int]:
    out, r = [], float(base)
    while len(out) < n:
        v = max(floor, int(round(r)))
        if out and v == out[-1]:
            break
        out.append(v)
        r /= 2.0
    return out


class SceneBuilder:
    """Accumulates objects into a Scene, handing out ids and semantic weights."""

    def __init__(self, name: str, title: str = "", blurb: str = "",
                 seed: int = 7, weights: dict[str, float] | None = None,
                 volumetric_hz: float = VOLUMETRIC_HZ):
        self.name = name
        self.title = title or name
        self.blurb = blurb
        self.volumetric_hz = volumetric_hz
        self.weights = dict(DEFAULT_WEIGHTS)
        if weights:
            self.weights.update(weights)
        self.rng = np.random.default_rng(seed)
        self.objects: list[SceneObject] = []
        self._bodies: dict = {}

    # -- internals --------------------------------------------------------
    def _add(self, name, semantic, kind, poses, colour, dynamic=False) -> SceneObject:
        centre, radius = _bounds(poses[0][0].pos)
        obj = SceneObject(len(self.objects), name, semantic, kind, poses, dynamic,
                          self.weights.get(semantic, 1.0), colour, centre, radius)
        self.objects.append(obj)
        return obj

    @staticmethod
    def _add_variants(obj, variants, make_levels):
        """variants: state -> (build, colour) or (build, colour, anims)."""
        for state, spec in (variants or {}).items():
            build, colour, anims = (*spec, None)[:3]
            obj.variants[state] = make_levels(build)
            if colour is not None:
                obj.variant_colour[state] = tuple(colour)
            if anims:
                obj.anims[state] = list(anims)
        # a variant can be bigger than the default (liquid, vapour, a flame):
        # the bounds must cover every state the object can be in
        allp = np.concatenate([obj.poses[0][0].pos]
                              + [lv[0].pos for lv in obj.variants.values()])
        obj.centre, obj.radius = _bounds(allp)
        obj.base_centre = obj.centre.copy()

    # -- public -----------------------------------------------------------
    def mesh(self, name, semantic, build, base_res, colour, n_levels=4,
             variants: dict | None = None, anims: list | None = None) -> SceneObject:
        """build(res) -> mesh; LOD chain by halving the sampling resolution.

        `variants` maps a state name to (build, colour) -- another look the
        object can take during a lesson, with the same LOD chain.
        """
        res = _resolutions(base_res, n_levels)
        chain = lambda b: [LODLevel(k, *b(r)) for k, r in enumerate(res)]
        obj = self._add(name, semantic, "mesh", [chain(build)], colour)
        if anims:
            obj.anims["default"] = list(anims)
        if variants:
            self._add_variants(obj, variants, chain)
        return obj

    def solid(self, name, semantic, mesh, colour,
              variants: dict | None = None, anims: list | None = None) -> SceneObject:
        """Single-LOD object (boxes and other fixed-resolution primitives)."""
        obj = self._add(name, semantic, "mesh", [[LODLevel(0, *mesh)]], colour)
        if anims:
            obj.anims["default"] = list(anims)
        if variants:
            self._add_variants(obj, variants, lambda m: [LODLevel(0, *m)])
        return obj

    def participant(self, name, semantic, x, z, n_points, palette: dict,
                    face=None, yaw: float = 0.0, scale: float = 1.0,
                    default=("idle",), body_seed: int = 0) -> SceneObject:
        """A live volumetric participant: an articulated body at (x, z).

        `face` is a point (x, z) they turn towards at rest, otherwise `yaw`.
        People with the same palette, size and point count share one body
        template, so identical poses share one encoded capture.
        """
        key = (n_points, tuple(sorted((k, tuple(v)) for k, v in palette.items())),
               scale, body_seed)
        if key not in self._bodies:
            self._bodies[key] = bodies.Body(n_points, palette, scale, seed=body_seed)
        body = self._bodies[key]
        rest = bodies.Pose()
        pts, rgb = body.skin(rest)
        levels = [LODLevel(k, pts[idx], None, rgb[idx]) for k, idx in enumerate(body.level_idx)]
        base_yaw = bodies.facing_yaw((x, z), face) if face is not None else yaw
        colour = tuple(palette["shirt"])
        obj = SceneObject(len(self.objects), name, semantic, "points", [levels], True,
                          self.weights.get(semantic, 1.0), colour,
                          np.array([x, 1.0 * scale, z]), 1.15 * scale)
        obj.body, obj.base_yaw, obj.yaw = body, base_yaw, base_yaw
        obj.default_activity = tuple(default)
        obj.seed = float(self.rng.random())
        obj.pose = bodies.idle(0.0, obj.seed)
        self.objects.append(obj)
        return obj

    def finish(self) -> Scene:
        return Scene(self.objects, self.volumetric_hz, self.name, self.title, self.blurb)
