# UC7 — Lesson-aware 3D scene streaming for holographic classrooms

A testbed for **SOTA UC7 §1: Three-Dimensional Scene Representation for Immersive XR Streaming**.

**In one sentence:** 3D lessons are streamed live to a browser over a limited internet connection, and we compare ways
of deciding *what to send* when there isn't enough bandwidth for everything — including a new one that reads the
**lesson plan** to know what matters now and what is coming next.

Each lesson mixes static polygonal meshes (§1.4) with live volumetric point-cloud participants (§1.5). The SOTA's
stated gap (**§1.5.7**) is that streaming systems *pay comparatively less attention to semantic prioritisation and
AI-assisted adaptation*. This testbed measures that gap, shows where fixed semantic priorities break, and tests an
alternative.

## Quick start

```bash
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt   # Windows; use .venv/bin/python elsewhere
```

```bash
.venv/Scripts/python -m xrstream.server
```

Open <http://localhost:8000> and pick **Play a lesson**. The camera follows the lesson by itself, a caption describes
each step and a voice reads it aloud.

| key | what it does |
|---|---|
| `Space` or **Pause** | pause / resume the lesson |
| click the lesson's progress bar | jump to that moment |
| `H` | hide the panels, leaving only the lesson (for presenting) |
| `WASD`, drag | take control of the camera; **Follow the lesson again** hands it back |
| `R` | start the lesson over |

The strategy and the internet speed are on the left; changing them takes effect at once, without restarting the
lesson, so you can watch the picture get worse or better. The number top-right is picture quality out of 100.

**⚡ 5G** next to the speed puts the link straight to 100 Mbit/s, where everything arrives at full quality; clicking it
again goes back to the speed you were on, and a **5G** badge sits beside the quality score while it is on. Demo it
the other way round: run a lesson at 3 Mbit/s until it looks broken, then press 5G.

> After updating the code, **restart the server**: Python loads the code only at start-up, so a server left running
> keeps serving the old lessons to the new viewer.

```bash
.venv/Scripts/python -m xrstream.bench --lesson all              # the lessons  -> results/lessons/
.venv/Scripts/python -m xrstream.bench                           # the rooms    -> results/
.venv/Scripts/python -m xrstream.bench --link step --loss 0.05 --out results/congested
.venv/Scripts/python -m pytest tests -q                          # 132 tests
```

## Watching from a headset (WebXR)

The viewer is also a WebXR page, so the same lesson can be watched from inside a Quest 3: stereo, real head
tracking, and -- the point of doing it at all -- **the headset's own pose is what the server streams for**, so the
streaming policies react to where the teacher actually looks.

An immersive session needs a secure context: `https://`, or plain HTTP on `localhost`. Either works.

**Over the network (no cable, no developer account).** The server makes its own certificate, for localhost and for
whatever addresses this machine answers on:

```
python -m xrstream.server --https --port 8443
```

It prints the address to open in the headset. The certificate is self-signed, so the browser warns once:
Advanced -> Proceed.

**Over USB**, if the headset is in developer mode:

```
adb reverse tcp:8010 tcp:8010
```

