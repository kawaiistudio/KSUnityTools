using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDK3.Avatars.Components;
using VRC.Core;
using VRC.SDK3A.Editor;
using System.Linq;

namespace KawaiiStudio
{
    public class NSFWDetectorWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;

        private GameObject targetAvatar;
        private Vector2 scrollPosition;
        
        private bool checkKeywords = true;
        private bool checkTextures = true;
        private bool checkMeshes = true;

        private List<DetectionResult> results = new List<DetectionResult>();
        private bool isScanning = false;
        private bool scanComplete = false;
        private float scanProgress = 0f;
        private int riskScore = 0;
        private bool nsfwDetected = false;

        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string VRCHAT_URL = "https://vrchat.com/home/group/grp_7bf987ee-2f4a-4eae-b9b5-c060b97250ab";
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        
        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
        private static bool isDownloadingLogo = false;

        // Colors
        private static readonly Color primaryPink = new Color(0.9f, 0.35f, 0.6f);
        private static readonly Color darkPink = new Color(0.35f, 0.1f, 0.2f);
        private static readonly Color deepPurple = new Color(0.12f, 0.1f, 0.18f);
        private static readonly Color accentCyan = new Color(0.4f, 0.85f, 0.95f);
        private static readonly Color safeGreen = new Color(0.2f, 0.75f, 0.35f);
        private static readonly Color dangerRed = new Color(0.9f, 0.25f, 0.25f);
        private static readonly Color warningOrange = new Color(1f, 0.65f, 0.2f);

        // UI styles (cached) - keeps everything clickable (no overlay draw after layout)
        private static bool uiReady = false;
        private static readonly Dictionary<string, Texture2D> uiTexCache = new Dictionary<string, Texture2D>();
        private static GUIStyle pagePaddingStyle;
        private static GUIStyle sectionBoxStyle;
        private static GUIStyle sectionTitleStyle;
        private static GUIStyle smallTextStyle;
        private static GUIStyle toggleTextStyle;
        private static GUIStyle bigButtonStyle;
        private static GUIStyle chipStyle;

        private const string TAG_SEXUALLY_SUGGESTIVE = "content_sex";
        private const string TAG_ADULT_THEMES = "content_adult";

        private readonly string[] suspiciousKeywords = new string[]
        {
            "nude", "naked", "sex", "genital", "penis", "vagina", "dick", "cock", 
            "pussy", "nipple", "breast_exposed", "dps", "penetration", "raliv", 
            "toy", "dildo", "vibrator", "nsfw", "h-scene", "18+", "anal", "orifice",
            "lewd", "explicit", "uncensored", "adult"
        };

        private readonly string[] safeKeywords = new string[]
        {
            "eye", "mouth", "blink", "smile", "brow", "lips", "teeth", "tongue", "face",
            "neck", "wrist", "waist", "sleeve", "ankle", "button", "zipper", "sock", "shoe"
        };

        [MenuItem("Kawaii Studio/VRC/NSFW Detector")]
        public static void ShowWindow()
        {
            NSFWDetectorWindow window = GetWindow<NSFWDetectorWindow>("NSFW Detector");
            window.minSize = new Vector2(450, 700);
        }

        private void OnEnable()
        {
            if ((logoTexture == null || bannerTexture == null) && !isDownloadingLogo)
                DownloadAssets();
        }

