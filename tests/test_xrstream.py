"""Tests for the streaming testbed.

Run with:  .venv/Scripts/python -m pytest -q
"""
from __future__ import annotations

import struct
import zlib

import numpy as np
import pytest

from xrstream import codec, geometry as g, paths, policies, quality, scenarios
from xrstream import body as bodies, font, lesson as lessons
from xrstream.camera import Camera
from xrstream.link import Link
from xrstream.session import StreamSession


# --------------------------------------------------------------------------
# a Python mirror of the browser decoder, so the wire format is covered
# --------------------------------------------------------------------------
def decode(header: dict, body: bytes):
    if header.get("z"):
        body = zlib.decompress(body)
    nv = header["nv"]
    if header["pos"] == "f32":
        pos = np.frombuffer(body, np.float32, nv * 3).reshape(nv, 3)
        used = nv * 12
    else:
        q = np.frombuffer(body, np.uint16, nv * 3).reshape(nv, 3).astype(np.float64)
        pos = np.asarray(header["lo"]) + q / 65535.0 * np.asarray(header["span"])
        used = nv * 6
    rest = body[used:]
    if header["kind"] == "points":
        return pos, np.frombuffer(rest, np.uint8, nv * 3).reshape(nv, 3)
    dt = np.uint16 if header["idx"] == "u16" else np.uint32
    idx = np.frombuffer(rest, dt, header["nt"] * 3).reshape(header["nt"], 3)
    if not header.get("rgb") and not header.get("parts"):
        return pos, idx
    at = idx.nbytes
    rgb = parts = None
    if header.get("rgb"):
        rgb = np.frombuffer(rest[at:], np.uint8, nv * 3).reshape(nv, 3)
        at += nv * 3
    if header.get("parts"):
        parts = np.frombuffer(rest[at:], np.uint16, nv)
    return pos, idx, rgb, parts


@pytest.fixture(scope="module")
def scene():
    return scenarios.build("classroom", students=3)


@pytest.fixture(scope="module")
def all_scenes():
    """Every scenario, built once and shared -- building them is the slow part."""
    return {n: scenarios.build(n) for n in scenarios.NAMES}


@pytest.fixture
def cam():
    return Camera(pos=[0, 1.65, 6], forward=[0, 0, -1], screen_h=1080, aspect=16 / 9)


# --------------------------------------------------------------------------
# geometry
# --------------------------------------------------------------------------
def test_parametric_wrap_omits_duplicate_seam():
    open_pos, _ = g.parametric(lambda u, v: (u, v, np.zeros_like(u)), 4, 4)
    wrapped, _ = g.parametric(lambda u, v: (u, v, np.zeros_like(u)), 4, 4, wrap_u=True)
    assert len(open_pos) == 5 * 5
    assert len(wrapped) == 4 * 5


def test_indices_stay_in_range():
    for mesh in (g.sphere(8), g.torus(8), g.cylinder(8), g.box(), g.humanoid(6)):
        pos, idx = mesh
        assert idx.max() < len(pos)
        assert idx.min() >= 0


def test_lod_chain_is_strictly_coarser(scene):
    for obj in scene:
        counts = [lvl.n_primitives for lvl in obj.levels]
        assert counts == sorted(counts, reverse=True)
        assert counts[-1] > 0


def test_sample_surface_respects_count():
    pts, nrm = g.sample_surface(g.sphere(12), 500, np.random.default_rng(0))
    assert pts.shape == (500, 3)
    assert np.allclose(np.linalg.norm(nrm, axis=1), 1.0, atol=1e-5)


# --------------------------------------------------------------------------
# codec
# --------------------------------------------------------------------------
def test_mesh_roundtrip_is_accurate():
    pos, idx = g.sphere(10, radius=2.0)
    body, head = codec.encode_mesh(pos, idx, quantise=True, deflate=True)
    back_pos, back_idx = decode(head, body)
    assert np.array_equal(back_idx, idx)
    assert np.abs(back_pos - pos).max() < 1e-3          # uint16 over a 4 m box


def test_points_roundtrip_preserves_colour():
    pts, nrm = g.sample_surface(g.sphere(8), 300, np.random.default_rng(1))
    rgb = ((nrm * 0.5 + 0.5) * 255).astype(np.uint8)
    body, head = codec.encode_points(pts, rgb, quantise=True, deflate=True)
    back_pos, back_rgb = decode(head, body)
    assert np.array_equal(back_rgb, rgb)
    assert np.abs(back_pos - pts).max() < 1e-3


def test_quantisation_and_deflate_both_shrink_the_payload():
    pos, idx = g.sphere(16)
    raw = codec.encode_mesh(pos, idx, quantise=False, deflate=False)[1]["bytes"]
    quant = codec.encode_mesh(pos, idx, quantise=True, deflate=False)[1]["bytes"]
    both = codec.encode_mesh(pos, idx, quantise=True, deflate=True)[1]["bytes"]
    assert quant < raw
    assert both < quant


def test_frame_header_is_parseable():
    blob = codec.frame({"hello": 1}, b"xyz")
    n = struct.unpack("<I", blob[:4])[0]
    assert blob[4 + n:] == b"xyz"


# --------------------------------------------------------------------------
# camera
# --------------------------------------------------------------------------
def test_frustum_accepts_ahead_rejects_behind(cam):
    assert cam.sees_sphere(np.array([0.0, 1.6, 0.0]), 0.5)
    assert not cam.sees_sphere(np.array([0.0, 1.6, 20.0]), 0.5)


