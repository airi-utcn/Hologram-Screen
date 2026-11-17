# ChemiNova showcase presentation

Edit `Resources/Cheminova/PresentationSettings.asset` in the Unity Inspector to change
the logo, caption, website, footer lines, colors, positions, sizes or line spacing.

The ChemiNova header and model pair are raised by contentVerticalOffset (0.35),
without changing other models or the physical floor. footerPosition.y is now 1,
before this same offset, so the footer and object-name line leave room for the
EU funding banner at the bottom. The banner uses Resources/EU_disclaimer.png,
centered at (0, 0.4, -4.8), width 4.9, with its original aspect ratio. Change
euDisclaimer, euDisclaimerPosition and euDisclaimerWidth to adjust it.
It renders Unlit after tone mapping to preserve image colors and disappears
when this presentation is hidden. It stays outside all model-fitting bounds;
its quad/material are cleaned up on scene unload. Restart Play to apply settings.

Footer separators can be `•` or `*`; they render as filled bullets with the height of
the font's lowercase `o`, centered vertically on each line.

An object-name line sits above the three footer lines, using the same text size
and burnt orange (#A63D00). It reads "Sisi's Shoe, Schönbrunn, Vienna, Austria" for
the shoe and "Palchetto Della Musica, Palermo, Italy" for the building. The label
follows actual model visibility through rotations, zoom transitions, interaction
pauses and object switching. It belongs to the footer, outside the model bounds.
Adjust shoeObjectName, buildingObjectName, objectNameColor, objectNameGap and
objectNameHeight in PresentationSettings. The default objectNameGap is -0.06,
which lowers only the object title by 0.2 stage units from its previous position.

`CheminovaBootstrap` subscribes to `SceneManager.sceneLoaded` before the first scene.
It runs after `ObjectSwitcher.Awake` creates the models, before `GesturePlayer.Start`
fits and grounds them. It includes inactive models and attaches one presentation
to the configured Shoe + Parchetto `RevealSequence`. Visibility follows that model.
The header/footer are children of the stage, outside the models' renderer bounds.
Subscriptions reset on Play entry, including when domain reload is disabled.

The height profile preserves the previous change: the listed Resources model
instances use height offset 2; the three authored Character/Man/Woman models keep
their serialized height 1. Combined model names are matched only if every member
is listed. Add newly introduced showcase Resources paths to the height profile
if they should also use height 2. Only scenes listed in `targetScenes` are affected.
Saved F5 poses, shared depth and animation controls continue through GesturePlayer.

GesturePlayer exposes generic HeightOffset, interaction notifications, and
CapturePoseRestorer. Its original gestures remain the source of all manipulation.
ObjectSwitcher, Character.unity, RevealSequence and VQ1Hologram need no ChemiNova
presentation changes.
There is no assembly definition because the adapter references the existing
Assembly-CSharp showcase classes.

After changing assets while Play is running, stop and restart Play. Verify:

1. Logo width 1.9, caption, blue website, and three footer lines match the prior view.
2. Footer text/bullets remain doubled, with half line spacing and centered bullets.
3. Switching to another model or a StoryDirector spot hides the presentation.
4. Returning to Shoe + Parchetto shows exactly one header/footer.
5. TAB away and back restores the presentation and height profile without duplicates.
6. Character/Man/Woman remain at height 1; Resources models retain height 2,
   with the additional contentVerticalOffset applied only to the Shoe/Palchetto pair.

The alternating showcase is enabled in PresentationSettings. The unchanged
RevealSequence builds the original hierarchy, then CheminovaShowcaseLoop disables
its animation at runtime. GesturePlayer fits the original full-size pair first.
The new controller runs afterward: shoe at 5x, three rotations, 1.5-second zoom-out,
building at its original scale, two rotations, zoom-out, then repeat indefinitely.
Only one model is visible at a time; both rotate around their own centers.
Switching away restores the original bounds so returning does not change grounding.
The loop owns automatic rotation; GesturePlayer still handles saved pose/depth/touch.

Set alternatingShowcase to false and restart Play to use the original reveal again.
After each zoom-out, the next model zooms in from near-zero to full size before
starting its rotations. Both zoom directions last 1.5 seconds by default.
The initial shoe still starts at full 5x size; later appearances use zoom-in.
Adjust shoeScaleMultiplier, shoeTurns (shoe only), turnsPerModel (building),
turnSpeed, zoomOutSeconds and zoomInSeconds
in the asset.

The shoe uses HDRP/Unlit while shoeUnlit is enabled (the default). The bootstrap
attaches CheminovaShoeMaterial only to the instantiated shoe. It copies the base
texture, texture scale/offset and tint from the imported material into a runtime
material with no emissive contribution or light-dependent/specular shading.
The glTF, source textures, building and other models are not edited. Original
material assignments are restored and runtime copies released on destruction.
Disable shoeUnlit in PresentationSettings and restart Play to use the original PBR
material again. The loop and zoom transitions are independent of this option.

The shoe starts each appearance with its original albedo. Halfway through its
three rotations (after 1.5 turns, 6 seconds at 90 degrees/second), it switches to
Chemi-AU-O005-diffuse-RTI, assigned as shoeAlternateAlbedo in PresentationSettings.
It keeps that texture through zoom-out and restores the original before the next
shoe zoom-in. Interaction pauses the clock and therefore also delays this switch.
Only the albedo texture reference is changed: tint, UV scale/offset and all other
runtime material settings stay unchanged. When shoeUnlit is disabled, the runtime
material is a full copy of the source, including normal/occlusion maps and shader
settings. In the existing Unlit mode those lighting maps remain unused as before.
Source materials and texture import settings are never modified. A separate copy
of the RTI image with its own GUID is included inside this module for deployment.

## Temporary interaction and automatic restoration

Existing drag, pinch, mouse wheel and object-transform keyboard controls pause the
showcase clock before the input changes the object. No new gesture or key is added.
The original root pose (translation, quaternion rotation and scale), both model
pivots, GesturePlayer's depth/height/lift state and shared depth preference are
captured in memory. Latest local/world TRS coordinates of both models and the root
are also kept while interacting. Existing F5 saving remains an explicit user action;
this feature does not write object_configs.json automatically.

Holding a finger, primary mouse button or transform key keeps the clock paused.
After interactionResumeSeconds (default 3) of inactivity, measured in unscaled
real time, the original transforms/control state are restored and the same model
resumes the same rotation/zoom phase. The timeout is restarted by each interaction.
Switching away restores the pose immediately and clears the temporary session.

Verify drag/pinch/wheel and Q/E, W/S, Ctrl+arrows on both Shoe and Parchetto; wait 10
seconds after the last input, including when inspecting during a zoom transition.
Repeat interaction before the timeout, hold still for longer than 3 seconds, and
switch models/TAB away during interaction. Later depth/scale controls should start
from the restored pose; no shared-depth offset should leak to the other models.
## Soft floor shadows

CheminovaFloorShadow draws a procedural, feathered oval underneath each visible
model. Default opacity is 0.12, reduced further with height and zoom scale. The
shadow follows position and current footprint during rotation, resizing, temporary
interaction and pose restoration. Hidden models have no visible shadow.

The floor is detected with downward physics queries; the model's own colliders
and non-horizontal surfaces are excluded. Shadow quads have no collider and belong
to the stage, outside GesturePlayer's fitting/grounding bounds. A small surface
offset avoids z-fighting. HDRP transparent Unlit rendering after tone mapping keeps
the shadow faint on the very bright floor while retaining depth testing.

Detailed light-cast shadows are disabled only on the two runtime model instances
while the generic shadow component exists, and restored on destruction. No global
light, imported model, source material, floor prefab or other scene object is edited.
Runtime mesh, texture and materials are cleaned up on scene unload.

Adjust floorShadows, floorShadowOpacity, floorShadowSize and floorShadowLift in
PresentationSettings, then restart Play. Check both models, zoom transitions,
manual movement/resize, three-second restoration, object switching and TAB reload.
