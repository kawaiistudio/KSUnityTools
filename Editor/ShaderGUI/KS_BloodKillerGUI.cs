using UnityEngine;
using UnityEditor;

namespace KawaiiStudio.Shaders.Editor
{
    public class KS_BloodKillerGUI : ShaderGUI
    {
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string VRCHAT_URL = "https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab";
        
        // Branding comes from KawaiiStudioBranding. The literals that used to live
        // here pointed at "Editor/Cache", a folder renamed to "References" in v1.4,
        // so the logo and banner silently never loaded.

        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
    
    // Foldout states
    private static bool showBloodAppearance = true;
    private static bool showEmission = true;
    private static bool showRotationFlow = true;
    private static bool showPhysicalMovement = true;
    private static bool showDistortion = true;
    private static bool showDoppelganger = true;
    private static bool showMicroWaves = true;
    private static bool showSurfaceFinish = true;
    private static bool showEdgeOpacity = true;
    private static bool showRenderingQuality = false;
    
    // Colors
    private static readonly Color bloodRed = new Color(0.8f, 0.1f, 0.1f);
    private static readonly Color darkRed = new Color(0.4f, 0.05f, 0.05f);
    private static readonly Color deepBlack = new Color(0.1f, 0.1f, 0.1f);
    private static readonly Color headerBg = new Color(0.15f, 0.02f, 0.02f);
    private static readonly Color sectionBg = new Color(0.18f, 0.18f, 0.18f);
    private static readonly Color accentGold = new Color(1f, 0.85f, 0.4f);

    public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
    {
        if (logoTexture == null || bannerTexture == null)
        {
            LoadAssets();
        }
        
        // Draw banner header
        DrawBanner();
        
        EditorGUILayout.Space(10);
        
        // Draw sections with custom styling
        DrawSection("🩸 BLOOD APPEARANCE", ref showBloodAppearance, bloodRed, () => {
            DrawProperty(materialEditor, properties, "_BloodColor", "Main Blood Color");
            DrawProperty(materialEditor, properties, "_DeepBloodColor", "Deep/Shadow Color");
        });
        
        DrawSection("✨ EMISSION & GLOW", ref showEmission, new Color(1f, 0.5f, 0.2f), () => {
            DrawProperty(materialEditor, properties, "_EmissionPower", "Glow Intensity");
            DrawProperty(materialEditor, properties, "_EmissionThreshold", "Glow Threshold");
        });
        
        DrawSection("🌀 ROTATION & FLOW", ref showRotationFlow, new Color(0.3f, 0.6f, 1f), () => {
            DrawProperty(materialEditor, properties, "_RotationSpeed", "Rotation Speed");
            DrawProperty(materialEditor, properties, "_FlowSpeed", "Flow Speed");
            DrawProperty(materialEditor, properties, "_FlowScale", "Flow Scale");
            DrawProperty(materialEditor, properties, "_Viscosity", "Viscosity");
        });
        
        DrawSection("💧 PHYSICAL MOVEMENT", ref showPhysicalMovement, new Color(0.4f, 0.8f, 1f), () => {
            DrawProperty(materialEditor, properties, "_WobbleAmount", "Wobble Amount");
            DrawProperty(materialEditor, properties, "_WobbleSpeed", "Wobble Speed");
            DrawProperty(materialEditor, properties, "_WobbleScale", "Wobble Scale");
        });
        
        DrawSection("🔮 DISTORTION", ref showDistortion, new Color(0.8f, 0.4f, 1f), () => {
            DrawProperty(materialEditor, properties, "_Distortion", "Distortion Strength");
            DrawProperty(materialEditor, properties, "_DistortionDetail", "Distortion Detail");
            DrawProperty(materialEditor, properties, "_ChromaticAberr", "Chromatic Aberration");
        });
        
        DrawSection("👻 DOPPELGANGER NOISE", ref showDoppelganger, new Color(0.6f, 0.2f, 0.8f), () => {
            DrawProperty(materialEditor, properties, "_DoppelPower", "Noise Power");
            DrawProperty(materialEditor, properties, "_DoppelScale", "Noise Scale");
            DrawProperty(materialEditor, properties, "_DoppelSpeed", "Noise Speed");
        });
        
        DrawSection("🌊 MICRO WAVES", ref showMicroWaves, new Color(0.2f, 0.7f, 0.9f), () => {
            DrawProperty(materialEditor, properties, "_MicroWaveScale", "Wave Scale");
            DrawProperty(materialEditor, properties, "_MicroWaveSpeed", "Wave Speed");
            DrawProperty(materialEditor, properties, "_MicroWaveStrength", "Wave Strength");
            DrawProperty(materialEditor, properties, "_MicroDistortion", "Wave Distortion");
        });
        
        DrawSection("🎨 SURFACE FINISH", ref showSurfaceFinish, new Color(0.9f, 0.7f, 0.3f), () => {
            DrawProperty(materialEditor, properties, "_Transparency", "Transparency");
            DrawProperty(materialEditor, properties, "_Shininess", "Shininess");
        });
        
        DrawSection("⭕ EDGE OPACITY", ref showEdgeOpacity, new Color(0.9f, 0.3f, 0.5f), () => {
            DrawProperty(materialEditor, properties, "_EdgeOpacity", "Edge Opacity");
            DrawProperty(materialEditor, properties, "_EdgePower", "Edge Sharpness");
        });
        
        DrawSection("⚙️ RENDERING QUALITY", ref showRenderingQuality, new Color(0.5f, 0.5f, 0.5f), () => {
            DrawProperty(materialEditor, properties, "_EnableDetail", "Enable Details");
            DrawProperty(materialEditor, properties, "_DetailDistance", "Detail Distance");
            DrawProperty(materialEditor, properties, "_DetailComplexity", "Detail Complexity");
        });
        
        EditorGUILayout.Space(15);
        DrawFooter();
    }
    
