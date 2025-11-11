// Kawaii Studio - Video Animator v2.0
// Convertissez vos vidéos en animations texturées Unity
// Tout-en-un : Pas besoin de shaders externes !

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
        const int MAX_TEXTURE_SIZE = 8192;
        const int MAX_ATLAS_COUNT = 64;
        const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        const string LOGO_URL = "https://github.com/kawaiistudio/KSUnityTools/blob/main/logo_v2.png?raw=true";
        
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
        private bool generateMipmaps = true;
        
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
            VideoAnimatorWindow window = GetWindow<VideoAnimatorWindow>("Video Animator v2.0");
            window.minSize = new Vector2(500, 700);
            window.Show();
        }
        
        // ========== INITIALISATION ==========
        private void OnEnable()
        {
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
            discordTexture = MakeTex(2, 2, new Color(0.345f, 0.396f, 0.949f, 1f)); // Discord blurple

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
            string[] guids = AssetDatabase.FindAssets("ffmpeg");
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
            outputDirectory = EditorPrefs.GetString("KawaiiStudio.VideoAnimator.OutputDirectory", "Assets/Animations");
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
            
            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.102f, 0.059f, 0.122f, 1f));
            
            // Logo en haut à gauche
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
                
                // Bouton Discord à côté du logo
                Rect discordRect = new Rect(80, 20, 120, 40);
                if (GUI.Button(discordRect, "💬 Discord", discordButtonStyle))
                {
                    Application.OpenURL(DISCORD_URL);
                }
            }
        }
        
        private void DrawHeader()
        {
            GUILayout.Space(logoTexture != null ? 50 : 0); // Space for logo
            GUILayout.Label("⚡ VIDEO ANIMATOR v2.0 ⚡", headerStyle);
            
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
            inputVideoPath = EditorGUILayout.TextField("Video", inputVideoPath);
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
            audioClip = (AudioClip)EditorGUILayout.ObjectField("Audio", audioClip, typeof(AudioClip), false);
        }
        
        private void DrawFrameSizeSection()
        {
            targetFrameSize = EditorGUILayout.Vector2IntField("Frame size", targetFrameSize);
            if (videoInfo.IsValid)
            {
                targetFrameSize.x = Mathf.Clamp(targetFrameSize.x, 32, videoInfo.FrameSize.x);
                targetFrameSize.y = Mathf.Clamp(targetFrameSize.y, 32, videoInfo.FrameSize.y);
            }
        }
        
        private void DrawTimeSection()
        {
            if (!videoInfo.IsValid) return;
            
            EditorGUILayout.MinMaxSlider("Time", ref timeStart, ref timeEnd, 0f, videoInfo.Duration);
            
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
                frameRate = EditorGUILayout.Slider("Frame rate", frameRate, 1f, videoInfo.FrameRate);
            }
            else
            {
                frameRate = EditorGUILayout.Slider("Frame rate", frameRate, 1f, 60f);
            }
        }
        
        private void DrawAdvancedSettings()
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced settings", true);
            if (showAdvancedSettings)
            {
                EditorGUI.indentLevel++;
                
                loopAnimation = EditorGUILayout.Toggle("Loop animation", loopAnimation);
                useCrunchCompression = EditorGUILayout.Toggle("Crunch compression", useCrunchCompression);
                
                useCustomMaterial = EditorGUILayout.Toggle("Use custom material", useCustomMaterial);
                if (useCustomMaterial)
                {
                    EditorGUI.indentLevel++;
                    
                    EditorGUI.BeginChangeCheck();
                    customMaterial = (Material)EditorGUILayout.ObjectField("Material", customMaterial, typeof(Material), false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        customShaderTextures = GetCustomShaderTextures(customMaterial);
                        customShaderTexture = 0;
                    }
                    
                    if (customMaterial == null)
                    {
                        EditorGUILayout.HelpBox("If there is no custom material, the built-in Unlit/Texture will be used", MessageType.Info);
                    }
                    else if (customShaderTextures.Names.Length == 0)
                    {
                        EditorGUILayout.HelpBox("Shader has not 2D textures, the built-in Unlit/Texture will be used", MessageType.Warning);
                    }
                    else
                    {
                        customShaderTexture = EditorGUILayout.Popup("Texture2D", customShaderTexture, customShaderTextures.Names);
                    }
                    
                    EditorGUI.indentLevel--;
                }
                
                saveAsJPEG = EditorGUILayout.Toggle("Save in JPEG", saveAsJPEG);
                if (saveAsJPEG)
                {
                    EditorGUI.indentLevel++;
                    jpegQuality = EditorGUILayout.IntSlider("Quality", jpegQuality, 1, 100);
                    EditorGUI.indentLevel--;
                }
                
                useAtlasMode = EditorGUILayout.Toggle("Use atlases", useAtlasMode);
                
                EditorGUI.BeginDisabledGroup(!useAtlasMode);
                useSingleAtlas = EditorGUILayout.Toggle("Single atlas", useSingleAtlas);
                limitAtlasSize = EditorGUILayout.Vector2IntField("Limit Atlas size", limitAtlasSize);
                limitAtlasSize.x = Mathf.Clamp(limitAtlasSize.x, 512, MAX_TEXTURE_SIZE);
                limitAtlasSize.y = Mathf.Clamp(limitAtlasSize.y, 512, MAX_TEXTURE_SIZE);
                EditorGUI.EndDisabledGroup();
                
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
                EditorGUILayout.HelpBox("Too much textures. Custom material will be used as a fallback", MessageType.Warning);
            }
        }
        
        private void DrawOutputSection()
        {
            GUILayout.BeginHorizontal();
            outputDirectory = EditorGUILayout.TextField("Output folder", outputDirectory);
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
                
                if (GUILayout.Button("Preview", buttonStyle, GUILayout.Width(120)))
                {
                    PreviewVideo();
                }
                
                if (GUILayout.Button("Create animation", buttonStyle, GUILayout.Width(150)))
                {
                    StartConversion();
                }
                
                GUILayout.EndHorizontal();
            }
        }
        
        private void DrawProgressBar()
        {
            Rect rect = GUILayoutUtility.GetRect(position.width - 40, 24);
            float progress = totalFrames > 0 ? (float)currentFrame / totalFrames : 0f;
            int percents = Mathf.FloorToInt(progress * 100f);
            EditorGUI.ProgressBar(rect, progress, $"{percents}% Frame {currentFrame}/{totalFrames}");
        }
        
        private void DrawLog()
        {
            GUIStyle logLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label("[ CONVERSION LOG ]", logLabelStyle);
            
            Vector2 scroll = GUILayout.BeginScrollView(scrollPosition, logStyle, GUILayout.Height(100));
            GUILayout.Label(logOutput, logStyle);
            scrollPosition = scroll;
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
            scrollPosition = new Vector2(0, float.MaxValue);
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
            
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🔍 Analyzing video...");
            
            videoInfo = GetVideoInfo(inputVideoPath);
            
            if (videoInfo.IsValid)
            {
                timeStart = 0f;
                timeEnd = videoInfo.Duration;
                frameRate = Mathf.Min(30f, videoInfo.FrameRate);
                
                string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
                string videoDir = Path.GetDirectoryName(inputVideoPath);
                string childFolder = videoDir + "/" + videoName;
                
                if (childFolder.StartsWith(Application.dataPath))
                {
                    outputDirectory = "Assets" + childFolder.Substring(Application.dataPath.Length);
                }
                
                AddLog($"✓ Video: {videoInfo.Width}x{videoInfo.Height}");
                AddLog($"✓ Duration: {videoInfo.Duration:F2}s");
                AddLog($"✓ Frame Rate: {videoInfo.FrameRate:F2} FPS");
                AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            }
            else
            {
                AddLog("✗ Failed to analyze video!");
                AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
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
            // Windows
            string exePath = Path.Combine(ffmpegPath, name + ".exe");
            if (File.Exists(exePath))
                return exePath;
            
            // Unix (macOS, Linux)
            string unixPath = Path.Combine(ffmpegPath, name);
            if (File.Exists(unixPath))
                return unixPath;
            
            return exePath; // Return Windows path as default
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
            // Parse r_frame_rate which can be "30/1" or "30000/1001"
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
        
        private void StartConversion()
        {
            if (!videoInfo.IsValid)
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid video file first!", "OK");
                return;
            }
            
            if (string.IsNullOrEmpty(outputDirectory) || !AssetDatabase.IsValidFolder(outputDirectory))
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid output folder in Assets!", "OK");
                return;
            }
            
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🚀 Starting video conversion...");
            AddLog($"📹 Input: {Path.GetFileName(inputVideoPath)}");
            AddLog($"⏱️ Duration: {FormatTime(timeEnd - timeStart)}");
            AddLog($"📊 Frames: {totalFrames} @ {frameRate} fps");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            
            SetupConversion();
            if (useAtlasMode)
            {
                StartAtlasConversion();
            }
            else
            {
                StartFrameConversion();
            }
        }
        
        private void SetupConversion()
        {
            string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
            string basePath = outputDirectory + "/" + videoName;
            
            if (!AssetDatabase.IsValidFolder(outputDirectory))
            {
                string parentFolder = Path.GetDirectoryName(outputDirectory);
                string folderName = Path.GetFileName(outputDirectory);
                AssetDatabase.CreateFolder(parentFolder, folderName);
            }
            
            atlasPaths.Clear();
            string extension = saveAsJPEG ? "jpg" : "png";
            byte[] placeholderBytes = saveAsJPEG ? Texture2D.blackTexture.EncodeToJPG(jpegQuality) : Texture2D.blackTexture.EncodeToPNG();
            
            for (int i = 0; i < atlasCount; i++)
            {
                string atlasPath = $"{basePath} Atlas {i}.{extension}";
                File.WriteAllBytes(atlasPath, placeholderBytes);
                atlasPaths.Add(atlasPath);
            }
            
            AssetDatabase.Refresh();
            
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in atlasPaths)
                {
                    TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
                    if (importer != null)
                    {
                        importer.alphaSource = TextureImporterAlphaSource.None;
                        importer.npotScale = TextureImporterNPOTScale.None;
                        importer.wrapMode = TextureWrapMode.Clamp;
                        importer.maxTextureSize = MAX_TEXTURE_SIZE;
                        importer.crunchedCompression = useCrunchCompression;
                        importer.compressionQuality = 100;
                        importer.mipmapEnabled = generateMipmaps;
                        importer.SaveAndReimport();
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
        
        private void StartAtlasConversion()
        {
            string ffmpegExe = GetFFMPEGExecutable("ffmpeg");
            if (!File.Exists(ffmpegExe))
            {
                AddLog("✗ ffmpeg not found!");
                return;
            }
            
            string filename = EscapeFilterGraph(EscapeFilterOption(inputVideoPath));
            string arguments = string.Format(CultureInfo.InvariantCulture,
                "-nostdin -ss {1} -to {2} -i \"{0}\" -filter_complex \"fps=fps={5}, format=pix_fmts=rgb24, scale={3}x{4}:flags=area:out_range=full, vflip\" -f rawvideo -frames {6} pipe:1",
                filename, timeStart, timeEnd, actualFrameSize.x, actualFrameSize.y, frameRate, totalFrames);
            
            try
            {
                ffmpegProcess = new Process();
                ffmpegProcess.StartInfo.FileName = ffmpegExe;
                ffmpegProcess.StartInfo.Arguments = arguments;
                ffmpegProcess.StartInfo.CreateNoWindow = true;
                ffmpegProcess.StartInfo.UseShellExecute = false;
                ffmpegProcess.StartInfo.RedirectStandardOutput = true;
                ffmpegProcess.StartInfo.RedirectStandardError = true;
                ffmpegProcess.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
                ffmpegProcess.EnableRaisingEvents = true;
                ffmpegProcess.ErrorDataReceived += OnFFMPEGError;
                ffmpegProcess.Exited += OnFFMPEGExited;
                
                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                
                ffmpegStream = ffmpegProcess.StandardOutput.BaseStream;
                
                outputTexture = new Texture2D(atlasSize.x, atlasSize.y, TextureFormat.RGB24, false);
                ClearCurrentAtlas();
                
                frameBuffer = new Color32[actualFrameSize.x * actualFrameSize.y];
                imageDataBuffer = new byte[actualFrameSize.x * actualFrameSize.y * 3];
                
                isEncoding = true;
                currentAtlas = 0;
                currentFrame = -1;
                
                EditorApplication.update += UpdateFFMPEGEncoding;
                
                AddLog("▶ Atlas conversion started...");
            }
            catch (Exception ex)
            {
                AddLog($"✗ FFMPEG error: {ex.Message}");
                StopEncoding();
            }
        }
        
        private void StartFrameConversion()
        {
            string ffmpegExe = GetFFMPEGExecutable("ffmpeg");
            string extension = saveAsJPEG ? "jpg" : "png";
            string quality = saveAsJPEG ? $"-qscale:v {QualityToQScale(jpegQuality)}" : "";
            
            string filename = EscapeFilterGraph(EscapeFilterOption(inputVideoPath));
            string arguments = string.Format(CultureInfo.InvariantCulture,
                "-nostdin -y -ss {1} -to {2} -i \"{0}\" -filter_complex \"fps=fps={5}, format=pix_fmts=rgb24, scale={3}x{4}:flags=area:out_range=full\" -f image2 -start_number 0 -frames {6} {7} \"{8}/{9} %d.{10}\"",
                filename, timeStart, timeEnd, actualFrameSize.x, actualFrameSize.y, frameRate, totalFrames, quality, outputDirectory, Path.GetFileNameWithoutExtension(inputVideoPath), extension);
            
            try
            {
                ffmpegProcess = new Process();
                ffmpegProcess.StartInfo.FileName = ffmpegExe;
                ffmpegProcess.StartInfo.Arguments = arguments;
                ffmpegProcess.StartInfo.CreateNoWindow = true;
                ffmpegProcess.StartInfo.UseShellExecute = false;
                ffmpegProcess.StartInfo.RedirectStandardOutput = true;
                ffmpegProcess.StartInfo.RedirectStandardError = true;
                ffmpegProcess.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
                ffmpegProcess.EnableRaisingEvents = true;
                ffmpegProcess.ErrorDataReceived += OnFFMPEGError;
                ffmpegProcess.Exited += OnFFMPEGFrameExited;
                
                ffmpegProcess.Start();
                ffmpegProcess.BeginErrorReadLine();
                
                isEncoding = true;
                
                AddLog("▶ Frame conversion started...");
            }
            catch (Exception ex)
            {
                AddLog($"✗ FFMPEG error: {ex.Message}");
                StopEncoding();
            }
        }
        
        private void UpdateFFMPEGEncoding()
        {
            if (!isEncoding || ffmpegProcess == null || ffmpegStream == null)
                return;
            
            int bytesPerFrame = imageDataBuffer.Length;
            float updateStartTime = Time.realtimeSinceStartup;
            
            do
            {
                int position = 0;
                int bytes = 0;
                while (position < bytesPerFrame)
                {
                    bytes = ffmpegStream.Read(imageDataBuffer, position, bytesPerFrame - position);
                    if (bytes == 0)
                        break;
                    position += bytes;
                }
                
                bool endOfStream = bytes == 0;
                bool flushAtlas = false;
                
                if (bytes > 0)
                {
                    currentFrame++;
                    
                    for (int i = 0, j = 0; j < bytesPerFrame; i++, j += 3)
                    {
                        frameBuffer[i].r = imageDataBuffer[j];
                        frameBuffer[i].g = imageDataBuffer[j + 1];
                        frameBuffer[i].b = imageDataBuffer[j + 2];
                        frameBuffer[i].a = 255;
                    }
                    
                    endOfStream = endOfStream || (currentFrame + 1) >= totalFrames;
                    
                    int frameIndex = currentFrame % framesPerAtlas;
                    int column = frameIndex % slices.x;
                    int row = slices.y - 1 - frameIndex / slices.x;
                    
                    outputTexture.SetPixels32(column * actualFrameSize.x, row * actualFrameSize.y, actualFrameSize.x, actualFrameSize.y, frameBuffer, 0);
                    
                    flushAtlas = (frameIndex + 1) % framesPerAtlas == 0;
                }
                
                if (flushAtlas || endOfStream)
                {
                    FlushCurrentAtlas(endOfStream);
                }
                
                if (endOfStream)
                {
                    return;
                }
                
            } while ((Time.realtimeSinceStartup - updateStartTime) < 1f / 20f);
            
            Repaint();
        }
        
        private void FlushCurrentAtlas(bool endOfStream)
        {
            outputTexture.Apply();
            
            byte[] bytes;
            if (saveAsJPEG)
            {
                bytes = outputTexture.EncodeToJPG(jpegQuality);
            }
            else
            {
                bytes = outputTexture.EncodeToPNG();
            }
            
            File.WriteAllBytes(atlasPaths[currentAtlas], bytes);
            
            if (endOfStream)
            {
                StopEncoding();
                AssetDatabase.Refresh();
                AddLog("✅ Atlas conversion completed!");
                FinalizeConversion();
            }
            else
            {
                ClearCurrentAtlas();
                currentAtlas++;
            }
        }
        
        private void ClearCurrentAtlas()
        {
            RenderTexture tempRT = RenderTexture.GetTemporary(atlasSize.x, atlasSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture.active = tempRT;
            Graphics.DrawTexture(new Rect(Vector2.zero, atlasSize), Texture2D.blackTexture);
            outputTexture.ReadPixels(new Rect(Vector2.zero, atlasSize), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(tempRT);
        }
        
        private void StopEncoding()
        {
            isEncoding = false;
            currentFrame = 0;
            
            if (ffmpegProcess != null)
            {
                try
                {
                    if (!ffmpegProcess.HasExited)
                        ffmpegProcess.Kill();
                    ffmpegProcess.Dispose();
                }
                catch { }
                ffmpegProcess = null;
            }
            
            if (ffmpegStream != null)
            {
                ffmpegStream.Dispose();
                ffmpegStream = null;
            }
            
            if (outputTexture != null)
            {
                DestroyImmediate(outputTexture);
                outputTexture = null;
            }
            
            EditorApplication.update -= UpdateFFMPEGEncoding;
        }
        
        private void OnFFMPEGError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null && !string.IsNullOrEmpty(e.Data))
            {
                // Only log important errors, skip progress info
                if (e.Data.Contains("Error") || e.Data.Contains("Invalid") || e.Data.Contains("failed"))
                {
                    AddLog($"⚠️ FFMPEG: {e.Data}");
                }
            }
        }
        
        private void OnFFMPEGExited(object sender, EventArgs e)
        {
            StopEncoding();
            AddLog("✅ FFMPEG process completed.");
        }
        
        private void OnFFMPEGFrameExited(object sender, EventArgs e)
        {
            StopEncoding();
            AssetDatabase.Refresh();
            AddLog("✅ Frame conversion completed!");
            FinalizeConversion();
        }
        
        private void FinalizeConversion()
        {
            string basePath = outputDirectory + "/" + Path.GetFileNameWithoutExtension(inputVideoPath);
            
            AnimationClip animClip = CreateAnimationClip(basePath);
            GameObject videoPrefab = CreateVideoPrefab(basePath, animClip);
            
            Selection.activeObject = videoPrefab;
            
            AddLog($"🎉 Conversion finished! Prefab created at: {basePath}.prefab");
            EditorUtility.DisplayDialog("Success! 🎉", "Video conversion completed successfully!", "OK");
        }
        
        private AnimationClip CreateAnimationClip(string basePath)
        {
            string animPath = basePath + ".anim";
            AnimationClip anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(animPath);
            
            if (anim == null)
            {
                anim = new AnimationClip();
                AssetDatabase.CreateAsset(anim, animPath);
            }
            
            anim.ClearCurves();
            anim.frameRate = frameRate;
            
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(anim);
            settings.loopTime = loopAnimation;
            AnimationUtility.SetAnimationClipSettings(anim, settings);
            
            if (useCustomMaterial && customMaterial != null && customShaderTextures.Names.Length > 0)
            {
                AnimateCustomMaterial(anim, basePath);
            }
            else
            {
                AnimateAtlasDecoder(anim, basePath);
            }
            
            EditorUtility.SetDirty(anim);
            AssetDatabase.SaveAssets();
            
            return anim;
        }
        
        private void AnimateCustomMaterial(AnimationClip anim, string basePath)
        {
            if (atlasCount == 1)
            {
                AnimateSingleAtlasUV(anim);
            }
            else
            {
                AnimateMultiAtlasMaterial(anim, basePath);
                if (framesPerAtlas > 1)
                {
                    AnimateMultiAtlasUV(anim);
                }
            }
        }
        
        private void AnimateAtlasDecoder(AnimationClip anim, string basePath)
        {
            Shader decoderShader = CreateAtlasDecoderShader();
            if (decoderShader == null)
            {
                AddLog("✗ Failed to create decoder shader!");
                return;
            }
            
            string matPath = basePath + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(decoderShader);
                AssetDatabase.CreateAsset(mat, matPath);
            }
            else
            {
                mat.shader = decoderShader;
            }
            
            mat.SetFloat("_FrameRate", frameRate);
            mat.SetFloat("_AtlasSizeX", slices.x);
            mat.SetFloat("_AtlasSizeY", slices.y);
            
            for (int i = 0; i < atlasCount && i < MAX_ATLAS_COUNT; i++)
            {
                string texProp = i == 0 ? "_MainTex" : $"_MainTex{i}";
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[i]);
                mat.SetTexture(texProp, tex);
            }
            
            float timeLength = totalFrames / frameRate;
            AnimationCurve timeCurve = AnimationCurve.Linear(0, 0, timeLength, timeLength);
            anim.SetCurve("", typeof(MeshRenderer), "material._CustomTime", timeCurve);
            
            EditorUtility.SetDirty(mat);
        }
        
        private Shader CreateAtlasDecoderShader()
        {
            string shaderDir = "Assets/Shaders";
            string shaderPath = shaderDir + "/VideoAtlasDecoder.shader";
            
            Shader existingShader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (existingShader != null)
            {
                return existingShader;
            }
            
            if (!AssetDatabase.IsValidFolder(shaderDir))
            {
                AssetDatabase.CreateFolder("Assets", "Shaders");
            }
            
            string shaderCode = GetAtlasDecoderShaderCode();
            File.WriteAllText(shaderPath, shaderCode);
            AssetDatabase.Refresh();
            
            return AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        }

        private string GetAtlasDecoderShaderCode()
        {
            return @"Shader ""Kawaii Studio/Video Atlas Decoder""
{
    Properties
    {
        _MainTex (""Atlas 0"", 2D) = ""white"" {}
        _MainTex1 (""Atlas 1"", 2D) = ""white"" {}
        _MainTex2 (""Atlas 2"", 2D) = ""white"" {}
        _MainTex3 (""Atlas 3"", 2D) = ""white"" {}
        _MainTex4 (""Atlas 4"", 2D) = ""white"" {}
        _MainTex5 (""Atlas 5"", 2D) = ""white"" {}
        _MainTex6 (""Atlas 6"", 2D) = ""white"" {}
        _MainTex7 (""Atlas 7"", 2D) = ""white"" {}
        _FrameRate (""Frame Rate"", Float) = 30
        _AtlasSizeX (""Atlas Size X"", Float) = 4
        _AtlasSizeY (""Atlas Size Y"", Float) = 4
        _CustomTime (""Custom Time"", Float) = 0
    }
    
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include ""UnityCG.cginc""

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            sampler2D _MainTex1;
            sampler2D _MainTex2;
            sampler2D _MainTex3;
            sampler2D _MainTex4;
            sampler2D _MainTex5;
            sampler2D _MainTex6;
            sampler2D _MainTex7;
            float _FrameRate;
            float _AtlasSizeX;
            float _AtlasSizeY;
            float _CustomTime;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float currentFrame = floor(_CustomTime * _FrameRate);
                float framesPerAtlas = _AtlasSizeX * _AtlasSizeY;
                float atlasIndex = floor(currentFrame / framesPerAtlas);
                float frameInAtlas = fmod(currentFrame, framesPerAtlas);
                
                float column = fmod(frameInAtlas, _AtlasSizeX);
                float row = floor(frameInAtlas / _AtlasSizeX);
                
                float2 atlasUV = i.uv;
                atlasUV.x = (column + i.uv.x) / _AtlasSizeX;
                atlasUV.y = ((_AtlasSizeY - 1 - row) + i.uv.y) / _AtlasSizeY;
                
                fixed4 col = fixed4(0, 0, 0, 1);
                
                if (atlasIndex < 0.5)
                    col = tex2D(_MainTex, atlasUV);
                else if (atlasIndex < 1.5)
                    col = tex2D(_MainTex1, atlasUV);
                else if (atlasIndex < 2.5)
                    col = tex2D(_MainTex2, atlasUV);
                else if (atlasIndex < 3.5)
                    col = tex2D(_MainTex3, atlasUV);
                else if (atlasIndex < 4.5)
                    col = tex2D(_MainTex4, atlasUV);
                else if (atlasIndex < 5.5)
                    col = tex2D(_MainTex5, atlasUV);
                else if (atlasIndex < 6.5)
                    col = tex2D(_MainTex6, atlasUV);
                else if (atlasIndex < 7.5)
                    col = tex2D(_MainTex7, atlasUV);
                
                return col;
            }
            ENDCG
        }
    }
}";
        }
        
        private GameObject CreateVideoPrefab(string basePath, AnimationClip anim)
        {
            string prefabPath = basePath + ".prefab";
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            
            if (existingPrefab != null)
            {
                DestroyImmediate(existingPrefab);
            }
            
            GameObject tempObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tempObj.name = Path.GetFileNameWithoutExtension(inputVideoPath) + " Video";
            tempObj.transform.localScale = new Vector3(videoInfo.AspectRatio, 1, 1);
            
            DestroyImmediate(tempObj.GetComponent<MeshCollider>());
            
            MeshRenderer renderer = tempObj.GetComponent<MeshRenderer>();
            if (useCustomMaterial && customMaterial != null)
            {
                renderer.sharedMaterial = customMaterial;
            }
            else
            {
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(basePath + ".mat");
            }
            
            Animator animator = tempObj.AddComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            
            if (audioClip != null)
            {
                AudioSource audioSource = tempObj.AddComponent<AudioSource>();
                audioSource.clip = audioClip;
                audioSource.loop = loopAnimation;
                audioSource.playOnAwake = true;
                audioSource.dopplerLevel = 0;
            }
            
            string controllerPath = basePath + ".controller";
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
                AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
                AnimatorState state = stateMachine.AddState("Play");
                state.motion = anim;
                state.writeDefaultValues = false;
            }
            else
            {
                if (controller.layers.Length > 0)
                {
                    AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
                    if (stateMachine.states.Length > 0)
                    {
                        stateMachine.states[0].state.motion = anim;
                    }
                }
            }
            
            animator.runtimeAnimatorController = controller;
            
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tempObj, prefabPath);
            DestroyImmediate(tempObj);
            
            return prefab;
        }
        
        private void AnimateSingleAtlasUV(AnimationClip anim)
        {
            float timeLength = totalFrames / frameRate;
            Vector2 tileSize = new Vector2(1f / slices.x, 1f / slices.y);
            Vector2 frameSize = new Vector2(actualFrameSize.x, actualFrameSize.y);
            Vector2 pixelSize = new Vector2(1f / atlasSize.x, 1f / atlasSize.y);
            
            tileSize.x *= (frameSize.x - 1f) / frameSize.x;
            tileSize.y *= (frameSize.y - 1f) / frameSize.y;
            
            AnimationCurve scaleX = AnimationCurve.Constant(0, timeLength, tileSize.x);
            AnimationCurve scaleY = AnimationCurve.Constant(0, timeLength, tileSize.y);
            AnimationCurve offsetX = AnimationCurve.Linear(0, 0, timeLength, 1);
            AnimationCurve offsetY = AnimationCurve.Linear(0, 0, timeLength, 1);
            
            Keyframe[] offsetXKeys = new Keyframe[totalFrames];
            Keyframe[] offsetYKeys = new Keyframe[(totalFrames - 1) / slices.x + 1];
            Keyframe k = new Keyframe();
            
            int frameX = 0;
            int frameY = 0;
            
            for (int y = slices.y - 1; y >= 0 && frameX < totalFrames; y--, frameY++)
            {
                float pixelOffsetY = y * frameSize.y + 0.5f;
                float offsetYValue = pixelOffsetY * pixelSize.y;
                k.time = frameY * slices.x / frameRate;
                k.value = offsetYValue;
                offsetYKeys[frameY] = k;
                
                for (int x = 0; x < slices.x && frameX < totalFrames; x++, frameX++)
                {
                    float pixelOffsetX = x * frameSize.x + 0.5f;
                    float offsetXValue = pixelOffsetX * pixelSize.x;
                    k.time = frameX / frameRate;
                    k.value = offsetXValue;
                    offsetXKeys[frameX] = k;
                }
            }
            
            offsetX.keys = offsetXKeys;
            offsetY.keys = offsetYKeys;
            
            for (int i = 0; i < offsetX.keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offsetX, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offsetX, i, AnimationUtility.TangentMode.Constant);
            }
            for (int i = 0; i < offsetY.keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offsetY, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offsetY, i, AnimationUtility.TangentMode.Constant);
            }
            
            string property = customShaderTextures.PropertyNames[customShaderTexture];
            string stProperty = property + "_ST";
            
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.x", scaleX);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.y", scaleY);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.z", offsetX);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.w", offsetY);
        }
        
        private void AnimateMultiAtlasMaterial(AnimationClip anim, string basePath)
        {
            string property = customShaderTextures.PropertyNames[customShaderTexture];
            ObjectReferenceKeyframe[] matKeyframes = new ObjectReferenceKeyframe[atlasCount];
            
            AssetDatabase.StartAssetEditing();
            try
            {
                for (int i = 0; i < atlasCount; i++)
                {
                    string matPath = basePath + $" Mat {i}.mat";
                    Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (mat == null)
                    {
                        mat = new Material(customMaterial);
                        AssetDatabase.CreateAsset(mat, matPath);
                    }
                    
                    Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[i]);
                    mat.SetTexture(property, tex);
                    
                    matKeyframes[i].time = i * framesPerAtlas / frameRate;
                    matKeyframes[i].value = mat;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            
            EditorCurveBinding binding = EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(anim, binding, matKeyframes);
        }
        
        private void AnimateMultiAtlasUV(AnimationClip anim)
        {
            float timeLength = totalFrames / frameRate;
            Vector2 tileSize = new Vector2(1f / slices.x, 1f / slices.y);
            Vector2 frameSize = new Vector2(actualFrameSize.x, actualFrameSize.y);
            Vector2 pixelSize = new Vector2(1f / atlasSize.x, 1f / atlasSize.y);
            
            tileSize.x *= (frameSize.x - 1f) / frameSize.x;
            tileSize.y *= (frameSize.y - 1f) / frameSize.y;
            
            AnimationCurve scaleX = AnimationCurve.Constant(0, timeLength, tileSize.x);
            AnimationCurve scaleY = AnimationCurve.Constant(0, timeLength, tileSize.y);
            AnimationCurve offsetX = AnimationCurve.Linear(0, 0, timeLength, 1);
            AnimationCurve offsetY = AnimationCurve.Linear(0, 0, timeLength, 1);
            
            List<Keyframe> offsetXKeysList = new List<Keyframe>();
            List<Keyframe> offsetYKeysList = new List<Keyframe>();
            
            int globalFrame = 0;
            for (int atlasIdx = 0; atlasIdx < atlasCount && globalFrame < totalFrames; atlasIdx++)
            {
                int framesInThisAtlas = Mathf.Min(framesPerAtlas, totalFrames - globalFrame);
                
                int frameY = 0;
                for (int y = slices.y - 1; y >= 0 && globalFrame < totalFrames; y--, frameY++)
                {
                    float pixelOffsetY = y * frameSize.y + 0.5f;
                    float offsetYValue = pixelOffsetY * pixelSize.y;
                    
                    Keyframe kY = new Keyframe();
                    kY.time = globalFrame / frameRate;
                    kY.value = offsetYValue;
                    offsetYKeysList.Add(kY);
                    
                    for (int x = 0; x < slices.x && globalFrame < totalFrames; x++, globalFrame++)
                    {
                        float pixelOffsetX = x * frameSize.x + 0.5f;
                        float offsetXValue = pixelOffsetX * pixelSize.x;
                        
                        Keyframe kX = new Keyframe();
                        kX.time = globalFrame / frameRate;
                        kX.value = offsetXValue;
                        offsetXKeysList.Add(kX);
                    }
                }
            }
            
            offsetX.keys = offsetXKeysList.ToArray();
            offsetY.keys = offsetYKeysList.ToArray();
            
            for (int i = 0; i < offsetX.keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offsetX, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offsetX, i, AnimationUtility.TangentMode.Constant);
            }
            for (int i = 0; i < offsetY.keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offsetY, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offsetY, i, AnimationUtility.TangentMode.Constant);
            }
            
            string property = customShaderTextures.PropertyNames[customShaderTexture];
            string stProperty = property + "_ST";
            
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.x", scaleX);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.y", scaleY);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.z", offsetX);
            anim.SetCurve("", typeof(MeshRenderer), $"material.{stProperty}.w", offsetY);
        }
        
        private CustomShaderTextures GetCustomShaderTextures(Material mat)
        {
            CustomShaderTextures textures = new CustomShaderTextures();
            if (mat == null || mat.shader == null)
                return textures;
            
            int propCount = ShaderUtil.GetPropertyCount(mat.shader);
            List<string> names = new List<string>();
            List<string> propNames = new List<string>();
            
            for (int i = 0; i < propCount; i++)
            {
                if (ShaderUtil.GetPropertyType(mat.shader, i) == ShaderUtil.ShaderPropertyType.TexEnv &&
                    ShaderUtil.GetTexDim(mat.shader, i) == UnityEngine.Rendering.TextureDimension.Tex2D)
                {
                    names.Add(ShaderUtil.GetPropertyDescription(mat.shader, i));
                    propNames.Add(ShaderUtil.GetPropertyName(mat.shader, i));
                }
            }
            
            textures.Names = names.ToArray();
            textures.PropertyNames = propNames.ToArray();
            
            return textures;
        }
        
        private string EscapeFilterGraph(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(",", "\\,").Replace(";", "\\;").Replace("[", "\\[").Replace("]", "\\]");
        }
        
        private string EscapeFilterOption(string str)
        {
            return str.Replace("'", "\\'").Replace(":", "\\:");
        }
        
        private float QualityToQScale(float quality)
        {
            float q = 0.025f * quality - 1.68f;
            return -9.07557f * q - 5.37507f * q * q - 0.135614f * q * q * q + 9.78417f * q * q * q * q + 8.71064f;
        }
        
        private string FormatTime(float seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}";
        }
        
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}