using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hololuminescent.Oct
{
    /// <summary>
    /// Shows the OCT intensity volume as stacks of textured slices.
    ///
    /// Built for the Looking Glass HLD, where pure white is see-through and
    /// dark tones are visible: each slice is drawn as dark "ink" whose alpha
    /// follows the OCT signal, so bright tissue becomes visible ink and the
    /// empty background disappears into the physical alcove.
    ///
    /// Three stacks (one per axis) are built; the one facing the camera most
    /// directly is shown, so the volume never turns edge-on while it spins.
    /// </summary>
    [DisallowMultipleComponent]
    public class OctVolumeSlices : MonoBehaviour
    {
        public enum Mode { Volume, SingleBScan, Off }

        [Header("Data (set by the importer)")]
        public TextAsset volumeData;     // volume.bytes  (uint8, X fastest)
        public TextAsset metaJson;       // meta.json
        [Tooltip("HDRP/Unlit, Transparent, double-sided. Cloned per slice.")]
        public Material sliceTemplate;

        [Header("Look")]
        public Mode mode = Mode.Volume;
        [Tooltip("Ink colour. Dark = clearly visible on the HLD.")]
        public Color ink = new Color(0.07f, 0.10f, 0.20f, 1f);
        [Range(0.02f, 1f)] public float density = 0.22f;
        [Tooltip("Raise to hide more of the speckle noise (added to the exported threshold).")]
        [Range(0f, 0.6f)] public float extraThreshold = 0.05f;
        [Range(0.4f, 3f)] public float gamma = 1.4f;

        [Header("Single B-scan mode")]
        public int bscan = -1;           // -1 = middle
        [Range(0.2f, 1f)] public float bscanOpacity = 0.9f;

        [Tooltip("Leave empty: uses Camera.main, then any camera (the HLD camera is not tagged).")]
        public Camera viewCamera;

        public OctScanMeta Meta { get; private set; }
        public int BScanCount => _dims[2];

        readonly List<Renderer>[] _slices = { new List<Renderer>(), new List<Renderer>(), new List<Renderer>() };
        readonly List<Material> _materials = new List<Material>();
        readonly List<Texture2D> _textures = new List<Texture2D>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        readonly Transform[] _stacks = new Transform[3];
        int[] _dims = { 0, 0, 0 };
        float[] _sp, _org;
        int _shownAxis = -1, _shownSign;
        Mode _shownMode = (Mode)(-1);
        bool _built;

        void Awake() => Build();

        void OnDestroy() => Clear();

        void OnValidate()
        {
            if (_built && Application.isPlaying) ApplyColors();
        }

        // ------------------------------------------------------------------ build

        public void Build()
        {
            Clear();
            if (volumeData == null || metaJson == null || sliceTemplate == null)
            {
                Debug.LogWarning($"{name}: OctVolumeSlices needs volumeData, metaJson and sliceTemplate.", this);
                return;
            }
            Meta = OctScanMeta.Parse(metaJson.text);
            if (!Meta.HasVolume) return;

            var v = Meta.volume;
            _dims = v.dims; _sp = v.spacing; _org = v.origin;
            byte[] data = volumeData.bytes;
            int nx = _dims[0], ny = _dims[1], nz = _dims[2];
            if (data.Length < nx * ny * nz)
            {
                Debug.LogError($"{name}: volume has {data.Length} bytes, expected {nx * ny * nz}.", this);
                return;
            }
            if (bscan < 0 || bscan >= nz) bscan = nz / 2;

            // alpha lookup table: threshold + gamma ramp
            float thr = Mathf.Clamp01(v.threshold + extraThreshold);
            var lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                float t = Mathf.Clamp01((i / 255f - thr) / Mathf.Max(1e-4f, 1f - thr));
                lut[i] = (byte)Mathf.RoundToInt(Mathf.Pow(t, gamma) * 255f);
            }

            string[] axisName = { "X (lateral)", "Y (depth)", "Z (B-scans)" };
            for (int axis = 0; axis < 3; axis++)
            {
                var stack = new GameObject("Slices " + axisName[axis]).transform;
                stack.SetParent(transform, false);
                stack.gameObject.SetActive(false);
                _stacks[axis] = stack;

                // texture axes (u, v) for slices normal to `axis`
                int ua = axis == 0 ? 2 : 0;
                int va = axis == 1 ? 2 : 1;
                Mesh quad = BuildQuad(axis, ua, va);
                _meshes.Add(quad);

                int count = _dims[axis], w = _dims[ua], h = _dims[va];
                for (int s = 0; s < count; s++)
                {
                    var px = new Color32[w * h];
                    var idx = new int[3];
                    idx[axis] = s;
                    for (int y = 0; y < h; y++)
                    {
                        idx[va] = y;
                        for (int x = 0; x < w; x++)
                        {
                            idx[ua] = x;
                            byte a = lut[data[idx[0] + nx * (idx[1] + ny * idx[2])]];
                            px[y * w + x] = new Color32(255, 255, 255, a);
                        }
                    }
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                    {
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        name = $"oct_{axis}_{s}"
                    };
                    tex.SetPixels32(px);
                    tex.Apply(false, true);
                    _textures.Add(tex);

                    var mat = new Material(sliceTemplate) { name = tex.name };
                    SetTexture(mat, tex);
                    _materials.Add(mat);

                    var go = new GameObject($"slice {s:000}");
                    go.transform.SetParent(stack, false);
                    var off = new float[3];
                    off[axis] = s * _sp[axis];
                    go.transform.localPosition = OctSpace.ToUnity(off[0], off[1], off[2]);
                    go.AddComponent<MeshFilter>().sharedMesh = quad;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    _slices[axis].Add(mr);
                }
            }
            _built = true;
            ApplyColors();
            UpdateView(force: true);
        }

        Mesh BuildQuad(int axis, int ua, int va)
        {
            // plane through voxel-centre layer 0 of `axis`, covering the full
            // voxel extent along ua / va
            float u0 = _org[ua] - 0.5f * _sp[ua], u1 = u0 + _dims[ua] * _sp[ua];
            float v0 = _org[va] - 0.5f * _sp[va], v1 = v0 + _dims[va] * _sp[va];
            Vector3 P(float u, float v)
            {
                var p = new float[3];
                p[axis] = _org[axis]; p[ua] = u; p[va] = v;
                return OctSpace.ToUnity(p[0], p[1], p[2]);
            }
            var m = new Mesh { name = "oct slice " + axis };
            m.vertices = new[] { P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        void Clear()
        {
            foreach (var t in _stacks) if (t) Destroy(t.gameObject);
            foreach (var l in _slices) l.Clear();
            foreach (var m in _materials) if (m) Destroy(m);
            foreach (var t in _textures) if (t) Destroy(t);
            foreach (var m in _meshes) if (m) Destroy(m);
            _materials.Clear(); _textures.Clear(); _meshes.Clear();
            _shownAxis = -1; _built = false;
        }

        // ------------------------------------------------------------------ runtime

        void LateUpdate()
        {
            if (_built) UpdateView(false);
        }

        void UpdateView(bool force)
        {
            int axis = -1, sign = 1;
            if (mode == Mode.Volume)
            {
                var cam = ResolveCamera();
                Vector3 d = cam ? transform.InverseTransformDirection(cam.transform.forward)
                                : Vector3.forward;
                // Unity local axes match the volume axes (X is mirrored,
                // which does not matter for picking the dominant one)
                float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
                axis = ax > ay && ax > az ? 0 : ay > az ? 1 : 2;
                sign = d[axis] >= 0 ? 1 : -1;
            }
            else if (mode == Mode.SingleBScan) axis = 2;

            if (!force && axis == _shownAxis && sign == _shownSign && mode == _shownMode) return;
            bool modeChanged = mode != _shownMode;
            _shownAxis = axis; _shownSign = sign; _shownMode = mode;

            for (int a = 0; a < 3; a++)
                if (_stacks[a]) _stacks[a].gameObject.SetActive(a == axis);
            if (axis < 0) return;

            var list = _slices[axis];
            for (int s = 0; s < list.Count; s++)
            {
                list[s].enabled = mode != Mode.SingleBScan || s == bscan;
                // back-to-front: the farthest slice gets the lowest priority.
                // Distance along the view direction in local space:
                float depth = Vector3.Dot(list[s].transform.localPosition,
                                          AxisDir(axis) * sign);
                list[s].rendererPriority = -Mathf.RoundToInt(depth * 1000f);
            }
            if (modeChanged) ApplyColors();
        }

        static Vector3 AxisDir(int a) => a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward;

        Camera ResolveCamera()
        {
            if (viewCamera) return viewCamera;
            viewCamera = Camera.main;
            if (!viewCamera) viewCamera = FindAnyObjectByType<Camera>();
            return viewCamera;
        }

        public void ApplyColors()
        {
            if (!_built) return;
            for (int axis = 0; axis < 3; axis++)
            {
                // keep the look the same whichever stack is shown: thinner
                // slice spacing -> more slices -> less alpha per slice
                float rel = _sp[axis] / Mathf.Max(1e-6f, _sp[2]);
                float a = 1f - Mathf.Pow(1f - Mathf.Clamp01(density), rel);
                foreach (var r in _slices[axis])
                {
                    var c = ink;
                    c.a = mode == Mode.SingleBScan ? bscanOpacity : a;
                    SetColor(r.sharedMaterial, c);
                }
            }
        }

        // ------------------------------------------------------------------ API for controls

        public void CycleMode()
        {
            mode = (Mode)(((int)mode + 1) % 3);
            UpdateView(true);
        }

        public void StepBScan(int delta)
        {
            if (!_built) return;
            if (mode != Mode.SingleBScan) { mode = Mode.SingleBScan; }
            bscan = Mathf.Clamp(bscan + delta, 0, _dims[2] - 1);
            UpdateView(true);
        }

        public void ScaleDensity(float factor)
        {
            density = Mathf.Clamp(density * factor, 0.02f, 1f);
            ApplyColors();
        }

        // ------------------------------------------------------------------ material helpers (HDRP + URP)

        static readonly string[] TexProps = { "_UnlitColorMap", "_BaseColorMap", "_BaseMap", "_MainTex" };
        static readonly string[] ColProps = { "_UnlitColor", "_BaseColor", "_Color" };

        static void SetTexture(Material m, Texture t)
        {
            foreach (var p in TexProps) if (m.HasProperty(p)) m.SetTexture(p, t);
        }

        static void SetColor(Material m, Color c)
        {
            foreach (var p in ColProps) if (m.HasProperty(p)) m.SetColor(p, c);
        }
    }
}
