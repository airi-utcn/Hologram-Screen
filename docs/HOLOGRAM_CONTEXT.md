# Looking Glass HLD + Unity project: context for a new conversation

## The hardware: Looking Glass Hololuminescent Display (HLD)
- **How it works:** a "fixed 3D holographic volume embedded in a 2D display" (Looking Glass's own wording).
  - **The box is real depth.** The empty alcove you see is built into the optics and never changes.
  - **Your content is flat.** Anything the app draws is ONE flat layer at a fixed depth (the panel, near the front). There is no stereo, no multiple views and no head tracking.
  - **Consequence:** the box looks deep and objects look like flat "paper" in front of it. Software cannot give the objects real depth, only depth cues.
- **How it shows your image:** it blends the image roughly like a multiply layer.
  - **Pure white** = invisible / see-through to the physical alcove.
  - **Colours and dark tones** = visible objects.
  - The background must be **pure white**. Grey shows up as a dull wall.
- **Connection:** HDMI/DisplayPort from the PC, used as an extended monitor in **portrait**. 16" = 1080x1920; 27"/86" = 2160x3840. This unit runs at 2160x3840 portrait (Monitor 2).
- **Touch:** works only if Windows sees the display as a touchscreen (USB connected too). Not confirmed on this unit.
- **Content guidelines** (hlddocs.lookingglassfactory.com):
  - Keep the subject inside the alcove safe area.
  - Use soft shadows on the floor only, never on the side walls.
  - Use subtle floor reflections.
  - Use 360° rotation / orbit motion.
  - Solid background.
  - Don't render the alcove walls.
- **Contact:** developer@lookingglassfactory.com. The physical alcove depth and the ideal camera values are not published.

## The project
- **Repo:** https://github.com/Looking-Glass/HLD-Templates, branch `feat/realtime`, cloned at `C:\Users\Deea\hologram\HLD-Templates`. Nothing is committed; all work is local changes.
- **Unity:** 6000.6.4f1 (the templates were made with 6000.3; upgraded fine). Input System (new) is active.
- **Two Unity templates:**
  - `Unity/Hololuminescent`: **HDRP**. **This is the one in use.**
  - `Unity/Hololuminescent-URP`: URP. It got earlier versions of the work, but it is **out of date**; don't use it as the reference.
- **Scene:** `Assets/Hololuminescent/Character.unity` (the only scene in the build).
- **Alcove prefab:** `Assets/Hololuminescent/Alcove/Alcove.prefab`. It contains the camera (15° FOV, about 22 units away), the Mirror floor (planar reflection, has a collider), Ledge, Ceiling, the white unlit Backdrop, a directional light and a spot light, plus a Global Volume.
  - **The camera is NOT tagged MainCamera.** Scripts fall back to `FindAnyObjectByType<Camera>()`.
- **Build:** File → Build and Run → `Unity/Hololuminescent/build/Hololuminescent.exe`. On launch a monitor-picker dialog appears; pick the HLD (Ctrl+E reopens it).

## What was built (HDRP project)
All scripts are in `Assets/Hololuminescent/Assets/Character/`.

| Script | Where | Does |
|---|---|---|
| `GesturePlayer.cs` | on each object | auto-fit size (fitHeight 3.5, maxWidth 3.2), hover `heightOffset` (1) above the floor (raycast), start depth (`defaultDepth` -1.5 = toward the front ledge), start angle (pitch 15°, yaw 30°), spin on its own axis, touch/mouse rotate and pinch (no max size), animations (Playables for clip lists, or the model's legacy Animation clips), pause, speed |
| `ObjectSwitcher.cs` | on `Showcase` | shows one object at a time (←/→); scene objects in `objects`, plus `resourceModels` loaded from `Resources` at runtime (they get a GesturePlayer automatically) |
| `DisplayAlignment.cs` | on `Showcase` | owns the camera: alignment offset and zoom (saved in PlayerPrefs), help overlay (OnGUI), depth sway (figure-8 yaw+pitch orbit), perspective control (wider FOV + dolly, saved; default 1×) |
| `ContactShadow.cs` | URP only | blob shadow; it uses the URP Unlit shader, so it is not used in HDRP |

**Objects (← / → order):**
1. **robot:** FBX, 4 split moves plus the full take.
2. **Man:** Head Gesture.fbx, Ready Player Me avatar.
3. **Engine:** airplane engine FBX, 2 takes.
4. **Stallion:** `.glb` via the **glTFast 6.20.0** package; 3 animations, Legacy import.
5. **Globe:** OBJ.
6. **Tesseract**, 7. **Heart**, 8. **PlanetSystem:** converted from `.blend` to FBX with Blender (modifiers applied, animation baked to one "Scene" take, packed textures extracted, cameras/lights left out), Legacy animation.

Models 4–8 live in `Assets/Hololuminescent/Resources/Models/<Name>/`.

**Materials:** every model has hand-made HDRP Lit materials, cloned from the template's `Mirror.mat`.
- **Mask maps** are packed R = metallic, G = AO, A = 1 − roughness.
- **Clouds** use alpha cutout.
- Each model's `.fbx.meta` remaps its materials via `externalObjects`.

**Scene / settings changes:**
- **Tonemapping off and vignette off** in the alcove volume profile (`Assets/OutdoorsScene/Global Volume Profile 1.asset`). Otherwise white renders grey.
- **Contact shadows** length 0.6 and **AO** intensity 1 in the same profile.
- **Key light** rotated 55° to the side, with contact shadows turned on.
- **Build list path** fixed.
- **Monitor-selection auto-start / TMP font fix:** done for URP (the HDRP scene already had the bootstrap object).

## Controls (H shows them in the app)
| Key / gesture | Does |
|---|---|
| ← / → | switch object |
| Drag (finger/mouse) | turn (sideways) / tilt (up-down) |
| Pinch / wheel | resize, no upper limit |
| ↑ / ↓ | tilt |
| Q / E | turn |
| W / S | push back / pull forward in depth (saved, shared by all objects) |
| 1–9 | pick animation (loops) |
| Space | pause / play animation |
| − / + , Backspace | slower / faster, normal speed |
| R | auto-spin on/off |
| D | depth sway on/off |
| [ / ] | sway strength |
| O / P | less / more perspective |
| Shift + ↑/↓ | move the whole view to line up with the physical floor (saved) |
| Shift + PgUp/PgDn | zoom (saved) |
| Shift + Backspace | reset alignment |
| Ctrl + E | display picker |

## Where it stands / open problems
- **Main open problem:** the user still finds the objects too flat against the real depth of the box. This is the hardware limit described above.
- **Tried so far:**
  - white background
  - grounding plus contact shadows and AO
  - side lighting
  - depth sway
  - perspective control (made things worse at 2×; back to 1× because the template lens matches the physical box)
  - placing objects near the front ledge
  - bigger objects
  - starting at a three-quarter angle
- **Ideas not done:**
  - **Webcam head tracking** (Unity Inference Engine + BlazeFace, off-axis projection). This is the only way to get real parallax, but it needs a camera and works for one viewer. The user wanted no extra hardware.
  - **Emissive glow** on the tesseract's red inside (currently plain red).
  - **Soft cloud edges** (currently alpha cutout).
- **Not verified:**
  - most of the recent changes have not been seen on the real display
  - touch on the actual HLD
  - how the engine pivot behaves when spinning on its own axis
- **Blender:** a portable copy (5.2.3 LTS, checksum verified) was in a temporary folder that may be gone. To convert more `.blend` files, download it again from download.blender.org and run it headless with `--factory-startup --disable-autoexec`.
- **Adding a new model:** put it in `Resources/Models/<Name>/` and add `Models/<Name>/<file-without-extension>` to Showcase → ObjectSwitcher → Resource Models.
  - FBX/OBJ import natively; `.glb` needs glTFast (installed); `.blend` needs conversion.
  - Materials usually need hand-made HDRP Lit versions (write the `.meta` remap before Unity first imports the file).
