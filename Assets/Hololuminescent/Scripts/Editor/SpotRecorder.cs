using System.IO;
using UnityEditor;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>
/// HLD menu: records the AIRI or the health spot straight from the Game view at the display's own 2160 x 3840 portrait
/// resolution, frame by frame at 30 fps (no lag, no screen-recorder compression), into Recordings/ next to Assets.
/// It writes JPEG frames to Recordings/<spot>/: Unity's video encoders (H.264 and VP8 alike) refuse 3840 px high.
/// SourceModels/Story/frames_to_video.py then makes the MP4 for HLD Author.
/// </summary>
public static class SpotRecorder
{
    private const string Key = "HLD_SpotRecorder";   // 1 = AIRI spot, 2 = health spot, 0 = none
    private const int Width = 2160, Height = 3840, Fps = 30;
    // Seconds before the spot starts, and after it ends: none, since the spot ends on its own (the logos shrink away) and
    // the showcase's logo loop after it is framed for the HLD, not for this wider video.
    private const float Settle = 1f, Tail = 0f;

    private static RecorderController controller;
    private static StoryDirector director;
    private static float startAt, endAt;

    [MenuItem("HLD/Record AIRI spot (2160x3840)")]
    private static void RecordAiri() => Begin(1);

    [MenuItem("HLD/Record health spot (2160x3840)")]
    private static void RecordHealth() => Begin(2);

