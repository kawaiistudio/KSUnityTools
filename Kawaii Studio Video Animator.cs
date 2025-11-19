// Kawaii Studio - Video Animator v2.0
// Convertissez vos vidéos en animations texturées Unity
// Multi-language support via JSON files
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace KawaiiStudio
{
    [Serializable]
    public class VideoTranslationEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class VideoTranslationFile
    {
        public List<VideoTranslationEntry> entries;
    }

    [Serializable]
    public struct VideoInfo
    {
        public float Duration;
        public float FrameRate;
        public Vector2Int FrameSize;
        public bool IsValid;
        public float AspectRatio;
        public int Width => FrameSize.x;
        public int Height => FrameSize.y;

        public VideoInfo(float duration, float frameRate, int width, int height)
        {
            Duration = duration;
            FrameRate = frameRate;
            FrameSize = new Vector2Int(width, height);
            IsValid = true;
            AspectRatio = (float)width / height;
        }
    }

    [Serializable]
    public class CustomShaderTextures
    {
        public string[] Names = new string[0];
        public string[] PropertyNames = new string[0];
    }

    public class VideoAnimatorWindow : EditorWindow
    {
        // ========== CONFIGURATION ==========
        const string VERSION = "2.0";
        const int MAX_TEXTURE_SIZE = 8192;
        const int MAX_ATLAS_COUNT = 64;
        const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        const string LOGO_URL = "https://github.com/kawaiistudio/KSUnityTools/blob/main/logo_v2.png?raw=true";
        const string SHADER_PATH = "Assets/Kawaii Studio/Shaders/KSVideoDecoder.shader";
        const string DEFAULT_OUTPUT_PATH = "Assets/Kawaii Studio/Videos";
        const string LANGUAGES_FOLDER = "Assets/Kawaii Studio/Languages";
        const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        
        // Language
        private Dictionary<string, string> translations = new Dictionary<string, string>();
        private string currentLanguage = "en";
        
        // Variables principales
        private string inputVideoPath = "";
        private string outputDirectory = "";
        private string ffmpegPath = "";
        private AudioClip audioClip;
        
        // Info vidéo
        private VideoInfo videoInfo = new VideoInfo();
        private Vector2Int targetFrameSize = new Vector2Int(512, 512);
        private float frameRate = 30f;
        private float timeStart = 0f;
        private float timeEnd = 0f;
        private string timeStartStr = "Start position [00:00.000]";
        private string timeEndStr = "End position [00:00.000]";
        private int totalFrames = 0;
        
        // Paramètres atlas
        private Vector2Int limitAtlasSize = new Vector2Int(4096, 4096);
        private Vector2Int atlasSize;
        private Vector2Int slices;
        private Vector2Int actualFrameSize;
        private int atlasCount = 1;
        private int framesPerAtlas = 1;
        private bool useSingleAtlas = false;
        private bool useAtlasMode = true;
        
        // Paramètres de compression
        private bool useCrunchCompression = true;
        private bool saveAsJPEG = false;
        private int jpegQuality = 90;
        private bool loopAnimation = true;
        private bool generateMipmaps = false;
        
        // Custom Material
        private bool useCustomMaterial = false;
        private Material customMaterial;
        private int customShaderTexture = 0;
        private CustomShaderTextures customShaderTextures = new CustomShaderTextures();
        
        // État
        private bool isEncoding = false;
        private int currentFrame = 0;
        private int currentAtlas = 0;
        private Process ffmpegProcess;
        private Stream ffmpegStream;
        private Texture2D outputTexture;
        private Color32[] frameBuffer;
        private byte[] imageDataBuffer;
        private List<string> atlasPaths = new List<string>();
        
        // UI State
        private Vector2 scrollPosition;
        private Vector2 logScrollPosition;
        private string logOutput = "";
        private bool showAdvancedSettings = false;
        private string lastOpenedDirectory = "";
        private Texture2D logoTexture;
        private bool isDownloadingLogo = false;
        
        // UI Styles
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle buttonStyle;
        private GUIStyle logStyle;
        private GUIStyle discordButtonStyle;
        private Texture2D purpleTexture;
        private Texture2D redTexture;
        private Texture2D blackTexture;
        private Texture2D greenTexture;
        private Texture2D discordTexture;
        private bool stylesInitialized = false;
        
        // ========== MENU UNITY ==========
        [MenuItem("Kawaii Studio/Video Animator")]
        public static void ShowWindow()
        {
            VideoAnimatorWindow window = GetWindow<VideoAnimatorWindow>("Video Animator");
            window.minSize = new Vector2(500, 700);
            window.Show();
        }
        
        // ========== INITIALISATION ==========
        private void OnEnable()
        {
            LoadLanguage();
            FindFFMPEG();
            LoadPreferences();
            DownloadLogo();
        }
        
        private void OnDisable()
        {
            SavePreferences();
            if (isEncoding)
                StopEncoding();
        }
        
        private void OnDestroy()
        {
            if (isEncoding)
                StopEncoding();
        }

        private void LoadLanguage()
        {
            currentLanguage = EditorPrefs.GetString(PREFS_LANGUAGE, "en");
            translations.Clear();
            
            string jsonPath = Path.Combine(LANGUAGES_FOLDER, $"{currentLanguage}.json");
            
            if (File.Exists(jsonPath))
            {
                try
                {
                    string jsonContent = File.ReadAllText(jsonPath);
                    VideoTranslationFile translationFile = JsonUtility.FromJson<VideoTranslationFile>(jsonContent);
                    
                    if (translationFile != null && translationFile.entries != null)
                    {
                        foreach (var entry in translationFile.entries)
                        {
                            if (!string.IsNullOrEmpty(entry.key) && !string.IsNullOrEmpty(entry.value))
                            {
                                translations[entry.key] = entry.value;
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Failed to load translations: {e.Message}");
                    LoadFallbackTranslations();
                }
            }
            else
            {
                LoadFallbackTranslations();
            }
        }

        private void LoadFallbackTranslations()
        {
            translations = new Dictionary<string, string>
            {
                { "video_file", "Video" },
                { "audio", "Audio" },
                { "frame_size", "Frame size" },
                { "frame_rate", "Frame rate" },
                { "output_folder", "Output folder" },
                { "advanced_settings", "Advanced settings" },
                { "loop_animation", "Loop animation" },
                { "create_animation", "CREATE ANIMATION" },
                { "preview", "Preview" },
                { "time", "Time" },
                { "crunch_compression", "Crunch compression" },
                { "use_custom_material", "Use custom material" },
                { "material", "Material" },
                { "save_jpeg", "Save in JPEG" },
                { "quality", "Quality" },
                { "use_atlases", "Use atlases" },
                { "single_atlas", "Single atlas" },
                { "limit_atlas_size", "Limit Atlas size" },
                { "texture2d", "Texture2D" },
                { "generate_mipmaps", "Generate Mipmaps" },
                { "log", "CONVERSION LOG" }
            };
        }

        private string T(string key)
        {
            if (translations.ContainsKey(key))
                return translations[key];
            return key;
        }

        private void DownloadLogo()
        {
            if (isDownloadingLogo || logoTexture != null) return;
            
            isDownloadingLogo = true;
            
            UnityWebRequest request = UnityWebRequestTexture.GetTexture(LOGO_URL);
            var operation = request.SendWebRequest();
            
            operation.completed += (op) =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    logoTexture = DownloadHandlerTexture.GetContent(request);
                    Repaint();
                }
                else
                {
                    Debug.LogWarning("Failed to download logo: " + request.error);
                }
                isDownloadingLogo = false;
                request.Dispose();
            };
        }
        
        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            purpleTexture = MakeTex(2, 2, new Color(0.486f, 0.227f, 0.929f, 1f));
            redTexture = MakeTex(2, 2, new Color(1f, 0.278f, 0.341f, 1f));
            blackTexture = MakeTex(2, 2, new Color(0.039f, 0.039f, 0.059f, 1f));
            greenTexture = MakeTex(2, 2, new Color(0f, 1f, 0.255f, 1f));
            discordTexture = MakeTex(2, 2, new Color(0.345f, 0.396f, 0.949f, 1f));

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { background = redTexture, textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(1f, 0.42f, 0.506f, 1f)), textColor = Color.white },
                active = { background = redTexture, textColor = Color.white },
                padding = new RectOffset(20, 20, 10, 10),
                fixedHeight = 40
            };

            discordButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { background = discordTexture, textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(0.4f, 0.45f, 1f, 1f)), textColor = Color.white },
                active = { background = discordTexture, textColor = Color.white },
                padding = new RectOffset(15, 15, 8, 8),
                alignment = TextAnchor.MiddleCenter
            };

            logStyle = new GUIStyle(EditorStyles.textArea)
            {
                normal = { background = blackTexture, textColor = new Color(0f, 1f, 0.255f, 1f) },
                fontSize = 10,
                wordWrap = true
            };

            stylesInitialized = true;
        }
        
        private Texture2D MakeTex(int width, int height, Color col)
        {
            Color[] pix = new Color[width * height];
            for (int i = 0; i < pix.Length; i++)
                pix[i] = col;
            Texture2D result = new Texture2D(width, height);
            result.SetPixels(pix);
            result.Apply();
            return result;
        }
        
        private void FindFFMPEG()
        {
            string[] guids = AssetDatabase.FindAssets("ffmpeg t:folder");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (Directory.Exists(path))
                {
                    ffmpegPath = Path.GetFullPath(path);
                    return;
                }
            }
            
            ffmpegPath = Path.Combine(Application.dataPath, "ThirdParty", "FFMPEG");
        }
        
        private void LoadPreferences()
        {
            lastOpenedDirectory = EditorPrefs.GetString("KawaiiStudio.VideoAnimator.LastDirectory", Application.dataPath);
            outputDirectory = EditorPrefs.GetString("KawaiiStudio.VideoAnimator.OutputDirectory", DEFAULT_OUTPUT_PATH);
            
            // Créer le dossier par défaut s'il n'existe pas
            if (!AssetDatabase.IsValidFolder(DEFAULT_OUTPUT_PATH))
            {
                if (!AssetDatabase.IsValidFolder("Assets/Kawaii Studio"))
                {
                    AssetDatabase.CreateFolder("Assets", "Kawaii Studio");
                }
                AssetDatabase.CreateFolder("Assets/Kawaii Studio", "Videos");
                outputDirectory = DEFAULT_OUTPUT_PATH;
                SavePreferences();
            }
        }
        
        private void SavePreferences()
        {
            EditorPrefs.SetString("KawaiiStudio.VideoAnimator.LastDirectory", lastOpenedDirectory);
            EditorPrefs.SetString("KawaiiStudio.VideoAnimator.OutputDirectory", outputDirectory);
        }
        
        // ========== INTERFACE GRAPHIQUE ==========
        private void OnGUI()
        {
            InitializeStyles();
            
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.102f, 0.059f, 0.122f, 1f));
            
            DrawLogo();
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            
            DrawHeader();
            GUILayout.Space(15);
            
            using (new EditorGUI.DisabledGroupScope(isEncoding))
            {
                DrawVideoInputSection();
                DrawAudioSection();
                DrawFrameSizeSection();
                DrawTimeSection();
                DrawFrameRateSection();
                DrawAdvancedSettings();
                DrawStatsSection();
                DrawOutputSection();
            }
            
            GUILayout.Space(15);
            DrawActionButtons();
            
            if (isEncoding)
            {
                GUILayout.Space(10);
                DrawProgressBar();
            }
            
            GUILayout.Space(10);
            DrawLog();
            GUILayout.Space(10);
            DrawFooter();
            
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawLogo()
        {
            if (logoTexture != null)
            {
                Rect logoRect = new Rect(10, 10, 60, 60);
                GUI.DrawTexture(logoRect, logoTexture, ScaleMode.ScaleToFit);
                
                Rect discordRect = new Rect(80, 20, 120, 40);
                if (GUI.Button(discordRect, "💬 Discord", discordButtonStyle))
                {
                    Application.OpenURL(DISCORD_URL);
                }
            }
        }
        
        private void DrawHeader()
        {
            GUILayout.Space(logoTexture != null ? 50 : 0);
            GUILayout.Label($"⚡ VIDEO ANIMATOR v{VERSION} ⚡", headerStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.278f, 0.341f, 1f) }
            };
            GUILayout.Label("Convert Videos to Unity Texture Animations", subtitleStyle);
            
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 2), new Color(0.486f, 0.227f, 0.929f, 1f));
        }
        
        private void DrawVideoInputSection()
        {
            GUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            inputVideoPath = EditorGUILayout.TextField(T("video_file"), inputVideoPath);
            if (EditorGUI.EndChangeCheck() && File.Exists(inputVideoPath))
            {
                AnalyzeVideo();
            }
            
            if (GUILayout.Button("...", GUILayout.Width(30), GUILayout.Height(18)))
            {
                BrowseVideoFile();
            }
            GUILayout.EndHorizontal();
            
            if (videoInfo.IsValid)
            {
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                GUILayout.Label($"✓ Duration: {videoInfo.Duration:F1}s | {videoInfo.FrameRate:F1} fps | {videoInfo.FrameSize.x}x{videoInfo.FrameSize.y}", infoStyle);
            }
        }
        
        private void DrawAudioSection()
        {
            audioClip = (AudioClip)EditorGUILayout.ObjectField(T("audio"), audioClip, typeof(AudioClip), false);
        }
        
        private void DrawFrameSizeSection()
        {
            targetFrameSize = EditorGUILayout.Vector2IntField(T("frame_size"), targetFrameSize);
            if (videoInfo.IsValid)
            {
                targetFrameSize.x = Mathf.Clamp(targetFrameSize.x, 32, videoInfo.FrameSize.x);
                targetFrameSize.y = Mathf.Clamp(targetFrameSize.y, 32, videoInfo.FrameSize.y);
            }
        }
        
        private void DrawTimeSection()
        {
            if (!videoInfo.IsValid) return;
            
            EditorGUILayout.MinMaxSlider(T("time"), ref timeStart, ref timeEnd, 0f, videoInfo.Duration);
            
            TimeSpan t1 = TimeSpan.FromSeconds(timeStart);
            TimeSpan t2 = TimeSpan.FromSeconds(timeEnd);
            timeStartStr = $"Start position [{t1.Minutes:D2}:{t1.Seconds:D2}.{t1.Milliseconds:D3}]";
            timeEndStr = $"End position [{t2.Minutes:D2}:{t2.Seconds:D2}.{t2.Milliseconds:D3}]";
            
            timeStart = EditorGUILayout.FloatField(timeStartStr, timeStart);
            timeEnd = EditorGUILayout.FloatField(timeEndStr, timeEnd);
        }
        
        private void DrawFrameRateSection()
        {
            if (videoInfo.IsValid)
            {
                frameRate = EditorGUILayout.Slider(T("frame_rate"), frameRate, 1f, videoInfo.FrameRate);
            }
            else
            {
                frameRate = EditorGUILayout.Slider(T("frame_rate"), frameRate, 1f, 60f);
            }
        }
        
        private void DrawAdvancedSettings()
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, T("advanced_settings"), true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                
                loopAnimation = EditorGUILayout.Toggle(T("loop_animation"), loopAnimation);
                useCrunchCompression = EditorGUILayout.Toggle(T("crunch_compression"), useCrunchCompression);
                
                useCustomMaterial = EditorGUILayout.Toggle(T("use_custom_material"), useCustomMaterial);
                if (useCustomMaterial)
                {
                    EditorGUI.indentLevel++;
                    
                    EditorGUI.BeginChangeCheck();
                    customMaterial = (Material)EditorGUILayout.ObjectField(T("material"), customMaterial, typeof(Material), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        customShaderTextures = GetCustomShaderTextures(customMaterial);
                        customShaderTexture = 0;
                    }
                    
                    if (customMaterial == null)
                    {
                        EditorGUILayout.HelpBox("If there is no custom material, KSVideoDecoder shader will be used", MessageType.Info);
                    }
                    else if (customShaderTextures.Names.Length == 0)
                    {
                        EditorGUILayout.HelpBox("Shader has not 2D textures, KSVideoDecoder shader will be used", MessageType.Warning);
                    }
                    else
                    {
                        customShaderTexture = EditorGUILayout.Popup(T("texture2d"), customShaderTexture, customShaderTextures.Names);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                saveAsJPEG = EditorGUILayout.Toggle(T("save_jpeg"), saveAsJPEG);
                if (saveAsJPEG)
                {
                    EditorGUI.indentLevel++;
                    jpegQuality = EditorGUILayout.IntSlider(T("quality"), jpegQuality, 1, 100);
                    EditorGUI.indentLevel--;
                }
                
                useAtlasMode = EditorGUILayout.Toggle(T("use_atlases"), useAtlasMode);
                
                EditorGUI.BeginDisabledGroup(!useAtlasMode);
                useSingleAtlas = EditorGUILayout.Toggle(T("single_atlas"), useSingleAtlas);
                limitAtlasSize = EditorGUILayout.Vector2IntField(T("limit_atlas_size"), limitAtlasSize);
                limitAtlasSize.x = Mathf.Clamp(limitAtlasSize.x, 512, MAX_TEXTURE_SIZE);
                limitAtlasSize.y = Mathf.Clamp(limitAtlasSize.y, 512, MAX_TEXTURE_SIZE);
                EditorGUI.EndDisabledGroup();
                
                generateMipmaps = EditorGUILayout.Toggle(T("generate_mipmaps"), generateMipmaps);
                
                EditorGUI.indentLevel--;
            }
            
            CalculateAtlasLayout();
        }
        
        private void DrawStatsSection()
        {
            GUIStyle statsStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            
            EditorGUILayout.LabelField($"Total frames = {totalFrames}", statsStyle);
            
            if (useAtlasMode)
            {
                EditorGUILayout.LabelField($"Columns|Rows = {slices.x}x{slices.y}", statsStyle);
                EditorGUILayout.LabelField($"Result frame size = {actualFrameSize.x}x{actualFrameSize.y}", statsStyle);
                EditorGUILayout.LabelField($"Atlas size = {atlasSize.x}x{atlasSize.y}x{atlasCount}", statsStyle);
            }
            
            long vram = (long)atlasSize.x * atlasSize.y * atlasCount / 2;
            float vramMB = vram / (1024f * 1024f);
            EditorGUILayout.LabelField($"Using VRAM = {FormatBytes(vram)}", statsStyle);
            
            if (vramMB > 512f)
            {
                EditorGUILayout.HelpBox("Too much memory use. May cause performance drop on some systems. Try to reduce frame size, frame rate or length of strip", MessageType.Warning);
            }
            
            if (atlasCount > MAX_ATLAS_COUNT && !useCustomMaterial)
            {
                EditorGUILayout.HelpBox("Too much textures (max 64). Consider using custom material or reducing video length.", MessageType.Warning);
            }
        }
        
        private void DrawOutputSection()
        {
            GUILayout.BeginHorizontal();
            outputDirectory = EditorGUILayout.TextField(T("output_folder"), outputDirectory);
            if (GUILayout.Button("...", GUILayout.Width(30), GUILayout.Height(18)))
            {
                BrowseOutputFolder();
            }
            GUILayout.EndHorizontal();
        }
        
        private void DrawActionButtons()
        {
            bool canProcess = videoInfo.IsValid && totalFrames > 0 && !isEncoding;
            
            using (new EditorGUI.DisabledGroupScope(!canProcess))
            {
                GUILayout.BeginHorizontal();
                
                if (GUILayout.Button(T("preview"), buttonStyle, GUILayout.Width(120)))
                {
                    PreviewVideo();
                }
                
                if (GUILayout.Button(T("create_animation"), buttonStyle, GUILayout.Width(150)))
                {
                    StartConversion();
                }
                
                GUILayout.EndHorizontal();
            }
        }
        
        private void DrawProgressBar()
        {
            Rect rect = GUILayoutUtility.GetRect(position.width - 40, 24);
            float progress = totalFrames > 0 ? (float)(currentFrame + 1) / totalFrames : 0f;
            int percents = Mathf.FloorToInt(progress * 100f);
            EditorGUI.ProgressBar(rect, progress, $"{percents}% Frame {currentFrame + 1}/{totalFrames}");
        }
        
        private void DrawLog()
        {
            GUIStyle logLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"[ {T("log")} ]", logLabelStyle);
            
            logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, logStyle, GUILayout.Height(100));
            GUILayout.Label(logOutput, logStyle);
            GUILayout.EndScrollView();
        }
        
        private void DrawFooter()
        {
            GUIStyle footerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.278f, 0.341f, 1f) }
            };
            
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 1), new Color(0.486f, 0.227f, 0.929f, 1f));
            GUILayout.Label("★ Kawaii Studio ★", footerStyle);
        }
        
        // ========== LOGIQUE ==========
        private void AddLog(string message)
        {
            logOutput += message + "\n";
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        
        private void BrowseVideoFile()
        {
            string path = EditorUtility.OpenFilePanel("Select Video", lastOpenedDirectory, "mp4,m4v,webm,mkv,mov,ogv,swf,flv,3gp,mjpeg,avi,ts,gif");
            if (!string.IsNullOrEmpty(path))
            {
                inputVideoPath = path;
                lastOpenedDirectory = Path.GetDirectoryName(path);
                AnalyzeVideo();
            }
        }
        
        private void BrowseOutputFolder()
        {
            string folder = EditorUtility.SaveFolderPanel("Save to", string.IsNullOrEmpty(outputDirectory) ? "Assets" : outputDirectory, "");
            if (!string.IsNullOrEmpty(folder))
            {
                if (folder.StartsWith(Application.dataPath))
                {
                    outputDirectory = "Assets" + folder.Substring(Application.dataPath.Length);
                }
                else
                {
                    EditorUtility.DisplayDialog("Invalid folder path", "Selected folder must be in unity assets", "Ok");
                }
            }
        }
        
        private void AnalyzeVideo()
        {
            if (!File.Exists(inputVideoPath))
                return;
            
            AddLog("╔════════════════════════════════════");
            AddLog("🔍 Analyzing video...");
            
            videoInfo = GetVideoInfo(inputVideoPath);
            
            if (videoInfo.IsValid)
            {
                timeStart = 0f;
                timeEnd = videoInfo.Duration;
                frameRate = Mathf.Min(30f, videoInfo.FrameRate);
                
                string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
                outputDirectory = DEFAULT_OUTPUT_PATH + "/" + videoName;
                
                AddLog($"✓ Video: {videoInfo.Width}x{videoInfo.Height}");
                AddLog($"✓ Duration: {videoInfo.Duration:F2}s");
                AddLog($"✓ Frame Rate: {videoInfo.FrameRate:F2} FPS");
                AddLog($"📁 Output: {outputDirectory}");
                AddLog("╚════════════════════════════════════");
            }
            else
            {
                AddLog("✗ Failed to analyze video!");
                AddLog("╚════════════════════════════════════");
            }
        }
        
        private VideoInfo GetVideoInfo(string filename)
        {
            string ffprobePath = GetFFMPEGExecutable("ffprobe");
            if (!File.Exists(ffprobePath))
            {
                AddLog("✗ ffprobe not found!");
                return new VideoInfo();
            }
            
            string arguments = $"-print_format json -show_format -show_streams -i \"{filename}\"";
            
            ProcessStartInfo start = new ProcessStartInfo(ffprobePath, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            
            try
            {
                using (Process ffprobe = Process.Start(start))
                {
                    string json = ffprobe.StandardOutput.ReadToEnd();
                    string errors = ffprobe.StandardError.ReadToEnd();
                    ffprobe.WaitForExit();
                    
                    if (ffprobe.ExitCode != 0)
                    {
                        AddLog($"✗ ffprobe error: {errors}");
                        return new VideoInfo();
                    }
                    
                    float duration = ParseFloatFromJson(json, "duration");
                    float fps = ParseFrameRateFromJson(json);
                    int width = ParseIntFromJson(json, "width");
                    int height = ParseIntFromJson(json, "height");
                    
                    if (duration > 0 && fps > 0 && width > 0 && height > 0)
                    {
                        return new VideoInfo(duration, fps, width, height);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog($"✗ Error analyzing video: {ex.Message}");
            }
            
            return new VideoInfo();
        }

        private string GetFFMPEGExecutable(string name)
        {
            string exePath = Path.Combine(ffmpegPath, name + ".exe");
            if (File.Exists(exePath))
                return exePath;
            
            string unixPath = Path.Combine(ffmpegPath, name);
            if (File.Exists(unixPath))
                return unixPath;
            
            return exePath;
        }
        
        private float ParseFloatFromJson(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*\"?([0-9\\.]+)\"?";
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            }
            return 0f;
        }

        private float ParseFrameRateFromJson(string json)
        {
            string pattern = "\"r_frame_rate\"\\s*:\\s*\"([0-9]+)/([0-9]+)\"";
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                float numerator = float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                float denominator = float.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
                return numerator / denominator;
            }
            return 0f;
        }
        
        private int ParseIntFromJson(string json, string key)
        {
            string pattern = $"\"{key}\"\\s*:\\s*([0-9]+)";
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
            return 0;
        }
        
        private void CalculateAtlasLayout()
        {
            if (!videoInfo.IsValid)
                return;
            
            timeStart = Mathf.Clamp(timeStart, 0f, videoInfo.Duration);
            timeEnd = Mathf.Clamp(timeEnd, timeStart, videoInfo.Duration);
            totalFrames = Mathf.CeilToInt((timeEnd - timeStart) * frameRate);
            
            if (totalFrames <= 0 || targetFrameSize.x <= 0 || targetFrameSize.y <= 0)
                return;
            
            if (!useAtlasMode)
            {
                actualFrameSize = AlignToMultipleOf4(targetFrameSize);
                atlasCount = totalFrames;
                slices = Vector2Int.one;
                atlasSize = actualFrameSize;
                framesPerAtlas = 1;
                return;
            }
            
            if (useSingleAtlas)
            {
                atlasCount = 1;
                actualFrameSize = PackSingleAtlas(targetFrameSize, limitAtlasSize, totalFrames, out slices);
            }
            else
            {
                actualFrameSize = ComputePackedFrameSize(targetFrameSize, limitAtlasSize, totalFrames, out slices, out atlasCount);
            }
            
            atlasSize = Vector2Int.Scale(slices, actualFrameSize);
            framesPerAtlas = slices.x * slices.y;
        }
        
        private Vector2Int PackSingleAtlas(Vector2Int frameSize, Vector2Int limitAtlasSize, int frames, out Vector2Int slices)
        {
            float aspect = (float)frameSize.x / frameSize.y;
            slices = Vector2Int.zero;

            slices.x = limitAtlasSize.x / frameSize.x;
            slices.y = limitAtlasSize.y / frameSize.y;
            int framesPerAtlas = slices.x * slices.y;
            
            if (frames > framesPerAtlas)
            {
                slices.x = Mathf.RoundToInt(Mathf.Sqrt(frames / aspect));
                slices.y = Mathf.CeilToInt((float)frames / slices.x);

                frameSize.x = limitAtlasSize.x / slices.x;
                frameSize.y = limitAtlasSize.y / slices.y;
            }
            else
            {
                int minPerimeter = int.MaxValue;
                int minEmptySprites = int.MaxValue;
                Vector2Int bestCounts = Vector2Int.one;

                int minColumns = (frames - 1) / slices.y + 1;
                for (int x = minColumns; x <= slices.x; x++)
                {
                    int y = (frames - 1) / x + 1;
                    int emptySprites = x * y - frames;
                    int perimeter = x * frameSize.x + y * frameSize.y;
                    if (emptySprites < minEmptySprites || (emptySprites == minEmptySprites && perimeter < minPerimeter))
                    {
                        bestCounts.x = x;
                        bestCounts.y = y;
                        minEmptySprites = emptySprites;
                        minPerimeter = perimeter;
                    }
                }
                slices = bestCounts;
            }
            
            int strideX = LCM(slices.x, 4);
            int strideY = LCM(slices.y, 4);

            frameSize.x = frameSize.x * slices.x / strideX * strideX / slices.x;
            frameSize.y = frameSize.y * slices.y / strideY * strideY / slices.y;

            return frameSize;
        }
        
        private Vector2Int ComputePackedFrameSize(Vector2Int frameSize, Vector2Int limitAtlasSize, int totalFrames, out Vector2Int slices, out int atlases)
        {
            slices = Vector2Int.zero;
            slices.x = limitAtlasSize.x / frameSize.x;
            slices.y = limitAtlasSize.y / frameSize.y;
            int framesPerAtlas = slices.x * slices.y;
            atlases = (totalFrames - 1) / framesPerAtlas + 1;

            if (atlases <= 8)
            {
                int minPerimeter = int.MaxValue;
                int minEmptySprites = int.MaxValue;
                int bestAtlasCount = int.MaxValue;
                Vector2Int bestCounts = Vector2Int.one;
                
                for (int s = atlases; s <= 8; s++)
                {
                    int frames = (totalFrames - 1) / s + 1;
                    int minColumns = (frames - 1) / slices.y + 1;
                    for (int x = minColumns; x <= slices.x; x++)
                    {
                        int y = (frames - 1) / x + 1;
                        int emptySprites = x * y * s - totalFrames;
                        int perimeter = x * frameSize.x + y * frameSize.y + s * frameSize.x * frameSize.y;
                        if (emptySprites < minEmptySprites || (emptySprites == minEmptySprites && perimeter < minPerimeter))
                        {
                            bestCounts.x = x;
                            bestCounts.y = y;
                            bestAtlasCount = s;
                            minEmptySprites = emptySprites;
                            minPerimeter = perimeter;
                        }
                    }
                }
                atlases = bestAtlasCount;
                slices = bestCounts;
            }
            
            int strideX = LCM(slices.x, 4);
            int strideY = LCM(slices.y, 4);

            frameSize.x = frameSize.x * slices.x / strideX * strideX / slices.x;
            frameSize.y = frameSize.y * slices.y / strideY * strideY / slices.y;

            return frameSize;
        }
        
        private Vector2Int AlignToMultipleOf4(Vector2Int size)
        {
            return new Vector2Int(
                Mathf.RoundToInt(size.x / 4.0f) * 4,
                Mathf.RoundToInt(size.y / 4.0f) * 4
            );
        }
        
        private int GCD(int x, int y)
        {
            while (x != y)
            {
                if (x > y)
                    x -= y;
                else
                    y -= x;
            }
            return x;
        }

        private int LCM(int x, int y)
        {
            return x * y / GCD(x, y);
        }
        
        private void PreviewVideo()
        {
            string ffplayPath = GetFFMPEGExecutable("ffplay");
            
            if (!File.Exists(ffplayPath))
            {
                AddLog("✗ ffplay not found!");
                EditorUtility.DisplayDialog("Preview Error", 
                    $"ffplay not found at:\n{ffmpegPath}\n\nMake sure FFMPEG is installed correctly.", 
                    "OK");
                return;
            }
            
            string filename = EscapeFilterGraph(EscapeFilterOption(inputVideoPath));
            string arguments;
            
            if (audioClip != null)
            {
                string audioFilename = EscapeFilterGraph(EscapeFilterOption(AssetDatabase.GetAssetPath(audioClip)));
                arguments = string.Format(CultureInfo.InvariantCulture,
                    "-window_title Preview -volume 25 -f lavfi \"movie={0}:sp={1}, fps={5}:round=down, scale={3}x{4}, loop=-1:size={2}, setpts=N/({5}*TB)[out0]; amovie=filename={6}:loop=0, asetpts=N/SR/TB[out1]\"",
                    filename, timeStart, totalFrames, actualFrameSize.x, actualFrameSize.y, frameRate, audioFilename);
            }
            else
            {
                arguments = string.Format(CultureInfo.InvariantCulture,
                    "-window_title Preview -volume 25 -f lavfi \"movie={0}:sp={1}, fps={5}:round=down, scale={3}x{4}, loop=-1:size={2}, setpts=N/({5}*TB)\"",
                    filename, timeStart, totalFrames, actualFrameSize.x, actualFrameSize.y, frameRate);
            }
            
            try
            {
                Process ffplay = new Process();
                ffplay.StartInfo.FileName = ffplayPath;
                ffplay.StartInfo.Arguments = arguments;
                ffplay.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
                ffplay.StartInfo.UseShellExecute = false;
                ffplay.StartInfo.CreateNoWindow = true;
                ffplay.Start();
                
                AddLog("▶ Preview started!");
            }
            catch (Exception ex)
            {
                AddLog($"✗ Preview error: {ex.Message}");
            }
        }

        // KS: Ajout - arrête proprement un encodage en cours
        private void StopEncoding()
        {
            try
            {
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    ffmpegProcess.Kill();
                    ffmpegProcess.WaitForExit(2000);
                }
            }
            catch (Exception e)
            {
                AddLog($"✗ Error stopping encoder: {e.Message}");
            }
            finally
            {
                try { ffmpegStream?.Dispose(); } catch {}
                ffmpegProcess = null;
                ffmpegStream = null;
                isEncoding = false;
                currentFrame = 0;
                currentAtlas = 0;
                if (outputTexture != null)
                {
                    DestroyImmediate(outputTexture);
                    outputTexture = null;
                }
                Repaint();
            }
        }

        // KS: Ajout - récupère les textures 2D d'un shader custom
        private CustomShaderTextures GetCustomShaderTextures(Material mat)
        {
            var result = new CustomShaderTextures();
            if (mat == null || mat.shader == null) return result;

            try
            {
                Shader shader = mat.shader;
                int count = ShaderUtil.GetPropertyCount(shader);
                List<string> names = new List<string>();
                List<string> propNames = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    var type = ShaderUtil.GetPropertyType(shader, i);
                    if (type == ShaderUtil.ShaderPropertyType.TexEnv)
                    {
                        string propName = ShaderUtil.GetPropertyName(shader, i);
                        string display = ShaderUtil.GetPropertyDescription(shader, i);
                        if (string.IsNullOrEmpty(display)) display = propName;
                        names.Add(display);
                        propNames.Add(propName);
                    }
                }
                result.Names = names.ToArray();
                result.PropertyNames = propNames.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"GetCustomShaderTextures failed: {e.Message}");
            }
            return result;
        }

        // KS: Ajout - formatte une taille en octets
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024.0;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        // KS: Ajout - échappe une option pour un filtergraph ffmpeg
        private string EscapeFilterOption(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            var sb = new StringBuilder(s);
            sb.Replace("\\", "\\\\");
            sb.Replace(":", "\\:");
            sb.Replace(",", "\\,");
            sb.Replace("=", "\\=");
            sb.Replace("'", "\\'");
            sb.Replace("[", "\\[");
            sb.Replace("]", "\\]");
            return sb.ToString();
        }

        // KS: Ajout - échappe un filtergraph (actuellement passthrough)
        private string EscapeFilterGraph(string s)
        {
            return s;
        }

        // KS: Ajout - lancement minimal d'une conversion via ffmpeg (extraction PNG)
        private void StartConversion()
        {
            if (!videoInfo.IsValid)
            {
                AddLog("✗ No valid video analyzed.");
                return;
            }

            string ffmpegExe = GetFFMPEGExecutable("ffmpeg");
            if (!File.Exists(ffmpegExe))
            {
                EditorUtility.DisplayDialog("ffmpeg not found", $"ffmpeg not found at:\n{ffmpegPath}", "OK");
                return;
            }

            // S'assurer que le dossier de sortie est dans Assets
            if (!outputDirectory.StartsWith("Assets"))
            {
                outputDirectory = DEFAULT_OUTPUT_PATH;
            }

            // Créer la hiérarchie de dossiers si nécessaire
            if (!AssetDatabase.IsValidFolder(outputDirectory))
            {
                string parent = "Assets";
                foreach (var part in outputDirectory.Replace('\\','/').Split('/').Skip(1))
                {
                    if (string.IsNullOrEmpty(part)) continue;
                    string next = parent + "/" + part;
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(parent, part);
                    }
                    parent = next;
                }
            }

            string framesDir = Path.Combine(outputDirectory, "Frames");
            Directory.CreateDirectory(framesDir);

            // Nettoyer d'anciens fichiers
            try
            {
                foreach (var f in Directory.GetFiles(framesDir, "*.png")) File.Delete(f);
            }
            catch {}

            float duration = Mathf.Max(0f, timeEnd - timeStart);
            if (duration <= 0f)
            {
                AddLog("✗ Invalid time range.");
                return;
            }

            // S'assurer que les tailles sont correctes
            int width = Mathf.Max(16, actualFrameSize.x > 0 ? actualFrameSize.x : targetFrameSize.x);
            int height = Mathf.Max(16, actualFrameSize.y > 0 ? actualFrameSize.y : targetFrameSize.y);
            int fpsInt = Mathf.Max(1, Mathf.RoundToInt(frameRate));

            string outputPattern = Path.Combine(framesDir, "frame_%05d.png").Replace("\\", "/");

            string args = string.Format(CultureInfo.InvariantCulture,
                "-y -ss {0} -t {1} -i \"{2}\" -vf \"fps={3},scale={4}:{5}:flags=lanczos\" \"{6}\"",
                timeStart.ToString(CultureInfo.InvariantCulture),
                duration.ToString(CultureInfo.InvariantCulture),
                inputVideoPath,
                fpsInt,
                width, height,
                outputPattern);

            try
            {
                ffmpegProcess = new Process();
                ffmpegProcess.StartInfo.FileName = ffmpegExe;
                ffmpegProcess.StartInfo.Arguments = args;
                ffmpegProcess.StartInfo.UseShellExecute = false;
                ffmpegProcess.StartInfo.CreateNoWindow = true;
                ffmpegProcess.StartInfo.RedirectStandardError = true;
                ffmpegProcess.StartInfo.RedirectStandardOutput = true;
                ffmpegProcess.EnableRaisingEvents = true;
                ffmpegProcess.Exited += (s, e) =>
                {
                    isEncoding = false;
                    AssetDatabase.Refresh();
                    AddLog("✅ Extraction finished.");
                };

                bool started = ffmpegProcess.Start();
                if (!started)
                {
                    AddLog("✗ Failed to start ffmpeg.");
                    return;
                }

                isEncoding = true;
                currentFrame = 0;

                // Mise à jour de la progression en comptant les fichiers écrits
                EditorApplication.update -= PollProgress;
                EditorApplication.update += PollProgress;

                void PollProgress()
                {
                    if (!isEncoding)
                    {
                        EditorApplication.update -= PollProgress;
                        return;
                    }
                    try
                    {
                        if (Directory.Exists(framesDir))
                        {
                            var count = Directory.GetFiles(framesDir, "frame_*.png").Length;
                            currentFrame = Mathf.Clamp(count, 0, totalFrames);
                            Repaint();
                        }
                    }
                    catch {}
                }

                AddLog("▶ Conversion started with ffmpeg.");
            }
            catch (Exception ex)
            {
                AddLog($"✗ Conversion error: {ex.Message}");
                isEncoding = false;
            }
        }
    }
}

