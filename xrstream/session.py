"""Streaming session: budget accounting, delivery state and quality metrics.

One session = one connected viewer.  Each tick it is handed a camera pose and
an elapsed time, spends the corresponding bandwidth budget on whatever the
policy ranked highest, and records how good the client's view currently is.

Three details make the bandwidth numbers mean what they say:

* transfers are *drained across ticks* -- a large object occupies the link for
  several frames rather than teleporting;
* an in-flight update for a live participant is *pre-empted* as soon as its
  capture pose is superseded, and the bytes already spent are counted as
  wasted, exactly as a late frame would be on a real link;
* policies that know what is urgent may let a live update *interrupt* a
  scenery download, which is parked and later resumed from where it stopped --
  the multiplexing any real streaming stack does.  Without it, prefetching one
  large model on a slow link holds every participant back behind it.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from . import policies, quality
from .camera import Camera
from .lesson import occluders
from .link import Link



@dataclass
class DynHeld:
    """What the client currently displays for a live participant."""
    level: int
    pose: int
    t_recv: float


@dataclass
class StreamState:
    """The view of the world handed to a policy."""
    static_level: dict[int, int] = field(default_factory=dict)
    dyn: dict[int, DynHeld] = field(default_factory=dict)
    t: float = 0.0
    pose: int = 0
    bytes_per_second: float = 3.125e6
    volumetric_hz: float = 15.0
    quantise: bool = True
    deflate: bool = True
    #: what the lesson plan says is happening now (None outside a lesson)
    context: object | None = None
    #: every static variant the client holds: (oid, state) -> finest level.
    #: `static_level` is the view of this for each object's *current* state.
    variants: dict = field(default_factory=dict)


@dataclass
class Transfer:
    oid: int
    level: int
    pose: int
    dynamic: bool
    header: dict
    body: bytes
    delivered: int = 0
    state: str = "default"

    @property
    def total(self) -> int:
        return len(self.body)

    @property
    def done(self) -> bool:
        return self.delivered >= self.total


@dataclass
class Chunk:
    """A fully delivered object level, ready to hand to the renderer."""
    oid: int
    level: int
    pose: int
    header: dict
    body: bytes


@dataclass
class TickResult:
    chunks: list[Chunk] = field(default_factory=list)
    #: static objects that changed state this tick: oid -> {"state", "t0"}
    state_changes: dict = field(default_factory=dict)
    bytes_sent: int = 0
    metrics: dict = field(default_factory=dict)


class StreamSession:
    #: how much unused budget may be carried forward, in seconds of link time
    BURST_SECONDS = 0.25

    def __init__(self, scene, policy="adaptive", bandwidth_mbps=25.0,
                 quantise=True, deflate=True, link: Link | None = None,
                 lesson=None):
        self.scene = scene
        self.lesson = lesson
        scene.reset_dynamics()
        if lesson is not None and lesson.scene is not scene:
            lesson.bind(scene)
        self.policy = policies.get(policy) if isinstance(policy, str) else policy
        self.link = link if link is not None else Link(mbps=bandwidth_mbps)
        self.state = StreamState(quantise=quantise, deflate=deflate,
                                 volumetric_hz=scene.volumetric_hz,
                                 bytes_per_second=self.bytes_per_second)
        self.inflight: Transfer | None = None
        self.parked: dict[tuple[int, int], Transfer] = {}
        self.credit = 0.0
        self.total_bytes = 0
        self.wasted_bytes = 0
        self.lost_bytes = 0
        self.deliveries: dict[int, int] = {}
        self.visible_time: dict[int, float] = {}
        self.history: list[dict] = []
        self.step_ready: dict[int, float] = {}      # step index -> seconds to detail
        self.state_since: dict[int, float] = {}     # oid -> when its state changed
        self.state_lags: list[float] = []           # seconds each change took to show

    # -- configuration ----------------------------------------------------
    @property
    def bandwidth_mbps(self) -> float:
        """Nominal link capacity; the instantaneous value may differ."""
        return self.link.mbps

    @property
    def bytes_per_second(self) -> float:
        return self.link.bytes_per_second(self.state.t if hasattr(self, "state") else 0.0)

    def reset(self):
        st = self.state
        self.state = StreamState(quantise=st.quantise, deflate=st.deflate,
                                 volumetric_hz=self.scene.volumetric_hz,
                                 bytes_per_second=self.bytes_per_second)
        self.inflight = None
        self.parked.clear()
        self.credit = 0.0
        self.total_bytes = 0
        self.wasted_bytes = 0
        self.lost_bytes = 0
        self.deliveries.clear()
        self.visible_time.clear()
        self.history.clear()
        self.step_ready.clear()
        self.state_since.clear()
        self.state_lags.clear()
        self.scene.reset_dynamics()

    def configure(self, policy=None, bandwidth_mbps=None, quantise=None,
                  deflate=None, reset=True):
        if policy is not None:
            self.policy = policies.get(policy) if isinstance(policy, str) else policy
        if bandwidth_mbps is not None:
            self.link = Link(mbps=float(bandwidth_mbps), profile=self.link.profile,
                             loss=self.link.loss, period=self.link.period,
                             depth=self.link.depth)
        if quantise is not None:
            self.state.quantise = bool(quantise)
        if deflate is not None:
            self.state.deflate = bool(deflate)
        self.state.bytes_per_second = self.bytes_per_second
        if reset:
            self.reset()

    # -- main loop --------------------------------------------------------
    def tick(self, cam, dt: float) -> TickResult:
        st = self.state
        dt = max(0.0, min(0.5, float(dt)))
        st.t += dt
        st.pose = int(st.t * st.volumetric_hz)
        st.bytes_per_second = self.bytes_per_second
        st.context = self.lesson.context(st.t) if self.lesson is not None else None

        budget = self.credit + self.bytes_per_second * dt
        res = TickResult()
        if self.lesson is not None:
            res.state_changes = self._apply_lesson(st)
        else:
            self.scene.animate(st.t)          # people keep moving without a plan

        # drop an update that the capture rig has already superseded
        if self.inflight is not None and self.inflight.dynamic \
                and self.inflight.pose != st.pose:
            self.wasted_bytes += self.inflight.delivered
            self.inflight = None

        queue = list(self.policy.rank(self.scene, cam, st))

        # live data first: park a scenery download if a participant is waiting
        if (self.inflight is not None and not self.inflight.dynamic
                and self.policy.preempt_static_for_live
                and any(self.scene.by_id(r[0]).dynamic
                        and self.policy.needs(self.scene.by_id(r[0]), st, r[1], r[2])
                        for r in queue)):
            tr = self.inflight
            self.parked[(tr.oid, tr.level, tr.state)] = tr
            self.inflight = None

        while budget > 0:
            if self.inflight is None:
                nxt = self._next_transfer(queue)
                if nxt is None:
                    break
                self.inflight = nxt
            tr = self.inflight
            step = min(budget, tr.total - tr.delivered)
            tr.delivered += step
            budget -= step
            res.bytes_sent += step
            if tr.done:
                if self.link.lost():
                    self.lost_bytes += tr.total       # dropped: it must be re-sent
                else:
                    self._commit(tr, st)
                    res.chunks.append(
                        Chunk(tr.oid, tr.level, tr.pose, tr.header, tr.body))
                self.inflight = None
            else:
                break                                   # link is saturated

        self.credit = min(budget, self.bytes_per_second * self.BURST_SECONDS)
        self.total_bytes += res.bytes_sent
        res.metrics = self._measure(cam, res.bytes_sent, len(queue), dt,
                                    {c.oid for c in res.chunks})
        if self.lesson is not None:
            res.metrics.update(self._measure_lesson(cam))
        self.history.append(res.metrics)
        return res

    def _commit(self, tr: Transfer, st: StreamState):
        if tr.dynamic:
            st.dyn[tr.oid] = DynHeld(tr.level, tr.pose, st.t)
            return
        key = (tr.oid, tr.state)
        st.variants[key] = min(tr.level, st.variants.get(key, tr.level))
        obj = self.scene.by_id(tr.oid)
        if tr.state == obj.state:
            st.static_level[tr.oid] = st.variants[key]
            self._state_shown(tr.oid, st.t)

    def _apply_lesson(self, st: StreamState) -> dict:
        """Move people, set gestures and object states from the plan.

        When an object changes state, what the client holds for it is only
        useful if it already has the new variant -- otherwise it is showing
        the past, and the object counts as missing until the new one arrives.
        """
        changes = self.lesson.apply(st.t)
        for oid, state in changes.items():
            held = st.variants.get((oid, state))
            if held is None:
                st.static_level.pop(oid, None)
                self.state_since[oid] = st.t
            else:
                st.static_level[oid] = held          # prefetched: shown instantly
                self.state_lags.append(0.0)
        return {oid: {"state": state, "t0": round(self.lesson.state_since(oid, st.t), 3)}
                for oid, state in changes.items()}

    def state_of(self, obj) -> dict:
        """An object's current state and when it began (for the client's clock)."""
        t0 = self.lesson.state_since(obj.oid, self.state.t) if self.lesson else 0.0
        return {"state": obj.state, "t0": round(t0, 3)}

    def _state_shown(self, oid: int, t: float) -> None:
        if oid in self.state_since:
            self.state_lags.append(t - self.state_since.pop(oid))

    def _next_transfer(self, queue: list) -> Transfer | None:
        st = self.state
        while queue:
            req = queue.pop(0)
            oid, level, pose = req[0], req[1], req[2]
            obj = self.scene.by_id(oid)
            # a request may name a *future* state of a static object: prefetch
            state = req[4] if len(req) > 4 and not obj.dynamic else obj.state
            if obj.dynamic or state == obj.state:
                if not self.policy.needs(obj, st, level, pose):
                    continue
            elif st.variants.get((oid, state), 99) <= level:
                continue
            if not obj.dynamic and (oid, level, state) in self.parked:
                return self.parked.pop((oid, level, state))   # resume where it stopped
            body, header = obj.level(level, pose, state).encoded(st.quantise, st.deflate)
            header = dict(header, oid=oid, level=level, pose=pose, dynamic=obj.dynamic)
            if obj.dynamic:
                # the capture is in the body's own frame; where it stands and
                # which way it faces travel as four numbers
                header.update(place=obj.place, gesture=obj.gesture)
            else:
                # motion parameters travel with the geometry: the client plays
                # them locally instead of receiving a new mesh every frame
                header.update(state=state, colour=list(obj.colour_for(state)),
                              anim=obj.anims.get(state, []))
            return Transfer(oid, level, pose, obj.dynamic, header, body,
                            state=state if not obj.dynamic else "default")
        return None

    # -- quality of experience -------------------------------------------
    def _penalty(self, o, cam) -> float:
        """0 = the viewer sees what they should, 1 = nothing usable."""
        st = self.state
        req = o.required_level(cam)
        if o.dynamic:
            held = st.dyn.get(o.oid)
            if held is None:
                return quality.MISSING_PENALTY
            return max(quality.lod_penalty(held.level, req, o.coarsest),
                       quality.staleness_penalty(st.t - held.t_recv, st.volumetric_hz))
        held = st.static_level.get(o.oid)
        if held is None:
            return quality.MISSING_PENALTY
        return quality.lod_penalty(held, req, o.coarsest)

    def _measure(self, cam, bytes_sent: int, queue_len: int, dt: float,
                 delivered: set[int] = frozenset()) -> dict:
        st = self.state
        planes = cam.planes()
        vis = missing = 0
        pen_sum = w_pen = w_sum = 0.0
        stale_sum = stale_n = 0.0

        for o in self.scene:
            if not cam.sees_sphere(o.centre, o.radius, planes):
                continue
            vis += 1
            self.visible_time[o.oid] = self.visible_time.get(o.oid, 0.0) + dt
            if o.oid in delivered:
                self.deliveries[o.oid] = self.deliveries.get(o.oid, 0) + 1
            p = self._penalty(o, cam)
            if p >= 1.0 and (o.oid not in st.static_level and o.oid not in st.dyn):
                missing += 1
            # weight by how much of the viewport the object actually occupies,
            # so a missing instructor in front of you outweighs a distant chair
            w = o.weight * cam.attention(o.centre, o.radius)
            pen_sum += p
            w_pen += p * w
            w_sum += w
            if o.dynamic:
                held = st.dyn.get(o.oid)
                stale_sum += 2.0 if held is None else (st.t - held.t_recv)
                stale_n += 1

        return {
            "t": round(st.t, 4),
            "pose": st.pose,
            "bytes": int(bytes_sent),
            "cum_bytes": int(self.total_bytes),
            "mbps": round(bytes_sent * 8 / 1e6 / max(1e-6, dt), 3),
            "capacity_mbps": round(self.link.capacity_mbps(st.t), 2),
            "visible": vis,
            "missing": missing,
            "coverage": round(1.0 - missing / vis, 4) if vis else 1.0,
            "penalty": round(pen_sum / vis, 4) if vis else 0.0,
            "qoe": round(1.0 - w_pen / w_sum, 4) if w_sum else 1.0,
            "staleness_ms": round(1000 * stale_sum / stale_n, 1) if stale_n else 0.0,
            "static_resident": len(st.static_level),
            "queue": queue_len,
        }

    # -- lesson metrics ---------------------------------------------------
    def _ready(self, o, cam) -> bool:
        """Is `o` on the client at the detail (and freshness) the viewer needs?"""
        st = self.state
        req = o.required_level(cam)
        if o.dynamic:
            held = st.dyn.get(o.oid)
            return (held is not None and held.level <= req
                    and st.t - held.t_recv <= 2.0 / st.volumetric_hz)
        held = st.static_level.get(o.oid)
        return held is not None and held <= req

    def _measure_lesson(self, cam) -> dict:
        """What a teacher would ask: can the students see what is being taught?

        focus_q        quality of the objects the lesson is about, while visible
        clear_view     the same, but no better than the worst live participant
                       standing in the line of sight -- a frozen or blocky
                       classmate in front of the flask spoils the view of the
                       flask.  NB: occluder protection targets exactly this, so
                       read it alongside the neutral metrics, never instead.
        time-to-detail per step, how long until those objects are ready --
                       judged from where the plan puts the viewer, so every
                       policy is measured against the same target
        """
        st = self.state
        ctx = st.context
        step = ctx.step
        planes = cam.planes()
        seen = [oid for oid in step.focus_ids
                if cam.sees_sphere(self.scene.by_id(oid).centre,
                                   self.scene.by_id(oid).radius, planes)]
        vals = [1.0 - self._penalty(self.scene.by_id(oid), cam) for oid in seen]
        clear = None
        if vals:
            blockers = occluders(self.scene, cam.pos, frozenset(seen))
            clear = min([float(np.mean(vals))]
                        + [1.0 - self._penalty(self.scene.by_id(b), cam)
                           for b in blockers])

        if step.focus_ids and step.index not in self.step_ready:
            planned = Camera(pos=step.eye, forward=step.look - step.eye,
                             fov_y_deg=cam.fov_y_deg, aspect=cam.aspect,
                             screen_h=cam.screen_h)
            if all(self._ready(self.scene.by_id(oid), planned)
                   for oid in step.focus_ids):
                self.step_ready[step.index] = st.t - step.t

        return {"step": step.index,
                "focus_q": round(float(np.mean(vals)), 4) if vals else None,
                "clear_view": round(clear, 4) if clear is not None else None}

    def lesson_summary(self) -> dict:
        if self.lesson is None or not self.history:
            return {}
        # changes still not on screen when the session ended count in full
        lags = self.state_lags + [self.state.t - t0 for t0 in self.state_since.values()]
        fq = [r["focus_q"] for r in self.history if r.get("focus_q") is not None]
        cv = [r["clear_view"] for r in self.history if r.get("clear_view") is not None]
        waits, on_time, never = [], 0, 0
        taught = [s for s in self.lesson.steps
                  if s.focus_ids and s.t < self.state.t]
        for s in taught:
            if s.index in self.step_ready:
                w = self.step_ready[s.index]
                on_time += (s.t + w) <= s.arrival
            else:
                w = s.end - s.t                     # never got there: charge the step
                never += 1
            waits.append(w)
        return {
            "lesson": self.lesson.name,
            "focus_quality": round(float(np.mean(fq)), 4) if fq else float("nan"),
            "focus_quality_p05": (round(float(np.percentile(fq, 5)), 4)
                                  if fq else float("nan")),
            "clear_view": round(float(np.mean(cv)), 4) if cv else float("nan"),
            "clear_view_p05": (round(float(np.percentile(cv, 5)), 4)
                               if cv else float("nan")),
            "time_to_detail_s": round(float(np.mean(waits)), 3) if waits else 0.0,
            "ready_on_arrival": round(on_time / len(taught), 3) if taught else 1.0,
            "steps_never_ready": never,
            "state_changes": len(lags),
            "state_lag_s": round(float(np.mean(lags)), 3) if lags else 0.0,
            "state_lag_max_s": round(float(np.max(lags)), 3) if lags else 0.0,
        }

    # -- reporting --------------------------------------------------------
    def participant_fps(self) -> dict[str, float]:
        out = {}
        for o in self.scene.dynamic:
            vt = self.visible_time.get(o.oid, 0.0)
            out[o.name] = round(self.deliveries.get(o.oid, 0) / vt, 2) if vt > 0.2 else 0.0
        return out

    def summary(self) -> dict:
        if not self.history:
            return {}
        h = self.history
        ts = np.array([r["t"] for r in h])
        qoe = np.array([r["qoe"] for r in h])
        cov = np.array([r["coverage"] for r in h])
        stale = np.array([r["staleness_ms"] for r in h])

        def first_time(mask):
            idx = np.flatnonzero(mask)
            return round(float(ts[idx[0]]), 3) if len(idx) else float("nan")

        fps = self.participant_fps()
        inst = fps.get("instructor", 0.0)
        students = [v for k, v in fps.items() if k != "instructor"]

        return {
            "policy": self.policy.name,
            "bandwidth_mbps": self.bandwidth_mbps,
            "quantise": self.state.quantise,
            "deflate": self.state.deflate,
            "link": self.link.describe(),
            "total_mb": round(self.total_bytes / 1e6, 3),
            # parked downloads never resumed are bytes spent for nothing too
            "wasted_mb": round((self.wasted_bytes + sum(
                t.delivered for t in self.parked.values())) / 1e6, 3),
            "lost_mb": round(self.lost_bytes / 1e6, 3),
            "mean_qoe": round(float(qoe.mean()), 4),
            "p05_qoe": round(float(np.percentile(qoe, 5)), 4),
            "mean_coverage": round(float(cov.mean()), 4),
            "mean_staleness_ms": round(float(stale.mean()), 1),
            "instructor_fps": inst,
            "student_fps_mean": round(float(np.mean(students)), 2) if students else 0.0,
            "t_qoe_90": first_time(qoe >= 0.90),
            "ticks": len(h),
            "duration_s": round(float(ts[-1]), 3),
            **self.lesson_summary(),
        }

    def manifest(self) -> dict:
        return {
            "type": "manifest",
            "objects": [o.meta() for o in self.scene],
            "policies": [{"name": p.name, "description": p.description}
                         for p in policies.REGISTRY.values() if not p.ablation],
            "stats": self.scene.stats(self.state.quantise, self.state.deflate),
        }
