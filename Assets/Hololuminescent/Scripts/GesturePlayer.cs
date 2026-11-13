using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Plays gesture clips on the character, no Animator Controller needed.
/// Keys 1-9 pick a gesture, which then loops until another is picked; Space pauses / resumes it.
/// Minus/Plus slow down/speed up the animation, Backspace resets speed.
/// Also spins the character turntable-style so it is seen from all sides; R toggles the spin.
/// Touch: one finger drag rotates (sideways turns, up/down tilts), two finger pinch resizes (mouse: left drag / scroll wheel).
/// Keys: Up/Down tilt, Q/E turn, W/S push back / pull forward in depth (saved, shared by all objects).
/// To add gestures, drop more clips into the Gestures list in the Inspector.
/// With an empty list, the model's own legacy Animation clips are used (e.g. glTF models imported by glTFast).
/// </summary>
[RequireComponent(typeof(Animator))]
public class GesturePlayer : MonoBehaviour
{
    [SerializeField] private AnimationClip[] gestures;
    [Tooltip("Off: the picked gesture loops forever. On: moves to the next gesture by itself.")]
    [SerializeField] private bool autoCycle = false;
    [Tooltip("How many times each gesture repeats before auto-cycling to the next one.")]
    [SerializeField] private int repeatsPerGesture = 2;
    [Tooltip("Animation playback speed. 1 = original, 0.5 = half speed.")]
    [Range(0.1f, 2f)]
    [SerializeField] private float speed = 1f;

    [Tooltip("Scales the model to this height (world units) and stands it on the floor at start. 0 = keep as placed.")]
    [SerializeField] private float fitHeight = 0f;
    [Tooltip("Widest the model may get at start while spinning (pinching can go past it).")]
    [SerializeField] private float maxWidth = 3.2f;
    [Tooltip("How far above the alcove floor the model hovers (world units). 0 = standing on the floor.")]
    [SerializeField] private float heightOffset = 1f;
    [Tooltip("Start depth from the placed spot: negative = toward the viewer / front ledge, positive = toward the background. " +
             "W/S change it while running; the choice is saved and shared by all objects.")]
    [SerializeField] private float defaultDepth = -1.5f;
    [SerializeField] private float depthSpeed = 1.5f;

    [Header("Starting angle")]
    [Tooltip("Tilt at start, degrees; the top leans back so a bit of it shows, and the object reads as a solid, not a silhouette.")]
    [SerializeField] private float startPitch = 15f;
    [Tooltip("Turn at start, degrees: a three-quarter view shows two sides at once.")]
    [SerializeField] private float startYaw = 30f;
    [Tooltip("Starts straight, facing the viewer (no start turn or tilt), and only ever turns around Y: no tilting, A is ignored. For logos and text.")]
    [SerializeField] private bool uprightOnly = false;

    [Header("Turntable")]
    [SerializeField] private bool spin = true;
    [Tooltip("Y = upright turntable, X = tumbles toward / away from the viewer, Z = turns like a wheel facing the viewer. A cycles it.")]
    [SerializeField] private SpinAxis spinAxis = SpinAxis.Y;
    [Tooltip("Degrees per second. 30 = one full turn every 12 seconds.")]
    [SerializeField] private float spinSpeed = 30f;

    [Tooltip("Degrees per second while Up/Down (tilt) or Q/E (turn) are held.")]
    [SerializeField] private float keyTurnSpeed = 60f;

    [Header("Heartbeat")]
    [Tooltip("Beats per minute of a code-driven lub-dub pulse, for models with no animation of their own. 0 = off. Space pauses it, -/+ change its speed.")]
    [SerializeField] private float heartbeat = 0f;

    [Header("Touch / mouse")]
    [Tooltip("Smallest pinch / scroll size (world units, height). There is no upper limit.")]
    [SerializeField] private float minHeight = 0.5f;

    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private int current;
    private float nextSwitchTime;
    private Renderer[] renderers;
    private float floorY;
    private float height;
    private float lastPinch;
    private Animation legacy;
    private string[] legacyClips;
    private bool paused;
    private bool started;
    private float baseZ;
    private float depth;
    // Spin frame, in the model's own space: the direction that was "up" when it was placed, and its middle.
    // Model files differ in how their insides are turned, so the model's own Y axis can't be trusted to be vertical.
    private Vector3 uprightLocal = Vector3.up;
    private Vector3 centerLocal;
    private const string DepthKey = "HLD_ObjectDepth";

    // For models added at runtime: set before the first frame, Start applies it.
    public float FitHeight { set => fitHeight = value; }
    public float Heartbeat { set => heartbeat = value; }
    public bool Spin { set => spin = value; }
    public SpinAxis Axis { set => spinAxis = value; }
    public bool UprightOnly { set => uprightOnly = value; }

    public enum SpinAxis { Y, X, Z }

    private float beatPhase;
    private float beatFactor = 1f;

