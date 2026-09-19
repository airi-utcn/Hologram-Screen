"""Lessons: a teaching plan, as data, that the streamer can read ahead.

Existing viewport-adaptive streaming predicts attention from the viewer's own
recent head motion -- a horizon of a few hundred milliseconds.  A lesson has a
far better predictor: its *plan*.  "At 0:12 the tutor shows the burette; at
0:18 a student asks a question."  This module turns such a plan into:

  context(t)      who is speaking, what is being shown, which phase the lesson
                  is in, and what is coming up in the next few seconds
  camera_keys()   the viewpoint of a student following the lesson, generated
                  from the plan (react, turn, the occasional distracted glance)
  occluders()     live participants standing between the viewer and whatever
                  is being shown -- the ones a fixed class table starves
  apply(t)        what the room looks like at t: who has walked where, who is
                  raising a hand or pointing, which state each object is in --
                  so what the caption says is what the viewer sees

Lessons live in `lessons/*.json`, not in code.  See `lessons/README.md`.
"""
from __future__ import annotations

import json
import math
import pathlib
from dataclasses import dataclass, field

import numpy as np

from . import body as bodies

LESSON_DIR = pathlib.Path(__file__).resolve().parent.parent / "lessons"

DEFAULT_BOOSTS = {"speaker": 2.0, "focus": 2.5, "occluder": 1.6}

#: how long a viewer takes to notice a new step and start turning (seconds)
REACT_S = 0.6
#: how long a head turn takes
TURN_S = 1.0
#: how long walking to a new spot takes
WALK_S = 2.5
#: a distracted glance: turn, dwell, turn back
GLANCE_TURN_S = 0.45
GLANCE_DWELL_S = 1.2
#: participants walk at this pace when a step moves them (m/s)
WALK_SPEED = 1.4
#: they start moving this long after the step begins
ACTION_DELAY_S = 0.3
#: while writing, the teacher's position follows the pen averaged over this
#: long, so a new line is a short walk back rather than a jump
WRITE_FOLLOW_S = 0.3
#: jogging pace, for run_to and laps (m/s)
RUN_SPEED = 3.0
#: how long a change of activity or gesture takes to blend in
BLEND_S = 0.35
#: a writer arrives, turns to the board and lifts the pen before the first stroke
SETTLE_S = 0.45

#: what a plan can tell a person to do
ACTION_KEYS = {"walk_to", "run_to", "laps", "write", "gesture", "at", "face",
               "exercise", "depth", "tempo"}
GESTURES = ("none", "hand_up", "point", "wave", "clap", "whistle", "hands_on_hips",
            "shield")


@dataclass
class Step:
    index: int
    t: float
    end: float
    phase: str
    speakers: tuple[str, ...]
    focus: tuple[str, ...]
    caption: str
    viewer_to: tuple[float, float, float] | None = None
    actions: dict = field(default_factory=dict)    # name -> {"walk_to", "gesture", "write"}
    states: dict = field(default_factory=dict)     # name -> state
    # resolved against a scene by Lesson.bind()
    speaker_ids: frozenset[int] = frozenset()
    focus_ids: frozenset[int] = frozenset()
    state_ids: dict = field(default_factory=dict)  # oid -> state it enters here
    eye: np.ndarray | None = None          # where the viewer is during this step
    look: np.ndarray | None = None         # what they look at during this step

    @property
    def arrival(self) -> float:
        """When the viewer has actually turned (or walked) to this step's focus."""
        return self.t + REACT_S + (WALK_S if self.viewer_to else TURN_S)


@dataclass
class LessonContext:
    """What the lesson plan says is happening at time t."""
    t: float
    step: Step
    phase: str
    speakers: frozenset[int]
    focus: frozenset[int]
    phase_weights: dict[str, float]
    boosts: dict[str, float]
    upcoming: list[tuple[float, Step]] = field(default_factory=list)   # (seconds away, step)
    horizon: float = 4.0

    def to_dict(self, n_steps: int) -> dict:
        return {"t": round(self.t, 2), "step": self.step.index + 1, "steps": n_steps,
                "phase": self.phase, "caption": self.step.caption,
                "speakers": sorted(self.speakers), "focus": sorted(self.focus)}


