using TMPro;
using UnityEngine;

namespace Cheminova
{
    [CreateAssetMenu(fileName = "PresentationSettings", menuName = "Cheminova/Presentation Settings")]
    public sealed class CheminovaSettings : ScriptableObject
    {
        [Header("Scene integration")]
        public string[] targetScenes = { "Character" };
        [Tooltip("Resources models whose standalone or combined showcase instances use modelHeightOffset. Authored scene models retain their saved heights.")]
        public string[] elevatedModelResources;
        public float modelHeightOffset = 2f;
        [Header("Cheminova layout")]
        [Tooltip("Raises only this presentation and the Shoe/Palchetto pair, leaving floor shadows on the floor.")]
        public float contentVerticalOffset = 0.35f;
        public string revealFrontResource = "Models/Shoe/Chemi-AU-O005";
        public string revealBackResource = "Models/Parchetto/Chemi-IT-B001";

        [Header("Alternating showcase")]
        public bool alternatingShowcase = true;
        [Min(0.01f)] public float shoeScaleMultiplier = 5f;
        [Min(1)] public int turnsPerModel = 2;
        [Min(1)] public int shoeTurns = 3;
        [Min(0.01f)] public float turnSpeed = 90f;
        [Tooltip("Degrees per second for the building; 0 = turnSpeed.")]
        [Min(0f)] public float buildingTurnSpeed = 45f;
        [Min(0.01f)] public float zoomOutSeconds = 1.5f;
        [Min(0.01f)] public float zoomInSeconds = 1.5f;
        [Min(0.01f)] public float interactionResumeSeconds = 3f;

        [Header("Shoe material")]
        public bool shoeUnlit = true;
        public Texture2D shoeAlternateAlbedo;

        [Header("Floor shadows")]
        public bool floorShadows = true;
        [Range(0f, 0.4f)] public float floorShadowOpacity = 0.12f;
        [Min(0.1f)] public float floorShadowSize = 1.1f;
        [Min(0.001f)] public float floorShadowLift = 0.015f;

        [Header("Assets")]
        public Texture2D logo;
        public TMP_FontAsset font;
        public Shader logoShader;

        [Header("Logo")]
        public Vector3 logoPosition = new Vector3(0f, 4.45f, -4.8f);
        [Min(0.01f)] public float logoWidth = 1.9f;
        [Min(0f)] public float logoEmission = 1.5f;
        [Range(0f, 1f)] public float logoAlphaCutoff = 0.1f;

        [Header("Caption")]
        [TextArea] public string caption = "TECHNOLOGY TO PROTECT\nEUROPE'S CULTURAL LEGACY";
        public Vector2 captionRect = new Vector2(2.45f, 0.32f);
        public float captionGap = 0.06f;
        public FontStyles captionStyle = FontStyles.Bold;

        [Header("Website")]
        public string website = "http://cheminova.eu";
        public Color websiteColor = new Color32(0, 90, 156, 255);
        public float websiteHeight = 0.16f;
        public float websiteGap = 0.06f;

        [Header("Footer")]
        public string[] footerLines = {
            "Digital Twin \u2022 e3D Model \u2022 Multimodality",
            "Continuous Monitoring \u2022 Condition assessment \u2022 Condition report",
            "photogrammetry \u2022 lidar \u2022 HSI \u2022 RTI"
        };
        public Vector3 footerPosition = new Vector3(0f, 1f, -4.8f);
        public Vector3 footerScale = new Vector3(2f, 2f, 1f);
        public Vector2 footerRect = new Vector2(2.45f, 0.48f);
        [Range(0.1f, 2f)] public float footerLineSpacing = 0.5f;
        [Min(1)] public int fallbackBulletPercent = 150;

        [Header("Object names")]
        public string shoeObjectName = "Sisi's Shoe, Sch\u00f6nbrunn, Vienna, Austria";
        public string buildingObjectName = "Palchetto Della Musica, Palermo, Italy";
        public Color objectNameColor = new Color32(166, 61, 0, 255);
        [Tooltip("Gap above the footer layout box; negative values lower the title.")] public float objectNameGap = -0.06f;
        [Min(0.01f)] public float objectNameHeight = 0.16f;

        [Header("EU disclaimer")]
        public Texture2D euDisclaimer;
        public Vector3 euDisclaimerPosition = new Vector3(0f, 0.4f, -4.8f);
        [Min(0.01f)] public float euDisclaimerWidth = 4.9f;

        [Header("Typography")]
        public Color textColor = new Color32(31, 41, 55, 255);
        public Vector2 fontSizeRange = new Vector2(0.1f, 5f);
    }
}
