using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// One object's saved look: where it stands, how big, how it is turned, spin and start animation.
/// F5 saves the shown object, F8 forgets it; applied at start in place of the defaults.
/// </summary>
[System.Serializable]
public class ObjectConfig
{
    public string name;
    [Tooltip("World position. Y is only informative: the height comes from lift, so the object stays on the floor after a pinch.")]
    public Vector3 position;
    [Tooltip("Height above the default hover height, world units (Ctrl + Up / Down).")]
    public float lift;
    public Vector3 rotation;
    public Vector3 scale;
    public bool spin;
    public string spinAxis;
    public float spinSpeed;
    [Tooltip("Animation it starts with, 1-9 (the number key). 0 = none / the first.")]
    public int animation;
    public float animationSpeed;
    [Tooltip("Animation paused (Space) at animationTime seconds into the clip.")]
    public bool paused;
    public float animationTime;

    [Tooltip("View while this object shows (D, [ ], O / P). perspective 0 = not saved, the shared view is used.")]
    public bool sway;
    public float swayAngle;
    public float perspective;
}

/// <summary>
/// All saved objects, in StreamingAssets/object_configs.json: inside the project in the editor (so it is committed
/// and copied into builds), next to the app's data in a build. Hand-editable; the object name is the key.
/// </summary>
public static class ObjectConfigs
{
    [System.Serializable]
    private class Store { public List<ObjectConfig> objects = new List<ObjectConfig>(); }

    private static Store store;
    private static bool unreadable; // a broken file is never overwritten, so hand edits aren't lost

    public static string FilePath => Path.Combine(Application.streamingAssetsPath, "object_configs.json");

    private static Store Data
    {
        get
        {
            if (store != null) return store;
            try
            {
                if (File.Exists(FilePath))
                    store = JsonUtility.FromJson<Store>(File.ReadAllText(FilePath));
            }
            catch (System.Exception e)
            {
                unreadable = true;
                Debug.LogWarning($"ObjectConfigs: can't read {FilePath}, saved settings are ignored and saving is off until it is fixed: {e.Message}");
            }
            store ??= new Store();
            store.objects ??= new List<ObjectConfig>();
            return store;
        }
    }

    public static ObjectConfig Get(string name) => Data.objects.Find(c => c.name == name);

    public static bool Save(ObjectConfig config)
    {
        Data.objects.RemoveAll(c => c.name == config.name);
        Data.objects.Add(config);
        return Write();
    }

    public static bool Remove(string name) => Data.objects.RemoveAll(c => c.name == name) > 0 && Write();

    private static bool Write()
    {
        if (unreadable) return false;
        try
        {
            Directory.CreateDirectory(Application.streamingAssetsPath);
            File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"ObjectConfigs: can't write {FilePath}: {e.Message}");
            return false;
        }
    }
}