@dataclass
class Lesson:
    name: str
    title: str
    scenario: str
    summary: str
    duration: float
    steps: list[Step]
    seat: np.ndarray
    phase_weights: dict[str, dict[str, float]]
    boosts: dict[str, float]
    horizon_s: float = 4.0
    glances_per_minute: float = 4.0
    seed: int = 0
    board_pages: list = field(default_factory=list)
    banner: str = ""        # shown on screen for the whole lesson (e.g. a safety note)
    scene: object | None = None

    # -- loading ----------------------------------------------------------
    @classmethod
    def from_dict(cls, d: dict) -> "Lesson":
        raw = sorted(d["steps"], key=lambda s: float(s["t"]))
        duration = float(d.get("duration", raw[-1]["t"] + 6.0))
        steps = []
        for i, s in enumerate(raw):
            end = float(raw[i + 1]["t"]) if i + 1 < len(raw) else duration
            speaker = s.get("speaker") or ()
            speakers = (speaker,) if isinstance(speaker, str) else tuple(speaker)
            focus = s.get("focus") or ()
            focus = (focus,) if isinstance(focus, str) else tuple(focus)
            vt = s.get("viewer_to")
            steps.append(Step(i, float(s["t"]), end, s.get("phase", "lesson"),
                              speakers, focus, s.get("caption", ""),
                              tuple(map(float, vt)) if vt else None,
                              dict(s.get("actions", {})), dict(s.get("states", {}))))
        viewer = d.get("viewer", {})
        return cls(
            name=d["name"], title=d.get("title", d["name"]),
            scenario=d["scenario"], summary=d.get("summary", ""),
            duration=duration, steps=steps,
            seat=np.asarray(viewer.get("seat", [0, 1.62, 5]), dtype=np.float64),
            phase_weights={k: dict(v) for k, v in d.get("phase_weights", {}).items()},
            boosts={**DEFAULT_BOOSTS, **d.get("boosts", {})},
            horizon_s=float(d.get("horizon_s", 4.0)),
            glances_per_minute=float(viewer.get("glances_per_minute", 4.0)),
            seed=int(viewer.get("seed", 0)),
            board_pages=list(d.get("board", [])),
            banner=d.get("banner", ""),
        )

    @classmethod
    def load(cls, path) -> "Lesson":
        path = pathlib.Path(path)
        return cls.from_dict(json.loads(path.read_text(encoding="utf-8")))

    # -- binding to a scene ----------------------------------------------
    def _expand(self, names, by_name) -> list:
        """Names in a plan may be groups: @athletes, @students, @people, @class."""
        out = []
        for n in names:
            if not n.startswith("@"):
                out.append(n)
                continue
            group = n[1:]
            people = [o for o in by_name.values() if o.dynamic]
            if group in ("people", "everyone"):
                members = people
            elif group == "class":
                members = [o for o in people if o.semantic != "instructor"]
            else:                                      # @athletes, @students ...
                members = [o for o in people if o.semantic + "s" == group
                           or o.semantic == group]
            if not members:
                raise ValueError(f"lesson {self.name!r}: group {n!r} is empty here")
            out.extend(o.name for o in members)
        return out

    def bind(self, scene) -> "Lesson":
        """Resolve names (and groups) to objects, add any board pages the lesson
        brings, and plan what everyone does.  Unknown names are an error."""
        by_name = {o.name: o for o in scene}
        for s in self.steps:
            s.focus = tuple(self._expand(s.focus, by_name))
            s.speakers = tuple(self._expand(s.speakers, by_name))
            actions = {}
            for n, act in s.actions.items():
                for m in self._expand([n], by_name):
                    actions[m] = {**actions.get(m, {}), **act}
            s.actions = actions
        if self.board_pages:
            self._add_board_pages(by_name)
        named = {n for s in self.steps
                 for n in (*s.focus, *s.speakers, *s.actions, *s.states)}
        for s in self.steps:
            for act in s.actions.values():
                for key in ("at", "face"):
                    if isinstance(act.get(key), str) and act[key] not in ("viewer", "none"):
                        named.add(act[key])
        missing = sorted(named - set(by_name))
        if missing:
            raise ValueError(f"lesson {self.name!r} refers to objects not in "
                             f"scenario {self.scenario!r}: {missing}")
        for s in self.steps:
            for n, st in s.states.items():
                if st not in by_name[n].states:
                    raise ValueError(f"lesson {self.name!r}: {n!r} has no state "
                                     f"{st!r} (has {by_name[n].states})")
            for n, act in s.actions.items():
                if not by_name[n].dynamic:
                    raise ValueError(f"lesson {self.name!r}: only people can act, "
                                     f"{n!r} is an object")
                unknown = set(act) - ACTION_KEYS
                if unknown:
                    raise ValueError(f"lesson {self.name!r}: unknown action {sorted(unknown)} "
                                     f"for {n!r} at t={s.t}")
                ex = act.get("exercise")
                if ex and ex not in ("none", "idle") and ex not in bodies.EXERCISES:
                    raise ValueError(f"lesson {self.name!r}: no exercise {ex!r}; "
                                     f"have {sorted(bodies.EXERCISES)}")
                g = act.get("gesture")
                if g and g not in GESTURES:
                    raise ValueError(f"lesson {self.name!r}: no gesture {g!r}; have {GESTURES}")
        self.scene = scene
        self._by_name = by_name
        self._choreograph(by_name)

        eye = self.seat.copy()
        for s in self.steps:
            s.speaker_ids = frozenset(by_name[n].oid for n in s.speakers)
            s.focus_ids = frozenset(by_name[n].oid for n in s.focus)
            s.state_ids = {by_name[n].oid: st for n, st in s.states.items()}
            if s.viewer_to:
                eye = np.asarray(s.viewer_to, dtype=np.float64)
            s.eye = eye.copy()
            # look where things *will be* once this step's walking is done
            t_look = min(s.end, s.t + 3.0)
            targets = [self._look_at(by_name[n], t_look) for n in (s.focus or s.speakers)]
            s.look = (np.mean(targets, axis=0) if targets
                      else eye + np.array([0.0, -0.1, -1.0]))
        return self

    def _add_board_pages(self, by_name) -> None:
        """Lessons can bring their own board content; it becomes new states of
        the room's board, named after the lesson so lessons never collide."""
        from .scenarios import add_board_pages
        board = by_name.get("annotation_layer")
        if board is None:
            raise ValueError(f"lesson {self.name!r} has board pages but the room has no board")
        prefix = f"{self.name}:"
        if not any(st.startswith(prefix) for st in board.states):
            add_board_pages(board, self.board_pages, prefix)
        names = {p["state"] for p in self.board_pages}
        for s in self.steps:
            for n, st in list(s.states.items()):
                if n == "annotation_layer" and st in names:
                    s.states[n] = prefix + st

    # -- planning: where everyone goes, what they do ----------------------
    def _choreograph(self, by_name) -> None:
        """Turn per-step actions into timelines that can be sampled at any t.

        Everything here is a pure function of t, so the server, the benchmark
        and the tests can ask about any moment in any order.
        """
        self._moves: dict[int, list] = {}      # oid -> [move segment dicts]
        self._acts: dict[int, list] = {}       # oid -> [(t, activity spec)]
        self._gests: dict[int, list] = {}      # oid -> [(t, gesture spec)]
        self._faces: dict[int, list] = {}      # oid -> [(t, face target)]
        self._states: dict[int, list] = {}     # oid -> [(t, state)]
        self._writes: dict[int, list] = {}     # oid -> [(start, duration, pen path)]
        self._state_start: dict = {}           # (oid, step t) -> when it really starts
        for s in self.steps:
            t0 = s.t + ACTION_DELAY_S
            default_target = next((by_name[n].oid for n in s.focus), None)
            for n, act in s.actions.items():
                o = by_name[n]
                if "write" in act:
                    self._plan_writing(o, by_name[act["write"]], s, t0)
                for key, kind, speed in (("walk_to", "walk", WALK_SPEED),
                                         ("run_to", "run", RUN_SPEED)):
                    if key in act:
                        here = self.xz_at(o, t0)
                        to = (o.base_centre[[0, 2]] if act[key] == "home"
                              else np.asarray(act[key], dtype=np.float64)[:2])
                        dur = max(0.4, float(np.linalg.norm(to - here)) / speed)
                        self._add_move(o, {"kind": kind, "t0": t0, "t1": t0 + dur,
                                           "a": here, "b": to, "speed": speed})
                if "laps" in act:
                    lp = act["laps"]
                    here = self.xz_at(o, t0)
                    c = np.asarray(lp.get("centre", [0, 0]), dtype=np.float64)
                    r = float(lp.get("radius", 5.0))
                    theta = math.atan2(here[1] - c[1], here[0] - c[0])
                    start = c + r * np.array([math.cos(theta), math.sin(theta)])
                    approach = max(0.4, float(np.linalg.norm(start - here)) / RUN_SPEED)
                    self._add_move(o, {"kind": "laps", "t0": t0, "t1": math.inf,
                                       "a": here, "start": start, "c": c, "r": r,
                                       "theta": theta, "approach": approach,
                                       "speed": float(lp.get("speed", RUN_SPEED))})
                if "exercise" in act:
                    ex = act["exercise"]
                    spec = ({"kind": "idle"} if ex in ("none", "idle") else
                            {"kind": "exercise", "name": ex, "t0": t0,
                             "depth": float(act.get("depth", 1.0)),
                             "tempo": float(act.get("tempo", 1.0))})
                    self._acts.setdefault(o.oid, []).append((t0, spec))
                if "gesture" in act:
                    at = act.get("at")
                    target = (by_name[at].oid if isinstance(at, str) else
                              (default_target if default_target != o.oid else None))
                    self._gests.setdefault(o.oid, []).append(
                        (t0, {"name": act["gesture"] or "none", "at": target}))
                if "face" in act:
                    f = act["face"]
                    self._faces.setdefault(o.oid, []).append(
                        (t0, None if f in (None, "none") else
                         ("viewer" if f == "viewer" else by_name[f].oid)))
            for n, st in s.states.items():
                self._states.setdefault(by_name[n].oid, []).append((s.t, st))
        for table in (self._acts, self._gests, self._faces):
            for timeline in table.values():
                timeline.sort(key=lambda e: e[0])

    def _add_move(self, o, seg: dict) -> None:
        moves = self._moves.setdefault(o.oid, [])
        for m in moves:                         # a new move ends open-ended laps
            if m["kind"] == "laps" and m["t1"] > seg["t0"]:
                m["t1"] = seg["t0"]
        moves.append(seg)
        moves.sort(key=lambda m: m["t0"])

    # -- writing on the board --------------------------------------------
    @staticmethod
    def _pen_path(board, state: str) -> list:
        """Where the pen is for each stroke of a board state, in writing order."""
        (reveal,) = [a for a in board.anims.get(state, []) if a["kind"] == "reveal"]
        lv = board.level(0, state=state)
        path, times = [], []
        for i, k in enumerate(range(reveal["from"], reveal["to"] + 1)):
            pts = lv.pos[lv.part == k]
            if len(pts):
                path.append(pts.mean(axis=0).astype(np.float64))
                times.append(reveal["times"][i] if "times" in reveal
                             else i * reveal["duration"] / (reveal["to"] - reveal["from"] + 1))
        return (path, times), reveal["duration"]

    @staticmethod
    def _stance(obj, pen) -> np.ndarray:
        """Where to stand to write at `pen`: facing the board, the pen in front
        of the right shoulder, close enough for the arm to reach it."""
        sc = obj.body.scale if obj.body is not None else 1.0
        return np.array([pen[0] - (bodies.SHOULDER_X + 0.04) * sc, pen[2] + 0.24 * sc])

    def _plan_writing(self, writer, board, step, t0) -> None:
        """The writer walks to where the first word goes, then writes it: the
        board's strokes appear only once they are there, and they move along
        the line with the pen -- the hand on the pen, by inverse kinematics."""
        state = step.states.get(board.name)
        if state is None:
            raise ValueError(f"lesson {self.name!r}: step at t={step.t} writes on "
                             f"{board.name!r} but sets no state for it")
        if board.name == "annotation_layer" and self.board_pages and \
                state in {p["state"] for p in self.board_pages}:
            state = f"{self.name}:{state}"
        (path, times), duration = self._pen_path(board, state)
        here = self.xz_at(writer, t0)
        first = self._stance(writer, path[0])
        approach = float(np.clip(np.linalg.norm(first - here) / WALK_SPEED, 0.6, 4.0))
        self._add_move(writer, {"kind": "walk", "t0": t0, "t1": t0 + approach,
                                "a": here, "b": first, "speed": WALK_SPEED})
        arrive = t0 + approach
        start = arrive + SETTLE_S
        self._add_move(writer, {"kind": "write", "t0": arrive, "t1": start + duration,
                                "path": path, "times": [SETTLE_S + x for x in times],
                                "board_z": float(path[0][2])})
        self._writes.setdefault(writer.oid, []).append((start, duration, path))
        self._state_start[(board.oid, step.t)] = start          # first stroke here

    @staticmethod
    def _pen_at(seg, t: float) -> tuple[np.ndarray, np.ndarray]:
        """Where the pen is at t during a write segment, on the same timetable
        the viewer uses to reveal the strokes; between strokes it travels in a
        straight line.  (Returned twice: where the hand goes, where to stand.)"""
        path, times = seg["path"], seg["times"]
        u = t - seg["t0"]
        k = int(np.searchsorted(times, u, side="right")) - 1
        if k < 0:
            return path[0], path[0]
        if k >= len(path) - 1:
            return path[-1], path[-1]
        span = times[k + 1] - times[k]
        w = min(1.0, (u - times[k]) / span) if span > 1e-9 else 1.0
        p = path[k] + (path[k + 1] - path[k]) * w
        return p, p

    # -- sampling the plan at any t ---------------------------------------
    def _move_at(self, o, t: float):
        """The move segment governing time t (or the last finished one)."""
        current = None
        for m in self._moves.get(o.oid, []):
            if m["t0"] <= t:
                current = m
            else:
                break
        return current

    def _seg_xz(self, o, m, t: float) -> np.ndarray:
        k = m["kind"]
        if k in ("walk", "run"):
            u = 1.0 if t >= m["t1"] else (t - m["t0"]) / (m["t1"] - m["t0"])
            return m["a"] + (m["b"] - m["a"]) * u
        if k == "write":
            return self._stance(o, self._pen_at(m, min(t, m["t1"]))[1])
        # laps: jog to the circle, then round it
        tt = min(t, m["t1"])
        if tt < m["t0"] + m["approach"]:
            u = (tt - m["t0"]) / m["approach"]
            return m["a"] + (m["start"] - m["a"]) * u
        ang = m["theta"] + m["speed"] / m["r"] * (tt - m["t0"] - m["approach"])
        return m["c"] + m["r"] * np.array([math.cos(ang), math.sin(ang)])

    def xz_at(self, o, t: float) -> np.ndarray:
        m = self._move_at(o, t)
        if m is None:
            return o.base_centre[[0, 2]].astype(np.float64)
        return self._seg_xz(o, m, t)

    def offset_at(self, obj, t: float) -> np.ndarray:
        xz = self.xz_at(obj, t)
        return np.array([xz[0] - obj.base_centre[0], 0.0, xz[1] - obj.base_centre[2]])

    def _look_at(self, obj, t: float) -> np.ndarray:
        base = look_point(obj, obj.base_centre)
        return base + (self.offset_at(obj, t) if obj.dynamic else 0.0)

    @staticmethod
    def _latest(timeline, t: float, default):
        cur, since = default, 0.0
        for ti, v in timeline:
            if t >= ti:
                cur, since = v, ti
        return cur, since

    def _mode(self, o, t: float) -> tuple[str, dict | None]:
        m = self._move_at(o, t)
        if m is not None and t < m["t1"]:
            return m["kind"], m
        return "still", m

    def _target_point(self, o, target, t: float):
        if target is None:
            return None
        if target == "viewer":
            return self.step_at(t).eye
        tgt = self.scene.by_id(target)
        return self._look_at(tgt, t) if tgt.dynamic else look_point(tgt)

    def _yaw_target(self, o, t: float) -> float:
        xz = self.xz_at(o, t)
        mode, m = self._mode(o, t)
        if mode == "write":
            return bodies.facing_yaw(xz, (xz[0], m["board_z"]))
        if mode in ("walk", "run", "laps"):
            d = self.xz_at(o, t + 0.05) - self.xz_at(o, t - 0.05)
            if np.hypot(*d) > 1e-4:
                return bodies.facing_yaw(xz, xz + d)
        face, _ = self._latest(self._faces.get(o.oid, []), t, None)
        if face is not None:
            p = self._target_point(o, face, t)
            return bodies.facing_yaw(xz, p[[0, 2]])
        g, _ = self._latest(self._gests.get(o.oid, []), t, {"name": "none", "at": None})
        if g["name"] == "point" and g["at"] is not None:
            p = self._target_point(o, g["at"], t)
            return bodies.facing_yaw(xz, p[[0, 2]])
        return o.base_yaw

    def yaw_at(self, o, t: float) -> float:
        """Facing, turned smoothly: the circular mean of where they wanted to
        face over the last ~0.4 s (a body turns, it doesn't snap)."""
        ys = [self._yaw_target(o, t - dt) for dt in (0.0, 0.1, 0.2, 0.3, 0.4)]
        return math.atan2(sum(math.sin(y) for y in ys), sum(math.cos(y) for y in ys))

    def _base_pose(self, o, t: float, when: float):
        """What the body is doing, per the plan in force at `when`, at time t."""
        mode, m = self._mode(o, when)
        sc = o.body.scale
        if mode == "write" and m is not None:
            pen, _ = self._pen_at(m, min(t, m["t1"]))
            drop = float(np.clip((1.15 * sc - pen[1]) / (0.45 * sc), 0.0, 0.7))
            pose = bodies.squats(0.5, drop) if drop > 0 else bodies.idle(t, o.seed)
            pose = pose.with_arm(-1, bodies.limb(0.15, 0.1, -1), bodies.limb(0.3, 0.1, -1))
            stand = self._stance(o, self._pen_at(m, min(t, m["t1"]))[1])
            yaw = bodies.facing_yaw(stand, (stand[0], m["board_z"]))
            local = bodies.rot_y(-yaw) @ (pen - np.array([stand[0], 0.0, stand[1]]))
            return bodies.reach(pose, 1, local, sc)
        if mode in ("walk", "run", "laps") and m is not None:
            dist = m["speed"] * max(0.0, t - m["t0"])
            if mode == "walk":
                return bodies.walk(dist / 1.45)
            return bodies.run(dist / 2.2)
        spec, _ = self._latest(self._acts.get(o.oid, []), when, {"kind": "idle"})
        if spec["kind"] == "exercise":
            return bodies.activity(spec["name"], (t - spec["t0"]) * spec["tempo"],
                                   depth=spec["depth"], seed=o.seed)
        return bodies.idle(t, o.seed)

    def _changes(self, o) -> list[float]:
        """Every moment this person's plan switches what they are doing."""
        out = [e[0] for e in self._acts.get(o.oid, [])]
        for m in self._moves.get(o.oid, []):
            out.append(m["t0"])
            if math.isfinite(m["t1"]):
                out.append(m["t1"])
        return sorted(out)

    def _gesture(self, o, pose, spec, t: float):
        name, sc = spec["name"], o.body.scale
        if name == "none":
            return pose
        if name == "hand_up":
            return bodies.hand_up(pose)
        if name == "wave":
            return bodies.wave(pose, t)
        if name == "clap":
            return bodies.clap(pose, t, sc)
        if name == "whistle":
            return bodies.whistle(pose, sc)
        if name == "hands_on_hips":
            return bodies.hands_on_hips(pose, scale=sc)
        if name == "shield":
            return bodies.shield(pose, sc)
        # point: at the target if there is one, else forward and up
        xz = self.xz_at(o, t)
        p = self._target_point(o, spec["at"], t)
        if p is None:
            return bodies.point_at(pose, 1, [0.35, 1.7 * sc, -1.5], sc)
        yaw = self.yaw_at(o, t)
        local = bodies.rot_y(-yaw) @ (np.asarray(p) - np.array([xz[0], 0.0, xz[1]]))
        return bodies.point_at(pose, 1, local, sc)

    def pose_at(self, o, t: float):
        """The body's pose at t: the planned activity, blended over 0.35 s
        from whatever came before, with any gesture blended in on top."""
        base = self._base_pose(o, t, t)
        changes = [c for c in self._changes(o) if c <= t]
        if changes and t - changes[-1] < BLEND_S:
            before = self._base_pose(o, t, changes[-1] - 1e-3)
            base = bodies.blend(before, base, bodies.smooth((t - changes[-1]) / BLEND_S))
        mode, _ = self._mode(o, t)
        if mode == "write":
            return base
        spec, since = self._latest(self._gests.get(o.oid, []), t,
                                   {"name": "none", "at": None})
        pose = self._gesture(o, base, spec, t)
        if since and t - since < BLEND_S:
            prev, _ = self._latest(self._gests.get(o.oid, []), since - 1e-3,
                                   {"name": "none", "at": None})
            pose = bodies.blend(self._gesture(o, base, prev, t), pose,
                                bodies.smooth((t - since) / BLEND_S))
        return pose

    def gesture_at(self, o, t: float) -> str:
        if self._mode(o, t)[0] == "write":
            return "write"
        spec, _ = self._latest(self._gests.get(o.oid, []), t, {"name": "none", "at": None})
        return spec["name"]

    def state_since(self, oid: int, t: float) -> float:
        """When the object entered the state it is in at time t."""
        since = 0.0
        for ti, _ in self._states.get(oid, []):
            if t >= ti:
                since = self._state_start.get((oid, ti), ti)
        return since

    def apply(self, t: float) -> dict:
        """Put every object where the plan says it is at time t.

        Returns {oid: new_state} for static objects whose state changed, so
        the caller can react (a new state is new geometry to deliver).
        """
        if self.scene is None:
            raise RuntimeError("bind() the lesson to a scene first")
        changed = {}
        for o in self.scene:
            if o.dynamic:
                o.move(self.offset_at(o, t))
                o.yaw = self.yaw_at(o, t)
                o.pose = self.pose_at(o, t)
                o.gesture = self.gesture_at(o, t)
            elif o.oid in self._states:
                st, _ = self._latest(self._states[o.oid], t, "default")
                if st != o.state:
                    o.state = st
                    changed[o.oid] = st
        return changed

    # -- queries ----------------------------------------------------------
    def step_at(self, t: float) -> Step:
        for s in reversed(self.steps):
            if t >= s.t:
                return s
        return self.steps[0]

    def context(self, t: float) -> LessonContext:
        if self.scene is None:
            raise RuntimeError("bind() the lesson to a scene first")
        s = self.step_at(t)
        upcoming = [(n.t - t, n) for n in self.steps
                    if 0.0 < n.t - t <= self.horizon_s]
        return LessonContext(t, s, s.phase, s.speaker_ids, s.focus_ids,
                             self.phase_weights.get(s.phase, {}), self.boosts,
                             upcoming, self.horizon_s)

    # -- the viewer ------------------------------------------------------
    def camera_keys(self) -> list:
        """Keyframes for a student following the lesson.

        They notice each new step after REACT_S, turn (or walk) to it, and every
        so often glance at something unrelated -- which no plan can predict,
        and keeps the plan-driven policy honest.
        """
        if self.scene is None:
            raise RuntimeError("bind() the lesson to a scene first")
        rng = np.random.default_rng(self.seed)

        moves = []                     # (start, duration, eye, look)
        for s in self.steps[1:]:
            moves.append((s.t + REACT_S, WALK_S if s.viewer_to else TURN_S,
                          s.eye, s.look))

        candidates = [o for o in self.scene
                      if o.semantic not in ("structure",) and o.radius < 3.0]
        n_glances = int(round(self.duration / 60.0 * self.glances_per_minute))
        for _ in range(n_glances):
            s = self.steps[int(rng.integers(len(self.steps)))]
            lo, hi = s.arrival + 0.5, s.end - (2 * GLANCE_TURN_S + GLANCE_DWELL_S) - 0.3
            if hi <= lo:
                continue
            tg = float(rng.uniform(lo, hi))
            pool = [o for o in candidates if o.oid not in s.focus_ids
                    and np.linalg.norm(o.centre - s.eye) > 1.5]
            if not pool:
                continue
            target = self._look_at(pool[int(rng.integers(len(pool)))], tg)
            moves.append((tg, GLANCE_TURN_S, s.eye, target))
            moves.append((tg + GLANCE_TURN_S + GLANCE_DWELL_S, GLANCE_TURN_S,
                          s.eye, s.look))

        moves.sort(key=lambda m: m[0])
        first = self.steps[0]
        eye, look = first.eye, first.look
        keys = [(0.0, tuple(eye), tuple(look))]
        busy_until = 0.0
        for start, dur, new_eye, new_look in moves:
            if start < busy_until:                 # still mid-turn: skip it
                continue
            keys.append((start, tuple(eye), tuple(look)))
            keys.append((start + dur, tuple(new_eye), tuple(new_look)))
            eye, look, busy_until = new_eye, new_look, start + dur
        if keys[-1][0] < self.duration:
            keys.append((self.duration, tuple(eye), tuple(look)))
        return keys


