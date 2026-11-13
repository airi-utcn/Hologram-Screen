using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Hololuminescent.MonitorDemo
{
    /// <summary>
    /// Shows the app on an ordinary (landscape) monitor the way the Looking Glass HLD shows it.
    ///
    /// OFF unless the player is started with  -monitor-demo  on the command line, so the normal
    /// portrait build on the hologram behaves exactly as before. Nothing in the scene is changed.
    ///
    /// When on:
    ///  * skips the monitor-picker dialog and goes full screen on the main monitor (any orientation);
    ///  * multiplies a picture of the physical alcove over the final image, which is roughly how the HLD
    ///    blends its panel over the box: white = see-through to the box, colours and darks stay visible.
    ///
    /// All app keys keep working (the overlay takes no input). F9 toggles the alcove overlay.
    /// Optional:  -monitor-demo-windowed  keeps a window instead of going full screen.
    /// </summary>
    public class MonitorDemo : MonoBehaviour
    {
        private const string Flag = "-monitor-demo";
        private const string WindowedFlag = "-monitor-demo-windowed";

        private RawImage overlay;
        private Texture2D landscape, portrait;
        private bool showAlcove = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (!HasArg(Flag) && !HasArg(WindowedFlag)) return;
            var go = new GameObject("MonitorDemo");
            DontDestroyOnLoad(go);
            go.AddComponent<MonitorDemo>();
        }

        private static bool HasArg(string arg)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, arg, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private IEnumerator Start()
        {
            landscape = Resources.Load<Texture2D>("MonitorDemo/alcove_landscape");
            portrait = Resources.Load<Texture2D>("MonitorDemo/alcove_portrait");
            var shader = Shader.Find("Hidden/MonitorDemo/Multiply");
            if (shader == null || landscape == null || portrait == null)
                Debug.LogWarning("MonitorDemo: alcove shader or textures missing; showing the plain image.");
            else
                BuildOverlay(shader);

            // The monitor picker is built in Start(); let it appear, then remove it and take the main monitor.
            // Repeat for a moment, because a remembered selection may move the window again.
            bool windowed = HasArg(WindowedFlag);
            for (int i = 0; i < 6; i++)
            {
                yield return null;
                var picker = GameObject.Find("MonitorSelectionCanvas");
                if (picker != null) Destroy(picker);
                if (!windowed && i % 2 == 0)
                    Screen.SetResolution(Display.main.systemWidth, Display.main.systemHeight, FullScreenMode.FullScreenWindow);
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }

        private void BuildOverlay(Shader shader)
        {
            var canvasGo = new GameObject("MonitorDemoAlcove", typeof(Canvas));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;            // over the scene; the IMGUI help text still draws on top

            var imgGo = new GameObject("Alcove", typeof(RectTransform), typeof(RawImage));
            imgGo.transform.SetParent(canvasGo.transform, false);
            var rt = imgGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            overlay = imgGo.GetComponent<RawImage>();
            overlay.material = new Material(shader);
            overlay.raycastTarget = false;          // never blocks clicks, drags or touches
            UpdateTexture();
        }

        private void UpdateTexture()
        {
            if (overlay == null) return;
            overlay.texture = Screen.width >= Screen.height ? landscape : portrait;
            overlay.enabled = showAlcove;
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) showAlcove = !showAlcove;
            UpdateTexture();
            WidenSet();
        }

        // The alcove set (white sweep, mirror floor, ledge, ceiling) is sized for a 9:16 portrait view. On a wider
        // screen the scene around it shows at the sides as dark bands, so stretch those pieces sideways to match.
        // Only the horizontal size changes, only in this demo mode, and only in memory (nothing is saved).
        private static readonly string[] SetPieces = { "Backdrop", "Mirror", "Ledge", "Ceiling" };
        private readonly System.Collections.Generic.Dictionary<Transform, Vector3> baseScale =
            new System.Collections.Generic.Dictionary<Transform, Vector3>();
        private float widenedFor = -1f;

        private void WidenSet()
        {
            float aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (Mathf.Abs(aspect - widenedFor) < 0.01f) return;
            widenedFor = aspect;
            float factor = Mathf.Max(1f, aspect / (9f / 16f) * 1.15f);
            if (aspect <= 9f / 16f + 0.01f) factor = 1f;
            foreach (var name in SetPieces)
            {
                var go = GameObject.Find(name);
                if (go == null) continue;
                var t = go.transform;
                if (!baseScale.ContainsKey(t)) baseScale[t] = t.localScale;
                // stretch the local axis that points most along the screen's horizontal (world X)
                Vector3 s = baseScale[t];
                float ax = Mathf.Abs(Vector3.Dot(t.right, Vector3.right));
                float ay = Mathf.Abs(Vector3.Dot(t.up, Vector3.right));
                float az = Mathf.Abs(Vector3.Dot(t.forward, Vector3.right));
                if (ax >= ay && ax >= az) s.x *= factor;
                else if (ay >= az) s.y *= factor;
                else s.z *= factor;
                t.localScale = s;
            }
        }
    }
}