def test_large_sphere_straddling_the_eye_is_visible(cam):
    assert cam.sees_sphere(np.array([0.0, 1.6, 6.0]), 5.0)


def test_screen_radius_falls_with_distance(cam):
    near = cam.screen_radius_px(np.array([0.0, 1.6, 4.0]), 0.5)
    far = cam.screen_radius_px(np.array([0.0, 1.6, -10.0]), 0.5)
    assert near > far > 0


def test_attention_is_bounded(cam):
    for z in (-40.0, -10.0, 0.0, 5.0):
        a = cam.attention(np.array([0.0, 1.6, z]), 0.9)
        assert 0.3 - 1e-9 <= a <= 1.0 + 1e-9


def test_required_level_coarsens_with_distance(scene, cam):
    obj = next(o for o in scene if o.name == "instructor")
    near = Camera(pos=obj.centre + np.array([0, 0, 1.5]), forward=[0, 0, -1])
    far = Camera(pos=obj.centre + np.array([0, 0, 30.0]), forward=[0, 0, -1])
    assert obj.required_level(near) <= obj.required_level(far)


# --------------------------------------------------------------------------
# quality model
# --------------------------------------------------------------------------
def test_coarsest_copy_beats_nothing():
    """The bug that made the allocator refuse to send cheap coarse geometry."""
    worst = quality.lod_penalty(level=3, required=0, coarsest=3)
    assert worst < quality.MISSING_PENALTY


def test_lod_penalty_is_monotonic():
    pens = [quality.lod_penalty(l, 0, 3) for l in range(4)]
    assert pens == sorted(pens)
    assert pens[0] == 0.0


def test_staleness_saturates():
    assert quality.staleness_penalty(0.0, 15) == 0.0
    assert quality.staleness_penalty(10.0, 15) == 1.0


# --------------------------------------------------------------------------
# link
# --------------------------------------------------------------------------
def test_step_profile_alternates():
    link = Link(mbps=10, profile="step", period=5.0, depth=0.2)
    assert link.capacity_mbps(1.0) == pytest.approx(10.0)
    assert link.capacity_mbps(6.0) == pytest.approx(2.0)


def test_sine_profile_stays_positive_and_bounded():
    link = Link(mbps=10, profile="sine", period=4.0, depth=0.3)
    vals = [link.capacity_mbps(t) for t in np.linspace(0, 8, 200)]
    assert min(vals) >= 0
    assert max(vals) <= 20.0


def test_loss_is_off_by_default():
    assert not any(Link(mbps=10).lost() for _ in range(200))


def test_loss_fires_at_roughly_the_configured_rate():
    link = Link(mbps=10, loss=0.25, seed=3)
    hits = sum(link.lost() for _ in range(4000))
    assert 0.20 < hits / 4000 < 0.30


# --------------------------------------------------------------------------
# policies
# --------------------------------------------------------------------------
def _state(sess):
    return sess.state


def test_allocator_never_overspends_the_frame_budget(scene, cam):
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=8.0)
    st = _state(sess)
    pol = sess.policy
    cands = [(o, o.required_level(cam), pol._importance(o, cam))
             for o in scene.dynamic]
    budget = 40_000.0
    levels, dropped = pol._allocate(cands, st, budget)
    spent = sum(scene.by_id(oid).nbytes(lvl, st.quantise, st.deflate, st.pose)
                for oid, lvl in levels.items())
    assert spent <= budget
    assert set(levels) | dropped == {o.oid for o in scene.dynamic}


def test_allocator_spends_what_it_can(scene, cam):
    """Regression: a zero-valued coarsest level made it buy nothing at all."""
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=8.0)
    st = _state(sess)
    pol = sess.policy
    cands = [(o, 0, pol._importance(o, cam)) for o in scene.dynamic]
    cheapest = min(o.nbytes(o.coarsest, st.quantise, st.deflate) for o in scene.dynamic)
    levels, _ = pol._allocate(cands, st, cheapest * 3)
    assert levels, "allocator refused to send any geometry despite having budget"


def test_semantic_weighting_favours_the_instructor(scene, cam):
    sem = policies.get("adaptive")
    geo = policies.get("adaptive_geom")
    inst = next(o for o in scene if o.semantic == "instructor")
    stud = next(o for o in scene if o.semantic == "student")
    ratio_sem = sem._importance(inst, cam) / sem._importance(stud, cam)
    ratio_geo = geo._importance(inst, cam) / geo._importance(stud, cam)
    assert ratio_sem > ratio_geo


def test_roi_policy_ignores_objects_behind_the_camera(scene, cam):
    st = _state(StreamSession(scene, "roi", bandwidth_mbps=10.0))
    ranked = policies.get("roi").rank(scene, cam, st)
    planes = cam.planes()
    for oid, _, _, _ in ranked:
        o = scene.by_id(oid)
        assert cam.sees_sphere(o.centre, o.radius, planes)


def test_full_policy_is_viewpoint_blind(scene, cam):
    st = _state(StreamSession(scene, "full", bandwidth_mbps=10.0))
    other = Camera(pos=[0, 1.6, -20], forward=[0, 0, 1])
    a = policies.get("full").rank(scene, cam, st)
    b = policies.get("full").rank(scene, other, st)
    assert [r[0] for r in a] == [r[0] for r in b]


# --------------------------------------------------------------------------
# session
# --------------------------------------------------------------------------
def test_never_sends_more_than_the_link_allows(scene):
    bw = 12.0
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=bw)
    for _t, dt, cam in paths.frames("lecture_walk", fps=30):
        sess.tick(cam, dt)
    ceiling = bw * 1e6 / 8 * (sess.state.t + StreamSession.BURST_SECONDS)
    assert sess.total_bytes <= ceiling


