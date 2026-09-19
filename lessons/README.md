# Writing a lesson

A lesson is a plain JSON file in this folder. You don't need to write code: add a file, and the lesson shows up in
the viewer's "Play a lesson" menu, in `python -m xrstream.bench --lesson all`, and in the tests.

## The lessons in this folder

| file | room | length | what it shows |
|---|---|---|---|
| `anatomy.json` | `classroom` | 52 s | the heart as a pump: drawing on the board, models around the room, a question at a valve |
| `pythagoras.json` | `classroom` | 82 s | a board-heavy maths lesson; a student comes to the board and solves an example |
| `pendulum.json` | `physics_lab` | 128 s | theory first, then four benches each change one thing about a pendulum |
| `titration.json` | `chemistry_lab` | 64 s | a demonstration watched over a crowd; the flask turns pink |
| `pe_warmup.json` | `gym` | 150 s | laps and eight exercises, each shown by the coach; a squat corrected |
| `hazard_lab.json` | `hazard_lab` | 130 s | experiments too dangerous for school: an explosion, thermite, a hydrogen fireball; shows a safety banner |

## How a lesson is written

A lesson is a list of **steps**. Each step says when it starts, who is speaking, what is being shown, a caption (shown
and read aloud), and what **happens**: people walk, exercise, gesture and write on the board, and objects change.
**Captions should describe what is on screen.**

```json
{
  "name": "titration",
  "title": "Acid-base titration (chemistry)",
  "scenario": "chemistry_lab",
  "summary": "One line on what makes this lesson hard to stream.",
  "duration": 64,
  "horizon_s": 4.0,
  "viewer": {"seat": [0.15, 2.1, 1.6], "glances_per_minute": 3, "seed": 3},
  "boosts": {"speaker": 2.0, "focus": 2.5, "occluder": 1.6},
  "phase_weights": {"demo": {"demo": 1.3, "apparatus": 1.3}, "question": {"student": 1.4}},
  "steps": [
    {"t": 0, "phase": "intro", "speaker": "instructor", "focus": ["annotation_layer"],
     "actions": {"instructor": {"write": "annotation_layer"}},
     "states": {"annotation_layer": "intro"},
     "caption": "The teacher writes today's reaction on the board."},
    {"t": 13, "phase": "demo", "speaker": "instructor", "focus": ["reaction_demo"],
     "actions": {"instructor": {"walk_to": [0.0, -4.15], "gesture": "point"}},
     "states": {"reaction_demo": "indicator"},
     "caption": "She pours the acid into the flask and adds indicator."},
    {"t": 27, "phase": "question", "speaker": "student_2", "focus": ["student_2"],
     "actions": {"student_2": {"gesture": "hand_up"}, "instructor": {"face": "student_2"}},
     "caption": "A student raises a hand."}
  ]
}
```

| field | meaning |
|---|---|
| `scenario` | which room: `classroom`, `physics_lab`, `chemistry_lab`, `gym`, `hazard_lab` |
| `steps[].t` | when the step starts, in seconds; each step lasts until the next one |
| `steps[].speaker` | who is talking (a name, a list, or a group) — they get a ring on the floor |
| `steps[].focus` | what is being shown or discussed; the viewer turns to look at it, and it glows softly |
| `steps[].phase` | a label; `phase_weights` can re-weight whole classes during that phase |
| `steps[].caption` | shown in the viewer and read aloud |
| `steps[].actions` | what people do (below); keys are names or groups |
| `steps[].states` | per object: the look it takes from this step on, e.g. `"reaction_demo": "endpoint"`; `"default"` resets it |
| `steps[].viewer_to` | optional: the watching student walks to this spot `[x, y, z]` |
| `viewer.seat` | where the watching student starts |
| `viewer.glances_per_minute` | how often they look away at something unplanned (no plan can predict these, which keeps the comparison honest) |
| `horizon_s` | how far ahead the `lesson_aware` policy reads the plan |
| `boosts` | how much the speaker, the focus, and people standing in the line of sight are prioritised |
| `board` | optional: the lesson's own board pages (below) |
| `banner` | optional: a note shown at the bottom of the screen for the whole lesson, e.g. a safety note: "Explosions here cannot hurt anyone. Never try this in real life." |

