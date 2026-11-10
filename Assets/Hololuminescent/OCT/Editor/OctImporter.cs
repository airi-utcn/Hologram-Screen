using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hololuminescent.Oct.EditorTools
{
    /// <summary>
    /// HLD > Import OCT Export...
    /// Reads a folder written by exporter/plotly_to_hld.py (meta.json, model.glb,
    /// volume.u8) and builds Resources/Models/&lt;Name&gt;/&lt;Name&gt;.prefab with
    /// HDRP materials, then adds it to ObjectSwitcher's resource models.
    /// Re-importing the same name overwrites the assets in place.
    /// </summary>
    public static class OctImporter
    {
        const string ModelsRoot = "Assets/Hololuminescent/Resources/Models";
        const string LastDirKey = "Hololuminescent.Oct.LastImportDir";

        [MenuItem("HLD/Import OCT Export...", priority = 10)]
        static void ImportMenu()
        {
            string dir = EditorUtility.OpenFolderPanel("Folder with meta.json / model.glb / volume.u8",
                                                       EditorPrefs.GetString(LastDirKey, ""), "");
            if (string.IsNullOrEmpty(dir)) return;
            EditorPrefs.SetString(LastDirKey, dir);
            try { Import(dir); }
            catch (Exception e) { Debug.LogException(e); EditorUtility.DisplayDialog("OCT import failed", e.Message, "OK"); }
        }

        [MenuItem("HLD/Re-import last OCT Export", priority = 11)]
        static void ReimportLast()
        {
            string dir = EditorPrefs.GetString(LastDirKey, "");
            if (Directory.Exists(dir)) Import(dir); else ImportMenu();
        }

        public static string Import(string srcDir)
        {
            string metaPath = Path.Combine(srcDir, "meta.json");
            if (!File.Exists(metaPath)) throw new FileNotFoundException("meta.json not found in " + srcDir);
            string metaText = File.ReadAllText(metaPath);
            var meta = OctScanMeta.Parse(metaText);
            string name = Sanitize(string.IsNullOrEmpty(meta.name) ? Path.GetFileName(srcDir) : meta.name);
            string dst = $"{ModelsRoot}/{name}";
            Directory.CreateDirectory(dst);

            // 1) data files as TextAssets
            File.Copy(metaPath, $"{dst}/{name}_meta.json", true);
            bool hasVolume = meta.HasVolume && File.Exists(Path.Combine(srcDir, meta.volume.file));
            if (hasVolume) File.Copy(Path.Combine(srcDir, meta.volume.file), $"{dst}/{name}_volume.bytes", true);
            AssetDatabase.Refresh();
            var metaAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dst}/{name}_meta.json");
            var volAsset = hasVolume ? AssetDatabase.LoadAssetAtPath<TextAsset>($"{dst}/{name}_volume.bytes") : null;

            // 2) prefab hierarchy
            var root = new GameObject(name);
            var controls = root.AddComponent<OctScanControls>();
            controls.metaJson = metaAsset;
            var meshesRoot = new GameObject("Meshes").transform;
            meshesRoot.SetParent(root.transform, false);

            var tissue = new System.Collections.Generic.List<Renderer>();
            var fluids = new System.Collections.Generic.List<Renderer>();
            string glbPath = Path.Combine(srcDir, string.IsNullOrEmpty(meta.model) ? "model.glb" : meta.model);
            if (File.Exists(glbPath))
            {
                foreach (var part in Glb.Read(glbPath))
                {
                    string safe = Sanitize(part.name);
                    var mesh = SaveAsset(part.mesh, $"{dst}/{name}_{safe}.mesh.asset");
                    bool transparent = part.color.a < 0.999f;
                    var mat = SaveAsset(transparent ? MakeLit(part.color, true) : MakeFluid(part.color),
                                        $"{dst}/{name}_{safe}.mat");

                    var go = new GameObject(part.name);
                    go.transform.SetParent(meshesRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    mr.shadowCastingMode = transparent ? ShadowCastingMode.Off : ShadowCastingMode.On;
                    (transparent ? tissue : fluids).Add(mr);
                }
            }
            controls.tissue = tissue.ToArray();
            controls.fluids = fluids.ToArray();

            if (volAsset)
            {
                var volGo = new GameObject("Volume");
                volGo.transform.SetParent(root.transform, false);
                var slices = volGo.AddComponent<OctVolumeSlices>();
                slices.volumeData = volAsset;
                slices.metaJson = metaAsset;
                slices.sliceTemplate = SaveAsset(MakeSliceTemplate(), $"{dst}/{name}_slice.mat");
                controls.volume = slices;
            }

            string prefabPath = $"{dst}/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();

            string resPath = $"Models/{name}/{name}";
            bool added = RegisterWithSwitcher(resPath, prefabPath);
            Debug.Log($"[OCT] imported {name} -> {prefabPath}\n" +
                      (added ? $"added '{resPath}' to ObjectSwitcher (save the scene)."
                             : $"add '{resPath}' to Showcase > ObjectSwitcher > Resource Models."));
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath));
            return prefabPath;
        }

        // ------------------------------------------------------------------ assets

        static T SaveAsset<T>(T obj, string path) where T : UnityEngine.Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(obj, existing);
                EditorUtility.SetDirty(existing);
                UnityEngine.Object.DestroyImmediate(obj);
                return existing;
            }
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        static string Sanitize(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "") sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            var r = sb.ToString().Trim('_');
            while (r.Contains("__")) r = r.Replace("__", "_");
            return r.Length > 0 ? r : "Scan";
        }

        // ------------------------------------------------------------------ HDRP materials

        static Shader FindShader(params string[] names)
        {
            foreach (var n in names) { var s = Shader.Find(n); if (s) return s; }
            throw new Exception("none of these shaders found: " + string.Join(", ", names));
        }

        static Material MakeLit(Color c, bool transparent)
        {
            var m = new Material(FindShader("HDRP/Lit", "Universal Render Pipeline/Lit", "Standard"));
            var opaque = c; opaque.a = transparent ? c.a : 1f;
            Set(m, "_BaseColor", opaque); Set(m, "_Color", opaque);
            Set(m, "_Metallic", 0f);
            Set(m, "_Smoothness", transparent ? 0.35f : 0.65f);
            Hdrp.DoubleSided(m, true);
            if (transparent) Hdrp.Transparent(m);
            Hdrp.Validate(m);
            return m;
        }

        /// <summary>
        /// Fluid: looks opaque (alpha 1) but lives in the transparent queue
        /// with depth write on and the highest sort priority. It draws after
        /// the ink slices (which write no depth), so the fluid shows through
        /// the tissue like an X-ray, while still occluding itself correctly.
        /// </summary>
        static Material MakeFluid(Color c)
        {
            c.a = 1f;
            var m = MakeLit(c, false);
            Hdrp.Transparent(m);
            if (m.HasProperty("_TransparentZWrite")) m.SetFloat("_TransparentZWrite", 1f);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 1f);
            if (m.HasProperty("_TransparentSortPriority")) m.SetFloat("_TransparentSortPriority", 50f);
            Hdrp.Validate(m);
            if (!m.HasProperty("_TransparentSortPriority"))   // URP / built-in
                m.renderQueue = (int)RenderQueue.Transparent + 50;
            return m;
        }

        static Material MakeSliceTemplate()
        {
            var m = new Material(FindShader("HDRP/Unlit", "Universal Render Pipeline/Unlit", "Unlit/Transparent"));
            Set(m, "_UnlitColor", new Color(0.07f, 0.10f, 0.20f, 0.3f));
            Set(m, "_BaseColor", new Color(0.07f, 0.10f, 0.20f, 0.3f));
            Hdrp.DoubleSided(m, true);
            Hdrp.Transparent(m);
            Hdrp.Validate(m);
            return m;
        }

        static void Set(Material m, string p, Color c) { if (m.HasProperty(p)) m.SetColor(p, c); }
        static void Set(Material m, string p, float f) { if (m.HasProperty(p)) m.SetFloat(p, f); }

        /// <summary>
        /// HDRP material setup. Uses HDMaterial's public API through reflection
        /// so this file compiles whatever the HDRP version (or in URP);
        /// properties are also set directly as a fallback.
        /// </summary>
        static class Hdrp
        {
            static readonly Type HDMaterial = Type.GetType(
                "UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");

            static bool Call(string method, params object[] args)
            {
                if (HDMaterial == null) return false;
                foreach (var mi in HDMaterial.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (mi.Name != method) continue;
                    var ps = mi.GetParameters();
                    if (ps.Length != args.Length) continue;
                    try { mi.Invoke(null, args); return true; }
                    catch (Exception e) { Debug.LogWarning($"[OCT] HDMaterial.{method}: {e.InnerException?.Message ?? e.Message}"); }
                }
                return false;
            }

            public static void Transparent(Material m)
            {
                if (m.HasProperty("_SurfaceType")) m.SetFloat("_SurfaceType", 1f);
                if (m.HasProperty("_BlendMode")) m.SetFloat("_BlendMode", 0f);       // alpha
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty("_TransparentZWrite")) m.SetFloat("_TransparentZWrite", 0f);
                if (m.HasProperty("_EnableFogOnTransparent")) m.SetFloat("_EnableFogOnTransparent", 0f);
                if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);             // URP
                Call("SetSurfaceType", m, true);
                m.renderQueue = (int)RenderQueue.Transparent;
            }

            public static void DoubleSided(Material m, bool on)
            {
                if (m.HasProperty("_DoubleSidedEnable")) m.SetFloat("_DoubleSidedEnable", on ? 1 : 0);
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", on ? 0 : 2);           // URP
                Call("SetDoubleSided", m, on);
            }

            public static void Validate(Material m)
            {
                if (Call("ValidateMaterial", m)) return;
                // older HDRP: editor-side keyword reset
                var utils = Type.GetType("UnityEditor.Rendering.HighDefinition.HDShaderUtils, Unity.RenderPipelines.HighDefinition.Editor");
                utils?.GetMethod("ResetMaterialKeywords", BindingFlags.Public | BindingFlags.Static,
                                 null, new[] { typeof(Material) }, null)?.Invoke(null, new object[] { m });
            }
        }

        // ------------------------------------------------------------------ ObjectSwitcher

        static bool RegisterWithSwitcher(string resPath, string prefabPath)
        {
            foreach (var mb in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null || mb.GetType().Name != "ObjectSwitcher") continue;
                var so = new SerializedObject(mb);
                var prop = so.FindProperty("resourceModels");
                if (prop == null || !prop.isArray) return false;
                for (int i = 0; i < prop.arraySize; i++)
                {
                    var e = prop.GetArrayElementAtIndex(i);
                    if (e.propertyType == SerializedPropertyType.String && e.stringValue == resPath) return true;
                    if (e.propertyType == SerializedPropertyType.ObjectReference &&
                        AssetDatabase.GetAssetPath(e.objectReferenceValue) == prefabPath) return true;
                }
                prop.arraySize++;
                var last = prop.GetArrayElementAtIndex(prop.arraySize - 1);
                if (last.propertyType == SerializedPropertyType.String) last.stringValue = resPath;
                else if (last.propertyType == SerializedPropertyType.ObjectReference)
                    last.objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                else { prop.arraySize--; return false; }
                so.ApplyModifiedProperties();
                EditorSceneManager.MarkSceneDirty(mb.gameObject.scene);
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ minimal GLB reader (for files from plotly_to_hld.py)

        public struct Part { public string name; public Mesh mesh; public Color color; }

#pragma warning disable 0649 // fields are filled by JsonUtility
        static class Glb
        {
            [Serializable] class Root { public Accessor[] accessors; public View[] bufferViews; public GMesh[] meshes; public GMat[] materials; public GNode[] nodes; }
            [Serializable] class Accessor { public int bufferView; public int componentType; public int count; public string type; }
            [Serializable] class View { public int byteOffset; public int byteLength; }
            [Serializable] class GNode { public string name; public int mesh = -1; }
            [Serializable] class GMesh { public string name; public Prim[] primitives; }
            [Serializable] class Prim { public Attr attributes; public int indices = -1; public int material = -1; }
            [Serializable] class Attr { public int POSITION = -1; public int NORMAL = -1; }
            [Serializable] class GMat { public string name; public Pbr pbrMetallicRoughness; public string alphaMode; }
            [Serializable] class Pbr { public float[] baseColorFactor; }

            public static System.Collections.Generic.List<Part> Read(string path)
            {
                byte[] b = File.ReadAllBytes(path);
                if (BitConverter.ToUInt32(b, 0) != 0x46546C67) throw new Exception("not a GLB file: " + path);
                int jsonLen = BitConverter.ToInt32(b, 12);
                var root = JsonUtility.FromJson<Root>(Encoding.UTF8.GetString(b, 20, jsonLen));
                int binStart = 20 + jsonLen + 8;

                var parts = new System.Collections.Generic.List<Part>();
                foreach (var gm in root.meshes)
                {
                    var p = gm.primitives[0];
                    var pos = Vec3(root, b, binStart, p.attributes.POSITION);
                    var nrm = p.attributes.NORMAL >= 0 ? Vec3(root, b, binStart, p.attributes.NORMAL) : null;
                    var idx = Indices(root, b, binStart, p.indices);
                    // glTF -> Unity: mirror X, flip winding
                    for (int i = 0; i < pos.Length; i++) pos[i].x = -pos[i].x;
                    if (nrm != null) for (int i = 0; i < nrm.Length; i++) nrm[i].x = -nrm[i].x;
                    for (int t = 0; t + 2 < idx.Length; t += 3) { int s = idx[t + 1]; idx[t + 1] = idx[t + 2]; idx[t + 2] = s; }

                    var mesh = new Mesh { name = gm.name };
                    if (pos.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                    mesh.vertices = pos;
                    mesh.triangles = idx;
                    if (nrm != null) mesh.normals = nrm; else mesh.RecalculateNormals();
                    mesh.RecalculateBounds();

                    var col = Color.gray;
                    if (p.material >= 0 && root.materials[p.material].pbrMetallicRoughness?.baseColorFactor is float[] f && f.Length == 4)
                        col = new Color(f[0], f[1], f[2], f[3]);
                    parts.Add(new Part { name = gm.name, mesh = mesh, color = col });
                }
                return parts;
            }

            static Vector3[] Vec3(Root r, byte[] b, int bin, int ai)
            {
                var a = r.accessors[ai]; var v = r.bufferViews[a.bufferView];
                var o = new Vector3[a.count];
                int s = bin + v.byteOffset;
                for (int i = 0; i < a.count; i++)
                    o[i] = new Vector3(BitConverter.ToSingle(b, s + 12 * i), BitConverter.ToSingle(b, s + 12 * i + 4), BitConverter.ToSingle(b, s + 12 * i + 8));
                return o;
            }

            static int[] Indices(Root r, byte[] b, int bin, int ai)
            {
                var a = r.accessors[ai]; var v = r.bufferViews[a.bufferView];
                var o = new int[a.count];
                int s = bin + v.byteOffset;
                for (int i = 0; i < a.count; i++)
                    o[i] = a.componentType == 5125 ? (int)BitConverter.ToUInt32(b, s + 4 * i)
                         : a.componentType == 5123 ? BitConverter.ToUInt16(b, s + 2 * i)
                         : b[s + i];
                return o;
            }
        }
#pragma warning restore 0649
    }
}
