# Hologram Screen

A Unity (HDRP) showcase app for the **Looking Glass Hololuminescent Display (HLD)**. It shows 3D models one at a time inside the display's holographic alcove, with spin, touch and keyboard controls, and animations.

It is built on Looking Glass's [HLD-Templates](https://github.com/Looking-Glass/HLD-Templates/tree/feat/realtime) (the HDRP project). The HLD docs are at [hlddocs.lookingglassfactory.com](https://hlddocs.lookingglassfactory.com/).

## How the display works (read this first)

- The HLD has a **real, physical 3D alcove** built into the screen. What Unity draws is one flat layer in front of it.
- **Pure white = invisible** (you see the physical alcove through it). Colours and dark tones are what you see. That is why the background is pure white; grey shows up as a dull wall.
- Objects get their depth from cues: floor shadows, the mirror reflection, side lighting, spinning, and the camera's slow sway.
- The screen is used as an extra monitor in **portrait** (this unit: 2160 x 3840).

## Setting up the repo

### What you need
- **Windows 11** (or macOS 15+).
- **Unity 6000.6.4f1** with **Windows Build Support** (installed with Unity Hub). No Android support needed.
- **Git** and **Git LFS**. All models, textures and other files over 50 KB are stored in Git LFS.
- A good GPU (Looking Glass recommends an RTX 3060 or better). HDRP is demanding.

### Steps
1. Install Git LFS once per computer, **before cloning**:
   ```bash
   git lfs install
   ```
2. Clone the repo:
   ```bash
   git clone https://github.com/airi-utcn/Hologram-Screen.git
   ```
3. Check the models came down as real files and not small text placeholders:
   ```bash
   git lfs pull
   ```
   If you cloned before installing Git LFS, this command also repairs it.
4. Open **Unity Hub → Add → Add project from disk**, pick the cloned `Hologram-Screen` folder, and open it with **6000.6.4f1**. The first import takes several minutes.
   Packages (HDRP, Input System, glTFast for `.glb` files) install by themselves from `Packages/manifest.json`.
5. Open the scene **`Assets/Hololuminescent/Character.unity`**.
6. Press **Play** to try it in the editor.

### Running it on the HLD
- **From the editor:** connect the HLD (HDMI/DisplayPort, set to portrait in Windows display settings), then open **Window → Hololuminescent Display Preview Settings**. It finds the portrait display and shows the Game view there. If it picks the wrong screen, change **Target Monitor**.
- **As an app:** **File → Build and Run**. When the app starts, a small menu lists the monitors; pick the HLD (tick **Portrait Displays Only** to shorten the list). **Ctrl + E** opens that menu again.
- **Touch** only works if Windows sees the HLD as a touch screen (its USB cable must be connected too).

## Controls

Press **H** in the app to show them on screen.

| Key / gesture | Does |
|---|---|
| Tab | next experience: model showcase ↔ asteroid orbits (VQ1 / VN3) |
| ← / → | previous / next object |
| Drag (finger or mouse) | turn (sideways) / tilt (up-down) |
| Pinch / mouse wheel | resize (no upper limit) |
| ↑ / ↓ | tilt |
| Q / E | turn |
| W / S | push back / pull forward in depth (saved) |
| Ctrl + ← / → / ↑ / ↓ | move the object left / right / up / down |
| F5 | save this object's look to `object_configs.json` (see below) |
| F8 | forget this object's saved look (defaults again at next start) |
| Alt + any key above | steps ten times smaller (move, turn, depth, wheel size, speed, sway, perspective, alignment) |
| R | auto-spin on / off |
| A | spin axis: Y (upright turntable) → X (tumbles toward you) → Z (wheel facing you) |
| 1–9 | play animation 1–9 (it loops) |
| Space | pause / play the animation |
| − / + / Backspace | slower / faster / normal speed |
| D | camera sway (depth effect) on / off |
| [ / ] | less / more sway |
| O / P | less / more perspective |
| Shift + ↑ / ↓ | move the whole view up / down to line up with the physical floor (saved) |
| Shift + PgUp / PgDn | zoom (saved) |
| Shift + Backspace | reset the view alignment |
| Ctrl + E | monitor menu |