Then open `http://localhost:8010` in the headset and press **Enter VR** (the button only appears when the browser
reports immersive-vr support, so on a LAN address such as `http://192.168.1.20:8010` it stays hidden -- that is the
browser's rule, not ours). Wireless instead: serve over HTTPS with `uvicorn --ssl-keyfile/--ssl-certfile` and accept
the certificate on the headset.

Two things will waste an afternoon if nobody warns you. **Guest and phone-hotspot networks usually isolate their
clients**, so the headset has internet and still cannot reach the laptop; Windows' own Mobile Hotspot does not, and
is the quickest way out. And **a Windows firewall Block rule for python.exe beats any Allow rule you add** -- one
gets created every time someone dismisses the "allow network access?" prompt, and the symptom is a connection that
times out on every port. Look for it before blaming anything else:

```
Get-NetFirewallRule -Direction Inbound -Action Block -Enabled True |
  Where-Object { ($_ | Get-NetFirewallApplicationFilter).Program -match 'python' }
```

Inside:

| control | does |
|---|---|
| right thumbstick | glide, in the direction you are looking |
| left thumbstick left/right | snap turn, 30 degrees a step |
| A | stand back where the room puts the viewer |
| B / Y | next / previous lesson, without taking the headset off |

Snap turning rather than smooth turning, no screen shake during explosions, and no autopilot: a lesson that drags
the viewer around is unpleasant to wear, so entering VR hands control back to the person. The captions cannot be
read from a page you are no longer looking at, so in VR they are painted onto a panel below the line of sight,
together with the quality score.

The desktop viewer is unchanged -- same page, same keys.

## The idea: stream from the lesson plan

Existing viewport-adaptive streaming guesses where you'll look from your recent head movement, which works a few
hundred milliseconds ahead. A lesson has a much better predictor: its **plan**. "At 0:12 the teacher shows the burette;
at 0:20 a student asks a question." The `lesson_aware` policy reads the plan and does three things:

1. **Context weights.** Whoever is speaking and whatever is being shown get priority, and the lesson phase can
   re-weight whole groups (in group work the students matter more than the teacher). This replaces the fixed
   class-weight table, which the results below show is sometimes harmful.
2. **Occluder protection.** Live participants standing *between* the viewer and the thing being shown get priority
   too. A frozen or blocky classmate in front of the flask spoils your view of the flask.
3. **Reading ahead.** Scenery the plan will show in the next few seconds is fetched at the detail it'll need *before*
   the viewer turns to it.

> **On novelty:** the SOTA covers semantic streaming and AI orchestration, but not driving the streaming from the lesson
> plan itself. I haven't done a literature search, though, so check before claiming it's new.

## The lessons

Lessons are JSON files in [`lessons/`](lessons/README.md), not code. Each step names who speaks, what is shown, a
caption, and what *happens*: people walk, run, exercise, raise a hand, point, clap, write on the board; objects change.

| lesson | room | length | what happens |
|---|---|---|---|
| The heart as a pump | lecture classroom | 52 s | the teacher draws and labels the heart's chambers, walks to models on both sides of the room, answers a raised-hand question at a valve that flaps open one way, writes a summary |
| Pythagoras' theorem | lecture classroom | 82 s | the teacher writes the title, draws a right triangle and labels it, writes the formula in red; a student asks a question and she points at the triangle; **a student comes to the board and solves 3-4-5 himself**; the class applauds; the teacher adds the rule to remember |
| What sets a pendulum's period? | physics lab | 128 s | **theory first**: formula, variables and a prediction written on the board and a vacuum-pump demo; then **four benches each change one thing** (the reference, a 40 cm string, a heavy iron ball, a wide swing) and each finds something different; results and a conclusion written up at the end |
| Acid-base titration | chemistry lab | 64 s | a demonstration watched from a step behind the crowd: indicator, drops falling from the burette, the liquid creeping towards pink and then flipping at the end point, heating and steam; a student asks why the colour changes suddenly |
| Warm-up and exercises | PE hall | 150 s | the coach whistles; twelve students run two laps and walk back to their places; then **jumping jacks, arm circles, squats, high knees, lunges, toe touches, side stretches and a cool-down**, each shown by the coach first. One student squats too shallow: the coach walks over, points at him, demonstrates, and he goes deeper. Everything moves, all the time |
| Too dangerous for school | hazard lab | 130 s | **what VR can do that a school can't**: lithium, sodium and potassium in water (fizzing, an orange flame, a lilac flame with sparks), then **caesium explodes** — fireball, flash, flying glass, water spraying over the bench; **thermite** burns white-hot in a fountain of sparks, molten iron pours out and burns through a steel plate; a **hydrogen balloon** becomes a fireball. The class stands at arm's length and flinches; a banner says it is safe because it is virtual |

### What the viewer sees

- **People have real bodies.** Every participant is an articulated skeleton (hips, knees, shoulders, elbows, torso,
  head) sampled into a point cloud. Exercises are cycles of joint angles with the feet locked to the floor; people
  face where they walk, run or point; poses blend over a third of a second instead of snapping.
- **Everyone wears a VR headset**, as they would in a real holographic class.
- **Danger that looks real.** Fire glows, sparks arc and land, molten metal drips, smoke swells and drifts and is
  see-through, a blast flashes the screen, shakes the view and bangs. All of it is part of an object's state, played by
  the viewer: an explosion costs the network one state change, not a stream of meshes.
- **Gestures are exact.** Pointing is solved with inverse kinematics, so the arm aims at the target (within 3° in the
  tests), whether the coach points at a student or the teacher at the board. A raised hand is a hand above the head,
  not a sign.
- **Whoever writes, writes where the writing appears.** Each stroke has a time in a timetable the viewer shares; the
  writer's hand is placed on the stroke being drawn by inverse kinematics (within a centimetre on every stroke, in
  every lesson), crouching for the low lines. Board text uses a small built-in stroke font, revealed stroke by stroke.