    private static void Begin(int spot)
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("SpotRecorder: stop Play mode first."); return; }
        SessionState.SetInt(Key, spot);
        UseAppAlignment();
        EditorApplication.isPlaying = true;
    }

    // The Editor keeps its own PlayerPrefs; the built app saves the alignment set on the HLD (Shift+arrows, zoom, W/S
    // depth, perspective) under HKCU\Software\<company>\<product>. Record with the app's, then give the Editor its own back.
    // Share of the app's view height the video shows. 0.71 = ceiling band edge to the floor's edge (575..3300 of 3840 px);
    // larger keeps more room round the objects: the ceiling is hidden and the background is white while recording.
    private const float StageShare = 0.85f;
    private static readonly string[] AlignmentKeys = { "HLD_ViewOffsetY", "HLD_FovDelta", "HLD_Perspective_v2", "HLD_ObjectDepth" };

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegOpenKeyEx(System.UIntPtr root, string subKey, uint options, uint access, out System.IntPtr key);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int RegQueryValueEx(System.IntPtr key, string name, System.IntPtr reserved, System.IntPtr type, byte[] data, ref uint size);
    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern int RegCloseKey(System.IntPtr key);

    // Unity's PlayerPrefs value name: key + "_h" + djb2-xor hash (checked: HLD_ViewOffsetY -> 4147309475).
    private static uint PrefsHash(string s)
    {
        uint h = 5381;
        foreach (char c in s) h = (h * 33) ^ c;
        return h;
    }

    private static void UseAppAlignment()
    {
        string path = $"Software\\{PlayerSettings.companyName}\\{PlayerSettings.productName}";
        if (RegOpenKeyEx(new System.UIntPtr(0x80000001u), path, 0, 0x20019 /* KEY_READ */, out var key) != 0)
        {
            Debug.LogWarning($"SpotRecorder: no saved app alignment (HKCU\\{path}); using the Editor's.");
            return;
        }
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string used = "";
        foreach (string k in AlignmentKeys)
        {
            SessionState.SetString("SpotRecorder_" + k, PlayerPrefs.HasKey(k) ? PlayerPrefs.GetFloat(k).ToString("R", inv) : "-");
            // Unity writes floats as 8-byte doubles typed DWORD.
            var data = new byte[8];
            uint size = 8;
            if (RegQueryValueEx(key, $"{k}_h{PrefsHash(k)}", System.IntPtr.Zero, System.IntPtr.Zero, data, ref size) == 0 && size == 8)
            {
                float v = (float)System.BitConverter.ToDouble(data, 0);
                PlayerPrefs.SetFloat(k, v);
                used += $"  {k}={v.ToString("0.###", inv)}";
            }
        }
        RegCloseKey(key);

        // On the HLD the display's frame hides the ceiling band and the room under the alcove; a video shows them.
        // Narrow the lens so the stage between them (measured: 71% of the height, centred) fills the picture.
        var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (cam != null)
        {
            float appFov = cam.fieldOfView + PlayerPrefs.GetFloat("HLD_FovDelta", 0f);
            float fill = 2f * Mathf.Atan(Mathf.Tan(appFov * 0.5f * Mathf.Deg2Rad) * StageShare) * Mathf.Rad2Deg;
            PlayerPrefs.SetFloat("HLD_FovDelta", fill - cam.fieldOfView);
            used += $"  -> lens {fill.ToString("0.##", inv)} deg to fill the frame";
        }
        Debug.Log("SpotRecorder: app alignment" + used);
    }

    private static void RestoreEditorAlignment()
    {
        foreach (string k in AlignmentKeys)
        {
            string saved = SessionState.GetString("SpotRecorder_" + k, null);
            if (saved == null) continue;
            if (saved == "-") PlayerPrefs.DeleteKey(k); else PlayerPrefs.SetFloat(k, float.Parse(saved, System.Globalization.CultureInfo.InvariantCulture));
            SessionState.EraseString("SpotRecorder_" + k);
        }
        PlayerPrefs.Save();
    }

    [InitializeOnLoadMethod]
    private static void Hook()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetInt(Key, 0) != 0)
            {
                controller = null; director = null; startAt = endAt = 0f;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
                Done();
            else if (state == PlayModeStateChange.EnteredEditMode)
                RestoreEditorAlignment();   // after the app's last save on quit
        };
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying) { Done(); return; }
        int spot = SessionState.GetInt(Key, 0);

        if (controller == null)
        {
            director = Object.FindAnyObjectByType<StoryDirector>();
            if (director == null || Time.time < Settle) return;   // let the scene and the showcase start
            var display = Object.FindAnyObjectByType<DisplayAlignment>();
            if (display != null) display.HelpVisible = false;     // the controls box would be in the video
            // On the HLD the display's frame hides the ceiling band and the room past the alcove; a video would show them.
            // Play mode undoes both on exit.
            var ceiling = GameObject.Find("Ceiling");
            if (ceiling != null)
                foreach (var r in ceiling.GetComponentsInChildren<Renderer>())
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;   // unseen, still shades like on the HLD
            var cam = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
            var hd = cam != null ? cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>() : null;
            if (hd != null)
            {
                hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
                hd.backgroundColorHDR = Color.white * 16f;   // well past exposure, so it lands on pure white
            }
            foreach (var dialog in Object.FindObjectsByType<MonitorSelectionDialog>(FindObjectsInactive.Include))
                dialog.HideDialog();                              // the "choose display" menu: the Game view is the picture

            string name = spot == 2 ? "Health_spot" : "AIRI_spot";
            var movie = ScriptableObject.CreateInstance<ImageRecorderSettings>();
            movie.name = name;
            movie.Enabled = true;
            movie.OutputFormat = ImageRecorderSettings.ImageRecorderOutputFormat.JPEG;
            movie.JpegQuality = 95;
            movie.imageInputSettings = new GameViewInputSettings { OutputWidth = Width, OutputHeight = Height };
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings", name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);   // no frames left over from an earlier take
            Directory.CreateDirectory(folder);
            movie.OutputFile = folder + Path.DirectorySeparatorChar + "frame_" + DefaultWildcard.Frame;   // not Path.Combine: it rejects the <Frame> wildcard

            var settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);
            settings.SetRecordModeToManual();
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.FrameRate = Fps;
            settings.CapFrameRate = false;   // render each frame as long as it needs: the video still plays at 30 fps

            controller = new RecorderController(settings);
            controller.PrepareRecording();
            if (!controller.StartRecording()) { Debug.LogError("SpotRecorder: the recording did not start (see the Console)."); Stop(); return; }
            director.Play(spot == 2);
            startAt = Time.time;
            Debug.Log($"SpotRecorder: recording {name} at {Width}x{Height}...");
            return;
        }

        if (director == null) { Stop(); return; }
        if (endAt == 0f && !director.Running && Time.time > startAt + 1f) endAt = Time.time + Tail;
        if (endAt > 0f && Time.time >= endAt) Stop();
    }

    private static void Stop()
    {
        if (controller != null && controller.IsRecording())
        {
            controller.StopRecording();
            string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Recordings");
            Debug.Log($"SpotRecorder: done, {Time.time - startAt:0.0} s saved in {folder}");
            EditorUtility.RevealInFinder(folder);
        }
        Done();
        EditorApplication.isPlaying = false;
    }

    private static void Done()
    {
        EditorApplication.update -= Tick;
        SessionState.SetInt(Key, 0);
        if (controller != null && controller.IsRecording()) controller.StopRecording();
        controller = null;
    }
}
