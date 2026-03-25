using UnityEngine;
using UnityEditor;

namespace KawaiiStudio.Shaders.Editor
{
    public class KSHairRealisticGUI : ShaderGUI
    {
        // Assets URLs
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string VRCHAT_URL = "https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab";
        
        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
        private static bool isDownloading = false;

        // Foldout states
        private static bool showMain = true;
        private static bool showLighting = true;
        private static bool showStrands = true;
        private static bool showBlood = true;
        private static bool showEffects = true;
        private static bool showEmission = true;
        private static bool showTransparency = true;

        // Colors
        private static readonly Color kPink = new Color(0.9f, 0.4f, 0.6f);
        private static readonly Color kGold = new Color(1f, 0.85f, 0.4f);
        private static readonly Color kPurple = new Color(0.6f, 0.2f, 0.8f);
        private static readonly Color kBlue = new Color(0.3f, 0.6f, 1f);
        private static readonly Color kRed = new Color(0.8f, 0.1f, 0.1f);
        private static readonly Color kGreen = new Color(0.2f, 0.8f, 0.4f);
        
        private static readonly Color deepBlack = new Color(0.1f, 0.1f, 0.1f);
        private static readonly Color sectionBg = new Color(0.18f, 0.18f, 0.18f);

        // Properties
        MaterialProperty _MainTex;
        MaterialProperty _Color;
        MaterialProperty _Cutoff;
        MaterialProperty _AlphaSharp;
        
        MaterialProperty _BumpMap;
        MaterialProperty _BumpScale;
        MaterialProperty _OcclusionMap;
        MaterialProperty _OcclusionScale;

        // Lighting
        MaterialProperty _Metallic;
        MaterialProperty _Smoothness;
        MaterialProperty _AnisotropyA;
        MaterialProperty _TangentA;
        MaterialProperty _GlossA;
        MaterialProperty _TangentB;
        MaterialProperty _GlossB;
        MaterialProperty _TangentShiftTex;
        
        // Specular
        MaterialProperty _SpecularColor;
        MaterialProperty _SpecularColorB;
        MaterialProperty _SpecularStrengthA;
        MaterialProperty _SpecularStrengthB;

        // Strands
        MaterialProperty _UseStrands;
        MaterialProperty _StrandColor;
        MaterialProperty _StrandStrength;
        MaterialProperty _StrandTiling;
        MaterialProperty _StrandWidth;
        MaterialProperty _StrandSoftness;
        MaterialProperty _StrandNoise;

        // Blood
        MaterialProperty _UseBlood;
        MaterialProperty _BloodIsWater;
        MaterialProperty _BloodColor;
        MaterialProperty _BloodStrength;
        MaterialProperty _BloodScale;
        MaterialProperty _BloodThickness;
        MaterialProperty _BloodSmoothness;
        MaterialProperty _BloodFlow;

        // Rim / Backlight
        MaterialProperty _RimColor;
        MaterialProperty _RimStrength;
        MaterialProperty _RimPower;
        MaterialProperty _BacklightColor;
        MaterialProperty _BacklightStrength;
        MaterialProperty _BacklightPower;

        // Emission / Bloom
        MaterialProperty _UseEmission;
        MaterialProperty _EmissionMask;
        MaterialProperty _UseEmissionMask;
        MaterialProperty _EmissionMaskSource;
        MaterialProperty _EmissionMaskInvert;
        MaterialProperty _EmissionMaskStrength;
        MaterialProperty _EmissionMaskPower;
        MaterialProperty _EmissionColor;
        MaterialProperty _EmissionStrength;
        MaterialProperty _EmissionBloomBoost;
        MaterialProperty _EmissionClamp;

        // Transparency
        MaterialProperty _UseTipTransparency;
        MaterialProperty _TransparencyInvert;
        MaterialProperty _TransparencyRoot;
        MaterialProperty _TransparencyCurve;
        MaterialProperty _TipTransparency;
        MaterialProperty _UseTransparencyMask;
        MaterialProperty _TransparencyMask;

        // System
        MaterialProperty _Culling;