        private void OnGUI()
        {
            EnsureUIStyles();
            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), deepPurple);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(pagePaddingStyle);
            
            // === BANNER ===
            DrawBanner();
            
            GUILayout.Space(10);
            
            // === INFO BOX ===
            DrawSection("ℹ️ VRChat Moderation Rules", accentCyan, () => {
                EditorGUILayout.LabelField("• PUBLIC avatars must NEVER contain NSFW (even hidden)", GetInfoStyle());
                EditorGUILayout.LabelField("• PRIVATE avatars with NSFW require Content Warning tags", GetInfoStyle());
                EditorGUILayout.LabelField("• All bans are by HUMAN moderators, not AI", GetInfoStyle());
                EditorGUILayout.LabelField("• You are responsible for your uploaded content", GetInfoStyle());
            });
            
            GUILayout.Space(10);
            
            // === TARGET AVATAR ===
            DrawSection("🎯 TARGET AVATAR", primaryPink, () => {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Drop your avatar prefab here:", GetLabelStyle());
                
                EditorGUI.BeginChangeCheck();
                targetAvatar = (GameObject)EditorGUILayout.ObjectField(targetAvatar, typeof(GameObject), true, GUILayout.Height(30));
                if (EditorGUI.EndChangeCheck())
                {
                    scanComplete = false;
                    results.Clear();
                    nsfwDetected = false;
                }
                
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Scan Options:", GetLabelStyle());
                GUILayout.Space(3);
                
                checkKeywords = DrawToggle("🔍 Scan Names & Keywords", checkKeywords);
                checkTextures = DrawToggle("🖼️ Analyze Textures (Skin Tone)", checkTextures);
                checkMeshes = DrawToggle("📐 Analyze Meshes & Blendshapes", checkMeshes);
            });
            
            GUILayout.Space(15);
            
            // === SCAN BUTTON ===
            DrawScanButton();
            
            GUILayout.Space(15);
            
            // === VERDICT ===
            if (scanComplete)
            {
                DrawVerdict();
                GUILayout.Space(10);
                
                if (nsfwDetected)
                {
                    DrawVRChatRecommendations();
                    GUILayout.Space(10);
                }
            }
            
            // === RESULTS ===
            if (results.Count > 0 || scanComplete)
            {
                DrawResults();
            }
            
            GUILayout.Space(20);
            
            // === FOOTER ===
            DrawFooter();

            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawBanner()
        {
            GUILayout.Space(10);
            
            // Banner background
            Rect bannerRect = EditorGUILayout.BeginVertical();
            GUILayout.Space(120);
            EditorGUILayout.EndVertical();
            
            bannerRect.x += 10;
            bannerRect.width -= 20;
            bannerRect.height = 120;
            
            // Draw background
            EditorGUI.DrawRect(bannerRect, darkPink);
            DrawBorder(bannerRect, primaryPink, 2);
            
            // Banner texture (fill the whole pink rectangle)
            if (bannerTexture != null)
            {
                // Slight inset to keep the outer border visible
                Rect texRect = new Rect(bannerRect.x + 2, bannerRect.y + 2, bannerRect.width - 4, bannerRect.height - 4);
                GUI.DrawTexture(texRect, bannerTexture, ScaleMode.ScaleAndCrop);
            }
            
            // Logo
            if (logoTexture != null)
            {
                Rect logoRect = new Rect(bannerRect.x + 10, bannerRect.y + 30, 60, 60);
                GUI.color = new Color(0, 0, 0, 0.4f);
                GUI.DrawTexture(new Rect(logoRect.x + 2, logoRect.y + 2, logoRect.width, logoRect.height), logoTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit);
            }
            
            // Title
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            Rect titleRect = new Rect(bannerRect.x, bannerRect.y + 75, bannerRect.width - 15, 25);
            GUI.Label(titleRect, "NSFW DETECTOR", titleStyle);
            
            // Subtitle
            GUIStyle subStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.8f, 0.9f) }
            };
            Rect subRect = new Rect(bannerRect.x, bannerRect.y + 95, bannerRect.width - 15, 18);
            GUI.Label(subRect, "Kawaii Studio VRChat NSFW Detector", subStyle);
            
            // Version
            Rect verRect = new Rect(bannerRect.x + bannerRect.width - 45, bannerRect.y + 5, 40, 18);
            EditorGUI.DrawRect(verRect, primaryPink);
            GUI.Label(verRect, "v" + VERSION, new GUIStyle(EditorStyles.miniLabel) { 
                alignment = TextAnchor.MiddleCenter, 
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            });
        }

        private void DrawSection(string title, Color accentColor, System.Action content)
        {
            EnsureUIStyles();

            GUILayout.BeginVertical(sectionBoxStyle);

            // Header bar (draw first, so it won't block inputs)
            Rect headerRect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(headerRect, new Color(accentColor.r * 0.25f, accentColor.g * 0.25f, accentColor.b * 0.25f, 1f));
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4, headerRect.height), accentColor);
            GUI.Label(new Rect(headerRect.x + 10, headerRect.y + 6, headerRect.width - 20, headerRect.height - 12), title, sectionTitleStyle);

            GUILayout.Space(8);
            EditorGUI.indentLevel++;
            content();
            EditorGUI.indentLevel--;
            GUILayout.Space(6);

            GUILayout.EndVertical();
        }

        private bool DrawToggle(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal();
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = value ? safeGreen : new Color(0.3f, 0.3f, 0.3f);
            
            GUIStyle toggleBtn = new GUIStyle(GUI.skin.button)
            {
                fixedWidth = 28,
                fixedHeight = 20,
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            
            if (GUILayout.Button(value ? "✓" : " ", toggleBtn))
            {
                value = !value;
            }
            
            GUI.backgroundColor = oldBg;
            
            GUILayout.Label(label, toggleTextStyle);
            
            EditorGUILayout.EndHorizontal();
            return value;
        }

        private void DrawScanButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.enabled = targetAvatar != null && !isScanning;
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isScanning ? Color.gray : primaryPink;
            
            GUIStyle btnStyle = bigButtonStyle;
            
            string btnText = isScanning ? $"SCANNING... {(int)(scanProgress*100)}%" : "🔎 CHECK AVATAR";
            
            if (GUILayout.Button(btnText, btnStyle))
            {
                StartScan();
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            // Progress bar
            if (isScanning)
            {
                GUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(50);
                Rect progRect = EditorGUILayout.GetControlRect(GUILayout.Height(6));
                progRect.width = position.width - 100;
                EditorGUI.DrawRect(progRect, new Color(0.2f, 0.2f, 0.2f));
                EditorGUI.DrawRect(new Rect(progRect.x, progRect.y, progRect.width * scanProgress, progRect.height), primaryPink);
                GUILayout.Space(50);
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawVerdict()
        {
            Color verdictColor = nsfwDetected ? dangerRed : safeGreen;
            string verdictText = nsfwDetected ? "⚠️ NOT SAFE FOR PUBLIC" : "✅ SAFE FOR PUBLIC";
            string subText = nsfwDetected ? "NSFW content detected - see recommendations below" : "No obvious NSFW content found";
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            
            Rect verdictRect = EditorGUILayout.BeginVertical();
            GUILayout.Space(70);
            EditorGUILayout.EndVertical();
            
            verdictRect.width = position.width - 30;
            verdictRect.height = 70;
            
            EditorGUI.DrawRect(verdictRect, verdictColor);
            DrawBorder(verdictRect, new Color(1f, 1f, 1f, 0.3f), 2);
            
            GUI.Label(new Rect(verdictRect.x, verdictRect.y + 15, verdictRect.width, 30), verdictText, 
                new GUIStyle(EditorStyles.boldLabel) { 
                    fontSize = 20, 
                    alignment = TextAnchor.MiddleCenter, 
                    normal = { textColor = Color.white } 
                });
            
            GUI.Label(new Rect(verdictRect.x, verdictRect.y + 45, verdictRect.width, 20), subText, 
                new GUIStyle(EditorStyles.label) { 
                    fontSize = 11, 
                    alignment = TextAnchor.MiddleCenter, 
                    normal = { textColor = new Color(1f, 1f, 1f, 0.9f) } 
                });
            
            GUILayout.Space(15);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawVRChatRecommendations()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            EditorGUILayout.LabelField("⚠️ NSFW Content Detected - Action Required", new GUIStyle(EditorStyles.boldLabel) { 
                normal = { textColor = warningOrange }, 
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter
            });
            
            GUILayout.Space(8);
            
            EditorGUILayout.LabelField("📛 If uploading as PUBLIC:", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = dangerRed } });
            EditorGUILayout.LabelField("   → Remove ALL NSFW content before upload", GetInfoStyle());
            
            GUILayout.Space(5);
            
            EditorGUILayout.LabelField("✅ If uploading as PRIVATE:", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = safeGreen } });
            EditorGUILayout.LabelField("   → Use the button below to configure SDK", GetInfoStyle());
            
            GUILayout.Space(10);
            
            // SDK State
            DrawCurrentSDKState();
            
            GUILayout.Space(10);
            
            // Buttons
            Color oldBg = GUI.backgroundColor;
            
            GUI.backgroundColor = safeGreen;
            if (GUILayout.Button("🏷️ AUTO-SET: Private + Content Warnings", GUILayout.Height(32)))
            {
                AutoSetContentWarnings();
            }
            
            GUI.backgroundColor = new Color(0.3f, 0.5f, 0.8f);
            if (GUILayout.Button("📂 Open VRChat SDK Panel", GUILayout.Height(26)))
            {
                OpenVRChatSDK();
            }
            
            GUI.backgroundColor = oldBg;
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawCurrentSDKState()
        {
            string currentTags = AvatarBuilderSessionState.AvatarTags;
            string currentStatus = AvatarBuilderSessionState.AvatarReleaseStatus;

            bool hasSexTag = currentTags.Contains(TAG_SEXUALLY_SUGGESTIVE);
            bool hasAdultTag = currentTags.Contains(TAG_ADULT_THEMES);
            bool isPrivate = currentStatus == "private";

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Current SDK Settings:", EditorStyles.boldLabel);
            
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label) { richText = true, fontSize = 10 };
            EditorGUILayout.LabelField($"  {(isPrivate ? "✅" : "❌")} Visibility: {(isPrivate ? "<color=#80FF80>Private</color>" : "<color=#FF8080>Public</color>")}", statusStyle);
            EditorGUILayout.LabelField($"  {(hasSexTag ? "✅" : "❌")} Sexually Suggestive: {(hasSexTag ? "<color=#80FF80>ON</color>" : "<color=#FF8080>OFF</color>")}", statusStyle);
            EditorGUILayout.LabelField($"  {(hasAdultTag ? "✅" : "❌")} Adult Themes: {(hasAdultTag ? "<color=#80FF80>ON</color>" : "<color=#FF8080>OFF</color>")}", statusStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawResults()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(15);
            
            EditorGUILayout.BeginVertical();
            
            string headerText = results.Count > 0 ? $"📋 Issues Found ({results.Count})" : "📋 No Issues Found";
            EditorGUILayout.LabelField(headerText, new GUIStyle(EditorStyles.boldLabel) { 
                normal = { textColor = Color.white }, 
                fontSize = 12 
            });
            
            GUILayout.Space(5);
            
            foreach (var res in results)
            {
                Color boxColor = res.severity == Severity.High ? new Color(0.5f, 0.15f, 0.15f) : 
                                 (res.severity == Severity.Medium ? new Color(0.5f, 0.35f, 0.1f) : new Color(0.15f, 0.4f, 0.15f));
                
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                
                string icon = res.severity == Severity.High ? "⛔" : (res.severity == Severity.Medium ? "⚠️" : "ℹ️");
                EditorGUILayout.LabelField(icon, GUILayout.Width(25));
                
                EditorGUILayout.LabelField(res.message, new GUIStyle(EditorStyles.label) { 
                    wordWrap = true, 
                    normal = { textColor = new Color(0.9f, 0.9f, 0.9f) },
                    fontSize = 11
                });
                
                if (res.targetObject != null)
                {
                    if (GUILayout.Button("Find", GUILayout.Width(45), GUILayout.Height(20)))
                    {
                        EditorGUIUtility.PingObject(res.targetObject);
                        Selection.activeObject = res.targetObject;
                    }
                }
                
                EditorGUILayout.EndHorizontal();
                GUILayout.Space(3);
            }
            
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(15);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            // Separator
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(40);
            Rect sepRect = EditorGUILayout.GetControlRect(GUILayout.Height(2));
            sepRect.width = position.width - 80;
            for (int i = 0; i < 20; i++)
            {
                float t = (float)i / 20;
                EditorGUI.DrawRect(new Rect(sepRect.x + sepRect.width * t / 20 * 20, sepRect.y, sepRect.width / 20 + 1, 2), 
                    Color.Lerp(darkPink, primaryPink, t));
            }
            GUILayout.Space(40);
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(15);
            
            // Social buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            DrawSocialButton("Discord", DISCORD_URL, new Color(0.34f, 0.4f, 0.95f));
            GUILayout.Space(5);
            DrawSocialButton("Telegram", TELEGRAM_URL, new Color(0.16f, 0.63f, 0.89f));
            GUILayout.Space(5);
            DrawSocialButton("VRChat", VRCHAT_URL, new Color(0.07f, 0.71f, 0.65f));
            GUILayout.Space(5);
            DrawSocialButton("GitHub", GITHUB_URL, new Color(0.5f, 0.5f, 0.5f));
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(10);
            
            GUILayout.Label("Made with by Kawaii Studio", smallTextStyle);
            GUILayout.Label("Join our community!", new GUIStyle(smallTextStyle) { normal = { textColor = new Color(1f, 1f, 1f, 0.45f) } });
            
            GUILayout.Space(10);
        }

        private void DrawSocialButton(string label, string url, Color color)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            
            if (GUILayout.Button(label, GUILayout.Width(70), GUILayout.Height(25)))
            {
                Application.OpenURL(url);
            }
            
            GUI.backgroundColor = oldBg;
        }

        private void DrawBorder(Rect rect, Color color, int thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
        }

        private void EnsureUIStyles()
        {
            if (uiReady) return;

            pagePaddingStyle = new GUIStyle { padding = new RectOffset(12, 12, 12, 12) };

            sectionBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 0)
            };
            sectionBoxStyle.normal.background = GetRoundedTex(new Color(0.15f, 0.13f, 0.20f, 1f), new Color(1f, 1f, 1f, 0.08f), 10, 2);

            sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            smallTextStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 1f, 1f, 0.65f) }
            };

            toggleTextStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };

            bigButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                fixedHeight = 45,
                fixedWidth = 220
            };

            chipStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            uiReady = true;
        }

        private Texture2D GetRoundedTex(Color fill, Color border, int radius, int borderThickness)
        {
            string key = $"{fill.r:F3},{fill.g:F3},{fill.b:F3},{fill.a:F3}|{border.r:F3},{border.g:F3},{border.b:F3},{border.a:F3}|r{radius}|b{borderThickness}";
            if (uiTexCache.TryGetValue(key, out var tex) && tex != null) return tex;

            const int w = 32;
            const int h = 32;
            tex = new Texture2D(w, h, TextureFormat.ARGB32, false) { hideFlags = HideFlags.HideAndDontSave };
            tex.wrapMode = TextureWrapMode.Clamp;

            Color[] px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a = RoundedMask(x, y, w, h, radius);
                    Color c = fill; c.a *= a;

                    if (borderThickness > 0)
                    {
                        float ai = RoundedMask(x, y, w, h, Mathf.Max(0, radius - borderThickness));
                        float ba = Mathf.Clamp01(a - ai);
                        if (ba > 0.001f)
                        {
                            Color bc = border; bc.a *= ba;
                            c = AlphaBlend(c, bc);
                        }
                    }

                    px[y * w + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            uiTexCache[key] = tex;
            return tex;
        }

        private static float RoundedMask(int x, int y, int w, int h, int radius)
        {
            if (radius <= 0) return 1f;

            float r = radius - 0.5f;
            float px = x + 0.5f;
            float py = y + 0.5f;

            float minX = r + 0.5f;
            float minY = r + 0.5f;
            float maxX = w - (r + 0.5f);
            float maxY = h - (r + 0.5f);

            float dx = (px < minX) ? (minX - px) : (px > maxX ? (px - maxX) : 0f);
            float dy = (py < minY) ? (minY - py) : (py > maxY ? (py - maxY) : 0f);

            float dist = Mathf.Sqrt(dx * dx + dy * dy);
            return dist <= r ? 1f : 0f;
        }

        private static Color AlphaBlend(Color under, Color over)
        {
            float a = over.a + under.a * (1f - over.a);
            if (a <= 0.0001f) return new Color(0, 0, 0, 0);
            float r = (over.r * over.a + under.r * under.a * (1f - over.a)) / a;
            float g = (over.g * over.a + under.g * under.a * (1f - over.a)) / a;
            float b = (over.b * over.a + under.b * under.a * (1f - over.a)) / a;
            return new Color(r, g, b, a);
        }

        private GUIStyle GetLabelStyle()
        {
            return new GUIStyle(EditorStyles.label) { normal = { textColor = accentCyan }, fontStyle = FontStyle.Bold };
        }

        private GUIStyle GetInfoStyle()
        {
            return new GUIStyle(EditorStyles.label) { normal = { textColor = new Color(0.8f, 0.8f, 0.85f) }, fontSize = 11, wordWrap = true };
        }

        // === SCAN LOGIC ===

        private void AutoSetContentWarnings()
        {
            string currentTags = AvatarBuilderSessionState.AvatarTags;
            List<string> tagsList = new List<string>();
            
            if (!string.IsNullOrEmpty(currentTags))
                tagsList = currentTags.Split('|').Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

            if (!tagsList.Contains(TAG_SEXUALLY_SUGGESTIVE)) tagsList.Add(TAG_SEXUALLY_SUGGESTIVE);
            if (!tagsList.Contains(TAG_ADULT_THEMES)) tagsList.Add(TAG_ADULT_THEMES);

            AvatarBuilderSessionState.AvatarTags = string.Join("|", tagsList);
            AvatarBuilderSessionState.AvatarReleaseStatus = "private";

            EditorUtility.DisplayDialog("✅ Content Warnings Set", 
                "VRChat SDK configured:\n\n✅ Visibility: Private\n✅ Sexually Suggestive: Enabled\n✅ Adult Themes: Enabled", "OK");
            Repaint();
            OpenVRChatSDK();
        }

        private void OpenVRChatSDK()
        {
            EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
        }

        private void StartScan()
        {
            results.Clear();
            riskScore = 0;
            nsfwDetected = false;
            isScanning = true;
            scanComplete = false;
            EditorApplication.delayCall += PerformScan;
        }

        private void PerformScan()
        {
            try
            {
                scanProgress = 0.1f;
                if (checkKeywords) { AnalyzeHierarchyKeywords(); scanProgress = 0.4f; }
                if (checkMeshes) { AnalyzeMeshes(); scanProgress = 0.7f; }
                if (checkTextures) { AnalyzeTextures(); scanProgress = 1f; }
            }
            catch (System.Exception e) { Debug.LogError($"Scan error: {e.Message}"); }
            finally
            {
                isScanning = false;
                scanComplete = true;
                Repaint();
            }
        }

        private void AnalyzeHierarchyKeywords()
        {
            foreach (Transform t in targetAvatar.GetComponentsInChildren<Transform>(true))
                CheckNameForNSFW(t.name, t.gameObject, "GameObject");
        }

        private void AnalyzeMeshes()
        {
            foreach (var smr in targetAvatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                CheckNameForNSFW(smr.sharedMesh.name, smr.gameObject, "Mesh");
                
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string bsName = smr.sharedMesh.GetBlendShapeName(i);
                    CheckNameForNSFW(bsName, smr.gameObject, "Blendshape");
                    
                    string lowerBs = bsName.ToLower();
                    bool isSafe = safeKeywords.Any(s => lowerBs.Contains(s));
                    if (!isSafe && (lowerBs.Contains("hole") || lowerBs.Contains("penetrate")))
                        AddResult($"Suspicious Blendshape: '{bsName}'", Severity.High, smr.gameObject);
                }

                foreach (var mat in smr.sharedMaterials)
                    if (mat != null) CheckNameForNSFW(mat.name, mat, "Material");
            }
        }

        private void AnalyzeTextures()
        {
            HashSet<Texture> checked_ = new HashSet<Texture>();
            foreach (var r in targetAvatar.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null) continue;
                    Texture tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : 
                                  (mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null);

                    if (tex != null && tex is Texture2D t2d && !checked_.Contains(tex))
                    {
                        checked_.Add(tex);
                        CheckNameForNSFW(tex.name, tex, "Texture");
                        
                        if (t2d.isReadable)
                        {
                            float skin = CalculateSkinPercentage(t2d);
                            if (skin > 0.70f)
                                AddResult($"Texture '{tex.name}' is {skin:P0} skin-colored", Severity.Medium, tex);
                        }
                    }
                }
            }
        }

        private void CheckNameForNSFW(string name, Object obj, string context)
        {
            string lower = name.ToLowerInvariant();
            if (safeKeywords.Any(s => lower.Contains(s))) return;
            foreach (string kw in suspiciousKeywords)
                if (lower.Contains(kw)) { AddResult($"NSFW keyword '{kw}' in {context}: '{name}'", Severity.High, obj); return; }
        }

        private void AddResult(string msg, Severity sev, Object target)
        {
            results.Add(new DetectionResult(msg, sev, target));
            if (sev == Severity.High || sev == Severity.Medium) nsfwDetected = true;
        }

        private float CalculateSkinPercentage(Texture2D tex)
        {
            try 
            {
                RenderTexture tmp = RenderTexture.GetTemporary(64, 64);
                Graphics.Blit(tex, tmp);
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = tmp;
                Texture2D mini = new Texture2D(64, 64);
                mini.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                mini.Apply();
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(tmp);

                Color[] px = mini.GetPixels();
                int skin = 0, opaque = 0;
                foreach (Color p in px)
                {
                    if (p.a < 0.1f) continue;
                    opaque++;
                    if (p.r > 0.35f && p.g > 0.2f && p.b > 0.1f && p.r > p.g && p.r > p.b && (p.r - p.g) > 0.02f && (p.r - p.g) < 0.6f)
                        skin++;
                }
                DestroyImmediate(mini);
                return opaque == 0 ? 0f : (float)skin / opaque;
            }
            catch { return 0f; }
        }

        private void DownloadAssets()
        {
            if (isDownloadingLogo) return;
            if (logoTexture != null && bannerTexture != null) return;

            // Branding is loaded locally from Assets/Kawaii Studio/Editor/Cache (no network).
            isDownloadingLogo = true;
            logoTexture = KawaiiStudioBranding.Logo;
            bannerTexture = KawaiiStudioBranding.Banner;
            isDownloadingLogo = false;
        }

        private class DetectionResult
        {
            public string message;
            public Severity severity;
            public Object targetObject;
            public DetectionResult(string m, Severity s, Object t) { message = m; severity = s; targetObject = t; }
        }

        private enum Severity { Low, Medium, High }
    }
}
