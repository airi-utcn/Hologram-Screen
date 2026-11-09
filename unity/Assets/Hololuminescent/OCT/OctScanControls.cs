using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hololuminescent.Oct
{
    /// <summary>
    /// Layer toggles + info overlay for an OCT scan prefab.
    /// Keys are chosen not to clash with GesturePlayer / DisplayAlignment:
    ///   T tissue shell · F fluids · V volume mode (stack / single B-scan / off)
    ///   , and .  previous / next B-scan · K / L ink lighter / darker · I info
    /// Only the active scan reacts (ObjectSwitcher disables the others).
    /// </summary>
    public class OctScanControls : MonoBehaviour
    {
        public OctVolumeSlices volume;
        public Renderer[] tissue;
        public Renderer[] fluids;
        public TextAsset metaJson;

        public bool showTissue = false;   // grey shell = dull fog on the HLD; the slices already show the tissue
        public bool showFluids = true;
        public bool showInfo = true;

        OctScanMeta _meta;
        GUIStyle _style, _small;
        float _repeatAt;

        void Start()
        {
            if (metaJson) _meta = OctScanMeta.Parse(metaJson.text);
            Apply();
        }

        void Apply()
        {
            foreach (var r in tissue) if (r) r.enabled = showTissue;
            foreach (var r in fluids) if (r) r.enabled = showFluids;
        }

        void Update()
        {
            if (Pressed(Hk.T)) { showTissue = !showTissue; Apply(); }
            if (Pressed(Hk.F)) { showFluids = !showFluids; Apply(); }
            if (Pressed(Hk.I)) showInfo = !showInfo;
            if (volume)
            {
                if (Pressed(Hk.V)) volume.CycleMode();
                if (Pressed(Hk.K)) volume.ScaleDensity(1f / 1.25f);
                if (Pressed(Hk.L)) volume.ScaleDensity(1.25f);
                int step = Repeating(Hk.Comma) ? -1 : Repeating(Hk.Period) ? 1 : 0;
                if (step != 0) volume.StepBScan(step);
            }
        }

        // ---------------------------------------------------------------- input (new Input System or legacy)

        enum Hk { T, F, I, V, K, L, Comma, Period }

        static bool Pressed(Hk k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(MapLegacy(k));
#endif
        }

        static bool Held(Hk k)
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb[Map(k)].isPressed;
#else
            return Input.GetKey(MapLegacy(k));
#endif
        }

        // press = one step; hold = keeps stepping (scrub through the B-scans)
        bool Repeating(Hk k)
        {
            if (Pressed(k)) { _repeatAt = Time.unscaledTime + 0.35f; return true; }
            if (Held(k) && Time.unscaledTime >= _repeatAt) { _repeatAt = Time.unscaledTime + 0.06f; return true; }
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        static Key Map(Hk k) => k switch
        {
            Hk.T => Key.T, Hk.F => Key.F, Hk.I => Key.I, Hk.V => Key.V,
            Hk.K => Key.K, Hk.L => Key.L, Hk.Comma => Key.Comma, _ => Key.Period
        };
#else
        static KeyCode MapLegacy(Hk k) => k switch
        {
            Hk.T => KeyCode.T, Hk.F => KeyCode.F, Hk.I => KeyCode.I, Hk.V => KeyCode.V,
            Hk.K => KeyCode.K, Hk.L => KeyCode.L, Hk.Comma => KeyCode.Comma, _ => KeyCode.Period
        };
#endif

        // ---------------------------------------------------------------- overlay
        // Dark text, no background: on the HLD white is see-through, so the
        // text floats on the front glass and the alcove stays clean.

        void OnGUI()
        {
            if (!showInfo || _meta == null) return;
            int fs = Mathf.Max(12, Screen.height / 75);
            if (_style == null || _style.fontSize != fs)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = fs, richText = true, wordWrap = true };
                _style.normal.textColor = new Color(0.08f, 0.08f, 0.12f);
                _small = new GUIStyle(_style) { fontSize = Mathf.RoundToInt(fs * 0.72f) };
            }

            float pad = Screen.width * 0.05f, w = Screen.width - 2 * pad;
            float lineH = fs * 1.5f;
            int fluidCount = 0;
            if (_meta.layers != null) foreach (var l in _meta.layers) if (l.kind == "fluid") fluidCount++;
            float h = lineH * (2.6f + fluidCount) + fs * 3.5f;
            float y = Screen.height - pad - h;

            GUI.Label(new Rect(pad, y, w, lineH * 1.6f), "<b>" + _meta.title + "</b>", _style);
            y += lineH * 1.6f;

            // legend with the fluid volumes parsed from the notes
            string notes = _meta.notes != null ? string.Join("\n", _meta.notes) : "";
            if (_meta.layers != null)
            {
                foreach (var l in _meta.layers)
                {
                    if (l.kind != "fluid") continue;
                    var c = l.UnityColor; c.a = 1;
                    var old = GUI.color; GUI.color = c;
                    GUI.DrawTexture(new Rect(pad, y + fs * 0.25f, fs, fs), Texture2D.whiteTexture);
                    GUI.color = old;
                    GUI.Label(new Rect(pad + fs * 1.6f, y, w, lineH), l.name + "   " + FindAmount(notes, l.name), _style);
                    y += lineH;
                }
            }
            string total = FindLine(notes, "TOTAL");
            if (!string.IsNullOrEmpty(total)) { GUI.Label(new Rect(pad, y, w, lineH), "<b>" + total + "</b>", _style); y += lineH; }

            var sb = new StringBuilder();
            if (volume)
            {
                sb.Append(volume.mode == OctVolumeSlices.Mode.SingleBScan
                    ? $"B-scan {volume.bscan + 1}/{volume.BScanCount}"
                    : volume.mode == OctVolumeSlices.Mode.Volume ? "volume" : "volume off");
                sb.Append("  ·  ");
            }
            sb.Append("T tissue · F fluids · V mode · , . B-scan · K L ink · I info");
            sb.Append("\nresearch visual — segmentation approximate, not validated");
            GUI.Label(new Rect(pad, y + fs * 0.4f, w, fs * 3f), sb.ToString(), _small);
        }

        static string FindLine(string text, string key)
        {
            foreach (var line in text.Split('\n'))
                if (line.Contains(key)) return line.Trim().TrimStart('■', ' ');
            return "";
        }

        static string FindAmount(string text, string layer)
        {
            var line = FindLine(text, layer + ":");
            int i = line.IndexOf(':');
            return i >= 0 ? line.Substring(i + 1).Trim() : "";
        }
    }
}
