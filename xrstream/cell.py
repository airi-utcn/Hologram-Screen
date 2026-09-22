"""A whole class on one cell.

Everything else here measures one person watching a lesson. A teaching hub does
not work that way: a class sits in one room, on one 5G cell, and shares
whatever that cell can carry. That turns the question from *what do I send you*
into *how do I divide one link between twenty people who are, mostly, looking
at the same thing*.

Mostly is what the lesson plan knows. When the plan says the class is watching
the titration, that demonstration is one piece of geometry twenty people need
at the same moment: it can be sent once for all of them -- the way 5G broadcast
carries one stream to a cell -- instead of twenty times. Whatever is left pays
for what actually differs between students: where they sit, and who is in the
way.

Four schedulers, deliberately comparable:

  unicast     the obvious thing: split the cell evenly, stream to each student
              on their own, pay for every copy
  fair        the same, except bandwidth nobody used goes to the worst-served
              student instead of being wasted
  shared      send each piece once for everyone waiting on it, in the order the
              lesson plan ranks them
  hub         the same sharing, but each candidate priced by what it is worth
              to the room over what it costs to put on the air

The gap between those last two is worth stating plainly, because the numbers
do: most of what there is to win comes from sharing at all, and pricing adds a
smaller margin on top -- one that grows as the class stops moving together.

The number that matters is not the average student. It is the worst one: a hub
where two children at the back cannot read the board has failed, however good
the mean looks.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

from . import lesson as lessons, scenarios
from .camera import Camera
from .link import Link
from .session import StreamSession, Transfer

#: schedulers this module can run, in the order they are worth reading
MODES = ("unicast", "fair", "shared", "hub")

#: how many students must want the same geometry before it is worth sending
#: once for all of them rather than to each in turn
SHARE_MIN = 2


#: How a student spends their attention. A class does not look where a plan
#: says in unison, and a scheduler tested against one that does is measuring
#: itself.
KINDS = ("attentive",    # watches what the lesson is about
         "late",         # gets there a second and a half after everyone else
         "neighbour",    # watches another student instead
         "drift")        # eyes wandering around the room


@dataclass
class Seat:
    """Where a student sits, what they are there to watch, and whether they do."""
    name: str
    eye: np.ndarray
    look: np.ndarray
    kind: str = "attentive"
    phase: float = 0.0          # so the wandering ones do not wander in step
    neighbour: np.ndarray | None = None


def seats_for(scene, n: int, attention: float = 1.0, seed: int = 7) -> list[Seat]:
    """`n` places in this room, starting from where its own people stand.

    The students in the scene are the front rows; a bigger class is seated in
    rows behind them, which is also where a real hub would put the overflow.

    `attention` is the share of the class actually following the lesson. The
    rest arrive late, watch a neighbour, or let their eyes wander, which is the
    honest test of a scheduler that reads the plan: what a plan predicts and
    what a room of teenagers does are different things.
    """
    people = [o for o in scene if o.dynamic and o.semantic in ("student", "athlete")]
    people.sort(key=lambda o: (float(o.base_centre[2]), float(o.base_centre[0])))
    front = [np.asarray(o.base_centre, dtype=np.float64) for o in people]

    board = next((o for o in scene if "board" in o.name), None)
    target = (np.asarray(board.base_centre, dtype=np.float64) if board is not None
              else np.zeros(3))

    seats: list[Seat] = []
    for i in range(n):
        if i < len(front):
            eye = front[i].copy()
        else:
            # rows behind the class, two metres apart, filling outwards
            k = i - len(front)
            row, col = divmod(k, 5)
            back = max((p[2] for p in front), default=0.0)
            eye = np.array([-4.0 + 2.0 * col, 0.0, back + 1.8 + 1.8 * row])
        eye = np.array([eye[0], 1.55, eye[2]])
        seats.append(Seat(f"student_{i:02d}", eye, target.copy()))

    rng = np.random.default_rng(seed)
    following = int(round(max(0.0, min(1.0, attention)) * n))
    for i, seat in enumerate(seats):
        seat.phase = float(rng.uniform(0, 2 * np.pi))
        if i >= following:
            seat.kind = KINDS[1 + (i - following) % 3]
        if seat.kind == "neighbour" and len(seats) > 1:
            seat.neighbour = seats[(i + 1 + int(rng.integers(0, 3))) % len(seats)].eye
    return seats


class Watcher:
    """One student: what they can see, and what has reached them so far.

    The session is used for its bookkeeping -- what this student holds, and the
    quality that follows from it -- but not for its rate control. On a shared
    cell, deciding who gets bytes is the cell's job, not each student's.
    """

    def __init__(self, scene, seat: Seat, policy: str, quantise=True, deflate=True):
        self.seat = seat
        self.session = StreamSession(scene, policy=policy, quantise=quantise,
                                     deflate=deflate, link=Link(mbps=0.0))
        self.inflight: Transfer | None = None
        self.queue: list = []
        self.cam = self._camera(None)
        self.qoe = 1.0
        self.bytes_received = 0

    # -- what this student is looking at ---------------------------------
    def _focus(self, ctx):
        """Where the lesson says to look, as a point in the room."""
        if ctx is None or not ctx.focus:
            return None
        scene = self.session.scene
        at = [scene.by_id(o).centre for o in ctx.focus]
        return np.mean(at, axis=0) if at else None

    def _camera(self, ctx, late=None, t: float = 0.0) -> Camera:
        """Where this student is actually looking, which is not always the lesson."""
        seat = self.seat
        look = np.asarray(seat.look, dtype=np.float64)
        at = self._focus(ctx)
        if seat.kind == "attentive":
            look = at if at is not None else look
        elif seat.kind == "late":
            behind = self._focus(late)          # still on what it was about
            look = behind if behind is not None else look
        elif seat.kind == "neighbour" and seat.neighbour is not None:
            look = np.asarray(seat.neighbour, dtype=np.float64)
        elif seat.kind == "drift":
            base = at if at is not None else look
            a = 0.9 * np.sin(0.35 * t + seat.phase)     # a slow sweep either side
            d = base - seat.eye
            ca, sa = np.cos(a), np.sin(a)
            look = seat.eye + np.array([ca * d[0] + sa * d[2], d[1],
                                        -sa * d[0] + ca * d[2]])
        return Camera(pos=seat.eye, forward=look - seat.eye, screen_h=1080)

    def begin(self, t: float, ctx, changes: dict, rate: float, late=None) -> None:
        st = self.session.state
        st.t = t
        st.pose = int(t * st.volumetric_hz)
        st.context = ctx
        # what this student plans against: their fair share of the cell. A
        # policy that thinks it has no bandwidth asks for nothing at all.
        st.bytes_per_second = rate
        # a state this student has not got yet is a state they cannot show
        for oid, state in changes.items():
            held = st.variants.get((oid, state))
            if held is None:
                st.static_level.pop(oid, None)
                self.session.state_since[oid] = t
            else:
                st.static_level[oid] = held
                self.session.state_lags.append(0.0)
        self.cam = self._camera(ctx, late, t)
        self.queue = list(self.session.policy.rank(self.session.scene, self.cam, st))

    # -- receiving --------------------------------------------------------
    def wants(self, oid: int, level: int, pose: int, state: str) -> bool:
        obj = self.session.scene.by_id(oid)
        if obj.dynamic:
            return self.session.policy.needs(obj, self.session.state, level, pose)
        if state != obj.state:
            return self.session.state.variants.get((oid, state), 99) > level
        return self.session.policy.needs(obj, self.session.state, level, pose)

    def take(self, tr: Transfer) -> None:
        """Hand this student a finished transfer, whoever it was built for."""
        self.session._commit(tr, self.session.state)
        self.session.total_bytes += tr.total
        self.bytes_received += tr.total

    def next_transfer(self) -> Transfer | None:
        return self.session._next_transfer(self.queue)

    def measure(self, dt: float, bytes_sent: int) -> dict:
        m = self.session._measure(self.cam, bytes_sent, len(self.queue), dt)
        self.qoe = m["qoe"]
        return m


@dataclass
class CellResult:
    """What one run of a class on a cell came to."""
    mode: str
    students: int
    mbps: float
    seconds: float
    attention: float
    mean_qoe: float
    worst_qoe: float           # the average, over time, of the worst student
    floor_qoe: float           # the worst any student ever had
    spread: float              # mean - worst: how unequal the room is
    cell_mbytes: float
    delivered_mbytes: float    # what the students received between them
    sharing_gain: float        # delivered / sent: 1.0 means nothing was shared
    ticks: int = 0
    history: list[dict] = field(default_factory=list)

    def row(self) -> dict:
        return {"mode": self.mode, "students": self.students, "mbps": self.mbps,
                "attention": self.attention,
                "mean_qoe": round(self.mean_qoe, 4),
                "worst_qoe": round(self.worst_qoe, 4),
                "floor_qoe": round(self.floor_qoe, 4),
                "spread": round(self.spread, 4),
                "cell_mbytes": round(self.cell_mbytes, 2),
                "sharing_gain": round(self.sharing_gain, 3)}


class Cell:
    """One link, one class, one of the four schedulers."""

    def __init__(self, scenario: str = "classroom", lesson: str | None = None,
                 students: int = 8, mode: str = "hub", mbps: float = 50.0,
                 policy: str = "lesson_aware", tick_hz: float = 20.0,
                 attention: float = 1.0, quantise=True, deflate=True):
        if mode not in MODES:
            raise KeyError(f"unknown cell scheduler {mode!r}; have {list(MODES)}")
        self.mode = mode
        self.mbps = float(mbps)
        self.dt = 1.0 / float(tick_hz)
        self.lesson = lessons.get(lesson) if lesson else None
        name = self.lesson.scenario if self.lesson else scenario
        self.scene = scenarios.build(name)
        if self.lesson is not None:
            self.lesson.bind(self.scene)
        self.scene.reset_dynamics()

        self.attention = float(attention)
        seats = seats_for(self.scene, students, attention)
        self.watchers = [Watcher(self.scene, s, policy, quantise, deflate)
                         for s in seats]
        self.shared: Transfer | None = None
        self.shared_for: list[Watcher] = []
        self.credit = 0.0
        self.sent = 0          # bytes the cell put on the air
        self.delivered = 0     # bytes the students received between them
        self.t = 0.0

    # -- one tick ---------------------------------------------------------
    def tick(self) -> dict:
        dt, scene = self.dt, self.scene
        self.t += dt
        ctx = self.lesson.context(self.t) if self.lesson is not None else None
        # what the lesson was about a moment ago, for whoever is behind it
        late = (self.lesson.context(max(0.0, self.t - 1.5))
                if self.lesson is not None else None)
        if self.lesson is not None:
            changes = self.lesson.apply(self.t)      # once for the room, not once each
        else:
            scene.animate(self.t)
            changes = {}
        rate = self.mbps * 1e6 / 8.0 / max(1, len(self.watchers))
        for w in self.watchers:
            w.begin(self.t, ctx, changes, rate, late)

        budget = self.credit + self.mbps * 1e6 / 8.0 * dt
        spent_before = self.sent

        if self.mode == "hub":
            budget -= self._pour_by_value(budget)
        elif self.mode == "shared":
            budget -= self._pour_common(budget)
        else:
            budget -= self._pour_personal(budget)

        # unspent capacity is worth a fraction of a second, no more: a cell
        # cannot bank the airtime it did not use
        self.credit = min(budget, self.mbps * 1e6 / 8.0 * 0.25)
        sent = self.sent - spent_before

        qoes = [w.measure(dt, 0)["qoe"] for w in self.watchers]
        return {"t": round(self.t, 3), "bytes": sent,
                "mean_qoe": float(np.mean(qoes)), "worst_qoe": float(min(qoes)),
                "qoes": qoes}

    # -- what the class has in common -------------------------------------
    def _common_queue(self) -> list[tuple]:
        """Geometry more than one student is waiting for, most wanted first.

        The level sent is the finest any of them asked for, because a student
        who needed it coarser can use a finer copy, and the cost is paid once.
        """
        wanted: dict[tuple, dict] = {}
        for w in self.watchers:
            for req in w.queue:
                oid, level, pose = req[0], req[1], req[2]
                obj = self.scene.by_id(oid)
                state = req[4] if len(req) > 4 and not obj.dynamic else obj.state
                key = (oid, pose if obj.dynamic else -1, state)
                slot = wanted.setdefault(key, {"level": level, "priority": 0.0,
                                               "who": []})
                slot["level"] = min(slot["level"], level)
                slot["priority"] += float(req[3]) if len(req) > 3 else 1.0
                slot["who"].append(w)
        # Plain sharing carries whatever anyone waits for, even when that is
        # one person; the hub shares only where sharing actually saves.
        floor = 1 if self.mode == "shared" else SHARE_MIN
        out = [(k, v) for k, v in wanted.items() if len(v["who"]) >= floor]
        out.sort(key=lambda kv: -kv[1]["priority"])
        return out

    def _pour_common(self, budget: float) -> float:
        spent = 0.0
        # finish what a previous tick started before opening anything new
        if self.shared is not None:
            step = self._pour(self.shared, budget)
            spent += step
            budget -= step
            if self.shared.done:
                self._finish_shared()
            else:
                return spent

        for key, slot in self._common_queue():
            if budget <= 0:
                break
            oid, pose, state = key
            takers = [w for w in slot["who"]
                      if w.wants(oid, slot["level"], pose if pose >= 0 else 0, state)]
            if len(takers) < (1 if self.mode == "shared" else SHARE_MIN):
                continue
            tr = self._build(takers[0], oid, slot["level"], pose, state)
            if tr is None:
                continue
            self.shared, self.shared_for = tr, takers
            step = self._pour(tr, budget)
            spent += step
            budget -= step
            if tr.done:
                self._finish_shared()
            else:
                break                      # it carries on next tick
        return spent

    def _finish_shared(self) -> None:
        tr = self.shared
        for w in self.shared_for:
            if w.wants(tr.oid, tr.level, tr.pose, tr.state):
                w.take(tr)
                self.delivered += tr.total
        self.shared, self.shared_for = None, []

    # -- the hub: whatever is worth the most per byte ----------------------
    # A fixed share for the broadcast is a guess, and a wrong one whenever the
    # class is more united, or less, than the guess assumed. Every candidate is
    # priced instead: what it is worth to the room, over what it costs to put on
    # the air. Geometry twenty students are waiting for is worth twenty times
    # the same geometry for one, and costs the same to send once -- so a class
    # watching together is served like television, a scattered one like twenty
    # separate streams, and nobody has to choose which.
    def _pour_by_value(self, budget: float) -> float:
        spent = 0.0
        # whatever was half sent when the last tick ran out goes first
        if self.shared is not None:
            step = self._pour(self.shared, budget)
            spent += step
            budget -= step
            if not self.shared.done:
                return spent
            self._finish_shared()
        for w in self.watchers:
            if w.inflight is None or budget <= 0:
                continue
            step = self._pour(w.inflight, budget)
            spent += step
            budget -= step
            if not w.inflight.done:
                return spent
            w.take(w.inflight)
            self.delivered += w.inflight.total
            w.inflight = None

        common = self._common_queue()
        worst = min((w.qoe for w in self.watchers), default=1.0)
        while budget > 0:
            pick = self._best_candidate(common, worst)
            if pick is None:
                break
            tr, takers, who = pick
            step = self._pour(tr, budget)
            spent += step
            budget -= step
            if not tr.done:                   # carries on into the next tick
                if takers:
                    self.shared, self.shared_for = tr, takers
                else:
                    who.inflight = tr
                break
            if takers:
                self.shared, self.shared_for = tr, takers
                self._finish_shared()
            else:
                who.take(tr)
                self.delivered += tr.total
        return spent

    def _best_candidate(self, common: list, worst: float):
        """The next thing to send: the most value for its bytes.

        A student who is worse off than the rest of the room counts for more,
        which is what stops a cell from spending everything on the students who
        are already comfortable.
        """
        best, best_value = None, 0.0

        for key, slot in common:
            oid, pose, state = key
            level = slot["level"]
            takers = [w for w in slot["who"]
                      if w.wants(oid, level, pose if pose >= 0 else 0, state)]
            if len(takers) < SHARE_MIN:
                continue
            size = self._size(oid, level, pose, state)
            if size <= 0:
                continue
            value = slot["priority"] * self._need_weight(takers, worst) / size
            if value > best_value:
                best_value, best = value, ("shared", key, slot, takers)

        for w in self.watchers:
            req = self._head(w)
            if req is None:
                continue
            oid, level, pose = req[0], req[1], req[2]
            obj = self.scene.by_id(oid)
            state = req[4] if len(req) > 4 and not obj.dynamic else obj.state
            size = self._size(oid, level, pose if obj.dynamic else -1, state)
            if size <= 0:
                continue
            priority = float(req[3]) if len(req) > 3 else 1.0
            value = priority * self._need_weight([w], worst) / size
            if value > best_value:
                best_value, best = value, ("personal", w, None, None)

        if best is None:
            return None
        if best[0] == "personal":
            w = best[1]
            tr = w.next_transfer()
            return (tr, None, w) if tr is not None else None
        _, key, slot, takers = best
        oid, pose, state = key
        tr = self._build(takers[0], oid, slot["level"], pose, state)
        return (tr, takers, None) if tr is not None else None

    @staticmethod
    def _need_weight(takers: list, worst: float) -> float:
        """What the room gains: a point per student, more for the worst served."""
        return sum(1.0 + max(0.0, 0.6 - (w.qoe - worst)) for w in takers)

    def _head(self, w: "Watcher"):
        """What this student is waiting for next, past anything already arrived."""
        while w.queue:
            req = w.queue[0]
            oid, level, pose = req[0], req[1], req[2]
            obj = self.scene.by_id(oid)
            state = req[4] if len(req) > 4 and not obj.dynamic else obj.state
            if w.wants(oid, level, pose, state):
                return req
            w.queue.pop(0)
        return None

    def _size(self, oid: int, level: int, pose: int, state: str) -> int:
        """What this piece would cost on the air, before deciding to send it."""
        obj = self.scene.by_id(oid)
        st = self.watchers[0].session.state
        return int(obj.nbytes(level, st.quantise, st.deflate,
                              pose if obj.dynamic and pose >= 0 else 0, state))

    # -- what differs between students ------------------------------------
    def _pour_personal(self, budget: float) -> float:
        if budget <= 0 or not self.watchers:
            return 0.0
        order = sorted(self.watchers, key=lambda w: w.qoe)   # worst served first
        share = budget / len(order)
        spent = left = 0.0

        for w in order:
            used = self._pour_for(w, share if self.mode != "unicast" else share)
            spent += used
            left += share - used

        # what nobody could use goes to whoever is worst off, rather than being
        # thrown away -- which is the whole difference between `unicast` and `fair`
        if self.mode != "unicast" and left > 0:
            for w in order:
                if left <= 0:
                    break
                used = self._pour_for(w, left)
                spent += used
                left -= used
        return spent

    def _pour_for(self, w: Watcher, budget: float) -> float:
        spent = 0.0
        while budget > 0:
            if w.inflight is None:
                w.inflight = w.next_transfer()
                if w.inflight is None:
                    break
            step = self._pour(w.inflight, budget)
            spent += step
            budget -= step
            if w.inflight.done:
                w.take(w.inflight)
                self.delivered += w.inflight.total
                w.inflight = None
        return spent

    # -- plumbing ----------------------------------------------------------
    def _pour(self, tr: Transfer, budget: float) -> float:
        step = max(0.0, min(budget, tr.total - tr.delivered))
        tr.delivered += step
        self.sent += int(step)
        return step

    def _build(self, w: Watcher, oid: int, level: int, pose: int,
               state: str) -> Transfer | None:
        """Build one transfer by hand, for geometry the class shares."""
        obj = self.scene.by_id(oid)
        p = pose if obj.dynamic else 0
        body, header = obj.level(level, p, state).encoded(
            w.session.state.quantise, w.session.state.deflate)
        header = dict(header, oid=oid, level=level, pose=p, dynamic=obj.dynamic)
        if obj.dynamic:
            header.update(place=obj.place, gesture=obj.gesture)
        else:
            header.update(state=state, colour=list(obj.colour_for(state)),
                          anim=obj.anims.get(state, []))
        return Transfer(oid, level, p, obj.dynamic, header, body,
                        state=state if not obj.dynamic else "default")

    # -- a whole run -------------------------------------------------------
    def run(self, seconds: float = 20.0) -> CellResult:
        means, worsts, hist = [], [], []
        while self.t < seconds:
            row = self.tick()
            means.append(row["mean_qoe"])
            worsts.append(row["worst_qoe"])
            hist.append(row)
        mean = float(np.mean(means)) if means else 1.0
        worst = float(np.mean(worsts)) if worsts else 1.0
        return CellResult(
            mode=self.mode, students=len(self.watchers), mbps=self.mbps,
            seconds=seconds, attention=self.attention,
            mean_qoe=mean, worst_qoe=worst,
            floor_qoe=float(min(worsts)) if worsts else 1.0,
            spread=mean - worst,
            cell_mbytes=self.sent / 1e6,
            delivered_mbytes=self.delivered / 1e6,
            sharing_gain=(self.delivered / self.sent) if self.sent else 1.0,
            ticks=len(hist), history=hist)


def run(mode: str = "hub", students: int = 8, mbps: float = 50.0,
        lesson: str | None = "anatomy", scenario: str = "classroom",
        seconds: float = 20.0, policy: str = "lesson_aware",
        tick_hz: float = 20.0, attention: float = 1.0) -> CellResult:
    """One class, one cell, one scheduler."""
    return Cell(scenario=scenario, lesson=lesson, students=students, mode=mode,
                mbps=mbps, policy=policy, tick_hz=tick_hz,
                attention=attention).run(seconds)
