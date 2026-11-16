using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The 5G-SHARE logo, flat, in the top right corner while 5G-SHARE is on screen: the AIRI spot's 5G part
/// (StoryDirector), and the exhibit's 5G-SHARE exhibit and carousel place (HoloLab). An overlay, so it is drawn over the
/// scene, unlit, and the spot recorder's Game view capture includes it. The picture is Resources/Story/Logo_5GShare.png,
/// cut out of the project's slide on its red (white and the map lines are transparent).
/// </summary>
public static class FiveGLogo
{
    // Share of the screen: its width, and its margins from the top and right edges (portrait 2160 x 3840).
    private const float Width = 0.22f, Top = 0.04f, Right = 0.03f;
    private static GameObject canvas;

    public static void Show(bool on)
    {
        if (on && canvas == null) Create();
        if (canvas != null) canvas.SetActive(on);
    }

    private static void Create()
    {
        var picture = Resources.Load<Texture2D>("Story/Logo_5GShare");
        if (picture == null) { Debug.LogWarning("FiveGLogo: nothing at Resources/Story/Logo_5GShare"); return; }
        canvas = new GameObject("5G-SHARE logo");
        var c = canvas.AddComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = 50;
        var scaler = canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(2160f, 3840f);
        scaler.matchWidthOrHeight = 0.5f;
        var image = new GameObject("Logo").AddComponent<RawImage>();
        image.texture = picture;
        image.raycastTarget = false;
        image.transform.SetParent(canvas.transform, false);
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        float w = 2160f * Width;
        rect.sizeDelta = new Vector2(w, w * picture.height / picture.width);
        rect.anchoredPosition = new Vector2(-2160f * Right, -3840f * Top);
    }
}