- **Things change continuously, not in jumps.** Liquid colour creeps, drops fall, bubbles rise, the flame flickers,
  pendulums swing at their physical period (2π√(L/g), with the large-angle correction for the wide swing), the gauge
  needle falls, the laser pulses.
- **Who is speaking** has a ring under them; **what is being shown** glows softly; **a raised hand** gets a ✋ above it.
- **Sound:** the captions are read aloud, and effects (drips, bubbling, the burner, the pendulum's tick, the marker on
  the board, the pump, crackling sparks, rattling debris, the bang of an explosion) are synthesised in the browser and
  placed where they happen.
- **A safety banner** can accompany a lesson: in the hazard lab it says that nothing there can hurt anyone and that none
  of it should be tried in real life.

### How motion is streamed

People are still streamed as captures, frame by frame, like a real volumetric capture would be. Two things keep that
affordable in a class where everyone moves: **limb-preserving LOD** (the coarse levels keep a minimum number of points
on every hand, forearm and shin, instead of a uniform subsample that leaves a hand as three dots — which is exactly
where a gesture lives), and **shared captures** (twelve students doing the same jumping jack in step share one encoded
frame; each header carries where the person stands and which way they face).

A new *state* of an object (an empty flask becoming a pink one, a board with more writing) is new geometry the client
must receive. But the *motion within* a state — the swing, the drip, the colour creeping, the pen moving — is sent
once, as a handful of parameters with the geometry, and played by the client against the lesson clock. The network
never sends a new mesh per frame for any of it. For known, rule-driven motion this is far cheaper than streaming the
result, and it is the kind of semantic representation §1.5.7 asks for.

## Results: lessons

`results/lessons/lessons.md` has every policy at 3/10/25/50 Mbit/s. At 10 Mbit/s (typical home Wi-Fi):

| lesson | policy | overall QoE | quality of what's taught | …worst moments | clear view | detail ready after | ready before viewer turns |
|---|---|---|---|---|---|---|---|
| Heart (lecture) | adaptive | 0.867 | 0.919 | 0.47 | 0.841 | 1.56 s | 71% |
| | **lesson_aware** | 0.866 | **0.950** | **0.73** | **0.873** | **1.44 s** | 71% |
| Pendulum (physics) | adaptive | 0.850 | 0.954 | 0.73 | 0.811 | 1.65 s | 81% |
| | **lesson_aware** | 0.842 | **0.982** | **0.83** | **0.856** | **0.46 s** | **94%** |
| Titration (chemistry) | adaptive | 0.618 | 0.958 | 0.47 | 0.645 | 0.93 s | 88% |
| | **lesson_aware** | 0.613 | **0.978** | **0.73** | **0.695** | **0.64 s** | 88% |
| Pythagoras (maths) | adaptive | 0.871 | 0.889 | 0.47 | 0.841 | 2.38 s | 64% |
| | **lesson_aware** | 0.869 | **0.908** | **0.52** | **0.860** | **1.44 s** | 64% |
| Warm-up (PE) | adaptive | 0.717 | 0.677 | 0.47 | 0.677 | 8.33 s | 0% |
| | **lesson_aware** | 0.707 | **0.727** | **0.60** | **0.702** | **7.28 s** | **22%** |
| Too dangerous (hazard lab) | adaptive | 0.784 | 0.940 | 0.47 | 0.907 | 0.90 s | 88% |
| | **lesson_aware** | 0.783 | **0.965** | **0.73** | **0.932** | 0.90 s | 88% |

- **Quality of what's taught** — how well the objects the lesson is about are shown, while visible.
- **Clear view** — the same, but no better than the worst live participant standing in the line of sight.
- **Detail ready after** — seconds from a new step until its objects are at the detail the viewer needs.

What to take from this:

1. **What's being taught looks better, especially at the worst moments** (0.47 → 0.73 in the lecture and the
   titration), and detail is ready sooner. Overall quality costs little (−0.001 to −0.010).