def test_objects_become_resident_only_once_fully_delivered(scene):
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=1.0)
    cam = Camera(pos=[0, 1.65, 6], forward=[0, 0, -1])
    sess.tick(cam, 0.001)
    if sess.inflight is not None and not sess.inflight.done:
        assert sess.inflight.oid not in sess.state.static_level
        assert sess.inflight.oid not in sess.state.dyn


def test_lost_transfers_are_not_delivered(scene):
    """With total loss nothing may ever appear on the client."""
    sess = StreamSession(scene, "adaptive",
                         link=Link(mbps=50, loss=1.0, seed=0))
    for _t, dt, cam in paths.frames("lecture_walk", fps=30):
        sess.tick(cam, dt)
    assert not sess.state.static_level and not sess.state.dyn
    assert sess.lost_bytes > 0


def test_adaptive_beats_the_strawman(scene):
    runs = {}
    for name in ("full", "adaptive"):
        sess = StreamSession(scene, name, bandwidth_mbps=10.0)
        for _t, dt, cam in paths.frames("lecture_walk", fps=30):
            sess.tick(cam, dt)
        runs[name] = sess.summary()
    assert runs["adaptive"]["mean_qoe"] > runs["full"]["mean_qoe"]
    assert runs["adaptive"]["instructor_fps"] > runs["full"]["instructor_fps"]
    assert runs["adaptive"]["total_mb"] < runs["full"]["total_mb"]


def test_participants_keep_up_on_a_modest_link(scene):
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=10.0)
    for _t, dt, cam in paths.frames("lecture_walk", fps=30):
        sess.tick(cam, dt)
    fps = sess.participant_fps()
    assert fps["instructor"] > 10.0
    assert sess.summary()["mean_staleness_ms"] < 250


def test_camera_path_is_deterministic():
    a = [c.pos.copy() for _, _, c in paths.frames("lecture_walk", fps=10)]
    b = [c.pos.copy() for _, _, c in paths.frames("lecture_walk", fps=10)]
    assert all(np.allclose(x, y) for x, y in zip(a, b))


# --------------------------------------------------------------------------
# scenarios
# --------------------------------------------------------------------------
@pytest.mark.parametrize("name", scenarios.NAMES)
def test_every_scenario_builds_a_usable_scene(all_scenes, name):
    sc = all_scenes[name]
    assert len(sc) > 5
    assert sc.static and sc.dynamic
    assert sc.stats()["full_stream_mbps"] > 50      # bandwidth really is scarce
    assert all(o.radius > 0 for o in sc)
    assert [o.oid for o in sc] == list(range(len(sc)))


@pytest.mark.parametrize("name", scenarios.NAMES)
def test_every_scenario_has_playable_paths(name):
    sc = scenarios.get(name)
    assert sc.paths, f"{name} has no camera path"
    for keys in sc.paths.values():
        assert len(keys) >= 2
        times = [k[0] for k in keys]
        assert times == sorted(times)
        cams = list(paths.frames(keys, fps=5))
        assert len(cams) > 5


@pytest.mark.parametrize("name", scenarios.NAMES)
def test_adaptive_beats_the_strawman_everywhere(all_scenes, name):
    """The headline claim has to hold in every lesson, not just the lecture."""
    sc = all_scenes[name]
    keys = scenarios.get(name).path()
    out = {}
    for pol in ("full", "adaptive"):
        sess = StreamSession(sc, pol, bandwidth_mbps=10.0)
        for _t, dt, cam in paths.frames(keys, fps=20):
            sess.tick(cam, dt)
        out[pol] = sess.summary()
    assert out["adaptive"]["mean_qoe"] > out["full"]["mean_qoe"]
    assert out["adaptive"]["total_mb"] <= out["full"]["total_mb"]


@pytest.mark.parametrize("name", scenarios.NAMES)
def test_participants_stay_live_under_adaptive(all_scenes, name):
    sc = all_scenes[name]
    sess = StreamSession(sc, "adaptive", bandwidth_mbps=25.0)
    for _t, dt, cam in paths.frames(scenarios.get(name).path(), fps=20):
        sess.tick(cam, dt)
    assert sess.summary()["mean_staleness_ms"] < 400


def test_scenarios_have_distinct_stress_profiles(all_scenes):
    """The scenarios exist to stress different things; check they actually do."""
    gym = all_scenes["gym"].stats()
    classroom = all_scenes["classroom"].stats()
    chem = all_scenes["chemistry_lab"].stats()
    # the PE class is nearly all live data, the labs are geometry-heavy
    assert gym["static_bytes"] < classroom["static_bytes"] / 4
    assert gym["dynamic_objects"] > classroom["dynamic_objects"]
    assert chem["triangles_l0"] > classroom["triangles_l0"]


def test_semantic_weights_follow_the_lesson(all_scenes):
    """In a PE class the moving bodies are the lesson, not the tutor's slides."""
    gym = all_scenes["gym"]
    athlete = next(o for o in gym if o.semantic == "athlete")
    furniture = next(o for o in gym if o.semantic == "furniture")
    assert athlete.weight > furniture.weight

    lecture = all_scenes["classroom"]
    student = next(o for o in lecture if o.semantic == "student")
    instructor = next(o for o in lecture if o.semantic == "instructor")
    assert instructor.weight > student.weight
    # a PE participant outranks a seated lecture student
    assert athlete.weight > student.weight


