using System.Collections.Generic;
using UnityEngine;

namespace Cheminova
{
    /// <summary>Owns runtime shoe materials and switches only their albedo texture.</summary>
    public sealed class CheminovaShoeMaterial : MonoBehaviour
    {
        private static readonly string[] TextureProperties = {
            "baseColorTexture", "_BaseColorMap", "_BaseMap", "_MainTex", "_UnlitColorMap"
        };
        private static readonly string[] ColorProperties = { "baseColorFactor", "_BaseColor", "_Color", "_UnlitColor" };
        private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> owned = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, (string property, Texture texture)> baseTextures
            = new Dictionary<Material, (string, Texture)>();
        private Texture alternateAlbedo;
        private bool alternateApplied;

        public void Init(Shader shader, bool useUnlit, Texture alternateTexture)
        {
            if (originals.Count != 0) return;
            if (useUnlit && shader == null)
            {
                Debug.LogWarning("Cheminova: assign the HDRP/Unlit shader in PresentationSettings.", this);
                return;
            }
            alternateAlbedo = alternateTexture;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
            {
                var sourceMaterials = renderer.sharedMaterials;
                var replacements = (Material[])sourceMaterials.Clone();
                bool changed = false;
                for (int i = 0; i < sourceMaterials.Length; i++)
                {
                    var source = sourceMaterials[i];
                    if (source == null) continue;
                    if (!owned.TryGetValue(source, out var target))
                    {
                        string sourceProperty = FindBaseTextureProperty(source);
                        if (sourceProperty == null) continue;
                        target = useUnlit ? CreateUnlit(source, shader, sourceProperty)
                            : new Material(source) { name = source.name + " (Cheminova runtime)" };
                        if (target == null) continue;
                        owned.Add(source, target);
                        string targetProperty = useUnlit ? "_UnlitColorMap" : sourceProperty;
                        baseTextures.Add(target, (targetProperty, target.GetTexture(targetProperty)));
                    }
                    replacements[i] = target;
                    changed = true;
                }
                if (!changed) continue;
                originals.Add(renderer, sourceMaterials);
                renderer.sharedMaterials = replacements;
            }
        }

        public void SetAlternateAlbedo(bool alternate)
        {
            if (alternateAlbedo == null || alternateApplied == alternate) return;
            foreach (var entry in baseTextures)
                if (entry.Key != null)
                    entry.Key.SetTexture(entry.Value.property, alternate ? alternateAlbedo : entry.Value.texture);
            alternateApplied = alternate;
        }

        private string FindBaseTextureProperty(Material source)
        {
            foreach (var property in TextureProperties)
            {
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                    return property;
            }
            Debug.LogWarning($"Cheminova: base texture missing on shoe material {source.name}; preserving its original material.", this);
            return null;
        }

        private Material CreateUnlit(Material source, Shader shader, string textureProperty)
        {
            Color tint = Color.white;
            foreach (var property in ColorProperties)
            {
                if (!source.HasProperty(property)) continue;
                tint = source.GetColor(property);
                break;
            }
            var target = new Material(shader) { name = source.name + " (Cheminova unlit runtime)" };
            target.SetTexture("_UnlitColorMap", source.GetTexture(textureProperty));
            target.SetTextureScale("_UnlitColorMap", source.GetTextureScale(textureProperty));
            target.SetTextureOffset("_UnlitColorMap", source.GetTextureOffset(textureProperty));
            target.SetColor("_UnlitColor", tint);
            target.SetColor("_EmissiveColor", Color.black);
            return target;
        }

        private void OnDestroy()
        {
            foreach (var entry in originals)
                if (entry.Key != null) entry.Key.sharedMaterials = entry.Value;
            foreach (var material in owned.Values)
                if (material != null) Destroy(material);
            originals.Clear();
            owned.Clear();
            baseTextures.Clear();
        }
    }
}
