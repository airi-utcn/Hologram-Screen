using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// The colour grade while a spot (StoryDirector) or the exhibit (HoloLab) runs, over the alcove's own grade.
/// The alcove's volume (OutdoorsScene/Global Volume Profile 1) lifts the shadows by 0.13 (Shadows Midtones Highlights),
/// which laid a white veil over everything: black letters measured 0.24 in the recordings, colours looked washed out.
/// Here the shadows are neutral again, and saturation and contrast go up a little. White stays white.
/// </summary>
public static class StageGrade
{
    private const float Saturation = 35f, Contrast = 12f;
    private static GameObject grade;

    public static void Show(bool on)
    {
        if (!on)
        {
            if (grade != null) Object.Destroy(grade);
            grade = null;
            return;
        }
        if (grade != null) return;
        grade = new GameObject("Stage Grade");
        var volume = grade.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 100f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var colour = profile.Add<ColorAdjustments>(true);
        colour.saturation.Override(Saturation);
        colour.contrast.Override(Contrast);
        var tones = profile.Add<ShadowsMidtonesHighlights>(true);
        tones.shadows.Override(new Vector4(1f, 1f, 1f, 0f));   // no lift: black stays black
        volume.sharedProfile = profile;
    }
}