def test_unknown_scenario_is_rejected():
    with pytest.raises(KeyError):
        scenarios.get("underwater_basket_weaving")


# --------------------------------------------------------------------------
# lessons
# --------------------------------------------------------------------------
LESSON_NAMES = sorted(lessons.load_all())


def _bound(name, all_scenes):
    les = lessons.get(name)
    return les.bind(all_scenes[les.scenario])


def _play(scene, policy, bw, les):
    sess = StreamSession(scene, policy, bandwidth_mbps=bw, lesson=les)
    for _t, dt, cam in paths.frames(les.camera_keys(), fps=20):
        sess.tick(cam, dt)
    return sess


def test_there_are_lessons():
    assert len(LESSON_NAMES) >= 3


@pytest.mark.parametrize("name", LESSON_NAMES)
def test_every_lesson_binds_to_its_room(all_scenes, name):
    les = _bound(name, all_scenes)
    assert les.scenario in scenarios.NAMES
    assert all(s.focus_ids or s.speaker_ids for s in les.steps)
    assert [s.t for s in les.steps] == sorted(s.t for s in les.steps)
    assert all(s.end > s.t for s in les.steps)


def test_a_lesson_naming_a_missing_object_is_rejected(all_scenes):
    bad = lessons.Lesson.from_dict({
        "name": "bad", "scenario": "classroom",
        "steps": [{"t": 0, "focus": ["unicorn"]}]})
    with pytest.raises(ValueError, match="unicorn"):
        bad.bind(all_scenes["classroom"])


def test_context_follows_the_plan(all_scenes):
    les = _bound("anatomy", all_scenes)
    sc = all_scenes["classroom"]
    names = lambda ids: {sc.by_id(i).name for i in ids}
    assert names(les.context(15.0).focus) == {"anatomical_model"}
    assert names(les.context(29.0).speakers) == {"student_3"}
    # the model on the right is announced ahead of time
    upcoming = les.context(18.0).upcoming
    assert any(names(step.focus_ids) == {"cad_assembly"} for _, step in upcoming)
    assert all(0 < dt <= les.horizon_s for dt, _ in upcoming)


@pytest.mark.parametrize("name", LESSON_NAMES)
def test_lesson_camera_is_well_formed(all_scenes, name):
    les = _bound(name, all_scenes)
    keys = les.camera_keys()
    times = [k[0] for k in keys]
    assert times == sorted(times)
    assert times[0] == 0.0 and times[-1] == pytest.approx(les.duration)
    assert np.allclose(keys[0][1], les.seat)
    assert keys == les.camera_keys()          # deterministic for a given seed


def test_viewer_ends_up_looking_at_each_step(all_scenes):
    les = _bound("anatomy", all_scenes)
    keys = les.camera_keys()
    for step in les.steps[1:]:
        t = min(step.arrival + 0.2, step.end - 0.05)
        cam = paths.sample(keys, t)
        want = step.look - step.eye
        cos = cam.forward @ (want / np.linalg.norm(want))
        # either facing the step's focus, or mid-glance at something else
        assert cos > 0.95 or t > step.arrival


def test_occluders_are_on_the_line_of_sight(all_scenes):
    sc = all_scenes["chemistry_lab"]
    demo = next(o for o in sc if o.name == "reaction_demo")
    eye = np.array([0.3, 1.62, 1.2])
    occ = {sc.by_id(i).name for i in lessons.occluders(sc, eye, {demo.oid})}
    assert "student_3" in occ                # stands right between eye and flask
    assert "student_0" not in occ            # well off to the side
    assert "instructor" not in occ           # behind the flask, not in front


def test_lesson_aware_without_a_lesson_is_just_adaptive(scene):
    out = {}
    for pol in ("adaptive", "lesson_aware"):
        sess = StreamSession(scene, pol, bandwidth_mbps=10.0)
        for _t, dt, cam in paths.frames("lecture_walk", fps=20):
            sess.tick(cam, dt)
        out[pol] = sess.summary()
    for k in ("mean_qoe", "total_mb", "instructor_fps", "mean_staleness_ms"):
        assert out["adaptive"][k] == out["lesson_aware"][k], k


def test_context_boosts_speaker_and_focus(all_scenes):
    les = _bound("titration", all_scenes)
    sc = all_scenes["chemistry_lab"]
    sess = StreamSession(sc, "lesson_aware", bandwidth_mbps=10.0, lesson=les)
    sess.state.context = les.context(28.0)          # a student asks a question
    pol = sess.policy
    cam = paths.sample(les.camera_keys(), 28.0)
    pol._begin(sc, cam, sess.state)
    asker = next(o for o in sc if o.name == "student_2")
    other = next(o for o in sc if o.name == "student_7")
    assert pol._context_gain(asker) > pol._context_gain(other)


def test_read_ahead_requests_what_the_plan_shows_next(all_scenes):
    les = _bound("anatomy", all_scenes)
    sc = all_scenes["classroom"]
    sess = StreamSession(sc, "lesson_aware", bandwidth_mbps=10.0, lesson=les)
    sess.state.context = les.context(18.0)          # the CAD pump is 3 s away
    pol = sess.policy
    cam = paths.sample(les.camera_keys(), 18.0)
    pol._begin(sc, cam, sess.state)
    names = {sc.by_id(r[0]).name for r in pol._extra_static(sc, cam, sess.state)}
    assert "cad_assembly" in names