    private void DrawBanner()
    {
        // Main banner area
        Rect bannerRect = GUILayoutUtility.GetRect(0, 120, GUILayout.ExpandWidth(true));
        
        // Draw gradient background
        DrawGradientRect(bannerRect, deepBlack, darkRed);
        
        // Draw decorative borders
        DrawBorder(bannerRect, bloodRed, 3);
        
        // Inner glow effect
        Rect innerRect = new Rect(bannerRect.x + 5, bannerRect.y + 5, bannerRect.width - 10, bannerRect.height - 10);
        DrawBorder(innerRect, new Color(bloodRed.r, bloodRed.g, bloodRed.b, 0.3f), 1);
        
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
            
            // Shadow
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
        GUI.Label(titleRect, "BLOOD KILLER", titleStyle);
        
        GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
        {
            fontSize = 11,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 0.7f, 0.7f) },
            padding = new RectOffset(0, 20, 0, 0)
        };
        
        Rect subtitleRect = new Rect(bannerRect.x, bannerRect.y + 50, bannerRect.width, 20);
        GUI.Label(subtitleRect, "by Kawaii Studio", subtitleStyle);
        
        // Version badge
        Rect versionRect = new Rect(bannerRect.x + bannerRect.width - 70, bannerRect.y + bannerRect.height - 25, 60, 18);
        EditorGUI.DrawRect(versionRect, new Color(bloodRed.r, bloodRed.g, bloodRed.b, 0.8f));
        GUI.Label(versionRect, "v2.0", new GUIStyle(EditorStyles.miniLabel) { 
            alignment = TextAnchor.MiddleCenter, 
            normal = { textColor = Color.white },
            fontStyle = FontStyle.Bold
        });
    }
    
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
    
    private void DrawFooter()
    {
        // Separator
        Rect sepRect = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
        DrawGradientRect(sepRect, darkRed, bloodRed);
        
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
        // Simple gradient simulation with multiple rects
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
        // Top
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        // Bottom
        EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
        // Left
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        // Right
        EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
    }
    
    private void LoadAssets()
    {
        if (logoTexture == null)
        {
            logoTexture = KawaiiStudio.KawaiiStudioBranding.Logo;
        }
        if (bannerTexture == null)
        {
            bannerTexture = KawaiiStudio.KawaiiStudioBranding.Banner;
        }
    }
    }
}