# --------------------------------------------------------------------------
def look_point(obj, centre=None) -> np.ndarray:
    """Where a viewer looks at an object: a person's chest, a thing's centre."""
    c = np.asarray(obj.centre if centre is None else centre, dtype=np.float64)
    if obj.kind == "points":
        return c + np.array([0.0, 0.45, 0.0])
    return c


#: a standing person, for line-of-sight tests: a vertical capsule
BODY_BOTTOM, BODY_TOP, BODY_RADIUS = 0.1, 1.72, 0.26


def _segment_distance(p0, p1, q0, q1) -> tuple[float, float]:
    """Closest distance between segments p0-p1 and q0-q1, and where on p it is
    (0 = at p0, 1 = at p1)."""
    d1, d2, r = p1 - p0, q1 - q0, p0 - q0
    a, e, f = float(d1 @ d1), float(d2 @ d2), float(d2 @ r)
    c, b = float(d1 @ r), float(d1 @ d2)
    denom = a * e - b * b
    s = np.clip((b * f - c * e) / denom, 0.0, 1.0) if denom > 1e-12 else 0.0
    t = (b * s + f) / e if e > 1e-12 else 0.0
    if t < 0.0:
        t, s = 0.0, np.clip(-c / a, 0.0, 1.0)
    elif t > 1.0:
        t, s = 1.0, np.clip((b - c) / a, 0.0, 1.0)
    gap = (p0 + d1 * s) - (q0 + d2 * t)
    return float(np.linalg.norm(gap)), float(s)