## Saving each object's look

Every model gets a size, place and angle automatically. To fix one by hand:
1. Show it (← / →). Stop the spin with **R** if you want a fixed angle.
2. Set it up: **Ctrl + arrows** to move it, **W / S** for depth, pinch / wheel for size, drag or **↑ ↓ Q E** for the angle, **A** for the spin axis, **1–9** for the animation it should start with, **− / +** for its speed, **Space** to stop it on an exact frame, **D**, **[ ]** and **O / P** for its view. Hold **Alt** for fine steps.
   The **H** box shows the object's exact values live (position, lift, angle, size, spin, animation, frame, view).
3. Press **F5**. The app saves that object and shows where.

At the next start, that object comes up exactly like that instead of the automatic defaults. **F8** forgets it.

Everything goes into **`Assets/StreamingAssets/object_configs.json`**, one entry per object, keyed by its name (in a build: `<App>_Data/StreamingAssets/object_configs.json`). Commit it to share the setup. You can also edit it by hand while the app is closed:

| Field | Meaning |
|---|---|
| `name` | object name (the model's file name; the shoe + temple is `Chemi-AU-O005 + Chemi-IT-B001`) |
| `position` | where it stands (world units). `y` is only for reference, the height comes from `lift` |
| `lift` | height above the default hover height (Ctrl + ↑ / ↓) |
| `rotation` | angle in degrees (x = tilt, y = turn, z = roll) |
| `scale` | size |
| `spin`, `spinAxis`, `spinSpeed` | turntable on / off, `Y` / `X` / `Z`, degrees per second |
| `animation`, `animationSpeed` | animation it starts with (1–9, 0 = none) and its speed (1 = normal, steps of 0.01) |
| `paused`, `animationTime` | stopped with Space, on this frame (seconds into the animation) |
| `sway`, `swayAngle`, `perspective` | the view while this object shows (D, [ ], O / P). `perspective` 0 = use the shared view |

The display alignment (**Shift** + ↑ ↓ / PgUp PgDn) is not saved per object: it lines the image up with the physical alcove, so it stays one setting for all. A saved object keeps its own depth; **W / S** on it no longer changes the shared depth of the others. If the file can't be read (for example a typo after editing it by hand), the app ignores it, says so in the Console and doesn't overwrite it.

## Project layout

| Path | What it is |
|---|---|
| `Assets/Hololuminescent/Character.unity` | The app's only scene |
| `Assets/Hololuminescent/Alcove/` | Looking Glass's alcove: camera framed to the display, mirror floor, white backdrop, lights, safe-area box (Boundary Helper) |
| `Assets/Hololuminescent/Scripts/` | Our scripts (see below) |
| `Assets/Hololuminescent/Resources/Models/<Name>/` | **Every model the app shows**, each in its own folder with its textures and materials |
| `Assets/Hololuminescent/Windowing/` | Looking Glass's monitor picker |
| `Assets/VQ1Hologram/` | Second experience (from CGIS): orbits of asteroids 2018 VQ1 / VN3, scene `Generated/VQ1_Hologram.unity`, its own keys (H shows them). **Tab** switches to it and back |
| `SourceModels/` | Original downloads (`.blend`, `.zip`, source textures). Outside `Assets`, so Unity ignores them |

**Scripts**
- `ObjectSwitcher.cs` (on the **Showcase** object in the scene): the list of objects and the ← / → switching. Models listed in **Resource Models** are loaded when the app starts.
- `GesturePlayer.cs` (added to every object): sizing, placing it above the floor, start angle, spin, touch, depth, animations, and the optional heartbeat pulse.
- `DisplayAlignment.cs` (on **Showcase**): the camera: alignment, zoom, sway, perspective, and the help overlay.

## Adding a new model

### 1. Get it into a format Unity reads

| Format | What to do |
|---|---|
| `.fbx`, `.obj` | Nothing, Unity imports them directly. |
| `.glb` / `.gltf` | Nothing, the glTFast package imports them. |
| `.blend` (Blender) | Convert to FBX first (see below). Unity can't read `.blend` without Blender installed. |
| `.zip` download | Unzip it and use the model file inside. |

**Converting a `.blend` to FBX:** open it in [Blender](https://www.blender.org/download/) → **File → Export → FBX**. Tick **Apply Modifiers**, and under **Bake Animation** keep it on if the model moves. Set **Path Mode** to **Copy** and click the small box icon next to it (**Embed Textures**) so the textures come along. Leave out cameras and lights.

### 2. Put it in its own folder
Create `Assets/Hololuminescent/Resources/Models/<Name>/` and copy the model and its textures in. Keep **one folder per model**, and keep the original download in `SourceModels/<Name>/`.

> Copy files in with Unity **closed**, or in the Unity Project window. If you move files around in Explorer while Unity is open, always move the `.meta` file next to each file with it, or Unity loses the links between materials and textures.

### 3. Set up its animations (skip if it doesn't move)
Select the model in the Project window. In the Inspector:
- **FBX:** **Rig** tab → **Animation Type: Legacy** → Apply. The app then plays every animation in the file, and 1–9 pick between them.
- **glb:** set **Animation Method: Legacy** → Apply.

If the file has no animation at all but should pulse (like the heart), add its path to **Heartbeat Models** in step 5 instead.

### 4. Fix its materials (if it shows up white, grey or pink)
HDRP often can't use a downloaded model's materials as they are.
1. Select the model → **Materials** tab.
2. If the textures are inside the file, click **Extract Textures** and pick the model's `textures` folder.
3. For each part, create a material: right-click in the folder → **Create → Material**, set its shader to **HDRP/Lit**.
   - **Base Map:** the colour texture (often called `color`, `diffuse`, `albedo` or `basecolor`).
   - **Normal Map:** the normal texture. If Unity offers **Fix Now** (mark it as a normal map), accept.
   - Shine: set **Smoothness** by hand, or use a **Mask Map** (Red = metallic, Green = ambient occlusion, Alpha = smoothness).
   - See-through leaves / clouds: **Alpha Clipping** on.
4. Back on the model's **Materials** tab, under **Remapped Materials**, drag each new material onto the matching slot → **Apply**.

Easy shortcut: copy a material from another model's folder (for example `Robot/robot.mat`) and swap its textures.

### 5. Add it to the app
1. Open `Character.unity` and select **Showcase** in the Hierarchy.
2. On **Object Switcher**, add a line to **Resource Models** with the path inside `Resources`, **without the file extension**:
   `Models/<Name>/<file name>`, for example `Models/Stallion/Stallion`.
3. Optional: add the same path to **Heartbeat Models** for a beating pulse.
4. Optional: add it to **Still Models** to have it start standing still; **R** then spins it around **Still Models Axis** (now Z, used for the AIRI logo).
5. Optional: add it to **Upright Models** for logos and text. It starts straight, facing the viewer (no three-quarter turn or tilt), and only ever turns around Y. Tilting and **A** are ignored for it. Used for `Models/AIRI_Intro/AIRI_Intro` and `Models/UTCN/UTCN`. **Upright Models Shift X** nudges them all sideways after centring (now -0.02); for one of them only, move it with Ctrl + arrows and save with F5.
   **AIRI ↔ UTCN:** the two logos show as one object that swaps between them in a loop: each floats and sways evenly left and right of facing you (±12°) for 5 s, then whips round, shrinks and flies off into the depth while the other comes out of the depth, spins down and settles. Timings (hold, swap, sway, depth, turns) are on the **Logo Duet** component of the `AIRI_Intro + UTCN` object; the pair is set under **AIRI <-> UTCN** on Object Switcher. Clear **Duet First** to show the logos separately again. The logos keep their shape from the files; their depth comes from a **logo light** in front of and above them, which casts each logo's whole outline onto the floor behind it (the alcove's own lights meet a flat upright logo edge-on, so their shadow is only a line). While the logos show it is the main light and the alcove's overhead spot is dimmed to 25% (**Alcove Spot While Shown**), because the floor is lit past white and a shadow only shows where the remaining light falls below white. Other objects get the normal lighting back. Its position and strength are on **Logo Duet** (Light Up, Light Toward, Light Strength). The alcove lights' shadows are 2048 px for sharp floor shadows. The logos stand upright and centred, and the sway swings them evenly to both sides of facing you, so their sides show (**Sway** 12°, **Rest Yaw** 0, **Rest Pitch** 0).

   **UTCN logo source:** built by `blender/utcn_sigla.py` (branch `oct_hologram`) from the official SVG. The copy in `SourceModels/UTCN/` uses 24 curve points for the small line and 16 for the big text (was 4 and 6, which made C and O look like polygons). To rebuild it: `blender -b --factory-startup -P blender/utcn_sigla.py` on that branch, with those two numbers changed, then copy `utcn_sigla.fbx` over `Resources/Models/UTCN/UTCN.fbx`.
   **Shoe, then building:** the shoe (`Shoe`) and the temple (`Parchetto`) show as one object, as in the CGIS colleague's scene. The shoe turns twice alone, then the temple grows in behind it, with the shoe standing enlarged on the temple's base in front of the entrance columns, and then the set turns. Settings are under **Shoe, then building** on Object Switcher (spot, size, turn of the shoe). Clear **Reveal Front** to show them separately again.
6. Optional: put the path in **First Model** to show it when the app starts (now: `Models/AIRI_Intro/AIRI_Intro`, the AIRI intro logo).
7. Save the scene (**Ctrl + S**).

The app scales every model to the same height (**Resource Fit Height**, 3.5), so its original size doesn't matter, and it faces the camera at a three-quarter angle.

### 6. Check it
Press **Play** and use **→** until it shows.
- **Too big or touching the ceiling:** lower **Resource Fit Height**. It should stay inside the light-blue Boundary Helper box.
- **Too wide (crosses the alcove's sides):** lower **Max Width** on GesturePlayer.
- **Textures in the wrong place or blurry (glb):** the model may use a UV set above 1, which glTFast can't read. It needs converting so its textures use UV set 0.
- **Lying flat on the floor:** it was modelled lying down in Blender. Stand it up there (rotate 90° on X, **Ctrl + A → Rotation**) and export again. For `AIRI_Intro.fbx`, the root's -90° X turn in the file was set to 0 instead, which stands it up facing the viewer.
- **Nothing shows:** the Console says `ObjectSwitcher: nothing at Resources/...` when the path is wrong. Check the spelling and that the file is inside `Resources`.

### 7. Commit it
Model and texture files go to Git LFS automatically (see `.gitattributes`: models, images, audio, video, fonts and archives, in any letter case).
```bash
git add -A
```
```bash
git commit -m "Add <Name> model"
```
```bash
git push
```
To check a file went to LFS: `git lfs ls-files | grep <Name>`.

**Rule of thumb:** every file over 50 KB should be in LFS. A new file type that isn't in `.gitattributes` yet (or a Unity text file such as a `.unity` or `.asset` that grows past 50 KB) needs a line added there first, for example:
```bash
git lfs track "*.newtype"
```

## Tips from Looking Glass's guidelines
- Keep objects **inside the alcove** (the Boundary Helper box) and don't let them cut into the ceiling or sides.
- **Shadows on the floor only**, soft, never on the side walls.
- Keep reflections subtle.
- **Motion helps** the 3D effect most: a 360° spin or orbit.
