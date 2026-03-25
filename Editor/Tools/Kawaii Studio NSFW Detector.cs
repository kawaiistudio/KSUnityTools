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
        private const string VERSION = "1.4";

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
            KawaiiStudioGUI.Initialize();
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            KawaiiStudioGUI.DrawBanner(
                "NSFW DETECTOR",
                "VRChat Avatar Content Scanner",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner
            );

            GUILayout.Space(10);

            // === INFO BOX ===
            KawaiiStudioGUI.DrawSection("\u2139\ufe0f VRChat Moderation Rules", () =>
            {
                EditorGUILayout.LabelField("\u2022 PUBLIC avatars must NEVER contain NSFW (even hidden)", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("\u2022 PRIVATE avatars with NSFW require Content Warning tags", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("\u2022 All bans are by HUMAN moderators, not AI", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("\u2022 You are responsible for your uploaded content", KawaiiStudioGUI.InfoLabelStyle);
            });

            // === TARGET AVATAR ===
            KawaiiStudioGUI.DrawSection("\ud83c\udfaf TARGET AVATAR", () =>
            {
                EditorGUILayout.LabelField("Drop your avatar prefab here:", KawaiiStudioGUI.LabelStyle);
                GUILayout.Space(3);
                
                EditorGUI.BeginChangeCheck();
                targetAvatar = (GameObject)EditorGUILayout.ObjectField(targetAvatar, typeof(GameObject), true, GUILayout.Height(30));
                if (EditorGUI.EndChangeCheck())
                {
                    scanComplete = false;
                    results.Clear();
                    nsfwDetected = false;
                }
                
                GUILayout.Space(10);
                EditorGUILayout.LabelField("Scan Options:", KawaiiStudioGUI.LabelStyle);
                GUILayout.Space(3);
                
                checkKeywords = KawaiiStudioGUI.DrawToggle("\ud83d\udd0d Scan Names & Keywords", checkKeywords);
                checkTextures = KawaiiStudioGUI.DrawToggle("\ud83d\uddbc\ufe0f Analyze Textures (Skin Tone)", checkTextures);
                checkMeshes = KawaiiStudioGUI.DrawToggle("\ud83d\udcd0 Analyze Meshes & Blendshapes", checkMeshes);
            });

            GUILayout.Space(10);

            // === SCAN BUTTON ===
            DrawScanButton();

            GUILayout.Space(10);

            // === VERDICT ===
            if (scanComplete)
            {
                DrawVerdict();
                GUILayout.Space(5);

                if (nsfwDetected)
                {
                    DrawVRChatRecommendations();
                    GUILayout.Space(5);
                }
            }

            // === RESULTS ===
            if (results.Count > 0 || scanComplete)
            {
                DrawResults();
            }

            GUILayout.Space(15);
            KawaiiStudioGUI.DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        private void DrawScanButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUI.enabled = targetAvatar != null && !isScanning;
            GUI.backgroundColor = KawaiiStudioGUI.AccentColor;

            string btnText = isScanning ? $"SCANNING... {(int)(scanProgress * 100)}%" : "\ud83d\udd0e CHECK AVATAR";
            if (GUILayout.Button(btnText, KawaiiStudioGUI.ButtonStyle, GUILayout.Width(220)))
            {
                StartScan();
            }

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (isScanning)
            {
                GUILayout.Space(5);
                Rect progRect = EditorGUILayout.GetControlRect(GUILayout.Height(6));
                EditorGUI.DrawRect(progRect, new Color(0.2f, 0.2f, 0.2f));
                EditorGUI.DrawRect(new Rect(progRect.x, progRect.y, progRect.width * scanProgress, progRect.height), KawaiiStudioGUI.AccentColor);
            }
        }

        private void DrawVerdict()
        {
            Color verdictColor = nsfwDetected ? KawaiiStudioGUI.ErrorColor : KawaiiStudioGUI.SuccessColor;
            string verdictText = nsfwDetected ? "\u26a0\ufe0f NOT SAFE FOR PUBLIC" : "\u2705 SAFE FOR PUBLIC";
            string subText = nsfwDetected ? "NSFW content detected \u2014 see recommendations below" : "No obvious NSFW content found";

            Rect verdictRect = EditorGUILayout.BeginVertical();
            GUILayout.Space(70);
            EditorGUILayout.EndVertical();

            verdictRect.height = 70;
            EditorGUI.DrawRect(verdictRect, verdictColor);

            GUI.Label(new Rect(verdictRect.x, verdictRect.y + 15, verdictRect.width, 30), verdictText,
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 20,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = Color.white }
                });

            GUI.Label(new Rect(verdictRect.x, verdictRect.y + 45, verdictRect.width, 20), subText,
                new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(1f, 1f, 1f, 0.9f) }
                });
        }

        private void DrawVRChatRecommendations()
        {
            KawaiiStudioGUI.DrawSection("\u26a0\ufe0f NSFW Detected \u2014 Action Required", () =>
            {
                EditorGUILayout.LabelField("\ud83d\udeab If uploading as PUBLIC:", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = KawaiiStudioGUI.ErrorColor } });
                EditorGUILayout.LabelField("   \u2192 Remove ALL NSFW content before upload", KawaiiStudioGUI.InfoLabelStyle);
                
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField("\u2705 If uploading as PRIVATE:", new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = KawaiiStudioGUI.SuccessColor } });
                EditorGUILayout.LabelField("   \u2192 Use the button below to configure SDK", KawaiiStudioGUI.InfoLabelStyle);
                
                GUILayout.Space(10);
                DrawCurrentSDKState();
                GUILayout.Space(10);

                GUI.backgroundColor = KawaiiStudioGUI.SuccessColor;
                if (GUILayout.Button("\ud83c\udff7\ufe0f AUTO-SET: Private + Content Warnings", KawaiiStudioGUI.ButtonStyle))
                {
                    AutoSetContentWarnings();
                }
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                GUILayout.Space(3);
                if (GUILayout.Button("\ud83d\udcc2 Open VRChat SDK Panel", KawaiiStudioGUI.ButtonStyle))
                {
                    OpenVRChatSDK();
                }
                GUI.backgroundColor = Color.white;
            });
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
            EditorGUILayout.LabelField($"  {(isPrivate ? "\u2705" : "\u274c")} Visibility: {(isPrivate ? "<color=#80FF80>Private</color>" : "<color=#FF8080>Public</color>")}", statusStyle);
            EditorGUILayout.LabelField($"  {(hasSexTag ? "\u2705" : "\u274c")} Sexually Suggestive: {(hasSexTag ? "<color=#80FF80>ON</color>" : "<color=#FF8080>OFF</color>")}", statusStyle);
            EditorGUILayout.LabelField($"  {(hasAdultTag ? "\u2705" : "\u274c")} Adult Themes: {(hasAdultTag ? "<color=#80FF80>ON</color>" : "<color=#FF8080>OFF</color>")}", statusStyle);
            EditorGUILayout.EndVertical();
        }

        private void DrawResults()
        {
            KawaiiStudioGUI.DrawSection(results.Count > 0 ? $"\ud83d\udccb Issues Found ({results.Count})" : "\ud83d\udccb No Issues Found", () =>
            {
                foreach (var res in results)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                    string icon = res.severity == Severity.High ? "\u26d4" : (res.severity == Severity.Medium ? "\u26a0\ufe0f" : "\u2139\ufe0f");
                    EditorGUILayout.LabelField(icon, GUILayout.Width(25));

                    EditorGUILayout.LabelField(res.message, new GUIStyle(EditorStyles.label)
                    {
                        wordWrap = true,
                        normal = { textColor = KawaiiStudioGUI.TextColor },
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
                    GUILayout.Space(2);
                }
            });
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
