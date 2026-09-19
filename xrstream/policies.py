"""Streaming policies.

Each policy answers one question per tick: *given the current viewpoint and
what the client already holds, in what order -- and at what level of detail --
should geometry be sent?*  The session layer then pours the ranked requests
into the available bandwidth budget.

The policies map onto the strategies surveyed in SOTA 1.4.3 and 1.5.5:

  full           static asset streaming -- everything at full quality, no
                 viewpoint awareness and no rate control
  lod            level-of-detail streaming, no culling
  roi            region-of-interest / viewport-dependent streaming at full LOD
  adaptive_geom  ROI + LOD + explicit rate allocation across participants
  adaptive       the same, plus semantic prioritisation
  lesson_aware   the same, but priorities come from the lesson plan rather
                 than a fixed class table, and the plan is read ahead

`adaptive` minus `adaptive_geom` isolates what semantics alone contributes --
and that allocation step is exactly what a learned orchestrator replaces in
the next iteration.
"""
from __future__ import annotations

from . import quality
from .camera import Camera
from .lesson import occluders

#: (object id, level, capture pose, priority[, state]) -- the optional fifth
#: element asks for a variant other than the object's current state
Request = tuple


class Policy:
    name = "policy"
    description = ""
    #: ablation variants are benchmarked but kept out of the viewer's menu
    ablation = False
    #: may a live update interrupt a scenery download (which then resumes)?
    #: Off for the naive strategies, which have no notion of what is urgent.
    preempt_static_for_live = False

    def rank(self, scene, cam, st) -> list[Request]:
        raise NotImplementedError

    # -- shared helpers ---------------------------------------------------
    @staticmethod
    def needs(obj, st, level: int, pose: int) -> bool:
        """Does the client still need this object at `level` for `pose`?"""
        if obj.dynamic:
            held = st.dyn.get(obj.oid)
            return held is None or held.pose != pose or held.level > level
        held = st.static_level.get(obj.oid)
        return held is None or held > level


class FullQualityPolicy(Policy):
    name = "full"
    description = "full quality, viewpoint-blind, no rate control (the strawman)"

    def rank(self, scene, cam, st):
        out = []
        n = len(scene)
        for o in scene:
            pose = st.pose if o.dynamic else 0
            if self.needs(o, st, 0, pose):
                out.append((o.oid, 0, pose, float(n - o.oid)))
        return out


class LODPolicy(Policy):
    name = "lod"
    description = "distance-based level of detail, no culling, no rate control"

    def rank(self, scene, cam, st):
        out = []
        for o in scene:
            lvl = o.required_level(cam)
            pose = st.pose if o.dynamic else 0
            if self.needs(o, st, lvl, pose):
                out.append((o.oid, lvl, pose, 1.0 / cam.distance_to(o.centre, o.radius)))
        out.sort(key=lambda r: -r[3])
        return out


class ROIPolicy(Policy):
    name = "roi"
    description = "viewport culling only: visible objects at finest LOD"

    def rank(self, scene, cam, st):
        planes = cam.planes()
        out = []
        for o in scene:
            if not cam.sees_sphere(o.centre, o.radius, planes):
                continue
            pose = st.pose if o.dynamic else 0
            if self.needs(o, st, 0, pose):
                out.append((o.oid, 0, pose, 1.0 / cam.distance_to(o.centre, o.radius)))
        out.sort(key=lambda r: -r[3])
        return out