    private int ClipCount => legacyClips != null ? legacyClips.Length : gestures != null ? gestures.Length : 0;
    private bool Animated => ClipCount > 0 || heartbeat > 0f;

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();
        floorY = transform.position.y;
        baseZ = transform.position.z;
        depth = PlayerPrefs.GetFloat(DepthKey, defaultDepth);
        transform.position = new Vector3(transform.position.x, transform.position.y, baseZ + depth);
        started = true;
        if (uprightOnly)
        {
            startYaw = startPitch = 0f;
            spinAxis = SpinAxis.Y;
        }
        if (fitHeight > 0f)
            Fit(fitHeight, true);
        else if (renderers.Length > 0)
            height = GetBounds(renderers).size.y;
        uprightLocal = transform.InverseTransformDirection(Vector3.up);
        if (renderers.Length > 0)
            centerLocal = transform.InverseTransformPoint(GetBounds(renderers).center);
        Turn(startYaw, startPitch);
        Ground();

        if (gestures == null || gestures.Length == 0)
        {
            legacy = GetComponentInChildren<Animation>();
            if (legacy != null)
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (AnimationState state in legacy)
                    if (state.clip != null && !state.clip.empty) // some exports carry an empty take
                        names.Add(state.name);
                legacyClips = names.ToArray();
            }
        }

        if (ClipCount > 0)
            Play(0);
    }

    // Lets any model drop in regardless of its file units or pivot height, and keeps its base in place when resized.
    private void Fit(float targetHeight, bool limitWidth)
    {
        if (renderers.Length == 0) return;

        Bounds b = GetBounds(renderers);
        if (b.size.y <= 0f) return;

        float spinWidth = new Vector2(b.size.x, b.size.z).magnitude;
        float factor = targetHeight / b.size.y;
        if (limitWidth && maxWidth > 0f && spinWidth * factor > maxWidth)
            factor = maxWidth / spinWidth;

        transform.localScale *= factor;
        height = GetBounds(renderers).size.y;
        Ground();
    }

    // Hover heightOffset above the alcove floor under the model; a model with nothing under it reads as a flat cutout.
    private void Ground()
    {
        if (renderers.Length == 0) return;
        Bounds b = GetBounds(renderers);
        if (Physics.Raycast(b.center, Vector3.down, out var hit, 50f))
            floorY = hit.point.y;
        transform.position += Vector3.up * (floorY + heightOffset - b.min.y);
    }

    private void ApplyDepth()
    {
        transform.position = new Vector3(transform.position.x, transform.position.y, baseZ + depth);
        Ground();
    }

    // Objects are switched on and off; pick up a depth chosen while another object was showing.
    private void OnEnable()
    {
        if (!started) return;
        depth = PlayerPrefs.GetFloat(DepthKey, defaultDepth);
        ApplyDepth();
    }

    private void HandlePointer()
    {
        // Pinch / drag from a touch screen, mouse as fallback. Windows also turns touches into mouse
        // events, so the mouse is ignored while any finger is down to avoid doing everything twice.
        var touch = Touchscreen.current;
        int fingers = 0;
        Vector2 a = default, b = default, dragDelta = default;
        if (touch != null)
        {
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed) continue;
                if (fingers == 0) { a = t.position.ReadValue(); dragDelta = t.delta.ReadValue(); }
                else if (fingers == 1) b = t.position.ReadValue();
                fingers++;
            }
        }

        float scale = 1f;
        if (fingers >= 2)
        {
            float d = Vector2.Distance(a, b);
            if (lastPinch > 0f) scale = d / lastPinch;
            lastPinch = d;
            dragDelta = default;
        }
        else
        {
            lastPinch = 0f;
            var mouse = Mouse.current;
            if (fingers == 0 && mouse != null)
            {
                if (mouse.leftButton.isPressed) dragDelta = mouse.delta.ReadValue();
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f) scale = 1f + Mathf.Sign(scroll) * 0.1f;
            }
        }

        if (dragDelta != Vector2.zero)
        {
            spin = false; // the viewer took over; R brings the turntable back
            // A full-width swipe turns the object once around; a full-height swipe tilts it half over.
            Turn(-dragDelta.x / Screen.width * 360f, dragDelta.y / Screen.height * 180f);
        }

        if (scale != 1f && height > 0f)
            Fit(Mathf.Max(height * scale, minHeight), false);
    }

    // Yaw around the vertical, pitch around the left-right axis (the camera looks down +Z), both through
    // the object's middle so it turns in place instead of swinging around its pivot.
    private void Turn(float yaw, float pitch)
    {
        if (renderers.Length == 0) return;
        if (uprightOnly) pitch = 0f;
        Vector3 center = GetBounds(renderers).center;
        if (yaw != 0f) transform.RotateAround(center, Vector3.up, yaw);
        if (pitch != 0f) transform.RotateAround(center, Vector3.right, pitch);
    }

    private static Bounds GetBounds(Renderer[] renderers)
    {
        Bounds b = renderers[0].bounds;
        foreach (var r in renderers)
            b.Encapsulate(r.bounds);
        return b;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            spin = !spin;
        if (keyboard != null && keyboard.aKey.wasPressedThisFrame && !uprightOnly)
        {
            spinAxis = (SpinAxis)(((int)spinAxis + 1) % 3);
            string[] names = { "Y  (upright turntable)", "X  (tumbles toward you)", "Z  (wheel facing you)" };
            var display = FindAnyObjectByType<DisplayAlignment>();
            if (display != null) display.Show("Spin axis " + names[(int)spinAxis] + (spin ? "" : "   -  R starts it"));
        }

        HandlePointer();

        // Up/Down tilt, Q/E turn (Shift+Up/Down is the display alignment, so leave those alone).
        if (keyboard != null && !keyboard.shiftKey.isPressed)
        {
            float step = keyTurnSpeed * Time.deltaTime;
            float pitch = (keyboard.upArrowKey.isPressed ? step : 0f) - (keyboard.downArrowKey.isPressed ? step : 0f);
            float yaw = (keyboard.eKey.isPressed ? step : 0f) - (keyboard.qKey.isPressed ? step : 0f);
            if (yaw != 0f) spin = false;
            if (pitch != 0f || yaw != 0f) Turn(yaw, pitch);

            // W pushes the object back toward the background, S pulls it forward toward the viewer.
            float push = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
            if (push != 0f && started)
            {
                depth += push * depthSpeed * Time.deltaTime;
                ApplyDepth();
            }
            if (keyboard.wKey.wasReleasedThisFrame || keyboard.sKey.wasReleasedThisFrame)
            {
                PlayerPrefs.SetFloat(DepthKey, depth);
                PlayerPrefs.Save();
            }
        }

        // Y: the object's own upright axis (it follows the tilt, so the tilt keeps facing the viewer);
        // X / Z: the screen's left-right and in-out axes. Always through the object's middle, so it turns in place.
        if (spin)
        {
            Vector3 axis = spinAxis == SpinAxis.Y ? transform.TransformDirection(uprightLocal)
                         : spinAxis == SpinAxis.X ? Vector3.right : Vector3.forward;
            transform.RotateAround(transform.TransformPoint(centerLocal), axis, spinSpeed * Time.deltaTime);
        }

        if (!Animated) return;
        if (heartbeat > 0f) Beat();

        if (keyboard != null)
        {
            for (int i = 0; i < Mathf.Min(9, ClipCount); i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                    Play(i);
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                paused = !paused;
                SetSpeed(speed);
            }

            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                SetSpeed(speed - 0.1f);
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                SetSpeed(speed + 0.1f);
            if (keyboard.backspaceKey.wasPressedThisFrame)
                SetSpeed(1f);
        }

        // With a single clip this restarts it, so non-looping takes still repeat.
        if (autoCycle && ClipCount > 0 && !paused && Time.time >= nextSwitchTime)
            Play((current + 1) % ClipCount);
    }

    private void Play(int index)
    {
        paused = false; // picking an animation always plays it
        if (legacyClips != null)
        {
            current = index;
            var state = legacy[legacyClips[index]];
            state.wrapMode = WrapMode.Loop;
            state.speed = speed;
            legacy.Play(legacyClips[index]);
            nextSwitchTime = Time.time + state.length * Mathf.Max(1, repeatsPerGesture) / speed;
            return;
        }

        if (gestures[index] == null) return;

        if (graph.IsValid())
            graph.Destroy();

        current = index;
        clipPlayable = AnimationPlayableUtilities.PlayClip(GetComponent<Animator>(), gestures[index], out graph);
        clipPlayable.SetSpeed(speed);
        nextSwitchTime = Time.time + gestures[index].length * Mathf.Max(1, repeatsPerGesture) / speed;
    }

    // Lub-dub: a strong contraction, then a weaker one a quarter beat later, then rest. Scales about the
    // middle of the model so it pulses in place; only the change since last frame is applied, so pinch size is kept.
    private void Beat()
    {
        if (renderers.Length == 0) return;
        beatPhase = (beatPhase + Time.deltaTime * heartbeat / 60f * (paused ? 0f : speed)) % 1f;
        float Bump(float at, float width) => Mathf.Exp(-Mathf.Pow((beatPhase - at) / width, 2f));
        float factor = 1f + 0.08f * Bump(0.08f, 0.05f) + 0.05f * Bump(0.3f, 0.05f);

        float ratio = factor / beatFactor;
        beatFactor = factor;
        Vector3 center = GetBounds(renderers).center;
        transform.localScale *= ratio;
        transform.position = center + (transform.position - center) * ratio;
    }

    private void SetSpeed(float value)
    {
        speed = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.1f, 2f);
        float playing = paused ? 0f : speed;
        if (clipPlayable.IsValid())
            clipPlayable.SetSpeed(playing);
        if (legacyClips != null && legacyClips.Length > 0)
            legacy[legacyClips[current]].speed = playing;
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
            graph.Destroy();
    }
}