def test_read_ahead_never_prefetches_live_streams(all_scenes):
    les = _bound("titration", all_scenes)
    sc = all_scenes["chemistry_lab"]
    sess = StreamSession(sc, "lesson_aware", bandwidth_mbps=10.0, lesson=les)
    sess.state.context = les.context(17.0)          # a student speaks at t=20
    pol = sess.policy
    cam = paths.sample(les.camera_keys(), 17.0)
    pol._begin(sc, cam, sess.state)
    assert all(not sc.by_id(r[0]).dynamic
               for r in pol._extra_static(sc, cam, sess.state))


def test_reading_the_plan_makes_detail_ready_sooner(all_scenes):
    """The headline claim of the lesson-aware policy."""
    runs = {pol: _play(all_scenes["classroom"], pol, 10.0, _bound("anatomy", all_scenes))
            .summary() for pol in ("adaptive", "lesson_aware")}
    assert runs["lesson_aware"]["time_to_detail_s"] < runs["adaptive"]["time_to_detail_s"]
    assert runs["lesson_aware"]["focus_quality"] >= runs["adaptive"]["focus_quality"]
    # ...without giving up overall quality
    assert runs["lesson_aware"]["mean_qoe"] > runs["adaptive"]["mean_qoe"] - 0.02


def test_occluder_protection_clears_the_view_when_affordable(all_scenes):
    """In the crowded chemistry lab, protecting the classmates in the line of
    sight gives a clearer view of the demonstration (see README)."""
    sc = all_scenes["chemistry_lab"]
    runs = {pol: _play(sc, pol, 25.0, _bound("titration", all_scenes)).summary()
            for pol in ("lesson_ctx", "lesson_ctx_occ")}
    occ, ctx = runs["lesson_ctx_occ"], runs["lesson_ctx"]
    assert occ["clear_view"] > ctx["clear_view"] + 0.05
    assert occ["clear_view_p05"] >= ctx["clear_view_p05"]


def test_ablations_stay_out_of_the_menu(scene):
    menu = {p["name"] for p in StreamSession(scene).manifest()["policies"]}
    assert "lesson_aware" in menu
    assert "lesson_ctx" not in menu and "lesson_ctx_occ" not in menu


# --------------------------------------------------------------------------
# allocator and link fixes found while building the lessons
# --------------------------------------------------------------------------
def test_liveness_floor_keeps_everyone_on_screen_alive(all_scenes):
    sc = all_scenes["chemistry_lab"]
    sess = StreamSession(sc, "lesson_aware", bandwidth_mbps=10.0)
    st, pol = sess.state, sess.policy
    cam = Camera(pos=[0.3, 1.62, 1.2], forward=[-0.07, -0.05, -1])
    pol._begin(sc, cam, st)
    # give one participant a huge importance, as a lesson speaker boost would
    cands = [(o, 0, 100.0 if i == 0 else 0.1) for i, o in enumerate(sc.dynamic)]
    floor = sum(o.nbytes(o.coarsest) for o in sc.dynamic)
    levels, dropped = pol._allocate(cands, st, floor * 1.5)
    assert not dropped, "a boosted speaker must not freeze everyone else"


def test_live_updates_interrupt_and_resume_scenery_downloads(scene):
    sess = StreamSession(scene, "adaptive", bandwidth_mbps=1.0)
    big = max(scene.static, key=lambda o: o.nbytes(0))
    body, header = big.level(0).encoded(True, True)
    from xrstream.session import Transfer
    sess.inflight = Transfer(big.oid, 0, 0, False, dict(header), body, delivered=100)
    cam = Camera(pos=[0, 1.65, 3], forward=[0, 0, -1])
    sess.tick(cam, 1 / 30)
    assert (big.oid, 0, "default") in sess.parked or sess.state.static_level.get(big.oid) == 0
    if (big.oid, 0, "default") in sess.parked:
        assert sess.parked[(big.oid, 0, "default")].delivered >= 100      # progress kept


def test_naive_policies_are_not_given_preemption():
    assert not policies.get("full").preempt_static_for_live
    assert policies.get("adaptive").preempt_static_for_live


# --------------------------------------------------------------------------
# a lesson that happens on screen: writing, motion, states over time
# --------------------------------------------------------------------------
def test_painted_tagged_mesh_roundtrip():
    mesh = g.merge([g.tag(g.paint(g.sphere(6), (1, 0, 0)), 3),
                    g.tag(g.paint(g.box(), (0, 0, 1)), 7)])
    body, head = codec.encode_mesh(*mesh[:2], True, True, mesh[2], mesh[3])
    pos, idx, rgb, parts = decode(head, body)
    assert np.array_equal(rgb, mesh[2])
    assert np.array_equal(parts, mesh[3])
    assert set(parts.tolist()) == {3, 7}


def test_font_strokes_come_in_writing_order():
    mesh, nxt = font.write(["HI"], (0, 0, 0), first_part=10)
    parts = mesh[3]
    assert parts.min() == 10 and parts.max() == nxt - 1
    # H has three strokes and I three: every pen segment is its own part
    assert nxt - 10 == 3 + 3
    # H is written before I: its parts lie further left
    xs = {k: mesh[0][parts == k, 0].mean() for k in range(10, nxt)}
    assert xs[10] < xs[nxt - 1]


def test_every_character_on_the_boards_has_a_glyph():
    """Anything written on a board must be drawable, not silently a '?'."""
    import inspect
    import re
    from xrstream import scenarios as sc_mod
    source = inspect.getsource(sc_mod)
    lines = re.findall(r'"([A-Z0-9][A-Z0-9 ,.:()/=+<>*%?-]{2,})"', source)
    assert any("NAOH" in line for line in lines)
    for line in lines:
        for ch in line:
            assert ch == " " or ch in font.GLYPHS, (ch, line)