class AdaptivePolicy(Policy):
    name = "adaptive"
    description = ("ROI + LOD + per-pose rate allocation across participants "
                   "+ semantic prioritisation + static prefetch")

    #: fraction of the link reserved for live participants while static
    #: geometry is still being fetched
    DYN_SHARE_WARMUP = 0.75
    #: priority multiplier for static objects just outside the frustum
    PREFETCH_GAIN = 0.12
    #: how far outside the frustum we speculatively prefetch (fov multiplier)
    PREFETCH_FOV = 1.8
    #: an object with nothing resident leaves a visible hole -- weight it up
    HOLE_GAIN = 2.5

    use_semantics = True
    preempt_static_for_live = True
    #: give every visible participant its coarsest frame before any upgrade
    LIVENESS_FLOOR = True

    # -- priority ---------------------------------------------------------
    def _importance(self, o, cam) -> float:
        """How much of the viewer's attention this object is worth.

        Screen coverage times semantic class weight -- the same quantity the
        QoE metric is built from, so the policy is optimising what is measured.
        """
        return cam.attention(o.centre, o.radius) * (
            o.weight if self.use_semantics else 1.0)

    def _priority(self, o, cam, st, visible: bool) -> float:
        p = self._importance(o, cam)
        if o.dynamic:
            held = st.dyn.get(o.oid)
            stale = st.t if held is None else (st.t - held.t_recv)
            p *= 1.0 + stale * st.volumetric_hz          # the later, the louder
        elif o.oid not in st.static_level:
            p *= self.HOLE_GAIN
        if not visible:
            p *= self.PREFETCH_GAIN
        return p

    # -- rate allocation --------------------------------------------------
    def _allocate(self, cands, st, budget: float):
        """Fit one capture frame into the link, maximising delivered quality.

        A greedy knapsack over quality-per-byte: every participant starts
        dropped, and we repeatedly buy whichever single-level upgrade adds the
        most QoE per byte, until the budget runs out.  Coarse copies of
        important participants therefore get bought before fine copies of
        unimportant ones -- which is where the semantic weights bite.
        """
        options = {}
        for o, req, imp in cands:
            opts = []
            for lvl in range(o.coarsest, req - 1, -1):
                opts.append((lvl,
                             float(o.nbytes(lvl, st.quantise, st.deflate, st.pose)),
                             quality.value(imp, lvl, req, o.coarsest)))
            options[o.oid] = opts                        # cost and value ascend

        picked: dict[int, int] = {}                      # oid -> index in opts
        spent = 0.0

        # Liveness floor: everyone on screen gets a rough, current frame before
        # anyone gets extra detail.  Pure quality-per-byte greed will happily
        # freeze three bystanders to sharpen one speaker; a classroom full of
        # frozen people is worse than a slightly blurrier teacher.  If even the
        # floor does not fit, the most important participants get it first.
        if self.LIVENESS_FLOOR:
            for o, _, _ in sorted(cands, key=lambda c: -c[2]):
                cost = options[o.oid][0][1]
                if spent + cost <= budget:
                    picked[o.oid] = 0
                    spent += cost

        while True:
            best, best_gain = None, 0.0
            for oid, opts in options.items():
                nxt = picked.get(oid, -1) + 1
                if nxt >= len(opts):
                    continue
                cur_cost, cur_val = (0.0, 0.0) if oid not in picked else opts[picked[oid]][1:]
                d_cost = opts[nxt][1] - cur_cost
                d_val = opts[nxt][2] - cur_val
                if d_cost <= 0 or spent + d_cost > budget:
                    continue
                gain = d_val / d_cost
                if gain > best_gain:
                    best, best_gain = (oid, nxt, d_cost), gain
            if best is None:
                break
            oid, nxt, d_cost = best
            picked[oid] = nxt
            spent += d_cost

        levels = {oid: options[oid][i][0] for oid, i in picked.items()}
        dropped = {o.oid for o, _, _ in cands if o.oid not in picked}
        return levels, dropped

    # -- hooks for subclasses ---------------------------------------------
    def _begin(self, scene, cam, st):
        """Called once at the start of every rank()."""

    def _extra_static(self, scene, cam, st) -> list[Request]:
        """Additional static requests beyond what the viewpoint implies."""
        return []

    # -- main -------------------------------------------------------------
    def rank(self, scene, cam, st):
        self._begin(scene, cam, st)
        planes = cam.planes()
        wide = _widen(cam, self.PREFETCH_FOV)

        dyn_cands, static_reqs = [], []

        for o in scene:
            visible = cam.sees_sphere(o.centre, o.radius, planes)

            if o.dynamic:
                # a live stream that nobody is looking at is not worth a byte
                if not visible:
                    continue
                dyn_cands.append((o, o.required_level(cam),
                                  self._importance(o, cam)))
                continue

            if not (visible or wide.sees_sphere(o.centre, o.radius)):
                continue
            lvl = o.required_level(cam)
            if not visible:
                lvl = min(o.coarsest, lvl + 1)           # cheap speculative copy
            if self.needs(o, st, lvl, 0):
                static_reqs.append((o.oid, lvl, 0, self._priority(o, cam, st, visible)))

        static_reqs += self._extra_static(scene, cam, st)

        # Reserve part of the link for scenery only while scenery is actually
        # wanted.  (Previously "any static object not at its finest level"
        # counted as pending -- true almost forever, since distant objects never
        # need their finest level -- so a quarter of the link sat reserved for
        # nothing.)
        share = self.DYN_SHARE_WARMUP if static_reqs else 1.0
        budget = st.bytes_per_second / max(1e-6, st.volumetric_hz) * share
        levels, dropped = self._allocate(dyn_cands, st, budget)

        dyn_reqs = [(o.oid, levels[o.oid], st.pose,
                     self._priority(o, cam, st, True))
                    for o, _, _ in dyn_cands
                    if o.oid not in dropped
                    and self.needs(o, st, levels[o.oid], st.pose)]

        out = dyn_reqs + static_reqs
        out.sort(key=lambda r: -r[3])
        return out


class AdaptiveGeometricPolicy(AdaptivePolicy):
    name = "adaptive_geom"
    description = "ablation of `adaptive` with semantic weights removed"
    use_semantics = False