2. **Each mechanism has its own operating range.** Switching them off one at a time (`lesson_ctx`,
   `lesson_ctx_occ`, full `lesson_aware`) shows:
   - **context weights** give nearly all of the "quality of what's taught" gain, at every speed;
   - **reading ahead** gives most of the "detail ready" gain where scenery is involved (pendulum 0.90 → 0.46 s,
     titration 0.93 → 0.64 s at 10 Mbit/s; pendulum 0.52 → 0.09 s at 25). In the hazard lab at 25 Mbit/s every
     explosion's new look is already there when it goes off (detail ready 0.90 → **0.03 s**, ready before the viewer
     turns 88% → **100%**);
   - **occluder protection** pays off from about 10 Mbit/s up. In the titration it lifts the clear view from 0.668 to
     0.695 at 10 Mbit/s and **from 0.780 to 0.836 at 25** (worst moments 0.47 → 0.73); in the lecture at 25 Mbit/s,
     0.891 → 0.926; in the hazard lab, where the class stands in a line in front of the benches, 0.949 → 0.970
     (worst moments 0.47 → 0.73). With articulated bodies the classmates in front move their arms, so a clean copy
     of them matters more than it did with rigid mannequins.
3. **The PE class is the hard case.** Thirteen people moving every limb at once, with almost no scenery: at
   10 Mbit/s nobody reaches full detail (hence 7–8 s "detail ready"), and following the plan is a trade — the people
   being demonstrated to or corrected look better (0.68 → 0.73, worst moments 0.47 → 0.60) while the overall score
   drops 0.01 and its worst moments drop (0.55 → 0.49), because bystanders pay. At 3 Mbit/s the link cannot carry even
   a rough copy of thirteen moving bodies 15 times a second; no policy fixes that.
4. **At very low bandwidth it's a trade, not a win.** In the crowded titration at 3 Mbit/s `adaptive` lets the
   *teacher* fall to 3.2 updates/s; `lesson_aware` keeps the speaking teacher at 13.8 and bystanders pay for it
   (participant lag 0.5 → 1.8 s). In the pendulum lesson at 3 Mbit/s, by contrast, the plan *cuts* participant lag
   (825 → 714 ms), because it knows which bench to serve.
5. **Prefetching new object states does not matter here.** State changes (the flask turning pink, new writing) are
   small objects that arrive within a few milliseconds even without the plan.

## Results: the five rooms

QoE · instructor updates per second, scripted walk, constant link (full tables in `results/summary.md`):

| room | policy | 3 Mbit/s | 10 Mbit/s | 25 Mbit/s | 50 Mbit/s |
|---|---|---|---|---|---|
| Lecture | full | 0.68 · 0.0 | 0.69 · 0.0 | 0.69 · 0.0 | 0.76 · 14.5 |
| Lecture | adaptive | 0.78 · 15.0 | 0.86 · 15.1 | 0.92 · 15.3 | 0.94 · 15.2 |
| Physics | full | 0.63 · 0.0 | 0.66 · 0.0 | 0.67 · 0.0 | 0.72 · 14.5 |
| Physics | adaptive | 0.74 · 14.3 | 0.85 · 15.0 | 0.92 · 15.2 | 0.94 · 15.0 |
| Chemistry | full | 0.43 · 0.0 | 0.44 · 0.0 | 0.46 · 0.0 | 0.56 · 14.6 |
| Chemistry | adaptive | 0.50 · 13.5 | 0.66 · 14.9 | 0.77 · 15.0 | 0.85 · 15.0 |
| PE class | full | 0.13 · 0.0 | 0.13 · 0.0 | 0.13 · 0.0 | 0.21 · 14.8 |
| PE class | adaptive | 0.35 · 13.9 | 0.61 · 15.1 | 0.76 · 15.2 | 0.85 · 15.2 |
| Hazard lab | full | 0.56 · 0.0 | 0.56 · 0.0 | 0.56 · 0.0 | 0.66 · 14.8 |
| Hazard lab | adaptive | 0.66 · 14.9 | 0.77 · 15.1 | 0.85 · 15.0 | 0.90 · 15.1 |

1. **Without rate control, live participants never arrive at all.** Every naive strategy starts transfers it can't finish before the next capture frame; most of their bytes are wasted and participants sit at 0 fps. In the viewer, switch to "Send everything at full quality" and everyone vanishes.
2. **Rate allocation is the big win, and it grows with how live the lesson is:** 1.25× in the lecture, **4.7×** in the PE class at 10 Mbit/s, where there is almost no static scenery to prop the score up.
3. **A fixed class-weight table is not safe.** Compared with the same allocator without class weights (`adaptive_geom`), fixed weights at 3 Mbit/s leave the worst moments (p05 QoE) about unchanged in three rooms (lecture 0.71 vs 0.71, physics 0.53 vs 0.55, PE 0.28 vs 0.28), but **nearly halve them in the chemistry lab (0.13 vs 0.24)**: where the crowd stands between the viewer and the demonstration, chasing the teacher starves it. That is the evidence that motivated the lesson-aware policy.