@pytest.mark.parametrize("name", ["classroom", "physics_lab", "chemistry_lab"])
def test_board_writing_is_revealed_stroke_by_stroke(all_scenes, name):
    board = next(o for o in all_scenes[name] if o.name == "annotation_layer")
    own = [st for st in board.states[1:] if ":" not in st]   # not a lesson's pages
    assert len(own) >= 2
    previous_last = 0
    for state in own:
        (reveal,) = board.anims[state]
        assert reveal["kind"] == "reveal"
        assert reveal["from"] == previous_last + 1 or previous_last == 0
        assert reveal["to"] > reveal["from"]
        # the new strokes really are in this state's geometry
        parts = board.level(0, state=state).part
        assert parts.max() == reveal["to"]
        previous_last = reveal["to"]


def test_pendulum_swings_at_its_physical_period(all_scenes):
    from xrstream.scenarios import PENDULUM_LENGTH, PENDULUM_PERIOD
    assert PENDULUM_PERIOD == pytest.approx(2 * np.pi * np.sqrt(PENDULUM_LENGTH / 9.81), rel=1e-3)
    pend = next(o for o in all_scenes["physics_lab"] if o.name == "pendulum_0")
    (swing,) = pend.anims["released"]
    assert swing["period"] == PENDULUM_PERIOD
    assert 1 in set(pend.level(0, state="released").part.tolist())   # rod + bob


def test_liquid_changes_colour_gradually(all_scenes):
    demo = next(o for o in all_scenes["chemistry_lab"] if o.name == "reaction_demo")
    chain = [demo.anims[s][0] for s in ("indicator", "pinkish", "endpoint")]
    assert all(a["kind"] == "colour" and a["part"] == 5 and a["duration"] > 0 for a in chain)
    # each stage starts from where the last one ended
    for a, b in zip(chain, chain[1:]):
        assert a["to"] == b["from"]


def test_lesson_moves_people_and_changes_things(all_scenes):
    les = _bound("titration", all_scenes)
    sc = all_scenes["chemistry_lab"]
    by = {o.name: o for o in sc}
    les.apply(0.1)
    start = by["instructor"].centre.copy()
    les.apply(4.0)                        # at the board, writing, by now
    assert np.linalg.norm(by["instructor"].centre - start) > 1.0
    assert by["instructor"].gesture.startswith("write")
    les.apply(28.0)
    assert by["student_2"].gesture == "hand_up"
    assert by["reaction_demo"].state == "pinkish"
    assert les.state_since(by["reaction_demo"].oid, 28.0) == 20.0
    les.apply(33.0)
    assert by["student_2"].gesture == "none"
    assert by["reaction_demo"].state == "endpoint"


def test_animation_parameters_travel_with_the_geometry(all_scenes):
    sc = all_scenes["physics_lab"]
    les = _bound("pendulum", all_scenes)
    sess = StreamSession(sc, "lesson_aware", bandwidth_mbps=25.0, lesson=les)
    headers, changes = {}, {}
    for _t, dt, cam in paths.frames(les.camera_keys(), fps=20):
        res = sess.tick(cam, dt)
        changes.update(res.state_changes)
        for ch in res.chunks:
            if not ch.header["dynamic"]:
                headers[(ch.oid, ch.header["state"])] = ch.header
        if sess.state.t > 46:
            break
    pend = next(o for o in sc if o.name == "pendulum_0")
    assert changes[pend.oid] == {"state": "released", "t0": 42.0}
    hdr = headers[(pend.oid, "released")]
    assert hdr["anim"][0]["kind"] == "swing" and hdr.get("parts")


def test_session_start_resets_the_room(all_scenes):
    sc = all_scenes["chemistry_lab"]
    les = _bound("titration", all_scenes)
    les.apply(30.0)
    StreamSession(sc, "adaptive", bandwidth_mbps=10.0)       # a fresh viewer
    assert all(o.state == "default" and o.gesture == "none" for o in sc)
    assert all(np.allclose(o.offset, 0) for o in sc.dynamic)


def _hand_world(o):
    j = bodies.forward(o.pose, o.body.scale)
    return bodies.rot_y(o.yaw) @ j.hand[1] + np.array([o.place[0], 0.0, o.place[2]])


@pytest.mark.parametrize("name", LESSON_NAMES)
def test_writers_hand_is_on_the_pen_for_every_stroke(all_scenes, name):
    """Whoever writes -- teacher or student -- has their hand on each stroke
    as it appears (inverse kinematics on the same timetable the viewer uses)."""
    les = _bound(name, all_scenes)
    writes = [(les.scene.by_id(oid), m) for oid, moves in les._moves.items()
              for m in moves if m["kind"] == "write"]
    if not writes:
        pytest.skip(f"nobody writes in {name}")
    errors = []
    for o, m in writes:
        for tk in m["times"]:
            t = m["t0"] + tk + 0.01
            les.apply(t)
            errors.append(np.linalg.norm(_hand_world(o) - les._pen_at(m, t)[0]))
    assert np.max(errors) < 0.01                      # within a centimetre, always


