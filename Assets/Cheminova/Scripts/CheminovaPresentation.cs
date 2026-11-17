using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Cheminova
{
    /// <summary>Owns stage labels; it never adds renderers beneath the fitted, rotating models.</summary>
    public sealed class CheminovaPresentation : MonoBehaviour
    {
        private CheminovaSettings settings;
        private GameObject header, footer, disclaimer;
        private Material logoMaterial, disclaimerMaterial;
        private TextMeshPro footerText;
        private TextMeshPro objectNameText;
        private Transform shoe, building;
        private bool initialized;

        public void Init(Transform stage, CheminovaSettings profile, Transform shoeModel, Transform buildingModel)
        {
            if (initialized) return;
            initialized = true;
            settings = profile;
            shoe = shoeModel;
            building = buildingModel;
            if (settings == null || settings.logo == null || settings.font == null || settings.logoShader == null)
            {
                Debug.LogWarning("Cheminova: assign logo, font and shader in PresentationSettings.", this);
                return;
            }

            logoMaterial = new Material(settings.logoShader) { name = "Cheminova logo (runtime)" };
            logoMaterial.SetTexture("_UnlitColorMap", settings.logo);
            logoMaterial.SetColor("_UnlitColor", Color.white);
            logoMaterial.SetTexture("_EmissiveColorMap", settings.logo);
            logoMaterial.SetColor("_EmissiveColor", Color.white * settings.logoEmission);
            logoMaterial.SetFloat("_EmissiveExposureWeight", 0f);
            logoMaterial.EnableKeyword("_EMISSIVE_COLOR_MAP");
            logoMaterial.SetFloat("_AlphaCutoffEnable", 1f);
            logoMaterial.SetFloat("_AlphaCutoff", settings.logoAlphaCutoff);
            logoMaterial.EnableKeyword("_ALPHATEST_ON");

            header = new GameObject("Cheminova project header");
            header.transform.SetParent(stage, false);
            header.transform.localPosition = settings.logoPosition + Vector3.up * settings.contentVerticalOffset;
            var logo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            logo.name = "Cheminova project logo";
            logo.transform.SetParent(header.transform, false);
            float width = Mathf.Max(0.01f, settings.logoWidth);
            float logoHeight = width * settings.logo.height / settings.logo.width;
            logo.transform.localScale = new Vector3(width, logoHeight, 1f);
            Destroy(logo.GetComponent<Collider>());
            var renderer = logo.GetComponent<Renderer>();
            renderer.sharedMaterial = logoMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            AddCaption(logoHeight);
            AddFooter(stage);
            AddWebsite(logoHeight);
            AddObjectName();
            AddDisclaimer(stage);
            header.SetActive(isActiveAndEnabled);
            footer.SetActive(isActiveAndEnabled);
        }

        private TextMeshPro AddText(Transform parent, string name, Vector3 position, Vector2 rect)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            var text = obj.AddComponent<TextMeshPro>();
            text.font = settings.font;
            text.color = settings.textColor;
            text.alignment = TextAlignmentOptions.Center;
            text.rectTransform.sizeDelta = rect;
            text.enableAutoSizing = true;
            text.fontSizeMin = settings.fontSizeRange.x;
            text.fontSizeMax = settings.fontSizeRange.y;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.renderer.shadowCastingMode = ShadowCastingMode.Off;
            text.renderer.receiveShadows = false;
            return text;
        }

        private void AddCaption(float logoHeight)
        {
            var position = new Vector3(0f, -logoHeight * 0.5f - settings.captionGap - settings.captionRect.y * 0.5f, 0f);
            var text = AddText(header.transform, "Cheminova project tagline", position, settings.captionRect);
            text.text = settings.caption;
            text.fontStyle = settings.captionStyle;
        }

        private void AddWebsite(float logoHeight)
        {
            footerText.ForceMeshUpdate(true);
            float captionBottom = -logoHeight * 0.5f - settings.captionGap - settings.captionRect.y;
            var position = new Vector3(0f,
                captionBottom - settings.websiteGap - settings.websiteHeight * settings.footerScale.y * 0.5f, 0f);
            var text = AddText(header.transform, "Cheminova project website", position,
                new Vector2(settings.footerRect.x, settings.websiteHeight));
            text.transform.localScale = settings.footerScale;
            text.text = settings.website;
            text.fontStyle = footerText.fontStyle;
            text.color = settings.websiteColor;
            text.enableAutoSizing = false;
            text.fontSize = footerText.fontSize;
        }

        private void AddFooter(Transform stage)
        {
            footerText = AddText(stage, "Cheminova scanning details",
                settings.footerPosition + Vector3.up * settings.contentVerticalOffset, settings.footerRect);
            footer = footerText.gameObject;
            footer.transform.localScale = settings.footerScale;
            footerText.OnPreRenderText += FormatFooterText;
            int bulletPercent = settings.fallbackBulletPercent;
            if (settings.font.characterLookupTable.TryGetValue('o', out var letter)
                && settings.font.characterLookupTable.TryGetValue('\u2022', out var dot)
                && dot.glyph.metrics.height > 0f)
                bulletPercent = Mathf.RoundToInt(100f * letter.glyph.metrics.height / dot.glyph.metrics.height);
            string bullet = $"<size={bulletPercent}%>\u2022</size>";
            footerText.richText = true;
            footerText.text = string.Join("\n", settings.footerLines ?? System.Array.Empty<string>())
                .Replace("\u2022", bullet).Replace("*", bullet);
        }

        private void AddObjectName()
        {
            var position = new Vector3(0f,
                settings.footerRect.y * 0.5f + settings.objectNameGap + settings.objectNameHeight * 0.5f, 0f);
            objectNameText = AddText(footer.transform, "Cheminova object name", position,
                new Vector2(settings.footerRect.x, settings.objectNameHeight));
            objectNameText.enableAutoSizing = false;
            objectNameText.fontSize = footerText.fontSize;
            objectNameText.fontStyle = footerText.fontStyle;
            objectNameText.richText = false;
            objectNameText.color = settings.objectNameColor;
            UpdateObjectName();
        }

        private void AddDisclaimer(Transform stage)
        {
            if (settings.euDisclaimer == null)
            {
                Debug.LogWarning("Cheminova: assign the EU disclaimer image in PresentationSettings.", this);
                return;
            }
            disclaimerMaterial = new Material(settings.logoShader) { name = "Cheminova EU disclaimer (runtime)" };
            disclaimerMaterial.SetTexture("_UnlitColorMap", settings.euDisclaimer);
            disclaimerMaterial.SetColor("_UnlitColor", Color.white);
            disclaimerMaterial.SetColor("_EmissiveColor", Color.black);
            disclaimerMaterial.SetFloat("_BlendMode", 0f);
            disclaimerMaterial.SetFloat("_AlphaCutoffEnable", 0f);
            disclaimerMaterial.SetFloat("_TransparentZWrite", 0f);
            disclaimerMaterial.SetFloat("_EnableFogOnTransparent", 0f);
            HDMaterial.SetSurfaceType(disclaimerMaterial, true);
            // Preserve the supplied image's colors independently of scene exposure.
            HDMaterial.SetRenderingPass(disclaimerMaterial, HDMaterial.RenderingPass.AfterPostProcess);
            HDMaterial.ValidateMaterial(disclaimerMaterial);
            disclaimerMaterial.SetFloat("_ZTestTransparent", (float)CompareFunction.LessEqual);

            disclaimer = GameObject.CreatePrimitive(PrimitiveType.Quad);
            disclaimer.name = "Cheminova EU disclaimer";
            disclaimer.transform.SetParent(stage, false);
            disclaimer.transform.localPosition = settings.euDisclaimerPosition;
            float width = Mathf.Max(0.01f, settings.euDisclaimerWidth);
            disclaimer.transform.localScale = new Vector3(width,
                width * settings.euDisclaimer.height / settings.euDisclaimer.width, 1f);
            Destroy(disclaimer.GetComponent<Collider>());
            var renderer = disclaimer.GetComponent<Renderer>();
            renderer.sharedMaterial = disclaimerMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            disclaimer.SetActive(isActiveAndEnabled);
        }

        // Read visibility after the showcase Update, including while interaction pauses it.
        private void LateUpdate() => UpdateObjectName();

        private void UpdateObjectName()
        {
            if (objectNameText == null) return;
            bool showShoe = shoe != null && shoe.gameObject.activeInHierarchy;
            bool showBuilding = building != null && building.gameObject.activeInHierarchy;
            objectNameText.gameObject.SetActive(isActiveAndEnabled && (showShoe || showBuilding));
            string label = showShoe ? settings.shoeObjectName : settings.buildingObjectName;
            if (objectNameText.text != label) objectNameText.text = label;
        }

        private void FormatFooterText(TMP_TextInfo info)
        {
            FormatFooterText(info, settings.footerLineSpacing);
        }

        internal static void FormatFooterText(TMP_TextInfo info, float lineSpacing)
        {
            if (info.lineCount == 0) return;
            float middleBaseline = (info.lineInfo[0].baseline + info.lineInfo[info.lineCount - 1].baseline) * 0.5f;
            var lineCenters = new float[info.lineCount];
            for (int line = 0; line < lineCenters.Length; line++) lineCenters[line] = float.NaN;
            for (int i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (character.isVisible && character.character == 'o')
                    lineCenters[character.lineNumber] = (character.topRight.y + character.bottomLeft.y) * 0.5f;
            }
            for (int i = 0; i < info.characterCount; i++)
            {
                var character = info.characterInfo[i];
                if (!character.isVisible) continue;
                float offset = (middleBaseline - info.lineInfo[character.lineNumber].baseline) * (1f - lineSpacing);
                if (character.character == '\u2022')
                {
                    float lineCenter = lineCenters[character.lineNumber];
                    if (!float.IsNaN(lineCenter))
                        offset += lineCenter - (character.topRight.y + character.bottomLeft.y) * 0.5f;
                }
                var vertices = info.meshInfo[character.materialReferenceIndex].vertices;
                for (int vertex = 0; vertex < 4; vertex++) vertices[character.vertexIndex + vertex].y += offset;
            }
        }

        private void OnEnable()
        {
            if (header != null) header.SetActive(true);
            if (footer != null) footer.SetActive(true);
            if (disclaimer != null) disclaimer.SetActive(true);
        }

        private void OnDisable()
        {
            if (header != null) header.SetActive(false);
            if (footer != null) footer.SetActive(false);
            if (disclaimer != null) disclaimer.SetActive(false);
        }

        private void OnDestroy()
        {
            if (footerText != null) footerText.OnPreRenderText -= FormatFooterText;
            if (header != null) Destroy(header);
            if (footer != null) Destroy(footer);
            if (disclaimer != null) Destroy(disclaimer);
            if (logoMaterial != null) Destroy(logoMaterial);
            if (disclaimerMaterial != null) Destroy(disclaimerMaterial);
        }
    }
}
