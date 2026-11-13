using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows one object at a time in the alcove; Left/Right arrows switch between them.
/// Add more by dropping scene objects into the Objects list, or by putting a model in a Resources
/// folder and adding its path (without extension) to Resource Models.
/// </summary>
public class ObjectSwitcher : MonoBehaviour
{
    [SerializeField] private GameObject[] objects;
    [Tooltip("Models loaded at start from a Resources folder, e.g. Models/Stallion/Stallion. They get spin, touch and animation controls automatically.")]
    [SerializeField] private string[] resourceModels;
    [SerializeField] private float resourceFitHeight = 3.5f;
    [Tooltip("Resource Models with no animation of their own that should pulse like a heartbeat (same paths as above).")]
    [SerializeField] private string[] heartbeatModels = { "Models/HumanHeart/HumanHeart" };
    [SerializeField] private float heartbeatBpm = 60f;
    [Tooltip("Resource Models that start standing still; R then spins them around Still Models Axis (A still changes it).")]
    [SerializeField] private string[] stillModels;
    [SerializeField] private GesturePlayer.SpinAxis stillModelsAxis = GesturePlayer.SpinAxis.Z;
    [Tooltip("Resource Models (logos, text) that start straight facing the viewer and only ever turn around Y.")]
    [SerializeField] private string[] uprightModels;
    [Tooltip("Sideways nudge for Upright Models after centring, world units (negative = left).")]
    [SerializeField] private float uprightModelsShiftX = -0.02f;
    [Header("Shoe, then building")]
    [Tooltip("Shown together as one object: Reveal Front turns twice alone, then Reveal Back grows in behind it and the set turns. " +
             "Both must also be in Resource Models. Empty = off.")]
    [SerializeField] private string revealFront = "Models/Shoe/Chemi-AU-O005";
    [SerializeField] private string revealBack = "Models/Parchetto/Chemi-IT-B001";
    [Tooltip("Where Reveal Front stands, in Reveal Back's own units: on the temple's base, in front of its entrance columns.")]
    [SerializeField] private Vector3 revealFrontSpot = new Vector3(0f, -2.64f, -4.15f);
    [Tooltip("Reveal Front's longest side, in Reveal Back's units (the columns are about 5.4 tall).")]
    [SerializeField] private float revealFrontSize = 3.5f;
    [Tooltip("Turn of Reveal Front, degrees. 90 = side view, toe to the right.")]
    [SerializeField] private float revealFrontYaw = 90f;

    [Header("AIRI <-> UTCN")]
    [Tooltip("Shown together as one object that swaps between the two in a loop (LogoDuet: hold, then spin away into the depth / spin in). " +
             "Both must also be in Resource Models. Empty = off, each shows on its own.")]
    [SerializeField] private string duetFirst = "Models/AIRI_Intro/AIRI_Intro";
    [SerializeField] private string duetSecond = "Models/UTCN/UTCN";
    [Tooltip("Each logo is sized to fit this box (width, height), so both show about as big.")]
    [SerializeField] private Vector2 duetLogoBox = new Vector2(1f, 0.8f);
    [Tooltip("Sideways nudge of the logos after centring, world units (positive = right).")]
    [SerializeField] private float duetShiftX = 0.05f;
    [Tooltip("Resource Model shown when the app starts (same path as above). Empty = the first object in the list.")]
    [SerializeField] private string firstModel;

    private int current;
    private int first;

    // Awake runs before any Start, so the first object is still at its placed spot.
    private void Awake()
    {
        if (resourceModels == null || resourceModels.Length == 0) return;

        var list = new List<GameObject>(objects ?? new GameObject[0]);
        Vector3 spot = list.Count > 0 && list[0] != null ? list[0].transform.position : transform.position;
        bool reveal = !string.IsNullOrEmpty(revealFront) && System.Array.IndexOf(resourceModels, revealFront) >= 0
                      && System.Array.IndexOf(resourceModels, revealBack) >= 0;
        bool duet = !string.IsNullOrEmpty(duetFirst) && System.Array.IndexOf(resourceModels, duetFirst) >= 0
                    && System.Array.IndexOf(resourceModels, duetSecond) >= 0;
        foreach (var path in resourceModels)
        {
            if (reveal && path == revealBack) continue; // built together with Reveal Front
            if (duet && path == duetSecond) continue;   // built together with Duet First
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"ObjectSwitcher: nothing at Resources/{path}");
                continue;
            }

