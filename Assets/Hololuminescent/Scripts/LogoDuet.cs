using UnityEngine;

/// <summary>
/// Two logos taking turns, in a loop: one floats and sways in front for Hold seconds, then it whips round faster and faster,
/// shrinks and flies off into the depth to one side, while the other comes out of the depth from the other side,
/// spins down and settles with a small overshoot. Motion toward / away from the viewer is the strongest 3D cue on the HLD.
/// Built by ObjectSwitcher (Duet First / Duet Second); starts over with the first logo each time it is shown.
/// Lengths are in the set's own units: each logo is about 1 wide before GesturePlayer sizes the set.
/// </summary>
public class LogoDuet : MonoBehaviour
{
    [Tooltip("Seconds each logo stays in front.")]
    [SerializeField] private float hold = 5f;
    [Tooltip("Seconds the swap takes.")]
    [SerializeField] private float swap = 2.4f;
    [Tooltip("How far a logo sways left / right while it holds, degrees.")]
    [SerializeField] private float sway = 12f;
    [Tooltip("Turn a logo rests at, degrees. Facing the viewer square on, a flat logo reads as a 2D picture; a little turned, its sides and edges show.")]
    [SerializeField] private float restYaw = 0f; // 0: the sway swings evenly left and right of facing the viewer
    [Tooltip("Tilt a logo rests at, degrees (same direction as the other models' start tilt).")]
    [SerializeField] private float restPitch = 0f;
    [SerializeField] private float swayPeriod = 6f;
    [Tooltip("How far a logo floats up / down while it holds.")]
    [SerializeField] private float bob = 0.02f;
    [Tooltip("How far into the depth (away from the viewer) a logo leaves to / comes from.")]
    [SerializeField] private float depth = 0.6f;
    [Tooltip("How far to the side it leaves to / comes from.")]
    [SerializeField] private float side = 0.4f;
    [Tooltip("Turns while leaving / arriving.")]
    [SerializeField] private float turns = 1.5f;

    [Header("Logo light")]
    [Tooltip("A light from in front of and above the logos, so each casts its whole shape onto the floor behind it. " +
             "The alcove's own lights come from straight above and from the side, which a flat upright logo meets edge-on: their shadow is a line.")]
    [SerializeField] private bool logoLight = true;
    [Tooltip("Light position from the logos' middle, world units: up, and toward the viewer.")]
    [SerializeField] private float lightUp = 3.5f;
    [SerializeField] private float lightToward = 2.5f;
    [Tooltip("Brightness as a share of the alcove's Spot Light, which it copies (colour, shadow quality, range).")]
    [SerializeField] private float lightStrength = 1f;
    [Tooltip("The alcove's overhead Spot Light while the logos show, as a share of its own brightness. " +
             "The floor is lit past pure white; a shadow only shows where what's left of the light falls below white, " +
             "so the logo light has to be the main light here. 1 = unchanged.")]
    [SerializeField] private float alcoveSpotWhileShown = 0.25f;

    private Transform[] logos;
    private float time;
    private bool lit;
    private Light alcoveSpot;
    private float alcoveSpotIntensity;

    public void Init(Transform first, Transform second) => logos = new[] { first, second };

    private void OnEnable()
    {
        time = 0f;
        if (!logoLight) return;
        if (alcoveSpot == null)
        {
            var spot = GameObject.Find("Spot Light");
            alcoveSpot = spot != null ? spot.GetComponent<Light>() : null;
            if (alcoveSpot != null) alcoveSpotIntensity = alcoveSpot.intensity;
        }
        if (alcoveSpot != null) alcoveSpot.intensity = alcoveSpotIntensity * alcoveSpotWhileShown;
    }

    // Every other object gets the alcove's lighting back.
    private void OnDisable()
    {
        if (alcoveSpot != null) alcoveSpot.intensity = alcoveSpotIntensity;
    }

    // A copy of the alcove's Spot Light, so it has HDRP's light data and the same units; a child, so it only shines while the logos show.
    private void AddLogoLight()
    {
        lit = true;
        if (!logoLight) return;
        if (alcoveSpot == null)
        {
            Debug.LogWarning("LogoDuet: no 'Spot Light' in the alcove to copy, so no logo light.");
            return;
        }
        var go = Instantiate(alcoveSpot.gameObject, transform);
        go.name = "Logo Light";
        // After GesturePlayer.Start: the set is sized and placed, and its origin is the logos' middle.
        Vector3 middle = transform.position;
        go.transform.position = middle + Vector3.up * lightUp - Vector3.forward * lightToward;
        go.transform.rotation = Quaternion.LookRotation(middle - go.transform.position);
        var light = go.GetComponent<Light>();
        light.intensity = alcoveSpotIntensity * lightStrength; // the copy was taken while the original was dimmed
        light.shadows = LightShadows.Soft;
        light.enabled = true;
    }

    private void Update()
    {
        if (logos == null) return;
        if (!lit) AddLogoLight();
        time += Time.deltaTime;
        for (int i = 0; i < 2; i++)
        {
            Pose(time, i, out float scale, out float yaw, out Vector3 offset);
            logos[i].localScale = Vector3.one * Mathf.Max(0.001f, scale); // not 0: breaks bounds and matrices
            logos[i].localRotation = Quaternion.Euler(restPitch, yaw + restYaw, 0f);
            logos[i].localPosition = offset;
        }
    }

    /// <summary>Where logo `index` (0 or 1) is at `t` seconds: size (1 = full), turn and offset from the middle.</summary>
    public void Pose(float t, int index, out float scale, out float yaw, out Vector3 offset)
    {
        float idle = Mathf.Sin(t * 2f * Mathf.PI / swayPeriod);
        float swayYaw = sway * idle;
        float lift = bob * Mathf.Sin(t * 2f * Mathf.PI / (swayPeriod * 0.5f));
        float half = hold + swap;
        float c = Mathf.Repeat(t, 2f * half);
        int front = c < half ? 0 : 1;      // which logo this half of the loop belongs to
        float local = c - front * half;
        float s = Mathf.Clamp01((local - hold) / swap); // 0 while holding, 0..1 through the swap

        if (index == front)
        {
            // Leaving: accelerates (cubic ease-in), done by 60% of the swap.
            float e = Mathf.Pow(Mathf.Clamp01(s / 0.6f), 3f);
            scale = 1f - e;
            yaw = swayYaw + 360f * turns * e;
            offset = new Vector3(-side * e, lift + 0.15f * e, depth * e);
        }
        else
        {
            // Arriving from 30% of the swap: decelerates (cubic ease-out) and overshoots its size a little (ease-out-back).
            // While the other logo holds, this one waits at its start: tiny, back in the depth.
            float u = local < hold ? 0f : Mathf.Clamp01((s - 0.3f) / 0.7f);
            float settle = 1f - Mathf.Pow(1f - u, 3f);
            scale = EaseOutBack(u);
            yaw = swayYaw - 360f * turns * (1f - settle);
            offset = new Vector3(side * (1f - settle), lift, depth * (1f - settle));
        }
    }

    private static float EaseOutBack(float u)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(u - 1f, 3f) + c1 * Mathf.Pow(u - 1f, 2f);
    }
}
