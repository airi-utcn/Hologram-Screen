using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Switches between the experiences from any of them. Created by itself when the app starts, so no scene needs it.
///   Tab       the next scene in File > Build Profiles (the model showcase, Character; the planet and asteroids, VQ1_Hologram)
///   Numpad 1  the planet and asteroids
///   Numpad 2  the AIRI x UTCN spot (as Enter in the showcase)
///   Numpad 3  the AI for health spot (as M in the showcase)
///   Numpad 4  the model showcase, fresh: every object where it was saved (place, size, angle, spin, animation)
///   Numpad 5  the ChemiNova presentation: the showcase, fresh, on the shoe + Palchetto (Assets/Cheminova, its README)
///   Numpad 6  the AIRI exhibit (HoloLab): the official opening, then the research carousel
///   Numpad 7  Wake the Lab, the playful touch games (PlayLab)
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("SceneSwitcher");
        DontDestroyOnLoad(go);
        go.AddComponent<SceneSwitcher>();
        WhiteBackground();
        RoomierAlcove();
        SceneManager.sceneLoaded += (s, m) => { WhiteBackground(); RoomierAlcove(); };
    }

    // Pure white behind everything, in every scene, instead of the HDRI room past the alcove: white is invisible on the
    // HLD, and it meets the white alcove without a grey edge. Only what the camera shows behind: the sky still lights.
    private static void WhiteBackground()
    {
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include))
        {
            var hd = cam.GetComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
            if (hd == null) continue;
            hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
            hd.backgroundColorHDR = Color.white * 16f;   // well past exposure, so it lands on pure white
        }
    }

    // The alcove as it was (back wall, ceiling, ledge, mirror floor), only roomier, darker and with visible shadows.
    // Roomier: its Geometry (all of those) scaled
    // wider and taller from its base, where the floor is, so the floor and everything standing on it stay where they
    // are. The ceiling ends up above the alcove's Spot Light; it casts no shadow, so the spot still lights the stage.
    private static readonly Vector3 AlcoveScale = new Vector3(1.25f, 1.2f, 1f);   // wider, taller, same depth

    private static void RoomierAlcove()
    {
        var alcove = GameObject.Find("Alcove");
        var geometry = alcove != null ? alcove.transform.Find("Geometry") : null;
        if (geometry != null) geometry.localScale = AlcoveScale;

        // No grey bars: the ceiling (top) and the front ledge (bottom) go. The ceiling casts no shadow, so the light is
        // unchanged. Inactive, so the spots' Find no longer turns the ledge back on.
        foreach (var bar in new[] { "Geometry/Ceiling", "Geometry/Ledge" })
        {
            var part = alcove != null ? alcove.transform.Find(bar) : null;
            if (part != null) part.gameObject.SetActive(false);
        }

        // Shadows and true black. The backdrop is unlit (it can show no shadow); the mirror floor is the one surface that
        // takes the objects' shadows, and at smoothness 0.87 it mostly mirrored the white round it, so they barely
        // showed: matte now (a copy of its material; the asset is not touched).
        var mirror = alcove != null ? alcove.transform.Find("Geometry/Mirror") : null;
        var floor = mirror != null ? mirror.GetComponent<Renderer>() : null;
        if (floor != null)
        {
            // Only under the stage: the 10 x 10 plane reached far to the front, where, away from the spot, it showed as
            // a grey band across the bottom; past it the white (unlit) floor of the backdrop shows.
            mirror.localScale = new Vector3(0.4f, 1f, 0.4f);
            var matte = new Material(floor.sharedMaterial);
            matte.SetFloat("_Smoothness", 0.25f);
            matte.SetFloat("_Metallic", 0f);
            // A shadow catcher: a glow of its own (not scaled by exposure) brings it to white where the light falls, so
            // it no longer shows as a grey strip; in an object's shadow the light is missing and a soft grey is left.
            matte.SetColor("_EmissiveColor", Color.white * FloorGlow);
            matte.SetFloat("_EmissiveExposureWeight", 0f);
            floor.sharedMaterial = matte;
        }
        // The alcove's lights at 60%: everything was lit so far past white that blacks came out grey.
        if (alcove != null)
            foreach (var light in alcove.GetComponentsInChildren<Light>(true)) light.intensity *= LightLevel;

        // The alcove's volume lifts the shadows by 0.13 (Shadows Midtones Highlights), a white veil on everything; neutral
        // again, in every scene. (StageGrade adds colour on top while a spot or the exhibit runs.)
        var neutral = new GameObject("Neutral Shadows");
        var volume = neutral.AddComponent<UnityEngine.Rendering.Volume>();
        volume.isGlobal = true;
        volume.priority = 50f;
        var profile = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
        profile.Add<UnityEngine.Rendering.HighDefinition.ShadowsMidtonesHighlights>(true).shadows.Override(new Vector4(1f, 1f, 1f, 0f));
        volume.sharedProfile = profile;
    }

    private const float LightLevel = 0.6f;
    private const float FloorGlow = 0.7f;   // higher: whiter floor, fainter shadows

    private void Update()
    {
        var keyboard = Keyboard.current;
        int count = SceneManager.sceneCountInBuildSettings;
        if (keyboard == null) return;

        if (keyboard.tabKey.wasPressedThisFrame && count >= 2)
            SceneManager.LoadScene((SceneManager.GetActiveScene().buildIndex + 1) % count);
        else if (keyboard.numpad1Key.wasPressedThisFrame)
            Load(Scene("VQ1"));
        else if (keyboard.numpad2Key.wasPressedThisFrame)
            StartCoroutine(Spot(false));
        else if (keyboard.numpad3Key.wasPressedThisFrame)
            StartCoroutine(Spot(true));
        else if (keyboard.numpad4Key.wasPressedThisFrame)
            Load(Scene("Character"));   // reloading it puts every object back as saved, whatever ran before
        else if (keyboard.numpad5Key.wasPressedThisFrame)
            StartCoroutine(Cheminova());
        else if (keyboard.numpad6Key.wasPressedThisFrame)
            StartCoroutine(Showcase<HoloLab>(h => h.Begin()));
        else if (keyboard.numpad7Key.wasPressedThisFrame)
            StartCoroutine(Showcase<PlayLab>(p => p.Begin()));
    }

    // Build index of the scene whose path contains `name`; the first scene if none does.
    private static int Scene(string name)
    {
        for (int i = 0; i < SceneManager.sceneCountInBuildSettings; i++)
            if (SceneUtility.GetScenePathByBuildIndex(i).Contains(name)) return i;
        return 0;
    }

    private static void Load(int index) => SceneManager.LoadScene(index);

    // The showcase, reloaded, then straight to the shoe + Palchetto: CheminovaBootstrap dresses that object with its
    // logo, captions, EU banner and shoe / building loop whenever it shows.
    private IEnumerator Cheminova()
    {
        SceneManager.LoadScene(Scene("Character"));
        yield return null;   // the load happens at the end of this frame
        ObjectSwitcher showcase = null;
        for (int tries = 0; tries < 60 && showcase == null; tries++)
        {
            yield return null;
            showcase = FindAnyObjectByType<ObjectSwitcher>();
        }
        yield return null;   // after its Start has shown the first object
        if (showcase != null && showcase.ShowNamed("Chemi-AU-O005") == null)
            Debug.LogWarning("SceneSwitcher: the shoe + Palchetto object is not in the showcase's Resource Models.");
    }

    // The showcase, reloaded so nothing else is running, then `start` on its T (HoloLab, PlayLab: they attach themselves).
    private IEnumerator Showcase<T>(System.Action<T> start) where T : Component
    {
        SceneManager.LoadScene(Scene("Character"));
        yield return null;   // the load happens at the end of this frame
        T found = null;
        for (int tries = 0; tries < 60 && found == null; tries++)
        {
            yield return null;
            found = FindAnyObjectByType<T>();
        }
        yield return null;   // after its Start
        if (found != null) start(found);
    }

    // A spot plays in the showcase scene: go there first if needed, then start it once the scene is up.
    private IEnumerator Spot(bool health)
    {
        int showcase = Scene("Character");
        if (SceneManager.GetActiveScene().buildIndex != showcase)
        {
            SceneManager.LoadScene(showcase);
            yield return null;   // the load happens at the end of this frame
        }
        StoryDirector director = null;
        for (int tries = 0; tries < 60 && director == null; tries++)   // it attaches itself after the scene loads
        {
            yield return null;
            director = FindAnyObjectByType<StoryDirector>();
        }
        yield return null;   // after its Start
        if (director != null) director.Play(health);
    }
}
