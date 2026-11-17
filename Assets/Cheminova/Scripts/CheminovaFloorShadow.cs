using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Cheminova
{
    // Stage-owned translucent blobs, independent of lighting, model fitting and input colliders.
    [DefaultExecutionOrder(2000)]
    public sealed class CheminovaFloorShadow : MonoBehaviour
    {
        private sealed class Shadow
        {
            internal Renderer[] sources;
            internal GameObject quad;
            internal Material material;
            internal ShadowCastingMode[] originalCasting;
        }

        private CheminovaSettings settings;
        private CheminovaShowcaseLoop loop;
        private Shadow[] shadows;
        private Mesh mesh;
        private Texture2D texture;
        private readonly RaycastHit[] hits = new RaycastHit[32];

        public void Init(Transform stage, Transform shoe, Transform building, CheminovaSettings profile)
        {
            if (shadows != null || profile == null || profile.logoShader == null) return;
            settings = profile;
            loop = GetComponent<CheminovaShowcaseLoop>();
            mesh = new Mesh { name = "Cheminova floor shadow quad" };
            mesh.vertices = new[] {
                new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f),
                new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f)
            };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            const int resolution = 128;
            texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true) {
                name = "Cheminova soft shadow", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            var pixels = new Color[resolution * resolution];
            for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                    pixels[y * resolution + x] = new Color(0, 0, 0, RadialAlpha(x / (resolution - 1f), y / (resolution - 1f)));
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            shadows = new[] { Create(stage, shoe, "Shoe floor shadow"), Create(stage, building, "Parchetto floor shadow") };
        }

        private Shadow Create(Transform stage, Transform model, string name)
        {
            var material = new Material(settings.logoShader) { name = name + " (runtime)" };
            material.SetTexture("_UnlitColorMap", texture);
            material.SetColor("_UnlitColor", new Color(0, 0, 0, settings.floorShadowOpacity));
            material.SetColor("_EmissiveColor", Color.black);
            material.SetFloat("_BlendMode", 0f);
            material.SetFloat("_AlphaCutoffEnable", 0f);
            material.SetFloat("_TransparentZWrite", 0f);
            material.SetFloat("_EnableFogOnTransparent", 0f);
            HDMaterial.SetSurfaceType(material, true);
            // Keep a faint shadow visible even when the lit floor is brighter than white.
            HDMaterial.SetRenderingPass(material, HDMaterial.RenderingPass.AfterPostProcess);
            HDMaterial.ValidateMaterial(material);
            material.SetFloat("_ZTestTransparent", (float)CompareFunction.LessEqual);

            var quad = new GameObject(name);
            quad.layer = gameObject.layer;
            quad.transform.SetParent(stage, false);
            quad.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            quad.SetActive(false);
            var sources = model.GetComponentsInChildren<Renderer>(true);
            var originalCasting = new ShadowCastingMode[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                originalCasting[i] = sources[i].shadowCastingMode;
                sources[i].shadowCastingMode = ShadowCastingMode.Off;
            }
            return new Shadow { sources = sources, quad = quad, material = material, originalCasting = originalCasting };
        }

        private void LateUpdate()
        {
            if (shadows == null) return;
            foreach (var shadow in shadows)
            {
                if (!TryBounds(shadow.sources, out var bounds) || Mathf.Max(bounds.size.x, bounds.size.z) < 0.01f
                    || !TryFloor(bounds, out var hit))
                {
                    shadow.quad.SetActive(false);
                    continue;
                }
                float zoom = loop != null && loop.enabled ? loop.VisibleModelScale : 1f;
                float opacity = Opacity(settings.floorShadowOpacity, Mathf.Max(0, bounds.min.y - hit.point.y), zoom);
                if (opacity < 0.0001f) { shadow.quad.SetActive(false); continue; }
                shadow.quad.transform.SetPositionAndRotation(hit.point + hit.normal * settings.floorShadowLift,
                    Quaternion.FromToRotation(Vector3.up, hit.normal));
                var parentScale = shadow.quad.transform.parent.lossyScale;
                shadow.quad.transform.localScale = new Vector3(
                    Mathf.Max(0.15f, bounds.size.x * settings.floorShadowSize) / Mathf.Max(0.0001f, Mathf.Abs(parentScale.x)),
                    1f,
                    Mathf.Max(0.15f, bounds.size.z * settings.floorShadowSize) / Mathf.Max(0.0001f, Mathf.Abs(parentScale.z)));
                shadow.material.SetColor("_UnlitColor", new Color(0, 0, 0, opacity));
                shadow.quad.SetActive(true);
            }
        }

        private bool TryFloor(Bounds bounds, out RaycastHit floor)
        {
            floor = default;
            float closest = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(new Vector3(bounds.center.x, bounds.max.y + 0.25f, bounds.center.z),
                Vector3.down, hits, 50f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.8f || hit.distance >= closest) continue;
                floor = hit;
                closest = hit.distance;
            }
            return !float.IsPositiveInfinity(closest);
        }

        private static bool TryBounds(Renderer[] sources, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (var source in sources)
            {
                if (source == null || !source.enabled || !source.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = source.bounds; found = true; }
                else bounds.Encapsulate(source.bounds);
            }
            return found;
        }

        internal static float RadialAlpha(float u, float v)
        {
            float x = u * 2f - 1f, y = v * 2f - 1f;
            float edge = Mathf.Clamp01(1f - x * x - y * y);
            return edge * edge * edge;
        }

        internal static float Opacity(float strength, float height, float zoom)
        {
            return Mathf.Clamp(strength, 0f, 0.4f) * Mathf.Clamp01(zoom) / (1f + Mathf.Max(0f, height) * 0.15f);
        }

        private void OnDisable()
        {
            if (shadows == null) return;
            foreach (var shadow in shadows) if (shadow.quad != null) shadow.quad.SetActive(false);
        }

        private void OnDestroy()
        {
            if (shadows != null) foreach (var shadow in shadows)
            {
                for (int i = 0; i < shadow.sources.Length; i++)
                    if (shadow.sources[i] != null) shadow.sources[i].shadowCastingMode = shadow.originalCasting[i];
                if (shadow.quad != null) Destroy(shadow.quad);
                if (shadow.material != null) Destroy(shadow.material);
            }
            if (mesh != null) Destroy(mesh);
            if (texture != null) Destroy(texture);
        }
    }
}