        private static MaterialProperty TryFind(string name, MaterialProperty[] props)
        {
            return FindProperty(name, props, false);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if ((logoTexture == null || bannerTexture == null) && !isDownloading)
            {
                DownloadAssets();
            }

            FindProperties(properties);
            
            // Draw banner header
            DrawBanner();
            
            EditorGUILayout.Space(10);

            // ---------------- MAIN ----------------
            DrawSection("✨ MAIN SETTINGS", ref showMain, kPink, () => {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    materialEditor.TexturePropertySingleLine(new GUIContent("Main Texture", "Albedo (RGB) Alpha (A)"), _MainTex, _Color);
                    materialEditor.TexturePropertySingleLine(new GUIContent("Normal Map"), _BumpMap, _BumpScale);
                    materialEditor.TexturePropertySingleLine(new GUIContent("Occlusion"), _OcclusionMap, _OcclusionScale);
                    
                    GUILayout.Space(5);
                    DrawProperty(materialEditor, properties, "_Cutoff", "Alpha Cutoff");
                    DrawProperty(materialEditor, properties, "_AlphaSharp", "Alpha Sharpness");
                    DrawProperty(materialEditor, properties, "_Culling", "Culling Mode");
                }
            });

            // ---------------- LIGHTING ----------------
            DrawSection("💡 LIGHTING & ANISOTROPY", ref showLighting, kGold, () => {
                DrawProperty(materialEditor, properties, "_Metallic", "Metallic");
                DrawProperty(materialEditor, properties, "_Smoothness", "Smoothness (Reflectivity)");
                
                GUILayout.Space(5);
                GUILayout.Label("Anisotropy (Angel Ring)", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_AnisotropyA", "Anisotropy");
                
                EditorGUILayout.LabelField("Primary Highlight", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawProperty(materialEditor, properties, "_SpecularColor", "Color A");
                DrawProperty(materialEditor, properties, "_SpecularStrengthA", "Strength A");
                DrawProperty(materialEditor, properties, "_TangentA", "Position A");
                DrawProperty(materialEditor, properties, "_GlossA", "Gloss/Width A");
                EditorGUI.indentLevel--;

                GUILayout.Space(5);
                EditorGUILayout.LabelField("Secondary Highlight", EditorStyles.boldLabel);
                EditorGUI.indentLevel++;
                DrawProperty(materialEditor, properties, "_SpecularColorB", "Color B");
                DrawProperty(materialEditor, properties, "_SpecularStrengthB", "Strength B");
                DrawProperty(materialEditor, properties, "_TangentB", "Position B");
                DrawProperty(materialEditor, properties, "_GlossB", "Gloss/Width B");
                materialEditor.TexturePropertySingleLine(new GUIContent("Shift Texture (Noise)"), _TangentShiftTex);
                EditorGUI.indentLevel--;
            });

            // ---------------- STRANDS ----------------
            DrawSection("💇 HAIR STRANDS", ref showStrands, kPurple, () => {
                DrawProperty(materialEditor, properties, "_UseStrands", "Enable Strands");
                
                if (_UseStrands.floatValue > 0.5f)
                {
                    EditorGUI.indentLevel++;
                    DrawProperty(materialEditor, properties, "_StrandColor", "Strand Color");
                    DrawProperty(materialEditor, properties, "_StrandStrength", "Strength");
                    DrawProperty(materialEditor, properties, "_StrandTiling", "Density (Tiling)");
                    DrawProperty(materialEditor, properties, "_StrandWidth", "Thickness");
                    DrawProperty(materialEditor, properties, "_StrandSoftness", "Softness");
                    DrawProperty(materialEditor, properties, "_StrandNoise", "Variation Noise");
                    EditorGUI.indentLevel--;
                }
            });

            // ---------------- BLOOD / LIQUID ----------------
            DrawSection("🩸 BLOOD & LIQUID", ref showBlood, kRed, () => {
                DrawProperty(materialEditor, properties, "_UseBlood", "Enable Liquid Layer");

                if (_UseBlood != null && _UseBlood.floatValue > 0.5f)
                {
                    EditorGUI.indentLevel++;
                    // Optional: may not exist in the shader, so don't force it
                    if (_BloodIsWater != null)
                        DrawProperty(materialEditor, properties, "_BloodIsWater", "Is Water (Clear)");
                    DrawProperty(materialEditor, properties, "_BloodColor", "Liquid Color");
                    DrawProperty(materialEditor, properties, "_BloodStrength", "Coverage");
                    DrawProperty(materialEditor, properties, "_BloodScale", "Scale");
                    DrawProperty(materialEditor, properties, "_BloodThickness", "Thickness (Bump)");
                    DrawProperty(materialEditor, properties, "_BloodSmoothness", "Smoothness");
                    DrawProperty(materialEditor, properties, "_BloodFlow", "Flow Speed");
                    EditorGUI.indentLevel--;
                }
            });

            // ---------------- ADVANCED EFFECTS ----------------
            DrawSection("🌟 CINEMATIC EFFECTS", ref showEffects, kBlue, () => {
                GUILayout.Label("Rim Light", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_RimColor", "Rim Color");
                DrawProperty(materialEditor, properties, "_RimStrength", "Intensity");
                DrawProperty(materialEditor, properties, "_RimPower", "Sharpness");

                GUILayout.Space(5);
                GUILayout.Label("Backlight (Translucency)", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_BacklightColor", "Color");
                DrawProperty(materialEditor, properties, "_BacklightStrength", "Intensity");
                DrawProperty(materialEditor, properties, "_BacklightPower", "Sharpness");
            });

            // ---------------- EMISSION / BLOOM ----------------
            DrawSection("✨ EMISSION / BLOOM", ref showEmission, kGold, () => {
                DrawProperty(materialEditor, properties, "_UseEmission", "Enable Emission");

                if (_UseEmission != null && _UseEmission.floatValue > 0.5f)
                {
                    EditorGUI.indentLevel++;

                    DrawProperty(materialEditor, properties, "_UseEmissionMask", "Use Emission Mask");
                    if (_UseEmissionMask == null || _UseEmissionMask.floatValue > 0.5f)
                    {
                        EditorGUI.indentLevel++;
                        DrawProperty(materialEditor, properties, "_EmissionMaskSource", "Mask Source");
                        DrawProperty(materialEditor, properties, "_EmissionMaskInvert", "Invert Mask");
                        DrawProperty(materialEditor, properties, "_EmissionMaskStrength", "Mask Strength");
                        DrawProperty(materialEditor, properties, "_EmissionMaskPower", "Mask Power");

                        // Only really needed when source=EmissionMask, but harmless to show
                        if (_EmissionMask != null)
                            materialEditor.TexturePropertySingleLine(new GUIContent("Emission Mask (R)"), _EmissionMask);
                        EditorGUI.indentLevel--;
                    }

                    DrawProperty(materialEditor, properties, "_EmissionColor", "Emission Color (HDR)");
                    DrawProperty(materialEditor, properties, "_EmissionStrength", "Emission Strength");
                    DrawProperty(materialEditor, properties, "_EmissionBloomBoost", "Bloom Boost (HDR)");
                    DrawProperty(materialEditor, properties, "_EmissionClamp", "Emission Clamp (0 = Off)");
                    EditorGUI.indentLevel--;
                }
            });

            // ---------------- TIP TRANSPARENCY ----------------
            DrawSection("👻 TIP TRANSPARENCY", ref showTransparency, kGreen, () => {
                DrawProperty(materialEditor, properties, "_UseTipTransparency", "Enable Transparency");
                
                if (_UseTipTransparency.floatValue > 0.5f)
                {
                    EditorGUI.indentLevel++;
                    DrawProperty(materialEditor, properties, "_TipTransparency", "Transparency Amount");
                    DrawProperty(materialEditor, properties, "_TransparencyRoot", "Start Height (UV.y)");
                    DrawProperty(materialEditor, properties, "_TransparencyCurve", "Fade Curve");
                    DrawProperty(materialEditor, properties, "_TransparencyInvert", "Invert Direction");
                    
                    GUILayout.Space(5);
                    DrawProperty(materialEditor, properties, "_UseTransparencyMask", "Use Mask Texture");
                    if (_UseTransparencyMask.floatValue > 0.5f)
                    {
                        materialEditor.TexturePropertySingleLine(new GUIContent("Mask (R)"), _TransparencyMask);
                    }
                    EditorGUI.indentLevel--;
                }
            });
            
            EditorGUILayout.Space(15);
            DrawFooter();
        }

        // ---------------- HELPER FUNCTIONS ----------------

        private void DrawSection(string title, ref bool foldout, Color accentColor, System.Action drawContent)
        {
            EditorGUILayout.Space(3);
            
            // Section header
            Rect headerRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            
            // Background with gradient
            Color bgColor = foldout ? new Color(accentColor.r * 0.3f, accentColor.g * 0.3f, accentColor.b * 0.3f) : sectionBg;
            EditorGUI.DrawRect(headerRect, bgColor);
            
            // Left accent bar
            Rect accentRect = new Rect(headerRect.x, headerRect.y, 4, headerRect.height);
            EditorGUI.DrawRect(accentRect, accentColor);
            
            // Foldout arrow
            Rect arrowRect = new Rect(headerRect.x + 10, headerRect.y, 20, headerRect.height);
            string arrow = foldout ? "▼" : "▶";
            GUI.Label(arrowRect, arrow, new GUIStyle(EditorStyles.label) { 
                normal = { textColor = accentColor },
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            });
            
            // Title
            GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white },
                padding = new RectOffset(30, 0, 0, 0)
            };
            
            GUI.Label(headerRect, title, headerStyle);
            
            // Click to toggle
            if (GUI.Button(headerRect, "", GUIStyle.none))
            {
                foldout = !foldout;
            }
            EditorGUIUtility.AddCursorRect(headerRect, MouseCursor.Link);
            
            // Content
            if (foldout)
            {
                Rect contentRect = EditorGUILayout.BeginVertical();
                // Thin line on the left
                EditorGUI.DrawRect(new Rect(contentRect.x, contentRect.y, 2, contentRect.height), new Color(accentColor.r, accentColor.g, accentColor.b, 0.3f));
                
                EditorGUILayout.Space(5);
                EditorGUI.indentLevel++;
                drawContent();
                EditorGUI.indentLevel--;
                EditorGUILayout.Space(5);
                
                EditorGUILayout.EndVertical();
            }
        }
        
        private void DrawProperty(MaterialEditor editor, MaterialProperty[] props, string name, string label)
        {
            MaterialProperty prop = FindProperty(name, props, false);
            if (prop != null)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10);
                editor.ShaderProperty(prop, new GUIContent(label));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawBanner()
        {
            // Main banner area
            Rect bannerRect = GUILayoutUtility.GetRect(0, 120, GUILayout.ExpandWidth(true));
            
            // Draw gradient background (Pink to Purple for Hair)
            DrawGradientRect(bannerRect, kPurple, kPink);
            
            // Draw decorative borders
            DrawBorder(bannerRect, kGold, 3);
            
            // Inner glow effect
            Rect innerRect = new Rect(bannerRect.x + 5, bannerRect.y + 5, bannerRect.width - 10, bannerRect.height - 10);
            DrawBorder(innerRect, new Color(kGold.r, kGold.g, kGold.b, 0.3f), 1);
            
            // Draw banner texture if available
            if (bannerTexture != null)
            {
                float aspectRatio = (float)bannerTexture.width / bannerTexture.height;
                float bannerWidth = Mathf.Min(bannerRect.width - 20, 300);
                float bannerHeight = bannerWidth / aspectRatio;
                Rect texRect = new Rect(
                    bannerRect.x + (bannerRect.width - bannerWidth) / 2,
                    bannerRect.y + 10,
                    bannerWidth,
                    Mathf.Min(bannerHeight, bannerRect.height - 20)
                );
                GUI.DrawTexture(texRect, bannerTexture, ScaleMode.ScaleToFit);
            }
            
            // Draw logo overlay
            if (logoTexture != null)
            {
                float logoSize = 60;
                Rect logoRect = new Rect(
                    bannerRect.x + 15,
                    bannerRect.y + (bannerRect.height - logoSize) / 2,
                    logoSize,
                    logoSize
                );
                
                GUI.color = new Color(0, 0, 0, 0.5f);
                GUI.DrawTexture(new Rect(logoRect.x + 2, logoRect.y + 2, logoRect.width, logoRect.height), logoTexture);
                GUI.color = Color.white;
                
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit);
            }
            
            // Title text
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 20, 0, 0)
            };
            