class LessonAwarePolicy(AdaptivePolicy):
    """Priorities from the lesson plan instead of a fixed class table.

    Three things change relative to `adaptive`, all driven by the lesson's
    context at the current moment (see `lesson.py`):

    1. **Context weights** -- whoever is speaking and whatever is being shown
       are boosted, and the lesson phase can re-weight whole classes (during
       group work the students matter more than the tutor).
    2. **Occluder protection** -- live participants standing between the
       viewer and the thing being shown are boosted too.  A fixed class table
       starves exactly these people, which is why `adaptive` loses to
       `adaptive_geom` in the worst case of the chemistry lab.
    3. **Reading ahead** -- static objects the plan will show in the next
       `horizon_s` seconds are fetched at the detail they will need *before*
       the viewer turns to them.  Head-motion predictors see a few hundred
       milliseconds ahead; the plan sees seconds.

    Without a lesson attached it behaves exactly like `adaptive`.
    """
    name = "lesson_aware"
    description = ("adaptive, with priorities read from the lesson plan: who is "
                   "speaking, what is being shown, who is in the way, what is next")

    #: priority of plan-driven prefetch relative to on-screen requests
    ANTICIPATION_GAIN = 0.6
    #: the three ideas can be switched off one at a time for ablation
    use_occluders = True
    use_anticipation = True

    def _begin(self, scene, cam, st):
        self._ctx = getattr(st, "context", None)
        self._occ = (occluders(scene, cam.pos, self._ctx.focus)
                     if self._ctx is not None and self.use_occluders else set())

    def _context_gain(self, o) -> float:
        ctx = self._ctx
        if ctx is None:
            return 1.0
        g = ctx.phase_weights.get(o.semantic, 1.0)
        if o.oid in ctx.focus:
            g *= ctx.boosts["focus"]
        if o.oid in ctx.speakers:
            g *= ctx.boosts["speaker"]
        if o.oid in self._occ:
            g *= ctx.boosts["occluder"]
        return g

    def _importance(self, o, cam) -> float:
        return super()._importance(o, cam) * self._context_gain(o)

    def _extra_static(self, scene, cam, st):
        ctx = self._ctx
        if ctx is None or not self.use_anticipation:
            return []
        out, seen = [], set()
        for dt, step in ctx.upcoming:
            # judge the detail needed from where the plan says the viewer will be
            future = Camera(pos=step.eye, forward=step.look - step.eye,
                            fov_y_deg=cam.fov_y_deg, aspect=cam.aspect,
                            screen_h=cam.screen_h)
            urgency = 1.0 - dt / max(1e-6, ctx.horizon)
            for oid in step.focus_ids:
                o = scene.by_id(oid)
                if o.dynamic or oid in seen:
                    continue                 # a live stream cannot be fetched early
                seen.add(oid)
                lvl = o.required_level(future)
                if self.needs(o, st, lvl, 0):
                    prio = (future.attention(o.centre, o.radius) * o.weight
                            * ctx.boosts["focus"] * self.ANTICIPATION_GAIN
                            * max(0.2, urgency))
                    out.append((oid, lvl, 0, prio))
            # the plan also says when things will *change*: the flask turns
            # pink at the end point, writing appears on the board.  Fetch the
            # new look before it is needed, so it can be shown the instant it
            # happens instead of after a round of downloading.
            for oid, state in step.state_ids.items():
                o = scene.by_id(oid)
                if o.dynamic or (oid, state) in seen:
                    continue
                seen.add((oid, state))
                lvl = o.required_level(future)
                if st.variants.get((oid, state), 99) > lvl:
                    gain = ctx.boosts["focus"] if oid in step.focus_ids else 1.0
                    prio = (future.attention(o.centre, o.radius) * o.weight * gain
                            * self.ANTICIPATION_GAIN * max(0.2, urgency))
                    out.append((oid, lvl, 0, prio, state))
        return out


class LessonContextOnly(LessonAwarePolicy):
    name = "lesson_ctx"
    description = "ablation: lesson context weights only (no occluders, no read-ahead)"
    ablation = True
    use_occluders = False
    use_anticipation = False


class LessonContextOccluders(LessonAwarePolicy):
    name = "lesson_ctx_occ"
    description = "ablation: lesson context weights + occluder protection (no read-ahead)"
    ablation = True
    use_anticipation = False


def _widen(cam, factor: float) -> Camera:
    """A copy of the camera with a wider field of view, used for prefetch."""
    return Camera(pos=cam.pos, forward=cam.forward, up=cam.up,
                  fov_y_deg=min(170.0, cam.fov_y_deg * factor),
                  aspect=cam.aspect, near=cam.near, far=cam.far,
                  screen_h=cam.screen_h)


REGISTRY = {p.name: p for p in (
    FullQualityPolicy, LODPolicy, ROIPolicy, AdaptiveGeometricPolicy, AdaptivePolicy,
    LessonContextOnly, LessonContextOccluders, LessonAwarePolicy)}


def get(name: str) -> Policy:
    if name not in REGISTRY:
        raise KeyError(f"unknown policy {name!r}; have {sorted(REGISTRY)}")
    return REGISTRY[name]()
