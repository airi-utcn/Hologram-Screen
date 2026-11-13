using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Live alignment on the physical display: shifts and zooms the main camera so the virtual floor,
/// shadow and reflection sit exactly on the real alcove floor. Values are saved between runs.
/// Shift+Up/Down moves the scene, Shift+PageUp/PageDown zooms, Shift+Backspace resets. H shows all controls.
/// Also rocks the view gently around the object (D toggles, [ ] strength) in a slow figure-8 of yaw and pitch:
/// near and far parts move at different speeds, which the eye reads as depth even on a flat image (kinetic depth effect).
/// O/P lower/raise perspective: a wider lens from closer up, keeping the object the same size on screen, so its
/// near parts grow against its far parts. The template's 15 degree telephoto lens flattens objects. Saved between runs.
/// </summary>
public class DisplayAlignment : MonoBehaviour
{
    [SerializeField] private float moveStep = 0.05f;
    [SerializeField] private float zoomStep = 0.25f;
    [SerializeField] private bool showHelpOnStart = true;

    [Header("Depth sway")]
    [SerializeField] private bool sway = true;
    [Tooltip("How far the view rocks to each side, in degrees. Pitch rocks half as far.")]
    [SerializeField] private float swayAngle = 3f;
    [Tooltip("Seconds for one full left-right-left rock.")]
    [SerializeField] private float swayPeriod = 9f;

    [Header("Perspective")]
    [Tooltip("Lens widening when nothing is saved yet. 1 = template lens, matched to the physical alcove.")]
    [SerializeField] private float defaultPerspective = 1f;

    private const string OffsetKey = "HLD_ViewOffsetY";
    private const string ZoomKey = "HLD_FovDelta";
    // v2: earlier builds saved a 2x default that bent the view away from the physical alcove's lines.
    private const string PerspectiveKey = "HLD_Perspective_v2";
    private float perspective;
    // The shared view; an object saved with F5 may bring its own while it shows (UseView).
    private bool globalSway;
    private float globalSwayAngle, globalPerspective;
    private bool overridden;

    private Camera cam;
    private Vector3 basePosition;
    private Quaternion baseRotation;
    private float pivotDistance;
    private float baseFov;
    private float offsetY;
    private float fovDelta;
    private bool showHelp;
    private string message;
    private float messageUntil;

    private const string Help =
        "CONTROLS  (H hides this)\n\n" +
        "<- / ->      switch object\n" +
        "Tab          next experience (asteroids)\n" +
        "Drag         rotate   (1 finger / mouse)\n" +
        "Pinch        resize   (2 fingers / wheel)\n\n" +
        "1 - 9        pick animation (loops)\n" +
        "Space        pause / play animation\n" +
        "-  /  +      slower / faster\n" +
        "Backspace    normal speed\n" +
        "R            spin on / off\n" +
        "A            spin axis  (Y / X / Z)\n" +
        "Up / Down    tilt object\n" +
        "Q / E        turn object\n" +
        "W / S        push back / pull forward\n" +
        "Ctrl+arrows  move left / right / up / down\n" +
        "F5           save this object (place, size, angle,\n" +
        "             spin, animation)      F8  forget it\n\n" +
        "D            depth sway on / off\n" +
        "[  /  ]      weaker / stronger sway\n" +
        "O  /  P      less / more perspective\n\n" +
        "Shift + Up / Down         move scene up / down\n" +
        "Shift + PageUp / PageDn   zoom in / out\n" +
        "Shift + Backspace         reset alignment\n\n" +
        "Alt + any of the above   10x finer steps\n" +
        "Ctrl + E     choose display";

    // Awake, so objects can hand over their saved view in their Start.
    private void Awake()
    {
        perspective = globalPerspective = PlayerPrefs.GetFloat(PerspectiveKey, defaultPerspective);
        globalSway = sway;
        globalSwayAngle = swayAngle;
    }

    public void GetView(out bool swayOn, out float angle, out float lensWidening)
    {
        swayOn = sway; angle = swayAngle; lensWidening = perspective;
    }

    public void UseView(ObjectConfig c)
    {
        overridden = c != null && c.perspective > 0f;
        sway = overridden ? c.sway : globalSway;
        swayAngle = overridden ? c.swayAngle : globalSwayAngle;
        perspective = overridden ? c.perspective : globalPerspective;
    }

