// Tail Animator to VRC PhysBones Converter v1.4
// Converts Tail Animator (FImpossible Creations) components to VRC PhysBone components
// Improved: use Undo, avoid duplicates, set Root via reflection, set defaults, preserve prefab modifications
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.PhysBone.Components;
#endif

namespace KawaiiStudio
{
    public class TailToPhysBones : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;

        private GameObject avatarRoot;
        private List<Component> tailAnimatorComponents = new List<Component>();
        private int selectedTailIndex = 0;
        private Component selectedTailAnimator;
        private Transform tailRootBone;
        private bool autoDetect = true;
        private bool convertAll = false;

        // PhysBone settings
        private float pull = 0.2f;
        private float spring = 0.4f;
        private float stiffness = 0.3f;
        private float gravity = 0.1f;
        private float gravityFalloff = 0.3f;
        private float immobile = 0f;

        private bool advancedSettings = false;
        private Vector2 scrollPosition;
        
        // UI Styles & Assets
        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
        private static bool isDownloadingAssets = false;
        
        // Bake animation capture settings
        private bool bakeAnimation = true;
        private float captureDuration = 2.0f;
        private int captureFPS = 30;
        private string clipName = "Tail_Baked_Anim";
        private float loopBlendDuration = 0.25f; // seconds to blend end->start to create smoother loop
        private bool addClipToAnimator = true;
        private bool createPrefabOnBake = true;
        private bool usePrefabRootAsBinding = true;

        // Capture state
        private bool isCapturing = false;
        private float captureStartTime = 0f;
        private float lastSampleTime = 0f;
        private List<Transform> bonesToSample;
        private Dictionary<Transform, List<Vector3>> sampledPositions;
        private Dictionary<Transform, List<Quaternion>> sampledRotations;
        private int totalSamplesExpected = 0;
        private int samplesCaptured = 0;
        // auto set 'Is Animated' on PhysBone components
        private bool setIsAnimated = true;

        [MenuItem("Kawaii Studio/Universal Tools/Tail to PhysBones Converter")]
        public static void ShowWindow()
        {
            TailToPhysBones window = GetWindow<TailToPhysBones>("Tail → PhysBones");
            window.minSize = new Vector2(600, 750);
            window.Show();
        }

