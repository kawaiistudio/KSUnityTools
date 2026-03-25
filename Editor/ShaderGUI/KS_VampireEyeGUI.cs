using UnityEngine;
using UnityEditor;

namespace KawaiiStudio.Shaders.Editor
{
    public class KS_VampireEyeGUI : ShaderGUI
    {
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string VRCHAT_URL = "https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab";
        
        // Local cached assets
        private const string LOGO_PATH = "Assets/Kawaii Studio/References/logo.png";
        private const string BANNER_PATH = "Assets/Kawaii Studio/References/banner.png";

        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
    
        // Foldout states
        private static bool showMainSettings = true;
        private static bool showPupilSettings = true;
        private static bool showShapeSettings = true;
        private static bool showSparkleSettings = false;
        private static bool showHueSettings = false;
        private static bool showRingSettings = false;
        private static bool showVignetteSettings = false;
        private static bool showParallaxSettings = false;
        
        // Colors
        private static readonly Color eyeRed = new Color(0.9f, 0.1f, 0.1f);
        private static readonly Color darkRed = new Color(0.4f, 0.05f, 0.05f);
        private static readonly Color deepBlack = new Color(0.1f, 0.1f, 0.1f);
        private static readonly Color sectionBg = new Color(0.18f, 0.18f, 0.18f);
        
        // Section Colors
        private static readonly Color colMain = new Color(1f, 0.3f, 0.3f);       // Red
        private static readonly Color colPupil = new Color(1f, 0.6f, 0.2f);      // Orange
        private static readonly Color colShape = new Color(0.8f, 0.4f, 1f);      // Purple
        private static readonly Color colSparkle = new Color(1f, 1f, 0.6f);      // Yellow/Star
        private static readonly Color colHue = new Color(0.2f, 0.8f, 0.5f);      // Green/Teal
        private static readonly Color colRing = new Color(1f, 0.9f, 0.4f);       // Gold
        private static readonly Color colVignette = new Color(0.5f, 0.5f, 0.5f); // Grey
        private static readonly Color colParallax = new Color(0.3f, 0.6f, 1f);   // Blue

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            if (logoTexture == null || bannerTexture == null)
            {
                LoadAssets();
            }
            
            // Draw banner header
            DrawBanner();
            
            EditorGUILayout.Space(10);
            
            // Draw sections
            DrawSection("👁️ MAIN SETTINGS", ref showMainSettings, colMain, () => {
                DrawProperty(materialEditor, properties, "_MainColor", "Eye Color");
                // DrawProperty(materialEditor, properties, "_MainTex", "Texture (Optional)"); // Removed as requested
                DrawProperty(materialEditor, properties, "_SnakePupil", "Snake Pupil Blend");
                DrawProperty(materialEditor, properties, "_PupilSize", "Pupil Size");
                DrawProperty(materialEditor, properties, "_PupilThickness", "Pupil Thickness");
            });

            DrawSection("🔥 PUPIL SMOKE & FILL", ref showPupilSettings, colPupil, () => {
                EditorGUILayout.LabelField("Global Smoke", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_SmokeSpeed", "Global Smoke Speed");
                DrawProperty(materialEditor, properties, "_SmokeFrequency", "Global Smoke Freq");
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Pupil Smoke", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_PupilSmokeColor", "Smoke Color");
                DrawProperty(materialEditor, properties, "_PupilSmokeSize", "Smoke Size");
                DrawProperty(materialEditor, properties, "_PupilSmokeSpeed", "Smoke Speed");
                
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Solid Fill", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_PupilSolidColor", "Solid Color");
                DrawProperty(materialEditor, properties, "_PupilSolidOpacity", "Solid Opacity");
            });
            
