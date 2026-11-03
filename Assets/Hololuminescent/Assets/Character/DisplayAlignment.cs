using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Live alignment on the physical display: shifts and zooms the main camera so the virtual floor,
/// shadow and reflection sit exactly on the real alcove floor. Values are saved between runs.
/// Shift+Up/Down moves the scene, Shift+PageUp/PageDown zooms, Shift+Backspace resets. H shows all controls.
/// </summary>
public class DisplayAlignment : MonoBehaviour
{
    [SerializeField] private float moveStep = 0.05f;
    [SerializeField] private float zoomStep = 0.25f;
    [SerializeField] private bool showHelpOnStart = true;

    private const string OffsetKey = "HLD_ViewOffsetY";
    private const string ZoomKey = "HLD_FovDelta";

    private Camera cam;
    private Vector3 basePosition;
    private float baseFov;
    private float offsetY;
    private float fovDelta;
    private bool showHelp;
    private string message;
    private float messageUntil;

    private const string Help =
        "CONTROLS  (H hides this)\n\n" +
        "1 - 9        pick animation (loops)\n" +
        "Space / ->   next animation\n" +
        "-  /  +      slower / faster\n" +
        "Backspace    normal speed\n" +
        "R            spin on / off\n\n" +
        "Shift + Up / Down         move scene up / down\n" +
        "Shift + PageUp / PageDn   zoom in / out\n" +
        "Shift + Backspace         reset alignment\n\n" +
        "Ctrl + E     choose display";

    private void Start()
    {
        cam = Camera.main;
        if (cam == null)
        {
            enabled = false;
            return;
        }

        basePosition = cam.transform.position;
        baseFov = cam.fieldOfView;
        offsetY = PlayerPrefs.GetFloat(OffsetKey, 0f);
        fovDelta = PlayerPrefs.GetFloat(ZoomKey, 0f);
        showHelp = showHelpOnStart;
        Apply();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.hKey.wasPressedThisFrame)
            showHelp = !showHelp;

        if (!keyboard.shiftKey.isPressed) return;

        bool changed = true;
        // Moving the camera down makes the scene appear to move up.
        if (keyboard.upArrowKey.wasPressedThisFrame) offsetY -= moveStep;
        else if (keyboard.downArrowKey.wasPressedThisFrame) offsetY += moveStep;
        else if (keyboard.pageUpKey.wasPressedThisFrame) fovDelta -= zoomStep;
        else if (keyboard.pageDownKey.wasPressedThisFrame) fovDelta += zoomStep;
        else if (keyboard.backspaceKey.wasPressedThisFrame) { offsetY = 0f; fovDelta = 0f; }
        else changed = false;

        if (!changed) return;

        Apply();
        PlayerPrefs.SetFloat(OffsetKey, offsetY);
        PlayerPrefs.SetFloat(ZoomKey, fovDelta);
        PlayerPrefs.Save();
        message = $"Scene offset {-offsetY:+0.00;-0.00;0}   Zoom {(baseFov / cam.fieldOfView):0.00}x";
        messageUntil = Time.time + 2f;
    }

    private void Apply()
    {
        cam.transform.position = basePosition + Vector3.up * offsetY;
        cam.fieldOfView = Mathf.Clamp(baseFov + fovDelta, 1f, 60f);
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

        string text = showHelp ? Help : message;
        if (showHelp && Time.time <= messageUntil)
            text += "\n\n" + message;

        Vector2 size = style.CalcSize(new GUIContent(text));
        GUI.Box(new Rect(20, 20, size.x, size.y), text, style);
    }
}
