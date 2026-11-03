using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

/// <summary>
/// Plays gesture clips on the character, no Animator Controller needed.
/// Keys 1-9 pick a gesture, which then loops until another is picked; Space/Right Arrow skips to the next.
/// Minus/Plus slow down/speed up the animation, Backspace resets speed.
/// Also spins the character turntable-style so it is seen from all sides; R toggles the spin.
/// To add gestures, drop more clips into the Gestures list in the Inspector.
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

    [Header("Turntable")]
    [SerializeField] private bool spin = true;
    [Tooltip("Degrees per second. 30 = one full turn every 12 seconds.")]
    [SerializeField] private float spinSpeed = 30f;

    private PlayableGraph graph;
    private AnimationClipPlayable clipPlayable;
    private int current;
    private float nextSwitchTime;

    private void Start()
    {
        if (fitHeight > 0f)
            FitToHeight();

        if (gestures != null && gestures.Length > 0)
            Play(0);
    }

    // Lets any model drop in regardless of its file units or pivot height.
    private void FitToHeight()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;

        float floorY = transform.position.y;
        Bounds b = GetBounds(renderers);
        if (b.size.y <= 0f) return;

        transform.localScale *= fitHeight / b.size.y;
        b = GetBounds(renderers);
        transform.position += Vector3.up * (floorY - b.min.y);
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

        if (spin)
            transform.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);

        if (gestures == null || gestures.Length == 0) return;

        if (keyboard != null)
        {
            for (int i = 0; i < Mathf.Min(9, gestures.Length); i++)
            {
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame || keyboard[Key.Numpad1 + i].wasPressedThisFrame)
                    Play(i);
            }

            if (keyboard.spaceKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame)
                Play((current + 1) % gestures.Length);

            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                SetSpeed(speed - 0.1f);
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                SetSpeed(speed + 0.1f);
            if (keyboard.backspaceKey.wasPressedThisFrame)
                SetSpeed(1f);
        }

        // With a single clip this restarts it, so non-looping takes still repeat.
        if (autoCycle && Time.time >= nextSwitchTime)
            Play((current + 1) % gestures.Length);
    }

    private void Play(int index)
    {
        if (gestures[index] == null) return;

        if (graph.IsValid())
            graph.Destroy();

        current = index;
        clipPlayable = AnimationPlayableUtilities.PlayClip(GetComponent<Animator>(), gestures[index], out graph);
        clipPlayable.SetSpeed(speed);
        nextSwitchTime = Time.time + gestures[index].length * Mathf.Max(1, repeatsPerGesture) / speed;
    }

    private void SetSpeed(float value)
    {
        speed = Mathf.Clamp(Mathf.Round(value * 10f) / 10f, 0.1f, 2f);
        if (clipPlayable.IsValid())
            clipPlayable.SetSpeed(speed);
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
            graph.Destroy();
    }
}