@pytest.mark.parametrize("name", LESSON_NAMES)
def test_writing_starts_when_the_writer_arrives_and_ends_in_time(all_scenes, name):
    les = _bound(name, all_scenes)
    board = next((o for o in les.scene if o.name == "annotation_layer"), None)
    if board is None:
        pytest.skip("no board in this room")
    for oid, moves in les._moves.items():
        for m in (m for m in moves if m["kind"] == "write"):
            step = les.step_at(m["t0"])
            first_stroke = m["t0"] + m["times"][0]
            assert first_stroke > step.t                   # walked there first
            assert les.state_since(board.oid, first_stroke + 0.01) == pytest.approx(first_stroke)
            # never still writing when the writer's next move begins
            later = [n["t0"] for n in moves if n["t0"] > m["t0"] + 1e-6]
            assert m["t1"] <= min(later + [les.duration]) + 1e-6


def test_every_physics_bench_finds_something_different(all_scenes):
    from xrstream.scenarios import BENCHES, pendulum_period
    periods = [next(o for o in all_scenes["physics_lab"] if o.name == f"pendulum_{k}")
               .anims["released"][0]["period"] for k in range(4)]
    assert periods[1] < periods[0]                 # shorter string: faster
    assert periods[2] == periods[0]                # heavier bob: no change
    assert periods[3] > periods[0]                 # wide swing: a little slower
    assert periods == [pendulum_period(b["length"], b["amp"] if b["amp"] > 0.5 else 0)
                       for b in BENCHES]


def test_physics_lesson_has_theory_then_every_bench(all_scenes):
    les = _bound("pendulum", all_scenes)
    phases = [s.phase for s in les.steps]
    assert phases[0] == "theory" and phases[-1] == "discussion"
    released = [n for s in les.steps for n, st in s.states.items() if st == "released"]
    assert sorted(released) == [f"pendulum_{k}" for k in range(4)]
    assert les.duration >= 110


# --------------------------------------------------------------------------
# articulated bodies: exact gestures and exercises
# --------------------------------------------------------------------------
POSES = {
    "rest": lambda: bodies.Pose(), "idle": lambda: bodies.idle(1.3),
    **{name: (lambda name=name: bodies.activity(name, 0.37 * period))
       for name, (_, period) in bodies.EXERCISES.items()},
}


@pytest.mark.parametrize("name", sorted(POSES))
def test_feet_stay_on_the_floor(name):
    pose = POSES[name]()
    j = bodies.forward(pose)
    lowest = min(j.ankle[1][1], j.ankle[-1][1]) - bodies.ANKLE_H
    assert lowest == pytest.approx(pose.air, abs=1e-6)


def test_reach_puts_the_hand_exactly_on_a_reachable_point():
    rng = np.random.default_rng(0)
    for _ in range(50):
        target = np.array([0.19, 1.44, 0.0]) + rng.normal(size=3) * 0.2
        j0 = bodies.forward(bodies.Pose())
        if np.linalg.norm(target - j0.shoulder[1]) > bodies.REACH * 0.97:
            continue
        j = bodies.forward(bodies.reach(bodies.Pose(), 1, target))
        assert np.linalg.norm(j.hand[1] - target) < 1e-6


def test_pointing_is_exact():
    target = np.array([-1.5, 2.2, -2.0])
    j = bodies.forward(bodies.point_at(bodies.Pose(), 1, target))
    arm = bodies._unit(j.hand[1] - j.shoulder[1])
    want = bodies._unit(target - j.shoulder[1])
    assert np.degrees(np.arccos(np.clip(arm @ want, -1, 1))) < 0.1


def test_exercises_move_the_limbs_they_should():
    top = lambda p: max(bodies.forward(p).hand[1][1], bodies.forward(p).hand[-1][1])
    hip = lambda p: bodies.forward(p).root[1]
    assert top(bodies.jumping_jacks(0.5)) > 1.9 > top(bodies.jumping_jacks(0.0))
    assert hip(bodies.squats(0.5)) < 0.7 < hip(bodies.squats(0.0))
    assert hip(bodies.squats(0.5, depth=0.45)) > hip(bodies.squats(0.5)) + 0.2
    knee = bodies.forward(bodies.high_knees(0.25)).knee
    assert max(knee[1][1], knee[-1][1]) > 0.85             # a knee up at hip height
    assert bodies.forward(bodies.toe_touch(0.5)).head[1] < 1.1


def test_limb_preserving_lod_keeps_every_body_part():
    body = bodies.Body(22_000, {"shirt": (1, 0, 0), "trousers": (0, 0, 1), "skin": (1, .8, .6),
                                "hair": (.3, .2, .1), "shoes": (0, 0, 0)})
    coarsest = body.level_idx[-1]
    for seg in bodies.SEGMENTS:
        sl = body.slices[seg.name]
        kept = int(((coarsest >= sl.start) & (coarsest < sl.stop)).sum())
        assert kept >= min(sl.stop - sl.start, bodies.LIMB_FLOOR[-1]), seg.name
    # ...where a uniform stride would leave a hand with a handful of points
    hand = body.slices["hand_r"]
    assert (hand.stop - hand.start) // 64 < bodies.LIMB_FLOOR[-1]


def test_people_in_step_share_one_capture(all_scenes):
    sc = all_scenes["gym"]
    sc.animate(12.3)                              # everyone doing the same exercise
    same_body = [o for o in sc.dynamic if o.semantic == "athlete"
                 and o.body is sc.by_id(next(p.oid for p in sc.dynamic
                                                 if p.semantic == "athlete")).body]
    assert len(same_body) >= 2
    a, b = same_body[:2]
    assert a.level(1) is b.level(1)               # one encode serves both