**Groups:** `@athletes`, `@students` (everyone of that kind), `@class` (everyone except the teacher), `@people` or
`@everyone`. A name listed after a group overrides it for that person:
`{"@people": {"exercise": "squats"}, "athlete_5": {"exercise": "squats", "depth": 0.45}}`.

## Actions

People are articulated bodies (a skeleton with arms, legs, torso and head), so every action is a real movement of the
limbs, blended in over about a third of a second.

| action | example | what happens |
|---|---|---|
| `walk_to` | `[x, z]` or `"home"` | walks there, legs and arms swinging, facing the way they walk |
| `run_to` | `[x, z]` or `"home"` | jogs there |
| `laps` | `{"centre": [0, 0.7], "radius": 6.5, "speed": 3.0}` | runs round a circle until their next move |
| `exercise` | `"jumping_jacks"`, with optional `"depth"` (0–1) and `"tempo"` | repeats an exercise until told otherwise; `"none"` stops |
| `gesture` | `"point"`, with optional `"at": name` | an arm gesture on top of whatever the body is doing; `"none"` lowers it |
| `face` | a name, `"viewer"`, or `"none"` | turns to face someone or something; `"none"` returns to normal |
| `write` | `"annotation_layer"` | walks to the board and writes this step's new board state, hand on the pen |

**Exercises:** `jumping_jacks`, `arm_circles`, `squats` (`depth` 0.45 is a lazy one, 1.0 thighs level), `lunges`
(alternating, hands on hips), `high_knees`, `toe_touch`, `side_stretch`, `reach_up`, `run`, `walk`, `idle`.

**Gestures:** `point` (at the step's first focus unless `at` names something: the body turns to it and the arm points
exactly there), `hand_up`, `wave`, `clap`, `whistle`, `hands_on_hips`, `shield` (flinching:
leaning back, forearms up in front of the face — for explosions).

**Facing**, without an explicit `face`: people face where they walk or run; someone pointing faces what they point at;
a writer faces the board; otherwise people face their default (students the board or bench, a class its coach).

## Writing on the board