        void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if ((logoTexture == null || bannerTexture == null) && !isDownloadingAssets)
                DownloadAssets();
        }

        void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (isCapturing) EditorApplication.update -= CaptureUpdate;
        }

        void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Debug.Log("▶️ PLAY MODE: Tail Animator is now active! You can capture positions.");
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Debug.Log("⏸️ EXITING PLAY MODE: Positions will be applied.");
                Repaint(); // Fix GUI state errors
            }
        }

        void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            KawaiiStudioGUI.DrawWindowBackground(position);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });
            
            // Banner
            KawaiiStudioGUI.DrawBanner("TAIL → PHYSBONES CONVERTER", "Convert Tail Animator to VRC PhysBones", VERSION, logoTexture, bannerTexture);
            
            GUILayout.Space(10);
            
            // Play Mode Warning
            if (!Application.isPlaying)
            {
                KawaiiStudioGUI.DrawSection("⚠️ EDIT MODE DETECTED", () => {
                    EditorGUILayout.LabelField("For optimal conversion:", GetInfoStyle());
                    EditorGUILayout.LabelField("1️⃣ Press PLAY ▶️", GetInfoStyle());
                    EditorGUILayout.LabelField("2️⃣ Let Tail Animator initialize (2-3 seconds)", GetInfoStyle());
                    EditorGUILayout.LabelField("3️⃣ Then convert to PhysBones", GetInfoStyle());
                });
            }
            else
            {
                KawaiiStudioGUI.DrawSection("✅ PLAY MODE ACTIVE", () => {
                    EditorGUILayout.LabelField("Tail Animator is working! You can now convert.", GetInfoStyle());
                });
            }
            
            GUILayout.Space(10);
            
            DrawSetup();
            DrawPhysBoneSettings();
            DrawConvertButton();
            DrawInstructions();
            
            GUILayout.Space(20);
            KawaiiStudioGUI.DrawFooter();
            
            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        void DrawBanner_OLD()
        {
            GUILayout.Space(10);
            
            Rect bannerRect = EditorGUILayout.BeginVertical();
            GUILayout.Space(120);
            EditorGUILayout.EndVertical();
            
            bannerRect.x += 10;
            bannerRect.width -= 20;
            bannerRect.height = 120;
            
            Color darkPink = new Color(0.35f, 0.1f, 0.2f);
            Color primaryPink = new Color(0.9f, 0.35f, 0.6f);
            
            EditorGUI.DrawRect(bannerRect, darkPink);
            DrawBorder(bannerRect, primaryPink, 2);
            
            if (bannerTexture != null)
            {
                Rect texRect = new Rect(bannerRect.x + 2, bannerRect.y + 2, bannerRect.width - 4, bannerRect.height - 4);
                GUI.DrawTexture(texRect, bannerTexture, ScaleMode.ScaleAndCrop);
            }
            
            if (logoTexture != null)
            {
                Rect logoRect = new Rect(bannerRect.x + 10, bannerRect.y + 30, 60, 60);
                GUI.color = new Color(0, 0, 0, 0.4f);
                GUI.DrawTexture(new Rect(logoRect.x + 2, logoRect.y + 2, logoRect.width, logoRect.height), logoTexture);
                GUI.color = Color.white;
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit);
            }
            
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = Color.white }
            };
            Rect titleRect = new Rect(bannerRect.x, bannerRect.y + 75, bannerRect.width - 15, 25);
            GUI.Label(titleRect, "TAIL → PHYSBONES", titleStyle);
            
            GUIStyle subStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(1f, 0.8f, 0.9f) }
            };
            Rect subRect = new Rect(bannerRect.x, bannerRect.y + 95, bannerRect.width - 15, 18);
            GUI.Label(subRect, "Convert Tail Animator to VRChat PhysBones", subStyle);
            
            Rect verRect = new Rect(bannerRect.x + bannerRect.width - 45, bannerRect.y + 5, 40, 18);
            EditorGUI.DrawRect(verRect, new Color(0.9f, 0.35f, 0.6f));
            GUI.Label(verRect, "v" + VERSION, new GUIStyle(EditorStyles.miniLabel) {
                alignment = TextAnchor.MiddleCenter, 
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold
            });
        }

        void DrawSection_OLD(string title, Color accentColor, System.Action content)
        {
            GUIStyle sectionBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin = new RectOffset(0, 0, 0, 0)
            };
            
            GUIStyle sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };
            
            GUILayout.BeginVertical(sectionBoxStyle);
            
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

        void DrawBorder(Rect rect, Color color, int thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + rect.height - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x + rect.width - thickness, rect.y, thickness, rect.height), color);
        }

        void EnsureUIStyles()
        {
            KawaiiStudioGUI.Initialize();
        }


        void DownloadAssets()
        {
            // Branding is loaded locally from Assets/Kawaii Studio/Editor/Cache (no network).
            logoTexture = KawaiiStudioBranding.Logo;
            bannerTexture = KawaiiStudioBranding.Banner;
            isDownloadingAssets = false;
        }

        GUIStyle GetLabelStyle() => KawaiiStudioGUI.LabelStyle;
        GUIStyle GetInfoStyle() => KawaiiStudioGUI.InfoLabelStyle;

        void DrawSetup()
        {
            KawaiiStudioGUI.DrawSection("📦 CONFIGURATION", () => {
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField("Avatar Root:", GetLabelStyle());
                GameObject newAvatar = (GameObject)EditorGUILayout.ObjectField(avatarRoot, typeof(GameObject), true, GUILayout.Height(30));
                if (newAvatar != avatarRoot)
                {
                    avatarRoot = newAvatar;
                    if (autoDetect && avatarRoot != null) AutoDetectTailAnimator();
                }
                
                GUILayout.Space(5);
                autoDetect = DrawToggle("Auto-detection", autoDetect);
                
                GUILayout.Space(5);
                if (tailAnimatorComponents.Count > 0)
                {
                    EditorGUILayout.LabelField($"✅ {tailAnimatorComponents.Count} Tail Animator(s) found:", GetLabelStyle());
                    convertAll = DrawToggle("Convert ALL tails", convertAll);
                    
                    if (!convertAll)
                    {
                        string[] tailNames = tailAnimatorComponents.Select(c => c.gameObject.name).ToArray();
                        selectedTailIndex = EditorGUILayout.Popup("Select tail:", selectedTailIndex, tailNames);
                        if (selectedTailIndex >= 0 && selectedTailIndex < tailAnimatorComponents.Count)
                        {
                            selectedTailAnimator = tailAnimatorComponents[selectedTailIndex];
                        }
                        else if (tailAnimatorComponents.Count > 0)
                        {
                            selectedTailAnimator = tailAnimatorComponents[0];
                            selectedTailIndex = 0;
                        }
                        if (selectedTailAnimator != null)
                        {
                            EditorGUILayout.LabelField($"📍 Selected tail: {selectedTailAnimator.gameObject.name}", GetInfoStyle());
                        }
                    }
                    else
                    {
                        EditorGUILayout.LabelField("📍 All tails will be converted", GetInfoStyle());
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("⚠️ No Tail Animator detected", new GUIStyle(EditorStyles.label) { normal = { textColor = KawaiiStudioGUI.WarningColor } });
                    selectedTailAnimator = null;
                }
                
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Tail Root Bone:", GetLabelStyle());
                tailRootBone = (Transform)EditorGUILayout.ObjectField(tailRootBone, typeof(Transform), true);
                if (tailRootBone == null && selectedTailAnimator != null)
                {
                    EditorGUILayout.LabelField("💡 Tip: Root Bone is usually the base bone of the tail (e.g. BTailBone)", GetInfoStyle());
                }
                
                GUILayout.Space(5);
                usePrefabRootAsBinding = DrawToggle("Use Prefab Root as binding root", usePrefabRootAsBinding);
                if (usePrefabRootAsBinding)
                {
                    if (avatarRoot != null && PrefabUtility.IsPartOfPrefabInstance(avatarRoot))
                    {
                        if (GUILayout.Button("Set Avatar Root to Prefab Root", GUILayout.Height(22)))
                        {
                            var root = PrefabUtility.GetNearestPrefabInstanceRoot(avatarRoot);
                            if (root != null) avatarRoot = root;
                            AutoDetectTailAnimator();
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("Find and Set Prefab Root for selected avatar", GUILayout.Height(22)))
                        {
                            if (selectedTailAnimator != null)
                            {
                                var root = PrefabUtility.GetNearestPrefabInstanceRoot(selectedTailAnimator.gameObject);
                                if (root != null) { avatarRoot = root; AutoDetectTailAnimator(); }
                                else EditorUtility.DisplayDialog("No prefab root", "Nearest prefab root not found. Select your avatar root", "OK");
                            }
                        }
                    }
                }
                
                var br = GetBindingRoot();
                if (br != null)
                {
                    EditorGUILayout.LabelField($"Binding root: {br.name}", GetInfoStyle());
                }
                
                GUILayout.Space(5);
                if (GUILayout.Button("🔍 Search Tail Animator", GUILayout.Height(30))) AutoDetectTailAnimator();
                
                GUILayout.Space(5);
                // Bake controls
                KawaiiStudioGUI.DrawSection("🎬 ANIMATION BAKING", () => {
                    bakeAnimation = DrawToggle("Bake animation clip", bakeAnimation);
                    if (bakeAnimation)
                    {
                        captureDuration = EditorGUILayout.FloatField("Duration (s)", captureDuration);
                        captureFPS = EditorGUILayout.IntField("Sample FPS", captureFPS);
                        clipName = EditorGUILayout.TextField("Animation name", clipName);
                        loopBlendDuration = EditorGUILayout.FloatField("Loop blend duration (s)", loopBlendDuration);
                        addClipToAnimator = DrawToggle("Add clip to Animator", addClipToAnimator);
                        createPrefabOnBake = DrawToggle("Create Prefab after bake", createPrefabOnBake);
                        setIsAnimated = DrawToggle("Set PhysBone 'Is Animated'", setIsAnimated);
                        
                        EditorGUILayout.LabelField("Capture in Play Mode: samples bone transforms while Tail Animator runs.", GetInfoStyle());
                        
                        if (Application.isPlaying)
                        {
                            if (!isCapturing)
                            {
                                Color oldBg = GUI.backgroundColor;
                                GUI.backgroundColor = KawaiiStudioGUI.SuccessColor;
                                if (GUILayout.Button("▶️ Capture & Bake (Play Mode)", GUILayout.Height(28))) StartCapture();
                                GUI.backgroundColor = oldBg;
                            }
                            else
                            {
                                Color oldBg = GUI.backgroundColor;
                                GUI.backgroundColor = KawaiiStudioGUI.ErrorColor;
                                if (GUILayout.Button("⏹️ Stop Capture", GUILayout.Height(28))) StopCapture();
                                GUI.backgroundColor = oldBg;
                                EditorGUILayout.LabelField($"Samples captured: {samplesCaptured}/{totalSamplesExpected}", GetInfoStyle());
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("Start Play Mode before capturing.", new GUIStyle(EditorStyles.label) { normal = { textColor = KawaiiStudioGUI.WarningColor } });
                        }
                    }
                });
            });
        }

        bool DrawToggle(string label, bool value)
        {
            EditorGUILayout.BeginHorizontal();
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = value ? KawaiiStudioGUI.SuccessColor : new Color(0.3f, 0.3f, 0.3f);
            
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
            
            GUIStyle toggleTextStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
            };
            GUILayout.Label(label, toggleTextStyle);
            
            EditorGUILayout.EndHorizontal();
            return value;
        }

        void DrawPhysBoneSettings()
        {
            KawaiiStudioGUI.DrawSection("⚙️ PHYSBONES SETTINGS", () => {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(advancedSettings ? "Basic" : "Advanced", GUILayout.Width(80))) advancedSettings = !advancedSettings;
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(5);
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Presets:", KawaiiStudioGUI.LabelStyle, GUILayout.Width(60));
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = KawaiiStudioGUI.SuccessColor;
                if (GUILayout.Button("🐱 Soft Tail")) ApplyPreset("soft");
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("🦊 Medium Tail")) ApplyPreset("medium");
                GUI.backgroundColor = KawaiiStudioGUI.WarningColor;
                if (GUILayout.Button("🐺 Stiff Tail")) ApplyPreset("stiff");
                GUI.backgroundColor = oldBg;
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(10);
                pull = EditorGUILayout.Slider("Pull (Return)", pull, 0f, 1f);
                EditorGUILayout.LabelField("Force returning to original position", GetInfoStyle());
                GUILayout.Space(3);
                spring = EditorGUILayout.Slider("Spring (Bounce)", spring, 0f, 1f);
                EditorGUILayout.LabelField("Bouncing effect", GetInfoStyle());
                GUILayout.Space(3);
                stiffness = EditorGUILayout.Slider("Stiffness (Rigidity)", stiffness, 0f, 1f);
                EditorGUILayout.LabelField("Resistance to movement", GetInfoStyle());
                GUILayout.Space(3);
                gravity = EditorGUILayout.Slider("Gravity", gravity, -1f, 1f);
                EditorGUILayout.LabelField("Gravity force (negative = upward)", GetInfoStyle());
                GUILayout.Space(3);
                
                if (advancedSettings)
                {
                    GUILayout.Space(10);
                    EditorGUILayout.LabelField("Advanced settings:", KawaiiStudioGUI.LabelStyle);
                    gravityFalloff = EditorGUILayout.Slider("Gravity Falloff", gravityFalloff, 0f, 1f);
                    EditorGUILayout.LabelField("Gravity attenuation along the chain", GetInfoStyle());
                    GUILayout.Space(3);
                    immobile = EditorGUILayout.Slider("Immobile", immobile, 0f, 1f);
                    EditorGUILayout.LabelField("Resistance to avatar movement", GetInfoStyle());
                }
            });
        }

        void DrawConvertButton()
        {
            bool canConvert = avatarRoot != null && tailAnimatorComponents.Count > 0;
            
            if (!canConvert)
            {
                KawaiiStudioGUI.DrawSection("❌ ERROR", () => {
                    EditorGUILayout.LabelField("Please select:", GetInfoStyle());
                    EditorGUILayout.LabelField("• Avatar Root", GetInfoStyle());
                    EditorGUILayout.LabelField("• At least one Tail Animator must be detected", GetInfoStyle());
                });
                GUILayout.Space(10);
            }
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.enabled = canConvert;
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = canConvert ? KawaiiStudioGUI.SuccessColor : Color.gray;
            
            GUIStyle bigButtonStyle = new GUIStyle(KawaiiStudioGUI.ButtonStyle)
            {
                fontSize = 14,
                fixedHeight = 45,
                fixedWidth = 300
            };
            
            if (GUILayout.Button("✨ CONVERT TO PHYSBONES ✨", bigButtonStyle))
            {
                ConvertToPhysBones();
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(10);
        }

        void DrawInstructions()
        {
            KawaiiStudioGUI.DrawSection("📖 INSTRUCTIONS", () => {
                EditorGUILayout.LabelField("🎬 RECOMMENDED WORKFLOW:", KawaiiStudioGUI.LabelStyle);
                GUILayout.Space(3);
                EditorGUILayout.LabelField("1. Open this window", GetInfoStyle());
                EditorGUILayout.LabelField("2. Select your avatar", GetInfoStyle());
                EditorGUILayout.LabelField("3. ▶️ START PLAY MODE", KawaiiStudioGUI.LabelStyle);
                EditorGUILayout.LabelField("4. Wait 2-3 seconds (Tail Animator initializes)", GetInfoStyle());
                EditorGUILayout.LabelField("5. Adjust PhysBones settings", GetInfoStyle());
                EditorGUILayout.LabelField("6. Click 'CONVERT'", GetInfoStyle());
                EditorGUILayout.LabelField("7. ⏸️ Exit Play Mode", GetInfoStyle());
            });
            
            GUILayout.Space(10);
            
            if (!Application.isPlaying)
            {
                KawaiiStudioGUI.DrawSection("💡 WHY IN PLAY MODE?", () => {
                    EditorGUILayout.LabelField("Tail Animator calculates positions dynamically.", GetInfoStyle());
                    EditorGUILayout.LabelField("We must capture the tail once it's initialized!", GetInfoStyle());
                });
            }
            
            GUILayout.Space(10);
            
            KawaiiStudioGUI.DrawSection("⚠️ AFTER CONVERSION", () => {
                EditorGUILayout.LabelField("• Tail Animator will be REMOVED", GetInfoStyle());
                EditorGUILayout.LabelField("• PhysBones will be configured automatically", GetInfoStyle());
                EditorGUILayout.LabelField("• Backup created for safety", GetInfoStyle());
                EditorGUILayout.LabelField("• Test in VRChat to adjust", GetInfoStyle());
            });
        }


        void AutoDetectTailAnimator()
        {
            if (avatarRoot == null)
            {
                Debug.LogWarning("No avatar selected");
                return;
            }
            tailAnimatorComponents.Clear();
            Component[] allComponents = avatarRoot.GetComponentsInChildren<Component>(true);
            foreach (Component comp in allComponents)
            {
                if (comp == null) continue;
                string typeName = comp.GetType().Name;
                if (typeName.Contains("Tail") && typeName.Contains("Animator")) tailAnimatorComponents.Add(comp);
            }
            if (tailAnimatorComponents.Count > 0)
            {
                Debug.Log($"✅ {tailAnimatorComponents.Count} Tail Animator(s) found:");
                foreach (var comp in tailAnimatorComponents) Debug.Log($"   • {comp.gameObject.name} → {comp.GetType().Name}");
                selectedTailAnimator = tailAnimatorComponents[0];
                if (!convertAll) AutoDetectTailRootBone(selectedTailAnimator.gameObject);
            }
            else Debug.LogWarning("❌ No Tail Animator found on this avatar");
            Repaint();
        }

        // ----- Capture -----------------------------------------------------------------
        void StartCapture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Start Play Mode before capturing animation.");
                return;
            }
            if (tailRootBone == null && selectedTailAnimator != null)
            {
                AutoDetectTailRootBone(selectedTailAnimator.gameObject);
                if (tailRootBone == null) { Debug.LogWarning("Root bone missing. Aborting capture."); return; }
            }
            // Build bone chain from tailRootBone
            bonesToSample = GetBoneChain(tailRootBone);
            if (bonesToSample == null || bonesToSample.Count == 0) { Debug.LogWarning("No bones to sample."); return; }
            sampledPositions = new Dictionary<Transform, List<Vector3>>();
            sampledRotations = new Dictionary<Transform, List<Quaternion>>();
            foreach (var b in bonesToSample) { sampledPositions[b] = new List<Vector3>(); sampledRotations[b] = new List<Quaternion>(); }
            totalSamplesExpected = Mathf.CeilToInt(captureDuration * captureFPS);
            samplesCaptured = 0;
            captureStartTime = Time.time;
            lastSampleTime = Time.time;
            isCapturing = true;
            EditorApplication.update += CaptureUpdate;
            Debug.Log($"🔴 Capture started for {bonesToSample.Count} bones, duration {captureDuration}s at {captureFPS}fps");
        }

        void CaptureUpdate()
        {
            if (!isCapturing) return;
            if (!Application.isPlaying) { StopCapture(); return; }
            float now = Time.time;
            float interval = 1f / Mathf.Max(1, captureFPS);
            if (now - lastSampleTime >= interval)
            {
                // sample
                foreach (var b in bonesToSample)
                {
                    sampledPositions[b].Add(b.localPosition);
                    sampledRotations[b].Add(b.localRotation);
                }
                lastSampleTime = now;
                samplesCaptured++;
                if (samplesCaptured >= totalSamplesExpected) StopCapture();
            }
        }

        void StopCapture()
        {
            if (!isCapturing) return;
            isCapturing = false;
            EditorApplication.update -= CaptureUpdate;
            Debug.Log($"⏹️ Capture stopped. Samples captured: {samplesCaptured}");
            if (samplesCaptured > 0) {
                BakeAnimationClipFromSamples();
            }
        }

        List<Transform> GetBoneChain(Transform root)
        {
            List<Transform> chain = new List<Transform>();
            if (root == null) return chain;
            Transform t = root;
            chain.Add(t);
            while (t.childCount > 0)
            {
                // assume next bone is the first child (common in tail rigs)
                t = t.GetChild(0);
                chain.Add(t);
            }
            return chain;
        }

        void BakeAnimationClipFromSamples()
        {
            if (bonesToSample == null || bonesToSample.Count == 0) { Debug.LogWarning("No bone samples to bake."); return; }
            AnimationClip clip = new AnimationClip();
            clip.name = clipName + "_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
            // build curves
                // Determine binding root - use prefab root or avatarRoot depending on toggle
                GameObject bindingRoot = GetBindingRoot();

                foreach (var b in bonesToSample)
                {
                    if (!b.IsChildOf(bindingRoot.transform))
                    {
                        Debug.LogWarning($"⚠️ Binding root {bindingRoot.name} is not ancestor of bone {b.name}. Please set Avatar Root or toggle 'Use Prefab Root as binding root'.");
                    }
                    string path = AnimationUtility.CalculateTransformPath(b, bindingRoot.transform);
                    // position
                    AnimationCurve px = new AnimationCurve(); AnimationCurve py = new AnimationCurve(); AnimationCurve pz = new AnimationCurve();
                    // quaternion curves
                    AnimationCurve qx = new AnimationCurve(); AnimationCurve qy = new AnimationCurve(); AnimationCurve qz = new AnimationCurve(); AnimationCurve qw = new AnimationCurve();
                    // euler curves (unzipped for continuity)
                    AnimationCurve rxEuler = new AnimationCurve(); AnimationCurve ryEuler = new AnimationCurve(); AnimationCurve rzEuler = new AnimationCurve();
                    float prevEx = 0f, prevEy = 0f, prevEz = 0f;
                    int originalSamples = sampledPositions[b].Count;
                    // initialize unwrap references
                    if (originalSamples > 0)
                    {
                        Vector3 firstEul = sampledRotations[b][0].eulerAngles;
                        prevEx = firstEul.x; prevEy = firstEul.y; prevEz = firstEul.z;
                    }
                    for (int i = 0; i < originalSamples; i++)
                    {
                        float t = i / (float)captureFPS;
                        Vector3 pos = sampledPositions[b][i];
                        Quaternion rot = sampledRotations[b][i];
                        px.AddKey(new Keyframe(t, pos.x)); py.AddKey(new Keyframe(t, pos.y)); pz.AddKey(new Keyframe(t, pos.z));
                        qx.AddKey(new Keyframe(t, rot.x)); qy.AddKey(new Keyframe(t, rot.y)); qz.AddKey(new Keyframe(t, rot.z)); qw.AddKey(new Keyframe(t, rot.w));
                        // Euler with unwrap continuity
                        Vector3 eul = rot.eulerAngles;
                        float ex = UnwrapAngle(prevEx, eul.x); float ey = UnwrapAngle(prevEy, eul.y); float ez = UnwrapAngle(prevEz, eul.z);
                        prevEx = ex; prevEy = ey; prevEz = ez;
                        rxEuler.AddKey(new Keyframe(t, ex)); ryEuler.AddKey(new Keyframe(t, ey)); rzEuler.AddKey(new Keyframe(t, ez));
                    }
                    // Blend end to start for smooth loop (optional)
                    int blendSamples = Mathf.CeilToInt(Mathf.Clamp(loopBlendDuration, 0f, captureDuration) * captureFPS);
                    if (blendSamples > 0 && originalSamples > 0)
                    {
                        Quaternion firstQ = sampledRotations[b][0];
                        Quaternion lastQ = sampledRotations[b][originalSamples - 1];
                        Vector3 firstP = sampledPositions[b][0];
                        Vector3 lastP = sampledPositions[b][originalSamples - 1];
                        for (int bs = 1; bs <= blendSamples; bs++)
                        {
                            float tt = (originalSamples + bs - 1) / (float)captureFPS;
                            float alpha = bs / (float)(blendSamples + 1);
                            Vector3 p = Vector3.Lerp(lastP, firstP, alpha);
                            Quaternion q = Quaternion.Slerp(lastQ, firstQ, alpha);
                            px.AddKey(new Keyframe(tt, p.x)); py.AddKey(new Keyframe(tt, p.y)); pz.AddKey(new Keyframe(tt, p.z));
                            qx.AddKey(new Keyframe(tt, q.x)); qy.AddKey(new Keyframe(tt, q.y)); qz.AddKey(new Keyframe(tt, q.z)); qw.AddKey(new Keyframe(tt, q.w));
                            // do not change global samplesCaptured counter
                        }
                    }
                    EditorCurveBinding posX = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.x");
                    EditorCurveBinding posY = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.y");
                    EditorCurveBinding posZ = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalPosition.z");
                    EditorCurveBinding rotX = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.x");
                    EditorCurveBinding rotY = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.y");
                    EditorCurveBinding rotZ = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.z");
                    EditorCurveBinding rotW = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localRotation.w");
                    // euler bindings
                    EditorCurveBinding rotEx = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw.x");
                    EditorCurveBinding rotEy = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw.y");
                    EditorCurveBinding rotEz = EditorCurveBinding.FloatCurve(path, typeof(Transform), "localEulerAnglesRaw.z");
                    AnimationUtility.SetEditorCurve(clip, posX, px); AnimationUtility.SetEditorCurve(clip, posY, py); AnimationUtility.SetEditorCurve(clip, posZ, pz);
                    AnimationUtility.SetEditorCurve(clip, rotX, qx); AnimationUtility.SetEditorCurve(clip, rotY, qy); AnimationUtility.SetEditorCurve(clip, rotZ, qz); AnimationUtility.SetEditorCurve(clip, rotW, qw);
                    AnimationUtility.SetEditorCurve(clip, rotEx, rxEuler); AnimationUtility.SetEditorCurve(clip, rotEy, ryEuler); AnimationUtility.SetEditorCurve(clip, rotEz, rzEuler);
                }
            // loop settings
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            // save asset
            string folder = "Assets/Kawaii Studio/Animations";
            if (!AssetDatabase.IsValidFolder(folder)) { AssetDatabase.CreateFolder("Assets/Kawaii Studio", "Animations"); }
            string assetPath = folder + "/" + clip.name + ".anim";
            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"✅ AnimationClip baked and saved to: {assetPath}");
            // optionally assign to Animator
            if (addClipToAnimator)
            {
                var animator = bindingRoot.GetComponent<Animator>();
                if (animator == null) animator = bindingRoot.AddComponent<Animator>();
                var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller == null)
                {
                    var ctrl = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(folder + "/" + bindingRoot.name + "_TailController.controller");
                    animator.runtimeAnimatorController = ctrl;
                    controller = ctrl;
                }
                var rootLayer = controller.layers[0];
                var stateMachine = rootLayer.stateMachine;
                var state = stateMachine.AddState(clip.name);
                state.motion = clip;
                state.speed = 1f;
                // set default state
                stateMachine.defaultState = state;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log("✅ AnimationClip assigned to Animator Controller");
            }
            // Create a prefab from the avatar root if the option is enabled
            bindingRoot = GetBindingRoot();
            if (createPrefabOnBake && bindingRoot != null)
            {
                string prefabFolder = "Assets/Kawaii Studio/Prefabs";
                if (!AssetDatabase.IsValidFolder(prefabFolder))
                {
                    if (!AssetDatabase.IsValidFolder("Assets/Kawaii Studio")) AssetDatabase.CreateFolder("Assets", "Kawaii Studio");
                    AssetDatabase.CreateFolder("Assets/Kawaii Studio", "Prefabs");
                }
                string prefabPath = prefabFolder + "/" + bindingRoot.name + "_PhysBones.prefab";
                // Ensure unique
                int idx = 1; string tryPath = prefabPath;
                while (System.IO.File.Exists(tryPath) || AssetDatabase.LoadAssetAtPath<GameObject>(tryPath) != null)
                {
                    tryPath = prefabFolder + "/" + avatarRoot.name + $"_PhysBones_{idx}.prefab";
                    idx++;
                }
                prefabPath = tryPath;
                // Create prefab asset
                var newPrefab = PrefabUtility.SaveAsPrefabAssetAndConnect(bindingRoot, prefabPath, InteractionMode.UserAction);
                if (newPrefab != null)
                    Debug.Log($"✅ Prefab created: {prefabPath}");
                else
                    Debug.LogWarning($"⚠️ Could not create prefab at {prefabPath}");
            }
        }

        void AutoDetectTailRootBone(GameObject tailObject)
        {
            Transform[] children = tailObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                string name = child.name.ToLower();
                if (name.Contains("tail") && name.Contains("bone"))
                {
                    if (child.parent == null || !child.parent.name.ToLower().Contains("tail"))
                    {
                        tailRootBone = child;
                        Debug.Log($"✅ Tail Root Bone trouvé: {child.name}");
                        return;
                    }
                }
            }
            foreach (Transform child in children)
            {
                if (child.name.ToLower().Contains("tail"))
                {
                    tailRootBone = child;
                    Debug.Log($"⚠️ Probable Tail Root Bone: {child.name} (please verify!)");
                    return;
                }
            }
        }

        void ApplyPreset(string presetName)
        {
            switch (presetName)
            {
                case "soft":
                    pull = 0.1f; spring = 0.3f; stiffness = 0.2f; gravity = 0.2f; gravityFalloff = 0.4f; immobile = 0f; break;
                case "medium":
                    pull = 0.2f; spring = 0.4f; stiffness = 0.3f; gravity = 0.1f; gravityFalloff = 0.3f; immobile = 0f; break;
                case "stiff":
                    pull = 0.4f; spring = 0.6f; stiffness = 0.5f; gravity = 0.05f; gravityFalloff = 0.2f; immobile = 0.1f; break;
            }
            Debug.Log($"✅ Preset '{presetName}' applied");
            Repaint();
        }

        void ConvertToPhysBones()
        {
#if VRC_SDK_VRCSDK3
            if (avatarRoot == null) { EditorUtility.DisplayDialog("Error", "Please select the Avatar Root!", "OK"); return; }
            if (tailAnimatorComponents.Count == 0) { EditorUtility.DisplayDialog("Error", "No Tail Animator detected!", "OK"); return; }
            string message = convertAll ? $"Convert ALL {tailAnimatorComponents.Count} TAILS to PhysBones?\n\n" : $"Convert TAIL '{selectedTailAnimator.gameObject.name}' to PhysBones?\n\n";
            message += "• Tail Animator(s) will be removed\n• PhysBones will be added\n• A backup will be created";
            if (!EditorUtility.DisplayDialog("Confirm conversion", message, "Convert", "Cancel")) return;
            Undo.RegisterCompleteObjectUndo(avatarRoot, "Convert to PhysBones");
            CreateBackup();
            int convertedCount = 0;
            List<Component> tailsToConvert = convertAll ? new List<Component>(tailAnimatorComponents) : new List<Component> { selectedTailAnimator };
            foreach (Component tailComp in tailsToConvert) if (ConvertSingleTail(tailComp)) convertedCount++;
            AutoDetectTailAnimator(); Repaint(); EditorUtility.SetDirty(avatarRoot);
            EditorUtility.DisplayDialog("✅ Conversion successful!", $"{convertedCount} tail(s) converted to PhysBones!\n\n💾 Backup created\n\nTest in VRChat and adjust settings if needed!", "Awesome!");
            Debug.Log($"✨ CONVERSION COMPLETED: {convertedCount} tail(s)");
#else
            EditorUtility.DisplayDialog("VRChat SDK required", "Please import VRChat SDK3 to use PhysBones!", "OK");
#endif
        }

        bool ConvertSingleTail(Component tailComp)
        {
#if VRC_SDK_VRCSDK3
            try
            {
                Transform rootBone = FindTailRootBone(tailComp.gameObject);
                if (rootBone == null) { Debug.LogWarning($"⚠️ Root bone not found for {tailComp.gameObject.name}"); return false; }
                // Prevent duplicates
                VRCPhysBone existing = rootBone.GetComponent<VRCPhysBone>();
                if (existing != null)
                {
                    Debug.LogWarning($"⚠️ A VRCPhysBone already exists on {rootBone.name}. It will be updated.");
                    Undo.RecordObject(existing, "Update PhysBone");
                    existing.pull = pull; existing.spring = spring; existing.stiffness = stiffness; existing.gravity = gravity; existing.gravityFalloff = gravityFalloff; existing.immobile = immobile;
                    // optionally set Is Animated
                    if (setIsAnimated)
                    {
                        System.Type pbType = existing.GetType();
                        var prop = pbType.GetProperty("isAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("IsAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (prop != null && prop.PropertyType == typeof(bool)) { prop.SetValue(existing, true); Debug.Log($"✅ isAnimated=true set on existing PhysBone at {existing.gameObject.name}"); }
                        else
                        {
                            var field = pbType.GetField("isAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("IsAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (field != null && field.FieldType == typeof(bool)) { field.SetValue(existing, true); Debug.Log($"✅ isAnimated=true set on existing PhysBone at {existing.gameObject.name}"); }
                        }
                    }
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    // create via Undo so it can be reverted
                    VRCPhysBone physBone = Undo.AddComponent<VRCPhysBone>(rootBone.gameObject);
                    physBone.pull = pull; physBone.spring = spring; physBone.stiffness = stiffness; physBone.gravity = gravity; physBone.gravityFalloff = gravityFalloff; physBone.immobile = immobile;
                    // tuning default integration if available
                    try { physBone.integrationType = VRCPhysBone.IntegrationType.Simplified; } catch { }

                    // attempt to set Root/Target fields via reflection to ensure correct root
                    System.Type pbType = physBone.GetType();
                    // set root transform
                    bool rootAssigned = false;
                    var rootProp = pbType.GetProperty("Root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("rootTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (rootProp != null && rootProp.PropertyType == typeof(Transform)) { rootProp.SetValue(physBone, rootBone); rootAssigned = true; }
                    if (!rootAssigned)
                    {
                        var rootField = pbType.GetField("Root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("root", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("rootTransform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (rootField != null && rootField.FieldType == typeof(Transform)) { rootField.SetValue(physBone, rootBone); rootAssigned = true; }
                    }

                    // If Target / mesh is needed (common for PhysBone), try to set the first SkinnedMeshRenderer
                    var smr = rootBone.GetComponentInChildren<SkinnedMeshRenderer>();
                    if (smr != null)
                    {
                        var targetProp = pbType.GetProperty("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("m_Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (targetProp != null && targetProp.PropertyType == typeof(SkinnedMeshRenderer)) { targetProp.SetValue(physBone, smr); }
                        else
                        {
                            var targetField = pbType.GetField("Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("m_Target", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (targetField != null && targetField.FieldType == typeof(SkinnedMeshRenderer)) targetField.SetValue(physBone, smr);
                        }
                    }
                        // optionally set 'Is Animated'
                        if (setIsAnimated)
                        {
                            var isAnimProp = pbType.GetProperty("isAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetProperty("IsAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (isAnimProp != null && isAnimProp.PropertyType == typeof(bool)) { isAnimProp.SetValue(physBone, true); Debug.Log($"✅ isAnimated=true set on new PhysBone at {rootBone.name}"); }
                            else
                            {
                                var isAnimField = pbType.GetField("isAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ?? pbType.GetField("IsAnimated", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (isAnimField != null && isAnimField.FieldType == typeof(bool)) { isAnimField.SetValue(physBone, true); Debug.Log($"✅ isAnimated=true set on new PhysBone at {rootBone.name}"); }
                            }
                        }

                    EditorUtility.SetDirty(rootBone);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rootBone.gameObject);
                }

                // Remove tail animator component (use Undo so this is revertible)
                try { Undo.DestroyObjectImmediate(tailComp); }
                catch { DestroyImmediate(tailComp); }

                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error during conversion of {tailComp.gameObject.name}: {e.Message}");
                return false;
            }
#else
            return false;
#endif
        }

        Transform FindTailRootBone(GameObject tailObject)
        {
            Transform[] children = tailObject.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                string name = child.name.ToLower();
                if ((name.Contains("tail") && name.Contains("bone")) || name.Contains("ftail"))
                {
                    if (child.parent == tailObject.transform) return child;
                }
            }
            if (tailObject.transform.childCount > 0) return tailObject.transform.GetChild(0);
            return null;
        }

        void CreateBackup()
        {
            if (avatarRoot == null) return;
            if (PrefabUtility.IsPartOfPrefabInstance(avatarRoot))
            {
                string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(avatarRoot);
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    string backupPath = prefabPath.Replace(".prefab", $"_BACKUP_{System.DateTime.Now:yyyyMMdd_HHmmss}.prefab");
                    AssetDatabase.CopyAsset(prefabPath, backupPath);
                    Debug.Log($"💾 Backup created: {backupPath}");
                }
            }
        }

        GameObject GetBindingRoot()
        {
            if (avatarRoot == null && selectedTailAnimator != null) avatarRoot = PrefabUtility.GetNearestPrefabInstanceRoot(selectedTailAnimator.gameObject) ?? selectedTailAnimator.gameObject;
            if (usePrefabRootAsBinding && avatarRoot != null)
            {
                var root = PrefabUtility.GetNearestPrefabInstanceRoot(avatarRoot);
                if (root != null) return root;
                // fallback to topmost root
                return avatarRoot.transform.root.gameObject;
            }
            return avatarRoot ?? (selectedTailAnimator != null ? selectedTailAnimator.gameObject.transform.root.gameObject : null);
        }

        float UnwrapAngle(float prev, float current)
        {
            float delta = Mathf.DeltaAngle(prev, current);
            return prev + delta;
        }
    }
}
