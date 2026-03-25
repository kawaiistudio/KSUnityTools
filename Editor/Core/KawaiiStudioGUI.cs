using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace KawaiiStudio
{
    public static class KawaiiStudioGUI
    {
        // === COLORS (Professional Dark Theme) ===
        // Backgrounds
        public static readonly Color BaseBackground = new Color(0.12f, 0.12f, 0.14f); // Dark slate
        public static readonly Color BaseBackgroundTop = new Color(0.14f, 0.13f, 0.18f); // Subtle purple tint
        public static readonly Color BaseBackgroundBottom = new Color(0.09f, 0.09f, 0.11f); // Deeper bottom
        public static readonly Color BoxBackground = new Color(0.2f, 0.2f, 0.2f); // Lighter section
        public static readonly Color FieldBackground = new Color(0.1f, 0.1f, 0.1f);
        
        // Accents - Professional Blue/Cyan
        public static readonly Color AccentColor = new Color(0.23f, 0.62f, 0.88f); // Soft Cyan/Blue
        public static readonly Color WarningColor = new Color(1.0f, 0.65f, 0.0f); // Orange
        public static readonly Color ErrorColor = new Color(0.9f, 0.2f, 0.2f); // Red
        public static readonly Color SuccessColor = new Color(0.2f, 0.8f, 0.4f); // Green
        
        // Text
        public static readonly Color TextColor = new Color(0.9f, 0.9f, 0.9f);
        public static readonly Color SubTextColor = new Color(0.7f, 0.7f, 0.7f);

        // === TEXTURES CACHE ===
        private static Dictionary<string, Texture2D> textureCache = new Dictionary<string, Texture2D>();

        // === STYLES ===
        private static bool stylesInitialized = false;
        public static GUIStyle SectionStyle { get; private set; }
        public static GUIStyle TitleStyle { get; private set; }
        public static GUIStyle SubTitleStyle { get; private set; }
        public static GUIStyle ButtonStyle { get; private set; }
        public static GUIStyle LinkButtonStyle { get; private set; }
        public static GUIStyle LabelStyle { get; private set; }
        public static GUIStyle InfoLabelStyle { get; private set; }
        public static GUIStyle BoxStyle { get; private set; }

        public static void Initialize()
        {
            if (stylesInitialized) return;

            SectionStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(15, 15, 15, 15),
                margin = new RectOffset(0, 0, 10, 10)
            };
            SectionStyle.normal.background = GetRoundedTexture(BoxBackground, new Color(1, 1, 1, 0.05f), 10, 1);
            // Important: enable 9-slicing so corners don't stretch into ovals.
            SectionStyle.border = new RectOffset(10, 10, 10, 10);

            TitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = TextColor },
                margin = new RectOffset(0, 0, 5, 10)
            };

            SubTitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                normal = { textColor = SubTextColor },
                fontStyle = FontStyle.Italic
            };

            ButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                fixedHeight = 35,
                padding = new RectOffset(10, 10, 5, 5),
                normal = { textColor = TextColor }
            };
            
            LinkButtonStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = AccentColor },
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };

            LabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = AccentColor },
                fontStyle = FontStyle.Bold
            };

            InfoLabelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = SubTextColor },
                fontSize = 11,
                wordWrap = true
            };
            
            BoxStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = GetRoundedTexture(FieldBackground, Color.clear, 5, 0) },
                padding = new RectOffset(10, 10, 10, 10)
            };
            BoxStyle.border = new RectOffset(5, 5, 5, 5);

            stylesInitialized = true;
        }

        // === DRAWING METHODS ===

        public static void DrawWindowBackground(Rect position)
        {
            if (Event.current.type != EventType.Repaint) return;

            Rect full = new Rect(0, 0, position.width, position.height);

            // Subtle vertical gradient (less "flat grey")
            int steps = 28;
            float stepH = Mathf.Max(1f, full.height / steps);
            for (int i = 0; i < steps; i++)
            {
                float t = (float)i / (steps - 1);
                Color c = Color.Lerp(BaseBackgroundTop, BaseBackgroundBottom, t);
                EditorGUI.DrawRect(new Rect(full.x, full.y + i * stepH, full.width, stepH + 1), c);
            }

            // Slight vignette bands for depth
            Color topShade = new Color(0, 0, 0, 0.18f);
            Color botShade = new Color(0, 0, 0, 0.22f);
            EditorGUI.DrawRect(new Rect(full.x, full.y, full.width, 18), topShade);
            EditorGUI.DrawRect(new Rect(full.x, full.yMax - 28, full.width, 28), botShade);
        }

        public static void DrawBanner(string title, string subtitle, string version, Texture2D logo = null, Texture2D bannerBg = null)
        {
            Initialize();
            GUILayout.Space(10);
            
            Rect bannerRect = EditorGUILayout.BeginVertical();
            GUILayout.Space(100);
            EditorGUILayout.EndVertical();
            
            bannerRect.x += 10;
            bannerRect.width -= 20;
            bannerRect.height = 100;
            
            // Background
            if (bannerBg != null)
            {
                GUI.DrawTexture(bannerRect, bannerBg, ScaleMode.ScaleAndCrop);
                EditorGUI.DrawRect(bannerRect, new Color(0, 0, 0, 0.4f));
            }
            else
            {
                // Gradient fallback (avoid a "grey slab" when banner image isn't available)
                int steps = 20;
                float stepW = Mathf.Max(1f, bannerRect.width / steps);
                Color left = new Color(BaseBackgroundTop.r * 1.05f, BaseBackgroundTop.g * 1.05f, BaseBackgroundTop.b * 1.05f, 1f);
                Color right = new Color(AccentColor.r * 0.18f, AccentColor.g * 0.18f, AccentColor.b * 0.18f, 1f);
                for (int i = 0; i < steps; i++)
                {
                    float t = (float)i / (steps - 1);
                    Color c = Color.Lerp(left, right, t);
                    EditorGUI.DrawRect(new Rect(bannerRect.x + i * stepW, bannerRect.y, stepW + 1, bannerRect.height), c);
                }
                EditorGUI.DrawRect(bannerRect, new Color(0, 0, 0, 0.15f));
            }
            
            DrawRoundedBorder(bannerRect, AccentColor, 2);

            // Logo
            if (logo != null)
            {
                Rect logoRect = new Rect(bannerRect.x + 15, bannerRect.y + 15, 70, 70);
                GUI.color = new Color(0, 0, 0, 0.5f);
                GUI.DrawTexture(new Rect(logoRect.x + 2, logoRect.y + 2, logoRect.width, logoRect.height), logo);
                GUI.color = Color.white;
                GUI.DrawTexture(logoRect, logo, ScaleMode.ScaleToFit);
            }

            // Texts
            float textX = logo != null ? bannerRect.x + 100 : bannerRect.x + 20;
            float textW = bannerRect.width - (logo != null ? 110 : 30);

            Rect titleRect = new Rect(textX, bannerRect.y + 25, textW, 30);
            GUI.Label(titleRect, title.ToUpper(), TitleStyle);

            Rect subRect = new Rect(textX, bannerRect.y + 55, textW, 20);
            GUI.Label(subRect, subtitle, SubTitleStyle);

            // Version
            Rect verRect = new Rect(bannerRect.x + bannerRect.width - 60, bannerRect.y + 10, 50, 20);
            GUIStyle verStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1, 1, 1, 0.8f) },
                fontStyle = FontStyle.Bold
            };
            EditorGUI.DrawRect(verRect, new Color(0, 0, 0, 0.5f));
            GUI.Label(verRect, "v" + version, verStyle);
        }

        public static void DrawSection(string title, System.Action content, Texture2D icon = null)
        {
            Initialize();
            EditorGUILayout.BeginVertical(SectionStyle);
            
            EditorGUILayout.BeginHorizontal();
            
            // Colored bar
            Rect barRect = GUILayoutUtility.GetRect(4, 20, GUILayout.Width(4));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, AccentColor);
            
            GUILayout.Space(8);

            GUILayout.Label(title, new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = TextColor } });
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            content?.Invoke();
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(5);
        }

        public static bool DrawToggle(string label, bool value)
        {
            Initialize();
            EditorGUILayout.BeginHorizontal();
            
            bool newValue = EditorGUILayout.Toggle(value, GUILayout.Width(20));
            
            GUILayout.Label(label, new GUIStyle(EditorStyles.label) { normal = { textColor = value ? AccentColor : SubTextColor } });
            
            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        public static void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Space(20);
            
            Rect lineRect = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, new Color(1, 1, 1, 0.1f));
            
            GUILayout.Space(10);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            DrawSocialLink("Discord", "https://discord.gg/xAeJrSAgqG");
            GUILayout.Label("|", new GUIStyle(EditorStyles.label) { normal = { textColor = Color.grey } });
            DrawSocialLink("GitHub", "https://github.com/kawaiistudio/KSUnityTools");
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(5);
            var centerStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
            GUILayout.Label("Powered by Kawaii Studio", centerStyle);
            GUILayout.Space(10);
        }

        private static void DrawSocialLink(string name, string url)
        {
            if (GUILayout.Button(name, LinkButtonStyle))
            {
                Application.OpenURL(url);
            }
            EditorGUIUtility.AddCursorRect(GUILayoutUtility.GetLastRect(), MouseCursor.Link);
        }

        // === UTILITIES ===

        private static void DrawRoundedBorder(Rect rect, Color color, float thickness)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
        }

        public static Texture2D GetRoundedTexture(Color color, Color borderColor, int radius, int borderThickness)
        {
            // Stable cache key (avoid Color.ToString() differences across Unity versions)
            string key =
                $"{color.r:F3},{color.g:F3},{color.b:F3},{color.a:F3}|" +
                $"{borderColor.r:F3},{borderColor.g:F3},{borderColor.b:F3},{borderColor.a:F3}|" +
                $"r{radius}|b{borderThickness}";

            if (textureCache.TryGetValue(key, out var cached) && cached != null) return cached;

            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            int rad = Mathf.Clamp(radius, 0, size / 2);
            int bt = Mathf.Clamp(borderThickness, 0, rad);

            // Rounded-rect SDF: distance to nearest rounded corner outside the inner rect.
            float half = size * 0.5f;
            float innerHalf = half - rad;

            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Pixel center in local space [-half..half]
                    float px = (x + 0.5f) - half;
                    float py = (y + 0.5f) - half;

                    float dx = Mathf.Max(Mathf.Abs(px) - innerHalf, 0f);
                    float dy = Mathf.Max(Mathf.Abs(py) - innerHalf, 0f);
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // Inside rounded rect if dist <= rad
                    float edge = rad - dist;

                    // 1px anti-alias
                    float alpha = Mathf.Clamp01(edge + 0.5f);

                    if (alpha <= 0.0001f)
                    {
                        pixels[y * size + x] = Color.clear;
                        continue;
                    }

                    Color c = color;
                    c.a *= alpha;

                    if (bt > 0)
                    {
                        // Border region: within bt of the edge (approx)
                        float innerEdge = (rad - bt) - dist;
                        float innerAlpha = Mathf.Clamp01(innerEdge + 0.5f);
                        float borderA = Mathf.Clamp01(alpha - innerAlpha);

                        if (borderA > 0.0001f)
                        {
                            Color bc = borderColor;
                            bc.a *= borderA;
                            // Alpha blend border over fill
                            float outA = bc.a + c.a * (1f - bc.a);
                            if (outA > 0.0001f)
                            {
                                float outR = (bc.r * bc.a + c.r * c.a * (1f - bc.a)) / outA;
                                float outG = (bc.g * bc.a + c.g * c.a * (1f - bc.a)) / outA;
                                float outB = (bc.b * bc.a + c.b * c.a * (1f - bc.a)) / outA;
                                c = new Color(outR, outG, outB, outA);
                            }
                        }
                    }

                    pixels[y * size + x] = c;
                }
            }

            tex.SetPixels(pixels);
            tex.Apply(false, false);

            textureCache[key] = tex;
            return tex;
        }
    }
}