    private void Start()
    {
        // The HDRP template's camera is not tagged MainCamera, so fall back to the scene's camera.
        cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
        if (cam == null)
        {
            enabled = false;
            return;
        }

        basePosition = cam.transform.position;
        baseRotation = cam.transform.rotation;
        baseFov = cam.fieldOfView;
        // Rock around the shown objects (they all share one spot), so they stay centred.
        var target = FindAnyObjectByType<GesturePlayer>();
        pivotDistance = target != null ? Vector3.Dot(target.transform.position - basePosition, cam.transform.forward) : 20f;
        offsetY = PlayerPrefs.GetFloat(OffsetKey, 0f);
        fovDelta = PlayerPrefs.GetFloat(ZoomKey, 0f);
        showHelp = showHelpOnStart;
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.hKey.wasPressedThisFrame)
            showHelp = !showHelp;
        bool fine = keyboard.altKey.isPressed; // Alt: steps ten times smaller
        if (keyboard.dKey.wasPressedThisFrame)
        {
            sway = !sway;
            if (!overridden) globalSway = sway;
        }
        if (keyboard.leftBracketKey.wasPressedThisFrame || keyboard.rightBracketKey.wasPressedThisFrame)
        {
            float step = fine ? 0.05f : 0.5f;
            swayAngle = Mathf.Clamp(swayAngle + (keyboard.rightBracketKey.wasPressedThisFrame ? step : -step), 0f, 10f);
            if (!overridden) globalSwayAngle = swayAngle;
            Show($"Sway {swayAngle:0.00} degrees");
        }
        if (keyboard.oKey.wasPressedThisFrame || keyboard.pKey.wasPressedThisFrame)
        {
            float factor = fine ? 1.025f : 1.25f;
            perspective = Mathf.Clamp(perspective * (keyboard.pKey.wasPressedThisFrame ? factor : 1f / factor), 1f, 4f);
            if (!overridden)
            {
                globalPerspective = perspective;
                PlayerPrefs.SetFloat(PerspectiveKey, perspective);
                PlayerPrefs.Save();
            }
            Show($"Perspective {perspective:0.00}x   (lens {Mathf.Clamp(Mathf.Clamp(baseFov + fovDelta, 1f, 60f) * perspective, 1f, 75f):0} degrees)");
        }

        if (!keyboard.shiftKey.isPressed) return;

        bool changed = true;
        // Moving the camera down makes the scene appear to move up.
        float move = fine ? moveStep * 0.1f : moveStep, zoom = fine ? zoomStep * 0.1f : zoomStep;
        if (keyboard.upArrowKey.wasPressedThisFrame) offsetY -= move;
        else if (keyboard.downArrowKey.wasPressedThisFrame) offsetY += move;
        else if (keyboard.pageUpKey.wasPressedThisFrame) fovDelta -= zoom;
        else if (keyboard.pageDownKey.wasPressedThisFrame) fovDelta += zoom;
        else if (keyboard.backspaceKey.wasPressedThisFrame) { offsetY = 0f; fovDelta = 0f; }
        else changed = false;

        if (!changed) return;

        PlayerPrefs.SetFloat(OffsetKey, offsetY);
        PlayerPrefs.SetFloat(ZoomKey, fovDelta);
        PlayerPrefs.Save();
        Show($"Scene offset {-offsetY:+0.00;-0.00;0}   Zoom {(baseFov / (baseFov + fovDelta)):0.00}x");
    }

    public void Show(string text)
    {
        message = text;
        messageUntil = Time.time + 2f;
    }

    // Every frame, after objects have moved, so alignment, perspective and sway always agree.
    private void LateUpdate()
    {
        Vector3 forward = baseRotation * Vector3.forward;
        Vector3 position = basePosition + Vector3.up * offsetY;
        Vector3 pivot = position + forward * pivotDistance;

        // Wider lens from closer up; the distance scales so the object keeps its size on screen.
        float zoomFov = Mathf.Clamp(baseFov + fovDelta, 1f, 60f);
        float fov = Mathf.Clamp(zoomFov * perspective, 1f, 75f);
        float dolly = Mathf.Tan(zoomFov * 0.5f * Mathf.Deg2Rad) / Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        position = pivot - forward * (pivotDistance * dolly);

        Quaternion rotation = baseRotation;
        if (sway)
        {
            float t = Time.time * 2f * Mathf.PI / swayPeriod;
            // Pitch at twice the yaw rate traces a figure-8, so the view also looks a little over and under.
            Quaternion turn = Quaternion.AngleAxis(swayAngle * Mathf.Sin(t), Vector3.up)
                            * Quaternion.AngleAxis(swayAngle * 0.5f * Mathf.Sin(2f * t), baseRotation * Vector3.right);
            position = pivot + turn * (position - pivot);
            rotation = turn * baseRotation;
        }
        cam.transform.SetPositionAndRotation(position, rotation);
        cam.fieldOfView = fov;
    }

    private void OnGUI()
    {
        if (!showHelp && Time.time > messageUntil) return;

        // Scale with resolution so it is readable on a 4K portrait panel.
        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = Mathf.Max(14, Screen.height / 70),
            alignment = TextAnchor.UpperLeft,
            padding = new RectOffset(20, 20, 16, 16)
        };
        style.normal.textColor = Color.white;

        // Tells on the display itself whether Windows reported a touch screen to the app (if not, it's a cable / driver issue).
        string touch = Touchscreen.current != null ? "\n\nTouch screen: found" : "\n\nTouch screen: NOT found (check the USB cable)";
        var shown = showHelp ? FindAnyObjectByType<GesturePlayer>() : null; // only the shown object is active
        string view = $"\nview: sway {(sway ? "on" : "off")} {swayAngle:0.00}   perspective {perspective:0.000}{(overridden ? "  (saved with object)" : "")}";
        string text = showHelp ? Help + touch + (shown != null ? "\n\n" + shown.Describe() + view : "") : message;
        if (showHelp && Time.time <= messageUntil)
            text += "\n\n" + message;

        Vector2 size = style.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(20, 20, size.x, size.y), text, style);
    }
}