A step with `"write"` must also set the board's new state in `states`. The writer walks to the first word, turns to
the board, and the board state starts at the first stroke (not at the step's time). Strokes then appear on a
timetable, with a short pause to move across to a new line, and the writer's hand is on every stroke as it appears
(the tests check it is within a centimetre). A block of text takes at most 7 s. The writer should have nothing else
to do until it is finished — the tests check that no writing runs into the writer's next move. Students can write
too.

### A lesson's own board

A lesson can bring its own pages under `"board"`. They become states of the room's board named `<lesson>:<state>`,
so lessons never collide; in `states` you use the short name. Pages accumulate: each shows everything so far.

```json
"board": [
  {"state": "title", "lines": ["PYTHAGORAS' THEOREM"]},
  {"state": "triangle",
   "draw": [[[-2.3, 1.2], [-0.8, 1.2], [-2.3, 1.85], [-2.3, 1.2]]],
   "labels": [["A", -2.52, 1.46], ["B", -1.62, 1.03], ["C", -1.45, 1.58]]},
  {"state": "formula", "lines": ["A^2 + B^2 = C^2"], "at": [-0.3, 1.75], "colour": "red"}
]
```

`lines` without `at` continue down the first column; `at` is `[x, y]` on the board, `y` being the height in metres
(keep writing between about 1.0 and 2.0 m, where a hand can reach). `height` is the letter height, `draw` takes
polylines, `labels` small letters, `colour` is `ink`, `red` or `green`. The font has capital letters, digits and
`+ - = ( ) . , : / < > * % ? ' ^ !`.

## What each room has

```bash
.venv/Scripts/python -c "from xrstream import scenarios; print([o.name for o in scenarios.build('gym')])"
```

| room | people | objects and their states |
|---|---|---|
| `classroom` | `instructor`, `student_0..5` | `annotation_layer`: `sketch` (heart chambers), `summary`; `instrument_N`: `valve` (a red flap opening one way) |
| `physics_lab` | `instructor`, `student_0a..3b` (pairs at four benches) | `pendulum_N`: `released` (swings at its real period; bench 1 reference, bench 2 a 40 cm string, bench 3 a heavy ball, bench 4 a wide swing); `scope_N`: `trace`; `optics_N`: `measuring`; `vacuum_rig`: `running`; `annotation_layer`: `formula`, `variables`, `predict`, `results`, `conclusion` |
| `chemistry_lab` | `instructor`, `student_0..7` (round the demo bench) | `reaction_demo`: `indicator`, `pinkish` (slowly turns pink), `endpoint`, `heated`; `burette_demo`: `dripping`; `condenser_demo`: `vapour`; `annotation_layer`: `intro`, `result` |
| `gym` | `instructor` (the coach), `athlete_0..11` in three rows of four | — (it is all movement) |
| `hazard_lab` | `instructor`, `student_0..7` in a line in front of three benches | `alkali_demo`: `lithium` (fizzes), `sodium` (orange flame, sparks), `potassium` (lilac flame, sparks), `caesium` (explodes: fireball, flying glass, spray, smoke); `thermite_demo`: `burning` (white-hot, spark fountain, smoke), `molten` (molten iron dripping), `cooled` (hole burned through the steel plate, glowing lump cooling); `balloon_demo`: `lit` (a flame on a pole), `boom` (hydrogen fireball) |

Object names, states, exercises and gestures must exist; binding a lesson fails loudly otherwise.

## How objects move

Everything an object does within a state — a pendulum swinging, drops falling, an explosion — is played by the viewer
from a few parameters sent once with the geometry, in step with the lesson clock. An explosion costs the network one
state change, not a stream of meshes. The motions a room can give its objects (defined in `xrstream/scenarios.py`):

| kind | what it does |
|---|---|
| `reveal` | board writing appears stroke by stroke, on the writer's timetable |
| `swing` | swings about a pivot (a pendulum), optionally slowly damped |
| `turn` | turns once to a new angle (a valve opening, a gauge needle falling) |
| `sweep` | parts light up one after another, over and over (an oscilloscope trace) |
| `fall` | drops falling, over and over (a burette dripping, molten iron) |
| `rise` | bubbles or smoke rising; with `grow` and `drift`, smoke swells and drifts |
| `flicker` | a flame flickering |
| `colour` | a colour changing gradually (a liquid turning pink, hot metal cooling) |
| `pulse` | a colour pulsing back and forth (a light gate, a white-hot core) |
| `burst` | particles thrown from a point under gravity, once (debris, spray) or over and over (a fountain of sparks); they land on the bench or the floor and can cool from white-hot to red |
| `blast` | a fireball: bursts out in a fraction of a second, rolls upwards, burns from white through orange to smoke and fades; the screen flashes, the view shakes, and there is a bang |
| `hot` | these parts glow (fire, sparks, molten metal); `unlit` draws them as light rather than as a surface |
| `soft` | these parts are see-through (smoke) |
| `light` | a light at the fire: flickering, or a flash that dies away |

## Trying a lesson out

Start the server, pick the lesson in **Play a lesson**, and click anywhere on its progress bar to jump to that moment
instead of watching from the start. After editing a lesson, restart the server. Then run the tests: they check every
lesson in this folder (names exist, writing fits in time, the writer's hand is on the pen).

```bash
.venv/Scripts/python -m pytest tests -q
```