These numbers are lower than in earlier versions of this README because people are now articulated bodies that move
every limb (and are taller in the picture), not rigid mannequins; the comparisons between policies hold.

Congested link (10 Mbit/s nominal dropping to 2.5 every 6 s, 5% packet loss), QoE · instructor fps:

| room | full | adaptive_geom | adaptive |
|---|---|---|---|
| Lecture | 0.68 · 0.0 | 0.83 · 14.7 | 0.83 · 14.8 |
| Physics | 0.66 · 0.0 | 0.80 · 13.2 | 0.80 · 14.3 |
| Chemistry | 0.44 · 0.0 | 0.60 · 13.2 | 0.60 · 14.7 |
| PE class | 0.13 · 0.0 | 0.48 · 8.9 | 0.48 · 10.2 |
| Hazard lab | 0.56 · 0.0 | 0.73 · 13.1 | 0.73 · 14.9 |

## What it is built with

Everything is generated by code: there are no 3D models, textures, sound files or other assets.

| part | built with |
|---|---|
| Objects and rooms | Python and **NumPy**: parametric shapes (spheres, cylinders, boxes, tori) with per-vertex colour, each at several levels of detail |
| People | Python and NumPy: an articulated skeleton with inverse kinematics, sampled into a coloured point cloud like a volumetric capture |
| Board writing | a single-stroke font written for this project |
| Lessons | plain JSON files |
| Live server | **FastAPI** with **WebSockets**, run by **uvicorn**; data compressed with **zlib** (deflate) |
| Viewer | **three.js** (r128) in the browser; sound synthesised with the **Web Audio API**; narration by the browser's **speech synthesis** |
| Benchmarks and tests | **matplotlib** for the charts in `results/`, **pytest** for the tests |

## How it works

| piece | file | what it does |
|---|---|---|
| Rooms | `scenarios.py` | five rooms from parametric meshes (with LOD chains, per-vertex colour, animatable parts) and live point-cloud people |
| Bodies | `body.py` | articulated skeleton, exercises, gestures, inverse kinematics for pointing and writing, limb-preserving LOD, shared capture cache |
| Lessons | `lesson.py`, `lessons/*.json` | the plan: context at time *t*, choreography (walks, laps, exercises, gestures, facing, groups), board pages, the writer's hand on the pen, the viewer's camera |
| Board text | `font.py` | a single-stroke font; every pen segment is its own part, in writing order |
| Codec | `codec.py` | positions quantised to uint16 in each object's bounding box, colours and part ids, then deflate; real byte counts |
| Link | `link.py` | constant / sine / step / CSV-trace capacity, plus packet loss that forces a re-send |
| Policies | `policies.py` | `full`, `lod`, `roi`, `adaptive_geom`, `adaptive`, `lesson_aware` (+ ablations) |
| Session | `session.py` | budget accounting, delivery, pre-emption, all metrics |
| Quality | `quality.py` | the penalty model, shared by the metric and the allocator |

**The allocator** (`adaptive*` and `lesson_aware`): each capture frame, every visible participant first gets a rough
current copy (the *liveness floor*). The remaining budget is spent greedily on whichever single-level upgrade adds the
most quality per byte. Live updates may interrupt a scenery download, which later resumes from where it stopped.

**Quality model:** penalty is 0 when the viewer sees what they should and 1 when nothing usable is on screen. It
combines level of detail (capped at 0.8, so any copy beats none) and staleness for live participants (3 capture
periods late = 1). Each object is weighted by its class and its share of the viewport, floored so nothing visible
counts for zero. **QoE** = 1 − weighted mean penalty.

### Fixes along the way

- **Changing the speed or the strategy restarted the lesson:** now they take effect at once and the lesson carries on.
- **An empty strategy crashed the connection:** a page that sent its settings before the strategy menu had loaded
  dropped the connection; the server now ignores a strategy it doesn't know.
- **Gestures were fixed poses on a rigid mannequin:** a "raised hand" was hard to see, pointing went roughly in a
  direction, and people slid around without moving their legs or turning. Replaced by articulated bodies with IK.
