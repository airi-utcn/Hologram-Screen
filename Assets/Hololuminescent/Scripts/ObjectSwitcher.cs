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
        foreach (var path in resourceModels)
        {
            var prefab = Resources.Load<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"ObjectSwitcher: nothing at Resources/{path}");
                continue;
            }

            // Turned to face the camera, which looks down +Z into the alcove. The model's own root rotation is kept:
            // a single-object Blender FBX stores its "stand upright" turn (-90 on X) there.
            var go = Instantiate(prefab, spot, Quaternion.Euler(0f, 180f, 0f) * prefab.transform.rotation);
            go.name = prefab.name;
            var player = go.AddComponent<GesturePlayer>();
            player.FitHeight = resourceFitHeight;
            if (heartbeatModels != null && System.Array.IndexOf(heartbeatModels, path) >= 0)
                player.Heartbeat = heartbeatBpm;
            if (stillModels != null && System.Array.IndexOf(stillModels, path) >= 0)
            {
                player.Spin = false;
                player.Axis = stillModelsAxis;
            }
            if (uprightModels != null && System.Array.IndexOf(uprightModels, path) >= 0)
                player.UprightOnly = true;
            if (path == firstModel)
                first = list.Count;
            list.Add(go);
        }
        objects = list.ToArray();
    }

    private void Start() => Show(first);

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || objects == null || objects.Length == 0) return;

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
    }
}