            DrawSection("✨ PUPIL SHAPE", ref showShapeSettings, colShape, () => {
                DrawProperty(materialEditor, properties, "_PupilShape", "Shape Selection");
                DrawProperty(materialEditor, properties, "_PupilShapeLerp", "Shape Influence");
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Shape Visibility", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_PupilShapeSize", "Shape Size");
                DrawProperty(materialEditor, properties, "_PupilShapeSharpness", "Shape Sharpness");
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("Glow (Visible de loin)", EditorStyles.boldLabel);
                DrawProperty(materialEditor, properties, "_PupilGlowColor", "Glow Color");
                DrawProperty(materialEditor, properties, "_PupilGlowIntensity", "Glow Intensity");
                EditorGUILayout.Space(5);
                DrawProperty(materialEditor, properties, "_PupilParallax", "Pupil Depth (Parallax)");
                DrawProperty(materialEditor, properties, "_PupilVerticalOffset", "Vertical Offset");
                DrawProperty(materialEditor, properties, "_PupilAspectRatio", "Aspect Ratio");
            });

            DrawSection("🌟 SPARKLES", ref showSparkleSettings, colSparkle, () => {
                DrawProperty(materialEditor, properties, "_SparkleColor", "Glow Color");
                DrawProperty(materialEditor, properties, "_SparkleHueShift", "Glow Hueshift");
                DrawProperty(materialEditor, properties, "_SparkleBrightness", "Brightness");
                EditorGUILayout.Space(5);
                DrawProperty(materialEditor, properties, "_SparkleSize", "Size");
                DrawProperty(materialEditor, properties, "_SparkleCount", "Particle Number");
                DrawProperty(materialEditor, properties, "_SparkleSpeed", "Speed");
                DrawProperty(materialEditor, properties, "_SparkleTwinkle", "Twinkle Speed");
                EditorGUILayout.Space(5);
                DrawProperty(materialEditor, properties, "_SparkleFOV", "FOV (Spread)");
                DrawProperty(materialEditor, properties, "_SparkleSeed", "Seed");
            });

            DrawSection("🌈 HUE ANIMATION", ref showHueSettings, colHue, () => {
                DrawProperty(materialEditor, properties, "_MainHueShift", "Hue Offset");
                DrawProperty(materialEditor, properties, "_MainHueSpeed", "Hue Speed");
            });
            
            DrawSection("💫 RINGS", ref showRingSettings, colRing, () => {
                DrawProperty(materialEditor, properties, "_LimbusMode", "Use Limbus/Border Mode");
                DrawProperty(materialEditor, properties, "_RingCount", "Layer Count");
                DrawProperty(materialEditor, properties, "_RingThickness", "Thickness");
                DrawProperty(materialEditor, properties, "_RingSize", "Radius/Size");
            });

            DrawSection("🌑 VIGNETTE", ref showVignetteSettings, colVignette, () => {
                DrawProperty(materialEditor, properties, "_SurfaceVignette", "Surface Vignette");
                DrawProperty(materialEditor, properties, "_ParallaxVignette", "Parallax Vignette");
            });
            
            DrawSection("🌌 PARALLAX", ref showParallaxSettings, colParallax, () => {
                DrawProperty(materialEditor, properties, "_MainParallax", "Parallax Strength");
                DrawProperty(materialEditor, properties, "_ParallaxCenter", "Parallax Center (X, Y)");
                DrawProperty(materialEditor, properties, "_ParallaxMirror", "Mirror for Right Eye");
                DrawProperty(materialEditor, properties, "_DistanceBasedParallaxScaling", "Distance Scaling");
            });
            
            EditorGUILayout.Space(15);
            DrawFooter();
        }
        
        private void DrawBanner()
        {
            // Main banner area
            Rect bannerRect = GUILayoutUtility.GetRect(0, 130, GUILayout.ExpandWidth(true));
            
            // Draw gradient background
            DrawGradientRect(bannerRect, deepBlack, darkRed);
            
            // Draw decorative borders
            DrawBorder(bannerRect, eyeRed, 3);
            
            // Inner glow effect
            Rect innerRect = new Rect(bannerRect.x + 5, bannerRect.y + 5, bannerRect.width - 10, bannerRect.height - 10);
            DrawBorder(innerRect, new Color(eyeRed.r, eyeRed.g, eyeRed.b, 0.3f), 1);
            
            // Draw banner texture if available - CENTERED AND LARGER
            if (bannerTexture != null)
            {
                float aspectRatio = (float)bannerTexture.width / bannerTexture.height;
                // Allow larger width
                float bannerWidth = Mathf.Min(bannerRect.width - 40, 450); 
                float bannerHeight = bannerWidth / aspectRatio;
                
                // Clamp height to fit
                if (bannerHeight > bannerRect.height - 20)
                {
                    bannerHeight = bannerRect.height - 20;
                    bannerWidth = bannerHeight * aspectRatio;
                }
                
                Rect texRect = new Rect(
                    bannerRect.x + (bannerRect.width - bannerWidth) / 2,
                    bannerRect.y + (bannerRect.height - bannerHeight) / 2,
                    bannerWidth,
                    bannerHeight
                );
                
                GUI.DrawTexture(texRect, bannerTexture, ScaleMode.ScaleToFit);
            }
            
            // Draw logo overlay - LEFT
            if (logoTexture != null)
            {
                float logoSize = 70; // Slightly larger
                Rect logoRect = new Rect(
                    bannerRect.x + 20,
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
            
            // Title text - RIGHT
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 24,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white },
                padding = new RectOffset(0, 25, 0, 0),
                fontStyle = FontStyle.Bold
            };
            
            // Add shadow to text for readability
            GUIStyle shadowStyle = new GUIStyle(titleStyle) { normal = { textColor = new Color(0,0,0,0.8f) } };
            
            Rect titleRect = new Rect(bannerRect.x, bannerRect.y + 30, bannerRect.width, 35);
            GUI.Label(new Rect(titleRect.x + 2, titleRect.y + 2, titleRect.width, titleRect.height), "VAMPIRE EYE", shadowStyle);
            GUI.Label(titleRect, "VAMPIRE EYE", titleStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.8f, 0.8f) },
                padding = new RectOffset(0, 25, 0, 0)
            };
            
            Rect subtitleRect = new Rect(bannerRect.x, bannerRect.y + 60, bannerRect.width, 20);
             GUI.Label(new Rect(subtitleRect.x + 1, subtitleRect.y + 1, subtitleRect.width, subtitleRect.height), "by Kawaii Studio", new GUIStyle(subtitleStyle){ normal = { textColor = Color.black } });
            GUI.Label(subtitleRect, "by Kawaii Studio", subtitleStyle);
            
            // Version badge
            Rect versionRect = new Rect(bannerRect.x + bannerRect.width - 80, bannerRect.y + bannerRect.height - 30, 60, 20);
            EditorGUI.DrawRect(versionRect, new Color(eyeRed.r, eyeRed.g, eyeRed.b, 0.9f));
            GUI.Label(versionRect, "v2.0", new GUIStyle(EditorStyles.whiteMiniLabel) { 
                alignment = TextAnchor.MiddleCenter, 
                fontStyle = FontStyle.Bold,
                fontSize = 11
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
            DrawGradientRect(sepRect, darkRed, eyeRed);
            
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
                logoTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(LOGO_PATH);
            }
            if (bannerTexture == null)
            {
                bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BANNER_PATH);
            }
        }
    }
}