- **Bodies first cost twice the bandwidth at low detail:** the limb floor added points, and per-point colour noise
  compressed badly, so at 3 Mbit/s the lecture's participants lagged 5 s. The floor now takes its points from the
  torso (same total per level) and capture colour is quantised to 32 levels; captures are back to their old size.
- **Scenery reservation leaked:** a quarter of the link stayed reserved for scenery "still loading", because distant
  objects never need full detail. Utilisation at 10 Mbit/s went from about 16 of 25 MB to about 23.
- **Liveness floor:** pure quality-per-byte greed froze bystanders to sharpen one boosted speaker.
- **Interruptible downloads:** prefetching one big model on a slow link held every live participant behind it.
- **Frustum planes were rebuilt for every object:** caching them per camera made a server tick 4–5× faster
  (10.6 → 2.3 ms in the lecture), and the test suite 3.6× faster.
- **Stale viewer in the browser cache:** the server now forbids caching the viewer's files, so an update can't leave
  an old page talking to a new server.
- **Occluders as capsules:** people are tested as upright capsules, not bounding spheres; a sphere cannot tell a head
  in the sight line from a sight line passing just over it.

## Known limitations (say these out loud)

- **Synthetic people.** Participants are articulated skeletons sampled into point clouds, not real captures: no
  clothing folds, no faces, no fingers. Exercises are clean textbook cycles; real students are messier.
- **Motion within a state is played by the client**, from parameters the plan provides. It is rule-driven animation,
  not a physics simulation (the pendulum periods do follow the formula). The fire, explosions and smoke in the hazard
  lab are made to look convincing, not simulated: sizes and timings are illustrative.
- **The viewer follows the plan.** The lesson camera turns to each step's focus (plus random unplanned glances). Real
  students don't always, and the read-ahead benefit assumes they mostly do.
- **Some metrics are mine.** "Clear view" was defined here and is exactly what occluder protection targets. Read it
  next to overall QoE and freshness, never instead of them. More generally, the adaptive policies optimise the same
  quality model the benchmark scores, which flatters them. An independent check (rendered image error against a
  full-quality reference) would be stronger.
- **Sound is not in the bandwidth model.** In a real system each voice would add roughly 24–64 kbit/s, small next to
  the geometry. Narration uses the browser's speech synthesis.
- LOD comes from sampling resolution and point subsampling, not real mesh decimation. One viewer per session. No
  Gaussian Splatting yet; the interfaces don't depend on the representation.

## Directions

1. **Plans from real lessons:** generate the plan (steps, focus, board text) from a teacher's slides or a transcript
   with an LLM, so no one has to write JSON.
2. **Learn the context weights** instead of setting `boosts` by hand; the ablation variants are ready-made baselines.
3. **Independent image-based quality measure**, so policies stop being graded by their own objective.
4. **Real captures** (e.g. 8i VFB point-cloud sequences) or a trained 3DGS scene.
5. **Multiple viewers** sharing one uplink, each following the lesson from a different seat.

## Layout

```
xrstream/
  geometry.py   parametric primitives, LOD by resolution, colours, part ids, surface point sampling
  font.py       single-stroke font for board writing
  body.py       articulated bodies (with VR headsets): skeleton, exercises, gestures, IK, limb-preserving LOD
  scene.py      SceneObject / LODLevel / SceneBuilder, states, animation specs
  scenarios.py  the five rooms, their objects' states and motion (fire, blasts, smoke), camera paths
  lesson.py     lesson plans: context, choreography, writing, viewer camera, occluders
  codec.py      quantisation + deflate, wire format
  camera.py     frustum culling, projected size, attention weighting
  quality.py    the penalty model shared by the metric and the allocator
  link.py       time-varying capacity and packet loss
  policies.py   full / lod / roi / adaptive_geom / adaptive / lesson_aware (+ ablations)
  session.py    delivery, budget, pre-emption, QoE and lesson metrics
  paths.py      camera trajectory interpolation
  bench.py      headless sweeps over rooms or lessons -> results/
  server.py     FastAPI + WebSocket live server: lesson autopilot, pause, jump, live speed/strategy changes
client/         three.js viewer: HUD, captions, animation player (incl. fire, blasts, smoke), sound, safety banner
lessons/        lesson plans (JSON) and how to write one
tests/          132 tests
results/        benchmark output: rooms, congested link, lessons
```