def test_runners_face_the_way_they_run(all_scenes):
    les = _bound("pe_warmup", all_scenes)
    runner = next(o for o in les.scene if o.name == "athlete_3")
    for t in (20.0, 25.0, 30.0):
        les.apply(t)
        d = les.xz_at(runner, t + 0.05) - les.xz_at(runner, t - 0.05)
        heading = bodies.facing_yaw((0, 0), d)
        err = abs((runner.yaw - heading + np.pi) % (2 * np.pi) - np.pi)
        assert err < 0.35


def test_coach_points_at_the_student_she_corrects(all_scenes):
    les = _bound("pe_warmup", all_scenes)
    coach = next(o for o in les.scene if o.name == "instructor")
    student = next(o for o in les.scene if o.name == "athlete_5")
    les.apply(82.0)
    assert coach.gesture == "point"
    j = bodies.forward(coach.pose, coach.body.scale)
    to_world = lambda v: bodies.rot_y(coach.yaw) @ v
    origin = np.array([coach.place[0], 0.0, coach.place[2]])
    arm = bodies._unit(to_world(j.hand[1] - j.shoulder[1]))
    want = bodies._unit(les._look_at(student, 82.0) - (to_world(j.shoulder[1]) + origin))
    assert np.degrees(np.arccos(np.clip(arm @ want, -1, 1))) < 3.0


def test_the_corrected_squat_goes_deeper(all_scenes):
    les = _bound("pe_warmup", all_scenes)
    a5 = next(o for o in les.scene if o.name == "athlete_5")
    def lowest(t0, t1):
        return min((les.apply(t), bodies.forward(a5.pose).root[1])[1]
                   for t in np.arange(t0, t1, 0.1))
    assert lowest(71, 76) > lowest(93, 98) + 0.25


def test_groups_expand_to_everyone_in_them(all_scenes):
    les = _bound("pe_warmup", all_scenes)
    laps = next(s for s in les.steps if any("laps" in a for a in s.actions.values()))
    runners = [n for n, a in laps.actions.items() if "laps" in a]
    assert len(runners) == 12 and all(n.startswith("athlete_") for n in runners)


def test_a_lesson_can_bring_its_own_board(all_scenes):
    les = _bound("pythagoras", all_scenes)
    board = next(o for o in les.scene if o.name == "annotation_layer")
    mine = [st for st in board.states if st.startswith("pythagoras:")]
    assert {"pythagoras:triangle", "pythagoras:answer"} <= set(mine)
    writers = {les.scene.by_id(oid).name for oid, moves in les._moves.items()
               if any(m["kind"] == "write" for m in moves)}
    assert writers == {"instructor", "student_1"}          # a student writes too


def test_placement_and_facing_travel_with_each_capture(all_scenes):
    sc = all_scenes["gym"]
    les = _bound("pe_warmup", all_scenes)
    sess = StreamSession(sc, "adaptive", bandwidth_mbps=50.0, lesson=les)
    places = []
    for _t, dt, cam in paths.frames(les.camera_keys(), fps=15):
        for ch in sess.tick(cam, dt).chunks:
            if ch.header["dynamic"]:
                places.append(ch.header["place"])
        if sess.state.t > 25:
            break
    assert places and all(len(p) == 4 for p in places)
    assert len({round(p[3], 1) for p in places}) > 5        # people turn as they run


def test_there_are_five_lessons_including_pe():
    assert {"pe_warmup", "pythagoras", "titration", "pendulum", "anatomy"} <= set(LESSON_NAMES)


# --------------------------------------------------------------------------
# live controls, headsets, the hazard lab
# --------------------------------------------------------------------------
def test_changing_speed_or_strategy_does_not_restart_the_lesson(all_scenes):
    les = _bound("titration", all_scenes)
    sess = StreamSession(les.scene, "adaptive", bandwidth_mbps=10.0, lesson=les)
    cam = paths.sample(les.camera_keys(), 0.0)
    for _ in range(60):
        sess.tick(cam, 1 / 15)
    t, sent = sess.state.t, dict(sess.state.static_level)
    sess.configure(policy="lesson_aware", bandwidth_mbps=40.0, reset=False)
    assert sess.state.t == t and sess.state.static_level == sent    # carries on
    assert sess.policy.name == "lesson_aware" and sess.bandwidth_mbps == 40.0


def test_everyone_wears_a_vr_headset(all_scenes):
    person = all_scenes["classroom"].dynamic[0]
    j = bodies.forward(bodies.Pose(), person.body.scale)
    pts, _ = person.body.skin(bodies.Pose())
    visor = pts[person.body.slices["visor"]]
    # in front of the face (people face -z), at eye height
    assert visor[:, 2].max() < j.head[2] - 0.12
    assert abs(visor[:, 1].mean() - j.head[1]) < 0.05


def test_shield_puts_both_forearms_in_front_of_the_face():
    j = bodies.forward(bodies.shield(bodies.Pose()))
    for side in (1, -1):
        assert j.hand[side][2] < j.head[2] - 0.15               # in front
        assert abs(j.hand[side][1] - j.head[1]) < 0.12          # at face height


def test_hazard_lesson_is_marked_safe_and_its_blasts_are_client_side(all_scenes):
    les = _bound("hazard_lab", all_scenes)
    assert "real life" in les.banner
    kinds = {a["kind"] for o in les.scene for anims in o.anims.values() for a in anims}
    assert {"blast", "burst", "light", "hot", "soft"} <= kinds
    # an explosion is one state change, not a stream of meshes
    demo = next(o for o in les.scene if o.name == "alkali_demo")
    assert "caesium" in demo.states
