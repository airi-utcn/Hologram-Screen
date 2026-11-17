using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cheminova
{
    /// <summary>Decorates existing showcase models after their Awake, before their Start.</summary>
    public static class CheminovaBootstrap
    {
        private const string SettingsPath = "Cheminova/PresentationSettings";
        private static CheminovaSettings settings;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            settings = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (settings == null) settings = Resources.Load<CheminovaSettings>(SettingsPath);
            if (settings == null || Array.IndexOf(settings.targetScenes ?? Array.Empty<string>(), scene.name) < 0) return;

            var stages = new List<ObjectSwitcher>();
            var players = new List<GesturePlayer>();
            foreach (var root in scene.GetRootGameObjects())
            {
                stages.AddRange(root.GetComponentsInChildren<ObjectSwitcher>(true));
                players.AddRange(root.GetComponentsInChildren<GesturePlayer>(true));
            }
            if (stages.Count == 0) return;

            var elevatedNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in settings.elevatedModelResources ?? Array.Empty<string>())
            {
                var prefab = Resources.Load<GameObject>(path);
                if (prefab != null) elevatedNames.Add(prefab.name);
                else Debug.LogWarning($"Cheminova: height profile model missing at Resources/{path}");
            }
            var front = Resources.Load<GameObject>(settings.revealFrontResource);
            var back = Resources.Load<GameObject>(settings.revealBackResource);
            string revealName = front != null && back != null ? front.name + " + " + back.name : null;

            foreach (var player in players)
            {
                if (MatchesHeightProfile(player.name, elevatedNames)) player.HeightOffset = settings.modelHeightOffset;
                if (revealName == null || player.name != revealName || player.GetComponent<RevealSequence>() == null) continue;
                player.HeightOffset = settings.modelHeightOffset + settings.contentVerticalOffset;

                // Runtime model roots are siblings of Showcase, rather than its children.
                ObjectSwitcher stage = stages[0];
                float distance = (stage.Spot - player.transform.position).sqrMagnitude;
                foreach (var candidate in stages)
                {
                    float candidateDistance = (candidate.Spot - player.transform.position).sqrMagnitude;
                    if (candidateDistance < distance) { stage = candidate; distance = candidateDistance; }
                }
                var shoe = FindModel(player.transform, front.name);
                var building = FindModel(player.transform, back.name);
                if (player.GetComponent<CheminovaPresentation>() == null)
                    player.gameObject.AddComponent<CheminovaPresentation>().Init(stage.transform, settings, shoe, building);
                if (shoe != null && shoe.GetComponent<CheminovaShoeMaterial>() == null)
                    shoe.gameObject.AddComponent<CheminovaShoeMaterial>()
                        .Init(settings.logoShader, settings.shoeUnlit, settings.shoeAlternateAlbedo);
                if (settings.alternatingShowcase && player.GetComponent<CheminovaShowcaseLoop>() == null)
                {
                    if (shoe != null && building != null)
                        player.gameObject.AddComponent<CheminovaShowcaseLoop>()
                            .Init(shoe, building, player.GetComponent<RevealSequence>(), settings);
                    else Debug.LogWarning("Cheminova: alternating showcase models could not be found.", player);
                }
                if (settings.floorShadows && shoe != null && building != null && player.GetComponent<CheminovaFloorShadow>() == null)
                    player.gameObject.AddComponent<CheminovaFloorShadow>().Init(stage.transform, shoe, building, settings);
            }
        }

        private static Transform FindModel(Transform root, string prefabName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name == prefabName || child.name == prefabName + "(Clone)") return child;
            return null;
        }

        internal static bool MatchesHeightProfile(string name, HashSet<string> modelNames)
        {
            if (modelNames.Contains(name)) return true;
            var parts = name.Split(new[] { " + " }, StringSplitOptions.None);
            if (parts.Length < 2) return false;
            foreach (var part in parts) if (!modelNames.Contains(part)) return false;
            return true;
        }
    }
}
