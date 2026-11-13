using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Tab switches to the next experience (the next scene in File > Build Profiles / Build Settings), in any scene.
/// Now: the model showcase (Character) and the asteroid orbits (VQ1_Hologram). Tab is free in both.
/// Created by itself when the app starts, so no scene needs to contain it.
/// </summary>
public class SceneSwitcher : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("SceneSwitcher");
        DontDestroyOnLoad(go);
        go.AddComponent<SceneSwitcher>();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        int count = SceneManager.sceneCountInBuildSettings;
        if (keyboard == null || count < 2 || !keyboard.tabKey.wasPressedThisFrame) return;
        SceneManager.LoadScene((SceneManager.GetActiveScene().buildIndex + 1) % count);
    }
}
