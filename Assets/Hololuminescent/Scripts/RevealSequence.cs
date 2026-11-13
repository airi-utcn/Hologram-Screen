using UnityEngine;

/// <summary>
/// Shoe, then building (as in the CGIS colleague's scene): the front model turns a few times on its own,
/// then the back model grows in behind it out of the floor, then the whole set turns (GesturePlayer's spin, R toggles).
/// Starts over each time the object is shown. Built by ObjectSwitcher (Reveal Front / Reveal Back).
/// </summary>
public class RevealSequence : MonoBehaviour
{
    [SerializeField] private float turns = 2f;
    [Tooltip("Degrees per second while the front model turns alone.")]
    [SerializeField] private float turnSpeed = 90f;
    [Tooltip("Seconds the back model takes to grow in.")]
    [SerializeField] private float growTime = 2f;

    private Transform front, grow;
    private Renderer[] frontRenderers;
    private Vector3 frontPosition;
    private Quaternion frontRotation;
    private float turned, grown;
    private bool restart = true;

    public void Init(Transform frontModel, Transform backModel)
    {
        front = frontModel;
        frontRenderers = front.GetComponentsInChildren<Renderer>();
        frontPosition = front.localPosition;
        frontRotation = front.localRotation;
        // Grow from the middle of the building's base, so it rises out of the floor and the set's base height never changes.
        Bounds b = GesturePlayer.GetBounds(backModel.GetComponentsInChildren<Renderer>());
        grow = new GameObject("Grow").transform;
        grow.SetParent(backModel.parent, false);
        grow.position = new Vector3(b.center.x, b.min.y, b.center.z);
        backModel.SetParent(grow, true);
    }

    private void OnEnable() => restart = true;

    // Update, not Start: GesturePlayer.Start must size and centre the set with the building at full size first.
    private void Update()
    {
        if (front == null) return;
        var player = GetComponent<GesturePlayer>();
        if (restart)
        {
            restart = false;
            turned = grown = 0f;
            front.localPosition = frontPosition;
            front.localRotation = frontRotation;
            grow.localScale = Vector3.one * 0.001f; // not 0: a zero scale breaks the renderers' bounds and matrices
            if (player != null) player.Spin = false;
        }

        float full = turns * 360f;
        if (turned < full)
        {
            float step = Mathf.Min(turnSpeed * Time.deltaTime, full - turned);
            front.RotateAround(GesturePlayer.GetBounds(frontRenderers).center, transform.up, step);
            turned += step;
            return;
        }

        if (grown < growTime)
        {
            grown += Time.deltaTime;
            grow.localScale = Vector3.one * Mathf.Max(0.001f, Mathf.SmoothStep(0f, 1f, grown / growTime));
            if (grown >= growTime && player != null) player.Spin = true;
        }
    }
}
