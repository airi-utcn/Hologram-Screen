using System;
using UnityEngine;

namespace Hololuminescent.Oct
{
    /// <summary>Mirror of meta.json written by exporter/plotly_to_hld.py (JsonUtility).</summary>
    [Serializable]
    public class OctScanMeta
    {
        public string name;
        public string title;
        public string subtitle;
        public string[] notes;
        public string units;
        public float[] size;
        public OctLayer[] layers;
        public OctVolume volume;
        public string model;

        public static OctScanMeta Parse(string json) => JsonUtility.FromJson<OctScanMeta>(json);

        public bool HasVolume => volume != null && volume.dims != null && volume.dims.Length == 3
                                 && volume.dims[0] > 0;
    }

    [Serializable]
    public class OctLayer
    {
        public string name;
        public string label;
        public string kind;     // "tissue" | "fluid"
        public string color;    // #rrggbb
        public float opacity;
        public int vertices;
        public int triangles;

        public Color UnityColor
        {
            get
            {
                ColorUtility.TryParseHtmlString(color, out var c);
                c.a = opacity;
                return c;
            }
        }
    }

    [Serializable]
    public class OctVolume
    {
        public string file;
        public int[] dims;        // X, Y, Z  (X fastest)
        public float[] spacing;   // mm, glTF axes
        public float[] origin;    // centre of voxel (0,0,0), glTF axes
        public float threshold;   // 0..1, below = fully transparent
        public float sourceOpacity;
        public bool mirroredInSource;
    }

    /// <summary>glTF (right-handed) to Unity (left-handed): negate X.</summary>
    public static class OctSpace
    {
        public static Vector3 ToUnity(float x, float y, float z) => new Vector3(-x, y, z);
    }
}