            // Turned to face the camera, which looks down +Z into the alcove. The model's own root rotation is kept:
            // a single-object Blender FBX stores its "stand upright" turn (-90 on X) there.
            var back = reveal && path == revealFront ? Resources.Load<GameObject>(revealBack) : null;
            var second = duet && path == duetFirst ? Resources.Load<GameObject>(duetSecond) : null;
            GameObject go;
            if (back != null)
                go = BuildReveal(prefab, back, spot);
            else if (second != null)
                go = BuildDuet(prefab, second, spot);
            else
            {
                go = Instantiate(prefab, spot, Quaternion.Euler(0f, 180f, 0f) * prefab.transform.rotation);
                go.name = prefab.name;
            }
            var player = go.AddComponent<GesturePlayer>();
            player.FitHeight = resourceFitHeight;
            if (back != null || second != null)
            {
                player.UprightOnly = true; // straight and centred; the sequence moves the parts itself (R still spins the whole set)
                player.Spin = false;
            }
            if (heartbeatModels != null && System.Array.IndexOf(heartbeatModels, path) >= 0)
                player.Heartbeat = heartbeatBpm;
            if (stillModels != null && System.Array.IndexOf(stillModels, path) >= 0)
            {
                player.Spin = false;
                player.Axis = stillModelsAxis;
            }
            if (uprightModels != null && System.Array.IndexOf(uprightModels, path) >= 0)
            {
                player.UprightOnly = true;
                player.ShiftX = uprightModelsShiftX;
            }
            if (second != null)
                player.ShiftX = duetShiftX; // own nudge: Upright Models Shift X was tuned for AIRI alone
            if (path == firstModel)
                first = list.Count;
            list.Add(go);
        }
        objects = list.ToArray();
    }

    // Not turned 180 like the other models: the temple's stairs and entrance columns are already on its -Z side, toward the camera.
    private GameObject BuildReveal(GameObject frontPrefab, GameObject backPrefab, Vector3 spot)
    {
        var root = new GameObject(frontPrefab.name + " + " + backPrefab.name);
        root.transform.position = spot;
        var back = Instantiate(backPrefab, spot, backPrefab.transform.rotation, root.transform);
        var front = Instantiate(frontPrefab, spot, Quaternion.Euler(0f, revealFrontYaw, 0f) * frontPrefab.transform.rotation, root.transform);

        Bounds fb = GesturePlayer.GetBounds(front.GetComponentsInChildren<Renderer>());
        front.transform.localScale *= revealFrontSize / Mathf.Max(fb.size.x, fb.size.y, fb.size.z);
        fb = GesturePlayer.GetBounds(front.GetComponentsInChildren<Renderer>());
        Vector3 stand = back.transform.TransformPoint(revealFrontSpot);
        front.transform.position += stand - new Vector3(fb.center.x, fb.min.y, fb.center.z);

        root.AddComponent<RevealSequence>().Init(front.transform, back.transform);
        return root;
    }

    private GameObject BuildDuet(GameObject firstPrefab, GameObject secondPrefab, Vector3 spot)
    {
        var root = new GameObject(firstPrefab.name + " + " + secondPrefab.name);
        root.transform.position = spot;
        root.AddComponent<LogoDuet>().Init(DuetLogo(firstPrefab, root.transform), DuetLogo(secondPrefab, root.transform));
        return root;
    }

    // Each logo on its own pivot in the middle of the set, centred on it and sized to the same box, so it turns and grows in place.
    private Transform DuetLogo(GameObject prefab, Transform root)
    {
        var pivot = new GameObject(prefab.name).transform;
        pivot.SetParent(root, false);
        var go = Instantiate(prefab, root.position, Quaternion.Euler(0f, 180f, 0f) * prefab.transform.rotation, pivot);
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return pivot;
        Bounds b = GesturePlayer.GetBounds(renderers);
        go.transform.localScale *= Mathf.Min(duetLogoBox.x / b.size.x, duetLogoBox.y / b.size.y);
        b = GesturePlayer.GetBounds(renderers);
        go.transform.position += root.position - b.center;
        return pivot;
    }

    private void Start() => Show(first);

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || objects == null || objects.Length == 0 || keyboard.ctrlKey.isPressed) return; // Ctrl + arrows move the object

        if (keyboard.rightArrowKey.wasPressedThisFrame)
            Show((current + 1) % objects.Length);
        else if (keyboard.leftArrowKey.wasPressedThisFrame)
            Show((current + objects.Length - 1) % objects.Length);
    }

    private void Show(int index)
    {
        current = index;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(i == index);
        }
        // The shown object's saved view (sway, perspective) wins; before its own Start it hands over none, then Start does.
        var shown = objects[index] != null ? objects[index].GetComponent<GesturePlayer>() : null;
        if (shown != null) shown.UseSavedView();
    }
}