def occluders(scene, eye: np.ndarray, focus_ids) -> set[int]:
    """Live participants standing between the viewer and what is being shown.

    A frozen or blocky person in front of the demonstration spoils the view of
    the demonstration itself, so they matter as much as the thing behind them.
    People are tested as upright capsules rather than bounding spheres: a
    sphere cannot tell a head in the sight line from a sight line passing
    safely over it.
    """
    out: set[int] = set()
    eye = np.asarray(eye, dtype=np.float64)
    for fid in focus_ids:
        target = look_point(scene.by_id(fid))
        for o in scene.dynamic:
            if o.oid == fid or o.oid in focus_ids:
                continue
            base = np.array([o.centre[0], BODY_BOTTOM, o.centre[2]])
            top = np.array([o.centre[0], BODY_TOP, o.centre[2]])
            dist, along = _segment_distance(eye, target, base, top)
            if 0.05 < along < 0.97 and dist < BODY_RADIUS:
                out.add(o.oid)
    return out


# --------------------------------------------------------------------------
def available() -> list[pathlib.Path]:
    return sorted(LESSON_DIR.glob("*.json"))


def load_all() -> dict[str, Lesson]:
    out = {}
    for p in available():
        les = Lesson.load(p)
        out[les.name] = les
    return out


def get(name: str) -> Lesson:
    """Load a lesson by name (fresh copy, unbound)."""
    for p in available():
        les = Lesson.load(p)
        if les.name == name:
            return les
    raise KeyError(f"unknown lesson {name!r}; have {[Lesson.load(p).name for p in available()]}")
