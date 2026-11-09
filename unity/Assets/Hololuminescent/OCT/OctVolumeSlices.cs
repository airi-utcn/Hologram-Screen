using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hololuminescent.Oct
{
    /// <summary>
    /// Shows the OCT intensity volume as real 3D surfaces (no flat slices).
    ///
    /// The volume is lightly smoothed and two iso-surfaces are extracted with
    /// surface nets:
    ///   * skin   - the outer boundary of the retina (light, see-through)
    ///   * layers - the bright reflective bands inside it (RNFL, RPE; darker)
    /// Both are closed, smooth-shaded HDRP Lit meshes, so every view has
    /// shading, thickness and side walls. Fluid meshes still draw last and
    /// show through, like an X-ray.
    ///
    /// Slab mode cuts a thick 3D piece around one B-scan (closed on both cut
    /// faces) and lets you scrub through the scan with , and .
    ///
    /// (The class keeps its old name so existing prefabs keep working.)
    /// </summary>
    [DisallowMultipleComponent]
    public class OctVolumeSlices : MonoBehaviour
    {
        public enum Mode { Full, Layers, Slab, Off }

        [Header("Data (set by the importer)")]
        public TextAsset volumeData;     // volume.bytes  (uint8, X fastest)
        public TextAsset metaJson;       // meta.json
        [HideInInspector] public Material sliceTemplate;   // unused, kept for older prefabs

        [Header("Look")]
        public Mode mode = Mode.Full;
        [Tooltip("HDRP Lit material to clone. Taken from the scan's tissue mesh when empty.")]
        public Material surfaceTemplate;
        [Tooltip("Signal level (0..1) of the outer retina boundary.")]
        [Range(0.1f, 0.6f)] public float skinLevel = 0.28f;
        [Tooltip("Signal level (0..1) of the bright layers (RNFL, RPE).")]
        [Range(0.2f, 0.8f)] public float layerLevel = 0.45f;
        public Color skinColor = new Color(0.58f, 0.68f, 0.92f, 0.32f);
        public Color layerColor = new Color(0.16f, 0.22f, 0.46f, 0.6f);
        [Tooltip("Multiplies both opacities (K / L).")]
        [Range(0.2f, 2.5f)] public float opacity = 1f;

        [Header("Slab mode")]
        public int bscan = -1;           // -1 = middle
        [Range(1, 10)] public int slabHalfWidth = 2;

        public OctScanMeta Meta { get; private set; }
        public int BScanCount => _nz;

        int _nx, _ny, _nz, _px, _py, _pz;  // _p* = padded with one empty voxel on every side
        float[] _f;                        // smoothed volume, 0..1, padded
        float[] _sp, _org;
        MeshRenderer _skin, _layers, _slabSkin, _slabLayers;
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        int _slabFor = -1;
        bool _built;

        void Awake() => Build();

        void OnDestroy() => Clear();

        void OnValidate()
        {
            if (_built && Application.isPlaying) { ApplyColors(); UpdateView(); }
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            Clear();
            if (volumeData == null || metaJson == null)
            {
                Debug.LogWarning($"{name}: OctVolumeSlices needs volumeData and metaJson.", this);
                return;
            }
            Meta = OctScanMeta.Parse(metaJson.text);
            if (!Meta.HasVolume) return;
            var v = Meta.volume;
            _nx = v.dims[0]; _ny = v.dims[1]; _nz = v.dims[2];
            _sp = v.spacing; _org = v.origin;
            byte[] data = volumeData.bytes;
            if (data.Length < _nx * _ny * _nz)
            {
                Debug.LogError($"{name}: volume has {data.Length} bytes, expected {_nx * _ny * _nz}.", this);
                return;
            }
            if (bscan < 0 || bscan >= _nz) bscan = _nz / 2;

            if (!surfaceTemplate)
            {
                var controls = GetComponentInParent<OctScanControls>();
                if (controls && controls.tissue != null)
                    foreach (var r in controls.tissue) if (r && r.sharedMaterial) { surfaceTemplate = r.sharedMaterial; break; }
            }
            if (!surfaceTemplate)
            {
                Debug.LogWarning($"[OCT] {name}: no Lit material to clone for the 3D surfaces.", this);
                return;
            }

            _f = Smooth(data);
            var skinMat = SurfaceMaterial("oct skin");
            var layerMat = SurfaceMaterial("oct layers");
            _layers = AddPart("Layers 3D", Extract(layerLevel, 0, _pz - 1), layerMat, 100000);
            _skin = AddPart("Skin 3D", Extract(skinLevel, 0, _pz - 1), skinMat, 100001);
            _slabLayers = AddPart("Slab layers", null, layerMat, 100000);
            _slabSkin = AddPart("Slab skin", null, skinMat, 100001);
            _built = true;
            ApplyColors();
            UpdateView();
        }

        void Clear()
        {
            foreach (var r in new[] { _skin, _layers, _slabSkin, _slabLayers })
                if (r) Destroy(r.gameObject);
            foreach (var o in _owned) if (o) Destroy(o);
            _owned.Clear();
            _skin = _layers = _slabSkin = _slabLayers = null;
            _slabFor = -1; _built = false;
        }

        // [1 2 1]/4 twice along each axis: removes speckle so the surfaces are smooth
        float[] Smooth(byte[] data)
        {
            int n = _nx * _ny * _nz;
            var a = new float[n];
            for (int i = 0; i < n; i++) a[i] = data[i] / 255f;
            var b = new float[n];
            int[] dim = { _nx, _ny, _nz };
            int[] stride = { 1, _nx, _nx * _ny };
            for (int axis = 0; axis < 3; axis++)
                for (int pass = 0; pass < 2; pass++)
                {
                    int s = stride[axis], len = dim[axis];
                    for (int i = 0; i < n; i++)
                    {
                        int c = i / s % len;
                        float lo = a[c > 0 ? i - s : i], hi = a[c < len - 1 ? i + s : i];
                        b[i] = (lo + 2f * a[i] + hi) * 0.25f;
                    }
                    var t = a; a = b; b = t;
                }

            _px = _nx + 2; _py = _ny + 2; _pz = _nz + 2;
            var f = new float[_px * _py * _pz];   // zero border closes every surface
            for (int z = 0; z < _nz; z++)
                for (int y = 0; y < _ny; y++)
                    Array.Copy(a, _nx * (y + _ny * z), f, 1 + _px * (y + 1 + _py * (z + 1)), _nx);
            return f;
        }

        // ------------------------------------------------------------------ surface nets

        static readonly int[,] Corner = { { 0, 0, 0 }, { 1, 0, 0 }, { 0, 1, 0 }, { 1, 1, 0 }, { 0, 0, 1 }, { 1, 0, 1 }, { 0, 1, 1 }, { 1, 1, 1 } };
        static readonly int[,] CubeEdge = { { 0, 1 }, { 2, 3 }, { 4, 5 }, { 6, 7 }, { 0, 2 }, { 1, 3 }, { 4, 6 }, { 5, 7 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };

        /// <summary>
        /// Iso-surface at `level` of the padded grid, using only padded z
        /// layers z0..z1 (everything outside counts as empty, so a slab comes
        /// out closed on its cut faces).
        /// </summary>
        Mesh Extract(float level, int z0, int z1)
        {
            float F(int x, int y, int z) => z < z0 || z > z1 ? 0f : _f[x + _px * (y + _py * z)];

            int cx = _px - 1, cy = _py - 1, cz = _pz - 1;
            int cz0 = Mathf.Max(0, z0 - 1), cz1 = Mathf.Min(cz - 1, z1);
            var vid = new int[cx * cy * cz];
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var c = new float[8];

            for (int z = cz0; z <= cz1; z++)
                for (int y = 0; y < cy; y++)
                    for (int x = 0; x < cx; x++)
                    {
                        int id = x + cx * (y + cy * z);
                        vid[id] = -1;
                        int inside = 0;
                        for (int k = 0; k < 8; k++)
                        {
                            c[k] = F(x + Corner[k, 0], y + Corner[k, 1], z + Corner[k, 2]) - level;
                            if (c[k] > 0) inside++;
                        }
                        if (inside == 0 || inside == 8) continue;
                        Vector3 acc = Vector3.zero; int n = 0;
                        for (int e = 0; e < 12; e++)
                        {
                            int a = CubeEdge[e, 0], b = CubeEdge[e, 1];
                            if (c[a] > 0 == c[b] > 0) continue;
                            float t = c[a] / (c[a] - c[b]);
                            acc += new Vector3(Corner[a, 0] + t * (Corner[b, 0] - Corner[a, 0]),
                                               Corner[a, 1] + t * (Corner[b, 1] - Corner[a, 1]),
                                               Corner[a, 2] + t * (Corner[b, 2] - Corner[a, 2]));
                            n++;
                        }
                        acc /= n;
                        vid[id] = verts.Count;
                        // padded grid index -> voxel index -> glTF mm -> Unity
                        float gx = _org[0] + (x + acc.x - 1) * _sp[0];
                        float gy = _org[1] + (y + acc.y - 1) * _sp[1];
                        float gz = _org[2] + (z + acc.z - 1) * _sp[2];
                        verts.Add(OctSpace.ToUnity(gx, gy, gz));
                    }

            int Cell(int x, int y, int z) =>
                x < 0 || y < 0 || z < cz0 || z > cz1 ? -1 : vid[x + cx * (y + cy * z)];

            // every grid edge with a sign change gets a quad joining the 4 cells around it
            int zs = Mathf.Max(0, z0 - 1), ze = Mathf.Min(_pz - 1, z1 + 1);
            var p = new int[3];
            for (int z = zs; z <= ze; z++)
                for (int y = 0; y < _py; y++)
                    for (int x = 0; x < _px; x++)
                    {
                        float a0 = F(x, y, z) - level;
                        for (int axis = 0; axis < 3; axis++)
                        {
                            int bx = x + (axis == 0 ? 1 : 0), by = y + (axis == 1 ? 1 : 0), bz = z + (axis == 2 ? 1 : 0);
                            if (bx >= _px || by >= _py || bz >= _pz) continue;
                            float b0 = F(bx, by, bz) - level;
                            if (a0 > 0 == b0 > 0) continue;
                            int u = (axis + 1) % 3, w = (axis + 2) % 3;
                            int q0 = -1, q1 = -1, q2 = -1, q3 = -1;
                            for (int k = 0; k < 4; k++)
                            {
                                p[0] = x; p[1] = y; p[2] = z;
                                if (k == 1 || k == 2) p[u]--;
                                if (k >= 2) p[w]--;
                                int v = Cell(p[0], p[1], p[2]);
                                if (k == 0) q0 = v; else if (k == 1) q1 = v; else if (k == 2) q2 = v; else q3 = v;
                            }
                            if (q0 < 0 || q1 < 0 || q2 < 0 || q3 < 0) continue;
                            if (a0 > 0) { int s = q1; q1 = q3; q3 = s; }
                            tris.Add(q0); tris.Add(q1); tris.Add(q2);
                            tris.Add(q0); tris.Add(q2); tris.Add(q3);
                        }
                    }

            // make the normals face outward whatever the axis mirroring did:
            // a closed surface with outward normals has positive signed volume
            double vol = 0;
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = verts[tris[t]], b = verts[tris[t + 1]], d = verts[tris[t + 2]];
                vol += Vector3.Dot(a, Vector3.Cross(b - a, d - a));
            }
            if (vol < 0)
                for (int t = 0; t < tris.Count; t += 3) { int s = tris[t + 1]; tris[t + 1] = tris[t + 2]; tris[t + 2] = s; }

            var mesh = new Mesh { name = $"oct iso {level:0.00}", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _owned.Add(mesh);
            return mesh;
        }

        // ------------------------------------------------------------------ renderers and materials

        MeshRenderer AddPart(string goName, Mesh mesh, Material mat, int priority)
        {
            var go = new GameObject(goName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.rendererPriority = priority;   // layers, then skin, then (higher queue) fluids
            return mr;
        }

        static readonly Type HDMaterial = Type.GetType(
            "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");

        // Transparent Lit, front faces only: a see-through closed shell shows
        // just its near side, which keeps it clean instead of muddy.
        Material SurfaceMaterial(string matName)
        {
            var m = new Material(surfaceTemplate) { name = matName };
            Set(m, "_DoubleSidedEnable", 0f);
            Set(m, "_CullMode", 2f); Set(m, "_CullModeForward", 2f); Set(m, "_TransparentCullMode", 2f);
            Set(m, "_Cull", 2f);
            Set(m, "_Smoothness", 0.4f);
            m.DisableKeyword("_DOUBLESIDED_ON");
            var validate = HDMaterial?.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
            try { validate?.Invoke(null, new object[] { m }); }
            catch (Exception e) { Debug.LogWarning($"[OCT] HDMaterial.ValidateMaterial: {e.InnerException?.Message ?? e.Message}"); }
            _owned.Add(m);
            return m;
        }

        static void Set(Material m, string p, float f) { if (m.HasProperty(p)) m.SetFloat(p, f); }

        static void SetColor(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public void ApplyColors()
        {
            if (!_built) return;
            var s = skinColor; s.a = Mathf.Clamp01(s.a * opacity);
            var l = layerColor; l.a = Mathf.Clamp01(l.a * opacity);
            SetColor(_skin.sharedMaterial, s);
            SetColor(_layers.sharedMaterial, l);
        }

        // ------------------------------------------------------------------ view

        void UpdateView()
        {
            if (!_built) return;
            _skin.enabled = mode == Mode.Full;
            _layers.enabled = mode == Mode.Full || mode == Mode.Layers;
            bool slab = mode == Mode.Slab;
            if (slab && _slabFor != bscan) RebuildSlab();
            _slabSkin.enabled = _slabLayers.enabled = slab;
        }

        void RebuildSlab()
        {
            var mf = _slabSkin.GetComponent<MeshFilter>();
            var ml = _slabLayers.GetComponent<MeshFilter>();
            if (mf.sharedMesh) { _owned.Remove(mf.sharedMesh); Destroy(mf.sharedMesh); }
            if (ml.sharedMesh) { _owned.Remove(ml.sharedMesh); Destroy(ml.sharedMesh); }
            int z0 = 1 + Mathf.Max(0, bscan - slabHalfWidth);       // padded z
            int z1 = 1 + Mathf.Min(_nz - 1, bscan + slabHalfWidth);
            mf.sharedMesh = Extract(skinLevel, z0, z1);
            ml.sharedMesh = Extract(layerLevel, z0, z1);
            _slabFor = bscan;
        }

        // ------------------------------------------------------------------ API for controls

        public void CycleMode()
        {
            mode = (Mode)(((int)mode + 1) % 4);
            UpdateView();
        }

        public void StepBScan(int delta)
        {
            if (!_built) return;
            mode = Mode.Slab;
            bscan = Mathf.Clamp(bscan + delta, 0, _nz - 1);
            UpdateView();
        }

        public void ScaleDensity(float factor)
        {
            opacity = Mathf.Clamp(opacity * factor, 0.2f, 2.5f);
            ApplyColors();
        }
    }
}