            Rect titleRect = new Rect(bannerRect.x, bannerRect.y + 25, bannerRect.width, 30);
            GUI.Label(titleRect, "HAIR REALISTIC", titleStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 1f, 1f, 0.8f) },
                padding = new RectOffset(0, 20, 0, 0)
            };
            
            Rect subtitleRect = new Rect(bannerRect.x, bannerRect.y + 50, bannerRect.width, 20);
            GUI.Label(subtitleRect, "by Kawaii Studio", subtitleStyle);
            
            // Version badge
            Rect versionRect = new Rect(bannerRect.x + bannerRect.width - 70, bannerRect.y + bannerRect.height - 25, 60, 18);
            EditorGUI.DrawRect(versionRect, new Color(kPink.r, kPink.g, kPink.b, 0.8f));
            GUI.Label(versionRect, "v2.0", new GUIStyle(EditorStyles.miniLabel) { 
                alignment = TextAnchor.MiddleCenter, 
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            });
        }
        
        private void DrawFooter()
        {
            // Separator
            Rect sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            DrawGradientRect(sepRect, kPurple, kPink);
            
            EditorGUILayout.Space(10);
            
            // Social buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            DrawSocialButton("Discord", DISCORD_URL, new Color(0.34f, 0.4f, 0.95f));
            GUILayout.Space(5);
            DrawSocialButton("Telegram", TELEGRAM_URL, new Color(0.16f, 0.63f, 0.89f));
            GUILayout.Space(5);
            DrawSocialButton("VRChat", VRCHAT_URL, new Color(0.07f, 0.71f, 0.65f));
            GUILayout.Space(5);
            DrawSocialButton("GitHub", GITHUB_URL, new Color(0.9f, 0.9f, 0.9f));
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.Space(10);
            
            // Credits
            GUIStyle creditStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 0.6f, 0.6f) }
            };
            EditorGUILayout.LabelField("Made with ❤️ by Kawaii Studio", creditStyle);
            EditorGUILayout.LabelField("Join our community!", creditStyle);
            
            EditorGUILayout.Space(5);
        }
        
        private void DrawSocialButton(string label, string url, Color color)
        {
            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 25,
                fixedWidth = 70,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                hover = { textColor = Color.white }
            };
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            
            if (GUILayout.Button(label, buttonStyle))
            {
                Application.OpenURL(url);
            }
            
            GUI.backgroundColor = oldBg;
        }
        
        private void DrawGradientRect(Rect rect, Color left, Color right)
        {
            int steps = 20;
            float stepWidth = rect.width / steps;
            
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / steps;
                Color c = Color.Lerp(left, right, t);
                Rect stepRect = new Rect(rect.x + i * stepWidth, rect.y, stepWidth + 1, rect.height);
                EditorGUI.DrawRect(stepRect, c);
            }
        }
        
        private void DrawBorder(Rect rect, Color color, int thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
        }
        
        private void DownloadAssets()
        {
            if (isDownloading) return;
            if (logoTexture != null && bannerTexture != null) return;

            // Branding is loaded locally from Assets/Kawaii Studio/References (no network).
            isDownloading = true;
            logoTexture = KawaiiStudio.KawaiiStudioBranding.Logo;
            bannerTexture = KawaiiStudio.KawaiiStudioBranding.Banner;
            isDownloading = false;
        }

        void FindProperties(MaterialProperty[] props)
        {
            // Use TryFind everywhere so the GUI never breaks if a property is removed/renamed in the shader.
            _MainTex = TryFind("_MainTex", props);
            _Color = TryFind("_Color", props);
            _Cutoff = TryFind("_Cutoff", props);
            _AlphaSharp = TryFind("_AlphaSharp", props);
            _BumpMap = TryFind("_BumpMap", props);
            _BumpScale = TryFind("_BumpScale", props);
            _OcclusionMap = TryFind("_OcclusionMap", props);
            _OcclusionScale = TryFind("_OcclusionScale", props);

            _Metallic = TryFind("_Metallic", props);
            _Smoothness = TryFind("_Smoothness", props);
            _AnisotropyA = TryFind("_AnisotropyA", props);
            _TangentA = TryFind("_TangentA", props);
            _GlossA = TryFind("_GlossA", props);
            _TangentB = TryFind("_TangentB", props);
            _GlossB = TryFind("_GlossB", props);
            _TangentShiftTex = TryFind("_TangentShiftTex", props);

            _SpecularColor = TryFind("_SpecularColor", props);
            _SpecularColorB = TryFind("_SpecularColorB", props);
            _SpecularStrengthA = TryFind("_SpecularStrengthA", props);
            _SpecularStrengthB = TryFind("_SpecularStrengthB", props);

            _UseStrands = TryFind("_UseStrands", props);
            _StrandColor = TryFind("_StrandColor", props);
            _StrandStrength = TryFind("_StrandStrength", props);
            _StrandTiling = TryFind("_StrandTiling", props);
            _StrandWidth = TryFind("_StrandWidth", props);
            _StrandSoftness = TryFind("_StrandSoftness", props);
            _StrandNoise = TryFind("_StrandNoise", props);

            _UseBlood = TryFind("_UseBlood", props);
            _BloodIsWater = TryFind("_BloodIsWater", props); // optional
            _BloodColor = TryFind("_BloodColor", props);
            _BloodStrength = TryFind("_BloodStrength", props);
            _BloodScale = TryFind("_BloodScale", props);
            _BloodThickness = TryFind("_BloodThickness", props);
            _BloodSmoothness = TryFind("_BloodSmoothness", props);
            _BloodFlow = TryFind("_BloodFlow", props);

            _RimColor = TryFind("_RimColor", props);
            _RimStrength = TryFind("_RimStrength", props);
            _RimPower = TryFind("_RimPower", props);
            _BacklightColor = TryFind("_BacklightColor", props);
            _BacklightStrength = TryFind("_BacklightStrength", props);
            _BacklightPower = TryFind("_BacklightPower", props);

            _UseEmission = TryFind("_UseEmission", props);
            _EmissionMask = TryFind("_EmissionMask", props);
            _UseEmissionMask = TryFind("_UseEmissionMask", props);
            _EmissionMaskSource = TryFind("_EmissionMaskSource", props);
            _EmissionMaskInvert = TryFind("_EmissionMaskInvert", props);
            _EmissionMaskStrength = TryFind("_EmissionMaskStrength", props);
            _EmissionMaskPower = TryFind("_EmissionMaskPower", props);
            _EmissionColor = TryFind("_EmissionColor", props);
            _EmissionStrength = TryFind("_EmissionStrength", props);
            _EmissionBloomBoost = TryFind("_EmissionBloomBoost", props);
            _EmissionClamp = TryFind("_EmissionClamp", props);

            _UseTipTransparency = TryFind("_UseTipTransparency", props);
            _TransparencyInvert = TryFind("_TransparencyInvert", props);
            _TransparencyRoot = TryFind("_TransparencyRoot", props);
            _TransparencyCurve = TryFind("_TransparencyCurve", props);
            _TipTransparency = TryFind("_TipTransparency", props);
            _UseTransparencyMask = TryFind("_UseTransparencyMask", props);
            _TransparencyMask = TryFind("_TransparencyMask", props);

            _Culling = TryFind("_Culling", props);
        }
    }
}
