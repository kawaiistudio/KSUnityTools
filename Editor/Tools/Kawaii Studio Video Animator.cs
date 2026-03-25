// Kawaii Studio - Video Animator v1.4
// Based on Leviant's script which works perfectly
// Adapted to use the KSVideoDecoder shader from Kawaii Studio
// With improved interface, metadata and automatic resolution configuration

using System;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Video;
using Debug = UnityEngine.Debug;

namespace KawaiiStudio
{
    public class VideoAnimatorWindow : EditorWindow
    {
        private const string VERSION = "1.4";

        const int maxTextureSize = 8192;
        const int MAX_ATLAS_PER_MATERIAL = 1;
        const string SHADER_PATH = "Assets/Kawaii Studio/Shaders/KSVideoDecoder.shader";
        const string DEFAULT_OUTPUT_PATH = "Assets/Kawaii Studio/Videos";
        private const string VIDEO_SCREEN_OVERLAY_MAT_PATH = "Assets/Kawaii Studio/Materials/Video Screen Overlay.mat";
        private const string FFMPEG_PREF_KEY = "KawaiiStudio.VideoAnimator.FFMPEGPath";
        private static readonly string[] FFMPEG_ASSET_DIR_CANDIDATES = new[]
        {
            // Some projects ship FFMPEG at the root of Assets
            "Assets/ThirdParty/FFMPEG",
            // Some projects ship FFMPEG inside Kawaii Studio
            "Assets/Kawaii Studio/ThirdParty/FFMPEG",
        };
        
        // Colors & Assets
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";

        [SerializeField] Shader atlasDecoderShader = null;
        [SerializeField] Shader unlitShader = null;
        Process ffmpeg;
        Material customMaterial;
        AudioClip audio;
        MediaInfo mediaInfo = new MediaInfo();
        Texture2D outputTexture;
        TextureIDs customShaderAvailableTextures;
        GameObject prefab;
        Vector2 scroll;
        Vector2 logScrollPosition;
        string logOutput = "";
        
        // UI Assets
        private static GUIStyle logStyle;
        private static bool uiStylesReady = false;
        
        Vector2Int slices;
        Vector2Int targetFrameSize = new Vector2Int(512, 512);
        Vector2Int frameSize;
        Vector2Int limitAtlasSize = new Vector2Int(4096, 4096);
        Vector2Int atlasSize;
        Stream pipe;
        string ffmpegPath;
        string timeStartStr;
        string timeEndStr;
        string lastOpenedDirectory;
        string inputVideoPath;
        string outputDirectory;
        string outputName;
        string outputBaseName;
        string[] atlasPaths;
        float frameRate = 30.0f;
        float timeStart;
        float timeEnd;
        int imageQuality = 90;
        int atlasCount;
        int totalFrames;
        int framesPerAtlas;
        int currentFrame;
        int currentAtlas;
        int customShaderTexture;
        Color32[] frame;
        byte[] imageData;
        bool loopAnimation = true;
        bool useCrunchCompression = true;
        bool saveInJPEG = true;
        bool useAtlas = true;
        bool useSingleAtlas;
        bool useCustomMaterial;
        bool advancedSettings;
        bool isEncoding;
        static StringBuilder log;
        private static readonly object logLock = new object();

        [MenuItem("Kawaii Studio/Universal Tools/Video Animator")]
        public static void OnMenuSelected()
        {
            VideoAnimatorWindow window = GetWindow<VideoAnimatorWindow>("Video Animator");
            window.minSize = new Vector2(500, 700);
            window.Show();
        }
        
        // ... (GCD, LCM methods remain unchanged)

        
        static int GCD(int x, int y)
        {
            while(x != y)
            {
                if(x > y)
                    x -= y;
                else
                    y -= x;
            }
            return x;
        }
        
        static int LCM(int x, int y)
        {
            return x * y / GCD(x, y);
        }
        
        public static Vector2Int PackAtlas(Vector2Int frameSize, Vector2Int limitAtlasSize, int frames, out Vector2Int slices)
        {
            float aspect = (float)frameSize.x / frameSize.y;
            slices = Vector2Int.zero;

            slices.x = limitAtlasSize.x / frameSize.x;
            slices.y = limitAtlasSize.y / frameSize.y;
            int framesPerAtlas = slices.x * slices.y;
            if(frames > framesPerAtlas)
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
                for(int x = minColumns; x <= slices.x; x++)
                {
                    int y = (frames - 1) / x + 1;
                    int emptySprites = x * y - frames;
                    int perimeter = x * frameSize.x + y * frameSize.y;
                    if(emptySprites < minEmptySprites || (emptySprites == minEmptySprites && perimeter < minPerimeter))
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
        
        public static Vector2Int ComputePackedFrameSize(Vector2Int frameSize, Vector2Int limitAtlasSize, int totalFrames, out Vector2Int slices, out int atlases)
        {
            slices = Vector2Int.zero;
            slices.x = limitAtlasSize.x / frameSize.x;
            slices.y = limitAtlasSize.y / frameSize.y;
            int framesPerAtlas = slices.x * slices.y;
            atlases = (totalFrames - 1) / framesPerAtlas + 1;

            // No maximum atlas limit - allow unlimited video length
            if(atlases <= 16)
            {
                int minPerimeter = int.MaxValue;
                int minEmptySprites = int.MaxValue;
                int bestAtlasCount = int.MaxValue;
                Vector2Int bestCounts = Vector2Int.one;
                for(int s = atlases; s <= 16; s++)
                {
                    int frames = (totalFrames - 1) / s + 1;
                    int minColumns = (frames - 1) / slices.y + 1;
                    for(int x = minColumns; x <= slices.x; x++)
                    {
                        int y = (frames - 1) / x + 1;
                        int emptySprites = x * y * s - totalFrames;
                        int perimeter = x * frameSize.x + y * frameSize.y + s * frameSize.x * frameSize.y;
                        if(emptySprites < minEmptySprites || (emptySprites == minEmptySprites && perimeter < minPerimeter))
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
        
        public static Vector2Int AllignFrameSize(Vector2Int frameSize)
        {
            frameSize.x = Mathf.RoundToInt(frameSize.x / 4.0f) * 4;
            frameSize.y = Mathf.RoundToInt(frameSize.y / 4.0f) * 4;
            return frameSize;
        }
        
        // ========== INITIALISATION STYLES ==========
        private void EnsureUIStyles()
        {
            if(uiStylesReady) return;
            KawaiiStudioGUI.Initialize();
            
            logStyle = new GUIStyle(EditorStyles.textArea)
            {
                normal = { 
                    background = KawaiiStudioGUI.GetRoundedTexture(new Color(0.05f, 0.05f, 0.08f, 1f), Color.clear, 5, 0),
                    textColor = KawaiiStudioGUI.SuccessColor 
                },
                fontSize = 10,
                wordWrap = true
            };
            
            uiStylesReady = true;
        }
        
        private void AddLog(string message)
        {
            logOutput += message + "\n";
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private static string GetProjectRootFullPath()
        {
            // Application.dataPath = <project>/Assets
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            string projectRoot = GetProjectRootFullPath();
            string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }

        /// <summary>
        /// Réduit les noms de dossiers/fichiers trop longs pour éviter DirectoryNotFoundException
        /// (MAX_PATH 260 sur Windows) et remplace les caractères invalides.
        /// </summary>
        private static string SanitizeAndTruncatePathComponent(string name, int maxLen = 72)
        {
            if(string.IsNullOrEmpty(name)) return name;
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            for(int i = 0; i < name.Length && sb.Length < maxLen; i++)
            {
                char c = name[i];
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }
            return sb.ToString();
        }

        private static bool DirectoryLooksLikeFfmpeg(string fullDir)
        {
            if (string.IsNullOrEmpty(fullDir) || !Directory.Exists(fullDir))
                return false;

            // Windows + *nix support
            bool hasFfmpeg = File.Exists(Path.Combine(fullDir, "ffmpeg.exe")) || File.Exists(Path.Combine(fullDir, "ffmpeg"));
            bool hasFfplay = File.Exists(Path.Combine(fullDir, "ffplay.exe")) || File.Exists(Path.Combine(fullDir, "ffplay"));

            // We mainly need ffmpeg; ffplay is used for preview.
            return hasFfmpeg || hasFfplay;
        }

        private static string ResolveFfmpegDirectory()
        {
            // 1) Cached
            string saved = EditorPrefs.GetString(FFMPEG_PREF_KEY, string.Empty);
            if (DirectoryLooksLikeFfmpeg(saved))
                return saved;

            // 2) Known locations
            foreach (string assetDir in FFMPEG_ASSET_DIR_CANDIDATES)
            {
                string full = AssetPathToFullPath(assetDir);
                if (DirectoryLooksLikeFfmpeg(full))
                {
                    EditorPrefs.SetString(FFMPEG_PREF_KEY, full);
                    return full;
                }
            }

            // 3) Locate by ffmpeg.exe file in Assets
            string[] exeGuids = AssetDatabase.FindAssets("ffmpeg.exe");
            foreach (string guid in exeGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    continue;

                string assetDir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(assetDir))
                    continue;

                string full = AssetPathToFullPath(assetDir);
                if (DirectoryLooksLikeFfmpeg(full))
                {
                    EditorPrefs.SetString(FFMPEG_PREF_KEY, full);
                    return full;
                }
            }

            // 4) Legacy: find a folder named ffmpeg/FFMPEG
            string[] folderGuids = AssetDatabase.FindAssets("ffmpeg t:folder");
            foreach (string guid in folderGuids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string full = AssetPathToFullPath(assetPath);
                if (DirectoryLooksLikeFfmpeg(full))
                {
                    EditorPrefs.SetString(FFMPEG_PREF_KEY, full);
                    return full;
                }
            }

            return string.Empty;
        }
        
        void Awake()
        {
            minSize = new Vector2(500, 700);

            ffmpegPath = ResolveFfmpegDirectory();
            if (string.IsNullOrEmpty(ffmpegPath) || !DirectoryLooksLikeFfmpeg(ffmpegPath))
            {
                Debug.LogError(
                    "FFMPEG not found. Expected one of: " +
                    string.Join(", ", FFMPEG_ASSET_DIR_CANDIDATES) +
                    " (or any folder containing ffmpeg.exe)."
                );
            }

            lastOpenedDirectory = EditorPrefs.GetString("KawaiiStudio.VideoAnimator.LastDirectory", Application.dataPath);
            outputDirectory = EditorPrefs.GetString("KawaiiStudio.VideoAnimator.OutputDirectory", DEFAULT_OUTPUT_PATH);
            
            // Charger le shader KSVideoDecoder
            atlasDecoderShader = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_PATH);
            if(atlasDecoderShader == null)
                Debug.LogWarning("KSVideoDecoder shader not found at: " + SHADER_PATH);
        }
        
        void OnEnable()
        {
            customShaderAvailableTextures = GetTextureNames(customMaterial);
            KawaiiStudioGUI.Initialize();
        }
        
        void OnDisable()
        {
            if(isEncoding)
                StopEncoding();
        }
        
        void OnDestroy()
        {
            if(isEncoding)
                StopEncoding();
            EditorPrefs.SetString("KawaiiStudio.VideoAnimator.LastDirectory", lastOpenedDirectory);
            EditorPrefs.SetString("KawaiiStudio.VideoAnimator.OutputDirectory", outputDirectory);
        }
        
        void OnInspectorUpdate()
        {
            if(isEncoding)
            {
                Repaint();
                lock(logLock)
                {
                    if(log != null && log.Length > 0)
                    {
                        Debug.Log(log.ToString());
                        log.Length = 0;
                    }
                }
            }
        }
        
        void EditorUpdateFFMPEG()
        {
            float time = Time.realtimeSinceStartup;
            int bytesPerFrame = imageData.Length;
            do
            {
                int position = 0;
                int bytes = 0;
                while(position < bytesPerFrame)
                {
                    bytes = pipe.Read(imageData, position, bytesPerFrame - position);
                    if(bytes == 0)
                        break;
                    position += bytes;
                }
                bool endOfStream = bytes == 0;
                bool flush = false;
                if(bytes > 0)
                {
                    ++currentFrame;
                    for(int i = 0, j = 0; j < bytesPerFrame; i++, j += 3)
                    {
                        frame[i].r = imageData[j];
                        frame[i].g = imageData[j + 1];
                        frame[i].b = imageData[j + 2];
                        frame[i].a = 255;
                    }
                    endOfStream = endOfStream || (currentFrame + 1) >= totalFrames;

                    int frameIndex = currentFrame % framesPerAtlas;
                    int column = frameIndex % slices.x;
                    int row = slices.y - 1 - frameIndex / slices.x;
                    outputTexture.SetPixels32(column * frameSize.x, row * frameSize.y, frameSize.x, frameSize.y, frame, 0);
                    flush = (frameIndex + 1) % framesPerAtlas == 0;
                }
                if(flush || endOfStream)
                {
                    Flush(endOfStream);
                }
                if(endOfStream)
                    return;
            }
            while((Time.realtimeSinceStartup - time) < 1 / 20f);
        }
        
        void OnGUI()
        {
            EnsureUIStyles();
            
            KawaiiStudioGUI.DrawWindowBackground(position);
            
            scroll = GUILayout.BeginScrollView(scroll);
            
            KawaiiStudioGUI.DrawBanner(
                "VIDEO ANIMATOR",
                "Convert Videos to Unity Texture Animations",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner
            );
            GUILayout.Space(10);
            
            KawaiiStudioGUI.DrawSection("\ud83d\udcfc INPUT SETTINGS", () => {
                using(new EditorGUI.DisabledGroupScope(isEncoding))
                {
                    RenderFFMPEG_Settings();
                }
            });
            
            GUILayout.Space(10);
            DrawActionButtons();
            
            if(isEncoding)
            {
                GUILayout.Space(10);
                KawaiiStudioGUI.DrawSection("\u26a1 ENCODING PROGRESS", () => {
                    DrawProgressBar();
                });
            }
            
            GUILayout.Space(10);
            KawaiiStudioGUI.DrawSection("\ud83d\udccb LOG OUTPUT", () => {
                GUILayout.Space(5);
                logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(120));
                GUILayout.Label(logOutput, logStyle);
                GUILayout.EndScrollView();
            });
            
            GUILayout.Space(15);
            KawaiiStudioGUI.DrawFooter();
            
            GUILayout.EndScrollView();
        }

        private void DrawActionButtons()
        {
            bool canProcess = !mediaInfo.IsEmpty && totalFrames > 0 && !isEncoding;
            
            using(new EditorGUI.DisabledGroupScope(!canProcess))
            {
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = canProcess ? KawaiiStudioGUI.AccentColor : Color.gray;
                
                if(GUILayout.Button("\ud83c\udfac Preview", KawaiiStudioGUI.ButtonStyle, GUILayout.Width(150)))
                {
                    PlayPreview(inputVideoPath);
                }
                
                GUILayout.Space(10);
                
                GUI.backgroundColor = canProcess ? KawaiiStudioGUI.AccentColor : Color.gray;
                
                if(GUILayout.Button("\ud83d\ude80 Create Animation", KawaiiStudioGUI.ButtonStyle, GUILayout.Width(200)))
                {
                    if(useAtlas)
                        CreateAtlasWithFFMPEG(inputVideoPath);
                    else
                        SplitVideoToFrames(inputVideoPath);
                }
                
                GUI.backgroundColor = oldBg;
                
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
        }
        
        private void DrawProgressBar()
        {
            Rect rect = GUILayoutUtility.GetRect(position.width - 40, 24);
            float progress = totalFrames > 0 ? (float)(currentFrame + 1) / totalFrames : 0f;
            int percents = Mathf.FloorToInt(progress * 100f);
            EditorGUI.ProgressBar(rect, progress, String.Format("{0}% Frame {1}/{2}", percents, currentFrame + 1, totalFrames));
        }
        
        private void DrawLog() { } // Replaced by DrawLogOutput in OnGUI
        
        void ProbeInput()
        {
            mediaInfo = GetMediaInfo(inputVideoPath);
            string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
            string safeName = SanitizeAndTruncatePathComponent(videoName);
            outputDirectory = DEFAULT_OUTPUT_PATH + "/" + safeName;
            
            // NEW: Automatically configure frame size with video resolution
            if(!mediaInfo.IsEmpty && mediaInfo.FrameSize.x > 0 && mediaInfo.FrameSize.y > 0)
            {
                targetFrameSize = mediaInfo.FrameSize;
            AddLog("╔════════════════════════════════════");
            AddLog("🔍 Analyzing video...");
                AddLog($"✓ Video: {mediaInfo.FrameSize.x}x{mediaInfo.FrameSize.y}");
                AddLog($"✓ Duration: {mediaInfo.Duration:F2}s");
                AddLog($"✓ Frame Rate: {mediaInfo.Framerate:F2} FPS");
                AddLog($"✓ Frame Size auto-configured: {targetFrameSize.x}x{targetFrameSize.y}");
                AddLog($"📁 Output: {outputDirectory}");
                AddLog("╚════════════════════════════════════");
            }
            else
            {
                AddLog("╔════════════════════════════════════");
                AddLog("🔍 Analyzing video...");
                AddLog("✗ Failed to analyze video!");
                AddLog("╚════════════════════════════════════");
            }
        }
        
        void CalculateSize()
        {
            timeStart = Mathf.Clamp(timeStart, 0, mediaInfo.Duration);
            timeEnd = Mathf.Clamp(timeEnd, timeStart, mediaInfo.Duration);
            totalFrames = Mathf.CeilToInt((timeEnd - timeStart) * frameRate);
            if(totalFrames > 0 && targetFrameSize.x > 0 && targetFrameSize.y > 0)
            {
                if(useAtlas)
                {
                if(useSingleAtlas)
                {
                    atlasCount = 1;
                    frameSize = PackAtlas(targetFrameSize, limitAtlasSize, totalFrames, out slices);
                }
                else
                    frameSize = ComputePackedFrameSize(targetFrameSize, limitAtlasSize, totalFrames, out slices, out atlasCount);
                }
                else
                {
                    slices = Vector2Int.one;
                    atlasCount = totalFrames;
                    frameSize = AllignFrameSize(targetFrameSize);
                }
            }
            atlasSize = Vector2Int.Scale(slices, frameSize);
        }
        
        void RenderOutputFolder()
        {
            using(new EditorGUILayout.HorizontalScope())
            {
                outputDirectory = EditorGUILayout.TextField(new GUIContent("Output folder", "Folder in the assets where the animation and textures will be saved"), outputDirectory);
                if(GUILayout.Button(new GUIContent("...", "Choose output folder"), GUILayout.MaxWidth(24), GUILayout.MaxHeight(16)))
                {
                    string folder = String.IsNullOrEmpty(outputDirectory) ? "Assets" : outputDirectory;
                    string output = EditorUtility.SaveFolderPanel("Save to", folder, String.Empty);
                    if(!String.IsNullOrEmpty(output))
                    {
                        if(output.StartsWith(Application.dataPath))
                            outputDirectory = new Uri(Application.dataPath).MakeRelativeUri(new Uri(output)).ToString();
                        else
                            EditorUtility.DisplayDialog("Invalid folder path", "Selected folder must be in unity assets", "Ok");
                    }
                }
            }
        }
        
        void RenderFFMPEG_Settings()
        {
            string oldFileName = inputVideoPath;
            using(new EditorGUILayout.HorizontalScope())
            {
                using(var input = new EditorGUI.ChangeCheckScope())
                {
                    inputVideoPath = EditorGUILayout.TextField(new GUIContent("Video", "Video source"), inputVideoPath);
                    if(input.changed)
                    {
                        if(!String.IsNullOrEmpty(inputVideoPath) && File.Exists(inputVideoPath))
                            ProbeInput();
                        else
                            mediaInfo = new MediaInfo();
                    }
                }
                if(GUILayout.Button(new GUIContent("...", "Browse video clip"), GUILayout.MaxWidth(24), GUILayout.MaxHeight(16)))
                {
                    string newInputPath = EditorUtility.OpenFilePanel("VideoAnimator", lastOpenedDirectory, "mp4,m4v,webm,mkv,mov,ogv,swf,flv,3gp,mjpeg,avi,ts,gif");
                    if(!String.IsNullOrEmpty(newInputPath))
                    {
                        inputVideoPath = newInputPath;
                        ProbeInput();
                    }
                }
            }
            
            // Enhanced Metadata Display
            if(!mediaInfo.IsEmpty)
            {
                GUILayout.Space(10);
                
                // Container Box
                GUIStyle boxStyle = new GUIStyle(EditorStyles.helpBox);
                boxStyle.padding = new RectOffset(10, 10, 10, 10);
                
                using (new EditorGUILayout.VerticalScope(boxStyle))
                {
                    // Header
                    GUIStyle headerStyle = new GUIStyle(EditorStyles.boldLabel);
                    headerStyle.alignment = TextAnchor.MiddleCenter;
                    headerStyle.fontSize = 12;
                    headerStyle.normal.textColor = new Color(0.486f, 0.227f, 0.929f, 1f); // Kawaii Purple
                    
                    GUILayout.Label("📝 VIDEO INFORMATION", headerStyle);
                    GUILayout.Space(5);
                    
                    // Grid Layout for Stats
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            DrawStatItem("Resolution", $"{mediaInfo.FrameSize.x} x {mediaInfo.FrameSize.y}");
                            DrawStatItem("Frame Rate", $"{mediaInfo.Framerate:0.##} FPS");
                            DrawStatItem("Aspect Ratio", $"{mediaInfo.AspectRatio:0.##}:1");
                        }
                        
                        using (new EditorGUILayout.VerticalScope())
                        {
                            DrawStatItem("Duration", FormatTime(mediaInfo.Duration));
                            if (mediaInfo.Bitrate > 0)
                                DrawStatItem("Bitrate", FormatBitsPerSecond(mediaInfo.Bitrate));
                            if (mediaInfo.Frames > 0)
                                DrawStatItem("Total Frames", mediaInfo.Frames.ToString());
                        }
                    }
                }
                GUILayout.Space(10);
            }
            
            audio = (AudioClip)EditorGUILayout.ObjectField(new GUIContent("Audio", "Imported AudioClip"), audio, typeof(AudioClip), true);
            
            // Frame Size with Auto-Config Note
            using(new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent("Frame size", "Output frame size (Auto-configured)"), GUILayout.Width(100));
                targetFrameSize = EditorGUILayout.Vector2IntField(GUIContent.none, targetFrameSize);
                
                if(!mediaInfo.IsEmpty)
                {
                    if (GUILayout.Button(new GUIContent("Reset", "Reset to original video size"), GUILayout.Width(50)))
                    {
                        targetFrameSize = mediaInfo.FrameSize;
                    }
                }
            }
            
            if(!mediaInfo.IsEmpty)
            {
                if(oldFileName != inputVideoPath)
                {
                    timeStart = 0;
                    timeEnd = mediaInfo.Duration;
                    prefab = null;
                }
                targetFrameSize.Clamp(Vector2Int.one, mediaInfo.FrameSize);
            }
            
            EditorGUILayout.MinMaxSlider(new GUIContent("Time", "Recording video time range (seconds)"), ref timeStart, ref timeEnd, 0, mediaInfo.Duration);
            TimeSpan t = TimeSpan.FromSeconds(timeStart);
            TimeSpan t2 = TimeSpan.FromSeconds(timeEnd);
            timeStartStr = String.Format("Start position [{0:D2}:{1:D2}.{2:D3}]", t.Minutes, t.Seconds, t.Milliseconds);
            timeEndStr = String.Format("End position [{0:D2}:{1:D2}.{2:D3}]", t2.Minutes, t2.Seconds, t2.Milliseconds);
            timeStart = EditorGUILayout.FloatField(new GUIContent(timeStartStr, "Trim start of video clip"), timeStart);
            timeEnd = EditorGUILayout.FloatField(new GUIContent(timeEndStr, "Trim end of video clip"), timeEnd);
            
            if(mediaInfo.IsEmpty)
                EditorGUILayout.Slider(new GUIContent("Frame rate", "Target frame rate per second"), frameRate, 0, mediaInfo.Framerate);
            else
                frameRate = EditorGUILayout.Slider(new GUIContent("Frame rate", "Target frame rate per second"), frameRate, 0, mediaInfo.Framerate);

            RenderAdvancedSettings();
            CalculateSize();
            RenderStats();
            RenderOutputFolder();
        }

        private void DrawStatItem(string label, string value)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(label + ":", EditorStyles.label, GUILayout.Width(80));
                EditorGUILayout.LabelField(value, EditorStyles.boldLabel);
            }
        }
        
        void RenderStats()
        {
            KawaiiStudioGUI.DrawSection("\ud83d\udcca STATISTICS", () => {
                DrawStatItem("Total Frames", totalFrames.ToString());
                if(useAtlas)
                {
                    DrawStatItem("Tiles (Col/Row)", $"{slices.x} x {slices.y}");
                    DrawStatItem("Frame Size", $"{frameSize.x} x {frameSize.y}");
                    DrawStatItem("Atlas Size", $"{atlasSize.x} x {atlasSize.y} (x{atlasCount})");
                }
                
                long vram = atlasSize.x * atlasSize.y / 2L * atlasCount;
                float vram_mb = vram / (1024f * 1024f);
                DrawStatItem("VRAM Usage", ByteLengthToString(vram));
                
                if(vram_mb > 512)
                    EditorGUILayout.HelpBox("⚠️ High VRAM usage (>512MB). May cause performance issues.", MessageType.Warning);
                
                if(atlasCount > 1)
                    EditorGUILayout.HelpBox($"ℹ️ Using {atlasCount} atlases. {atlasCount} materials will be created.", MessageType.Info);
            });
        }
        
        string ByteLengthToString(long bytes)
        {
            if(bytes < 1024L)
                return bytes.ToString() + " B";
            else if(bytes < 1024L * 1024L)
                return (bytes / 1024.0).ToString("F3") + " KB";
            else if(bytes < 1024L * 1024L * 1024L)
                return (bytes / 1024L / 1024.0).ToString("F3") + " MB";
            else if(bytes < 1024L * 1024L * 1024L * 1024L)
                return (bytes / 1024L / 1024L / 1024.0).ToString("F3") + " GB";
            else
                return "(>.<)";
        }
        
        string FormatBitsPerSecond(long bps)
        {
            if(bps >= 1_000_000)
                return $"{(bps / 1_000_000f):0.##} Mb/s";
            if(bps >= 1_000)
                return $"{(bps / 1_000f):0.##} kb/s";
            return bps + " b/s";
        }

        void RenderAdvancedSettings()
        {
            KawaiiStudioGUI.DrawSection("\u2699\ufe0f ADVANCED SETTINGS", () => {
                advancedSettings = EditorGUILayout.Foldout(advancedSettings, "Show Settings", true);
                if(advancedSettings)
                {
                    loopAnimation = KawaiiStudioGUI.DrawToggle("Loop animation", loopAnimation);
                    useCrunchCompression = KawaiiStudioGUI.DrawToggle("Crunch compression", useCrunchCompression);
                    useCustomMaterial = KawaiiStudioGUI.DrawToggle("Use custom material", useCustomMaterial);
                    
                    if(useCustomMaterial)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUI.BeginChangeCheck();
                        customMaterial = (Material)EditorGUILayout.ObjectField(new GUIContent("Material", "Custom material with texture"), customMaterial, typeof(Material), true);
                        if(EditorGUI.EndChangeCheck())
                        {
                            customShaderAvailableTextures = GetTextureNames(customMaterial);
                        }

                        if(customMaterial == null)
                            EditorGUILayout.HelpBox("If there is no custom material, KSVideoDecoder shader will be used", MessageType.Info);
                        else if(customShaderAvailableTextures.textures.Length == 0)
                            EditorGUILayout.HelpBox("Shader has not 2D textures, KSVideoDecoder shader will be used", MessageType.Warning);
                        else
                            customShaderTexture = EditorGUILayout.Popup(new GUIContent("Texture2D", "Texture slot used to play video"), customShaderTexture, customShaderAvailableTextures.textures);

                        EditorGUI.indentLevel--;
                    }
                    
                    saveInJPEG = KawaiiStudioGUI.DrawToggle("Save in JPEG", saveInJPEG);
                    if(saveInJPEG)
                        imageQuality = EditorGUILayout.IntSlider(new GUIContent("Quality", "JPEG quality [0..100]"), imageQuality, 0, 100);

                    useAtlas = KawaiiStudioGUI.DrawToggle("Use atlases", useAtlas);
                    
                    EditorGUI.BeginDisabledGroup(!useAtlas);
                    useSingleAtlas = KawaiiStudioGUI.DrawToggle("Single atlas", useSingleAtlas);
                    limitAtlasSize = EditorGUILayout.Vector2IntField(new GUIContent("Limit Atlas size", "Limit maximum atlas size (in pixels)"), limitAtlasSize);
                    limitAtlasSize.x = Mathf.Max(targetFrameSize.x, Mathf.Clamp(limitAtlasSize.x, 32, maxTextureSize));
                    limitAtlasSize.y = Mathf.Max(targetFrameSize.y, Mathf.Clamp(limitAtlasSize.y, 32, maxTextureSize));
                    EditorGUI.EndDisabledGroup();
                }
            });
        }

        string EscapeFilterOption(string str)
        {
            return str.Replace(@"'", @"\'").Replace(@":", @"\:");
        }
        
        string EscapeFilterGraph(string str)
        {
            return str.Replace(@"\", @"\\").Replace(@"'", @"\'").Replace(@",", @"\,").Replace(@";", @"\;").Replace(@"[", @"\[").Replace(@"]", @"\]");
        }
        
        void PlayPreview(string filename)
        {
            string arguments;
            filename = EscapeFilterGraph(EscapeFilterOption(filename));
            if(audio != null)
            {
                string audioFilename = EscapeFilterGraph(EscapeFilterOption(AssetDatabase.GetAssetPath(audio)));
                arguments = String.Format(CultureInfo.InvariantCulture, "-window_title Preview -volume 25 -f lavfi \"movie={0}:sp={1}, fps={5}:round=down, scale={3}x{4}, loop=-1:size={2}, setpts=N/({5}*TB)[out0]; amovie=filename={6}:loop=0, asetpts=N/SR/TB[out1]\"", filename, timeStart, totalFrames, frameSize.x, frameSize.y, frameRate, audioFilename);
            }
            else
                arguments = String.Format(CultureInfo.InvariantCulture, "-window_title Preview -volume 25 -f lavfi \"movie={0}:sp={1}, fps={5}:round=down, scale={3}x{4}, loop=-1:size={2}, setpts=N/({5}*TB)\"", filename, timeStart, totalFrames, frameSize.x, frameSize.y, frameRate);

            string ffplayPath = Path.Combine(ffmpegPath, "ffplay.exe");
            if(!File.Exists(ffplayPath))
                ffplayPath = Path.Combine(ffmpegPath, "ffplay");
            
                Process ffplay = new Process();
                ffplay.StartInfo.FileName = ffplayPath;
                ffplay.StartInfo.Arguments = arguments;
                ffplay.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
                ffplay.StartInfo.UseShellExecute = false;
                ffplay.StartInfo.CreateNoWindow = true;
                ffplay.Start();
        }
        
        TextureIDs GetTextureNames(Material material)
        {
            TextureIDs result = new TextureIDs();
            if(material == null)
                return result;

            Shader shader = material.shader;
            if(shader == null)
                return result;

            int props = ShaderUtil.GetPropertyCount(shader);
            List<string> textures = new List<string>(props);
            List<int> ids = new List<int>(props);
            for(int p = 0; p < props; p++)
            {
                var propType = ShaderUtil.GetPropertyType(shader, p);
                if(propType == ShaderUtil.ShaderPropertyType.TexEnv)
                    if(ShaderUtil.GetTexDim(shader, p) == UnityEngine.Rendering.TextureDimension.Tex2D)
                    {
                        textures.Add(ShaderUtil.GetPropertyDescription(shader, p) + " (" + ShaderUtil.GetPropertyName(shader, p) + ")");
                        ids.Add(p);
                    }
            }
            result.textures = new GUIContent[textures.Count];
            result.propertyIDs = new int[ids.Count];
            for(int tex = 0; tex < textures.Count; tex++)
            {
                result.textures[tex] = new GUIContent(textures[tex]);
                result.propertyIDs[tex] = ids[tex];
            }
            return result;
        }
        
        void Flush(bool endOfStream)
        {
            outputTexture.Apply();
            byte[] bytes;
            if(saveInJPEG)
                bytes = outputTexture.EncodeToJPG(imageQuality);
            else
                bytes = outputTexture.EncodeToPNG();

            string fullPath = AssetPathToFullPath(atlasPaths[currentAtlas]);
            try
            {
                File.WriteAllBytes(fullPath, bytes);
            }
            catch(System.Exception ex)
            {
                AddLog($"✗ Erreur écriture atlas {currentAtlas + 1}: {ex.Message}");
                EditorUtility.DisplayDialog("Erreur", $"Impossible d'écrire:\n{fullPath}\n\n{ex.Message}", "OK");
                StopEncoding();
                return;
            }
            AddLog($"✓ Atlas {currentAtlas + 1}/{atlasCount} saved");
            
            if(endOfStream)
            {
                StopEncoding();
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();
                    AddLog("✅ Atlas conversion completed!");
                    ShowSuccessMessage();
                };
            }
            else
            {
                ClearTexture();
                ++currentAtlas;
            }
        }
        
        void ClearTexture()
        {
            RenderTexture tempRT = RenderTexture.GetTemporary(atlasSize.x, atlasSize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            RenderTexture.active = tempRT;
            Graphics.DrawTexture(new Rect(Vector2.zero, atlasSize), Texture2D.blackTexture);
            outputTexture.ReadPixels(new Rect(Vector2.zero, atlasSize), 0, 0);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(tempRT);
        }
        
        void CreateTextures()
        {
            atlasPaths = new string[atlasCount];
            byte[] bytes;
            if(saveInJPEG)
                bytes = Texture2D.blackTexture.EncodeToJPG(imageQuality);
            else
                bytes = Texture2D.blackTexture.EncodeToPNG();

            string extension = saveInJPEG ? "jpeg" : "png";
            for(int atlas = 0; atlas < atlasCount; atlas++)
            {
                atlasPaths[atlas] = String.Format("{0}/{1} Atlas {2}.{3}", outputDirectory, outputName, atlas, extension);
                string fullPath = AssetPathToFullPath(atlasPaths[atlas]);
                try
                {
                    string dir = Path.GetDirectoryName(fullPath);
                    if(!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    File.WriteAllBytes(fullPath, bytes);
                }
                catch(System.Exception ex)
                {
                    AddLog($"✗ Erreur écriture atlas {atlas}: {ex.Message}");
                    EditorUtility.DisplayDialog("Erreur", $"Impossible d'écrire:\n{fullPath}\n\n{ex.Message}", "OK");
                    throw;
                }
            }
            AssetDatabase.Refresh();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach(string atlas in atlasPaths)
                {
                    TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(atlas);
                    ti.alphaSource = TextureImporterAlphaSource.None;
                    ti.npotScale = TextureImporterNPOTScale.None;
                    ti.wrapMode = TextureWrapMode.Clamp;
                    ti.maxTextureSize = maxTextureSize;
                    ti.crunchedCompression = useCrunchCompression;
                    ti.compressionQuality = 100;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }
        
        // ========== CREATION LIKE LEVIANT: 1 MATERIAL PER ATLAS ==========
        void AnimateMaterialRefference(AnimationClip anim)
        {
            // EXACTLY line 786-804 from Leviant: 1 MATERIAL PER ATLAS!
            AddLog($"📦 Creating {atlasCount} materials (1 per atlas)...");
            
            string property = ShaderUtil.GetPropertyName(customMaterial.shader, customShaderAvailableTextures.propertyIDs[customShaderTexture]);
            ObjectReferenceKeyframe[] textureKeyframes = new ObjectReferenceKeyframe[atlasCount];
            AssetDatabase.StartAssetEditing();
            for(int i = 0; i < atlasCount; i++)
            {
                textureKeyframes[i].time = i * framesPerAtlas / frameRate;
                Material material = new Material(customMaterial);
                material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[i]));
                textureKeyframes[i].value = material;
                AssetDatabase.CreateAsset(material, outputBaseName + " Mat " + i + ".mat");
                if(i == 0)
                    prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
            AssetDatabase.StopAssetEditing();
            EditorCurveBinding bind = EditorCurveBinding.PPtrCurve("", typeof(MeshRenderer), "m_Materials.Array.data[0]");
            AnimationUtility.SetObjectReferenceCurve(anim, bind, textureKeyframes);
        }
        
        void SetST_Curves(AnimationClip anim, AnimationCurve scale_x, AnimationCurve scale_y, AnimationCurve offset_x, AnimationCurve offset_y)
        {
            for(int i = 0; i < offset_x.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offset_x, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offset_x, i, AnimationUtility.TangentMode.Constant);
            }
            for(int i = 0; i < offset_y.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(offset_y, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(offset_y, i, AnimationUtility.TangentMode.Constant);
            }
            string property = ShaderUtil.GetPropertyName(customMaterial.shader, customShaderAvailableTextures.propertyIDs[customShaderTexture]);

            anim.SetCurve("", typeof(MeshRenderer), "material." + property + "_ST.x", scale_x);
            anim.SetCurve("", typeof(MeshRenderer), "material." + property + "_ST.y", scale_y);
            anim.SetCurve("", typeof(MeshRenderer), "material." + property + "_ST.z", offset_x);
            anim.SetCurve("", typeof(MeshRenderer), "material." + property + "_ST.w", offset_y);
        }
        
        void AnimateSingleAtlasOffsets(AnimationClip anim)
        {
            Vector2 tileSize = new Vector2(1.0f / slices.x, 1.0f / slices.y);
            Vector2 frameSizeVec = new Vector2(frameSize.x, frameSize.y);
            Vector2 pixelSize = new Vector2(1.0f / atlasSize.x, 1.0f / atlasSize.y);
            tileSize.x *= (frameSizeVec.x - 1.0f) / frameSizeVec.x;
            tileSize.y *= (frameSizeVec.y - 1.0f) / frameSizeVec.y;

            float timeLength = totalFrames / frameRate;
            AnimationCurve scale_x = AnimationCurve.Constant(0, timeLength, tileSize.x);
            AnimationCurve scale_y = AnimationCurve.Constant(0, timeLength, tileSize.y);
            AnimationCurve offset_x = AnimationCurve.Linear(0, 0, timeLength, 1);
            AnimationCurve offset_y = AnimationCurve.Linear(0, 0, timeLength, 1);

            Keyframe[] offset_x_keys = new Keyframe[totalFrames];
            Keyframe[] offset_y_keys = new Keyframe[(totalFrames - 1) / slices.x + 1];
            Keyframe k = new Keyframe();
            int frameX = 0;
            int frameY = 0;

            for(int y = slices.y - 1; y >= 0 && frameX < totalFrames; y--, frameY++)
            {
                float pixelOffsetY = y * frameSizeVec.y + 0.5f;
                float offsetY = pixelOffsetY * pixelSize.y;
                k.time = frameY * slices.x / frameRate;
                k.value = offsetY;
                offset_y_keys[frameY] = k;
                for(int x = 0; x < slices.x && frameX < totalFrames; x++, frameX++)
                {
                    float pixelOffsetX = x * frameSizeVec.x + 0.5f;
                    float offsetX = pixelOffsetX * pixelSize.x;
                    k.time = frameX / frameRate;
                    k.value = offsetX;
                    offset_x_keys[frameX] = k;
                }
            }
            offset_x.keys = offset_x_keys;
            offset_y.keys = offset_y_keys;
            SetST_Curves(anim, scale_x, scale_y, offset_x, offset_y);

            string property = ShaderUtil.GetPropertyName(customMaterial.shader, customShaderAvailableTextures.propertyIDs[customShaderTexture]);
            Material material = LoadMaterial(customMaterial);
            material.SetTexture(property, AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[0]));
        }
        
        void AnimateAtlasOffsets(AnimationClip anim)
        {
            Vector2 tileSize = new Vector2(1.0f / slices.x, 1.0f / slices.y);
            Vector2 frameSizeVec = new Vector2(frameSize.x, frameSize.y);
            Vector2 pixelSize = new Vector2(1.0f / atlasSize.x, 1.0f / atlasSize.y);
            tileSize.x *= (frameSizeVec.x - 1.0f) / frameSizeVec.x;
            tileSize.y *= (frameSizeVec.y - 1.0f) / frameSizeVec.y;

            float timeLength = totalFrames / frameRate;
            AnimationCurve scale_x = AnimationCurve.Constant(0, timeLength, tileSize.x);
            AnimationCurve scale_y = AnimationCurve.Constant(0, timeLength, tileSize.y);
            AnimationCurve offset_x = AnimationCurve.Linear(0, 0, timeLength, 1);
            AnimationCurve offset_y = AnimationCurve.Linear(0, 0, timeLength, 1);

            Keyframe[] offset_x_keys = new Keyframe[totalFrames];
            Keyframe[] offset_y_keys = new Keyframe[(totalFrames - 1) / slices.x + 1];
            Keyframe k = new Keyframe();
            int frameX = 0;
            int frameY = 0;
            for(int atlas = 0; atlas < atlasCount; atlas++)
            {
                for(int y = slices.y - 1; y >= 0 && frameX < totalFrames; y--, frameY++)
                {
                    float pixelOffsetY = y * frameSizeVec.y + 0.5f;
                    float offsetY = pixelOffsetY * pixelSize.y;
                    k.time = frameY * slices.x / frameRate;
                    k.value = offsetY;
                    offset_y_keys[frameY] = k;
                    for(int x = 0; x < slices.x && frameX < totalFrames; x++, frameX++)
                    {
                        float pixelOffsetX = x * frameSizeVec.x + 0.5f;
                        float offsetX = pixelOffsetX * pixelSize.x;
                        k.time = frameX / frameRate;
                        k.value = offsetX;
                        offset_x_keys[frameX] = k;
                    }
                }
            }
            offset_x.keys = offset_x_keys;
            offset_y.keys = offset_y_keys;
            SetST_Curves(anim, scale_x, scale_y, offset_x, offset_y);
        }
        
        Material LoadMaterial(Material customMaterial)
        {
            Material material;
            material = AssetDatabase.LoadAssetAtPath<Material>(outputBaseName + ".mat");
            if(material != null)
            {
                material.shader = customMaterial.shader;
                material.CopyPropertiesFromMaterial(customMaterial);
            }
            else
            {
                material = new Material(customMaterial);
                AssetDatabase.CreateAsset(material, outputBaseName + ".mat");
            }
            prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }
        
        Material LoadMaterial(Shader shader)
        {
            Material material;
            material = AssetDatabase.LoadAssetAtPath<Material>(outputBaseName + ".mat");
            if(material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, outputBaseName + ".mat");
            }
            material.shader = shader;
            prefab.GetComponent<MeshRenderer>().sharedMaterial = material;
            return material;
        }
        
        void ShowSuccessMessage()
        {
            string videoName = Path.GetFileNameWithoutExtension(inputVideoPath);
            string basePath = outputDirectory + "/" + videoName;
            
            AddLog($"🎉 Conversion finished! Prefab created at: {basePath}.prefab");
            
            if (prefab != null)
                Selection.activeObject = prefab;
                
            EditorUtility.DisplayDialog("Success! 🎉", 
                $"Video conversion completed successfully!\n\n" +
                $"Prefab: {basePath}.prefab\n" +
                $"Location: {outputDirectory}\n\n" +
                $"Total Frames: {totalFrames}\n" +
                $"Atlases: {atlasCount}", 
                "OK");
        }
        
        AnimationClip CreateAnimation()
        {
            AnimationClip anim;
            anim = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputBaseName + ".anim");
            if(anim == null)
            {
                anim = new AnimationClip();
                AnimationClipSettings animSettings = AnimationUtility.GetAnimationClipSettings(anim);
                animSettings.loopTime = loopAnimation;
                AnimationUtility.SetAnimationClipSettings(anim, animSettings);
                AssetDatabase.CreateAsset(anim, outputBaseName + ".anim");
            }
            anim.ClearCurves();
            anim.frameRate = frameRate;
            return anim;
        }
        
        /// <summary>
        /// Injecte les métadonnées vidéo (résolution) dans le matériau Video Screen Overlay
        /// (KSScreenShader: _VideoWidth, _VideoHeight) pour la correction du ratio d'aspect.
        /// </summary>
        void InjectVideoMetadataIntoOverlayMaterial()
        {
            if(mediaInfo.IsEmpty || mediaInfo.FrameSize.x <= 0 || mediaInfo.FrameSize.y <= 0)
                return;

            Material overlayMat = AssetDatabase.LoadAssetAtPath<Material>(VIDEO_SCREEN_OVERLAY_MAT_PATH);
            if(overlayMat == null)
            {
                AddLog("⚠️ Video Screen Overlay.mat introuvable, métadonnées non injectées.");
                return;
            }

            // KSScreenShader utilise _VideoWidth et _VideoHeight pour la correction du ratio
            overlayMat.SetFloat("_VideoWidth", mediaInfo.FrameSize.x);
            overlayMat.SetFloat("_VideoHeight", mediaInfo.FrameSize.y);
            EditorUtility.SetDirty(overlayMat);
            AssetDatabase.SaveAssets();
            AddLog($"✓ Métadonnées injectées dans Video Screen Overlay: {mediaInfo.FrameSize.x}x{mediaInfo.FrameSize.y}");
        }
        
        // EXACTLY like Leviant line 835-913
        void CreatePrefab()
        {
            outputBaseName = outputDirectory + "/" + outputName;

            // Injection des métadonnées vidéo dans Video Screen Overlay.mat (KSScreenShader)
            InjectVideoMetadataIntoOverlayMaterial();

            if(prefab == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(outputBaseName + ".prefab");
                if(prefab != null)
                {
                    prefab = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                }
                else
                {
                    prefab = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    prefab.name = outputName;
                    prefab.transform.localScale = new Vector3(mediaInfo.AspectRatio, 1, 1);
                    DestroyImmediate(prefab.GetComponent<MeshCollider>());
                }
            }

            AnimationClip anim = CreateAnimation();

            float timeLength = totalFrames / frameRate;
            if(useCustomMaterial)
            {
                if(atlasCount == 1)
                    AnimateSingleAtlasOffsets(anim);
                else
                {
                    AnimateMaterialRefference(anim); // 1 MATERIAL PER ATLAS
                    if(framesPerAtlas > 1)
                        AnimateAtlasOffsets(anim);
                }
            }
            else
            {
                // EXACT logic from Leviant for AtlasDecoder shader
                Shader decoderShader = AssetDatabase.LoadAssetAtPath<Shader>(SHADER_PATH);
                Material d = LoadMaterial(decoderShader);
                d.SetFloat("_FrameRate", frameRate);
                d.SetFloat("_AtlasSizeX", slices.x);
                d.SetFloat("_AtlasSizeY", slices.y);
                d.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[0]));
                for(int i = 1; i < Mathf.Min(64, atlasCount); i++)
                    d.SetTexture("_MainTex" + i, AssetDatabase.LoadAssetAtPath<Texture2D>(atlasPaths[i]));

                AnimationCurve curve = AnimationCurve.Linear(0, 0, timeLength, timeLength);
                anim.SetCurve("", typeof(MeshRenderer), "material._CustomTime", curve);
            }

            AnimatorController controller;
            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(outputBaseName + ".controller");
            if(controller == null)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(outputBaseName + ".controller");

                AnimatorStateMachine sm = controller.layers[0].stateMachine;
                AnimatorState state = sm.AddState("Time scroll");
                state.motion = anim;
                state.writeDefaultValues = false;
            }
            Animator animator = prefab.GetComponent<Animator>();
            if(animator == null)
                animator = prefab.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if(audio != null)
            {
                AudioSource audioSource = prefab.GetComponent<AudioSource>();
                if(audioSource == null)
                    audioSource = prefab.AddComponent<AudioSource>();
                audioSource.clip = audio;
                audioSource.loop = loopAnimation;
                audioSource.dopplerLevel = 0;
            }
#if UNITY_2018_4_OR_NEWER
            PrefabUtility.SaveAsPrefabAsset(prefab, outputBaseName + ".prefab");
#else
            PrefabUtility.CreatePrefab(outputBaseName + ".prefab", prefab, ReplacePrefabOptions.ConnectToPrefab);
#endif
        }
        
        void SetupEnvironment()
        {
            framesPerAtlas = slices.x * slices.y;
            
            // LEVIANT LOGIC: Automatic switch if too many atlases
            if(atlasCount > 64 && !useCustomMaterial)
            {
                useCustomMaterial = true;
                AddLog("⚠️ Too many atlases (>64). Switching to Multi-Material mode.");
            }

            // LEVIANT LOGIC: Fallback to Unlit/Texture if needed
            if(useCustomMaterial && (customMaterial == null || customShaderAvailableTextures.textures.Length == 0))
            {
                Shader s = Shader.Find("Unlit/Texture");
                if(s != null)
                {
                    customMaterial = new Material(s);
                    customShaderAvailableTextures = GetTextureNames(customMaterial);
                    customShaderTexture = 0;
                }
            }

            outputName = SanitizeAndTruncatePathComponent(Path.GetFileNameWithoutExtension(inputVideoPath));
            if(String.IsNullOrEmpty(outputDirectory) || !AssetDatabase.IsValidFolder(outputDirectory))
            {
                outputDirectory = DEFAULT_OUTPUT_PATH + "/" + outputName;
                
                string[] folders = outputDirectory.Replace("Assets/", "").Split('/');
                string currentPath = "Assets";
                foreach(string folder in folders)
                {
                    if(string.IsNullOrEmpty(folder)) continue;
                    string newPath = currentPath + "/" + folder;
                    if(!AssetDatabase.IsValidFolder(newPath))
                        AssetDatabase.CreateFolder(currentPath, folder);
                    currentPath = newPath;
                }
                outputDirectory = currentPath;
            }
            CreateTextures();
        }
        
        MediaInfo GetMediaInfo(string filename)
        {
            if(String.IsNullOrEmpty(filename))
                return new MediaInfo();

            lastOpenedDirectory = Path.GetDirectoryName(filename);
            string ffprobePath = Path.Combine(ffmpegPath, "ffprobe.exe");
            if(!File.Exists(ffprobePath))
                ffprobePath = Path.Combine(ffmpegPath, "ffprobe");
            
            string arguments = String.Format("-print_format json -show_format -show_streams -i \"{0}\"", filename);

            ProcessStartInfo start = new ProcessStartInfo(ffprobePath, arguments)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using(Process ffprobe = Process.Start(start))
            {
                string json = ffprobe.StandardOutput.ReadToEnd();
                string errors = ffprobe.StandardError.ReadToEnd();
                ffprobe.WaitForExit();

                if(ffprobe.ExitCode != 0)
                {
                    Debug.LogError(errors);
                    return new MediaInfo();
                }
                MediaInfo mediaInfoResult = JsonUtility.FromJson<MediaInfo>(json);
                if(mediaInfoResult != null)
                    mediaInfoResult.Refresh();
                return mediaInfoResult ?? new MediaInfo();
            }
        }
        
        float QuatityToQScale(float quality)
        {
            float q = 0.025f * quality - 1.68f;
            return -9.07557f * q - 5.37507f * q * q - 0.135614f * q * q * q + 9.78417f * q * q * q * q + 8.71064f;
        }
        
        void SplitVideoToFrames(string filename)
        {
            log = new StringBuilder();
            AddLog("╔════════════════════════════════════");
            AddLog("🚀 Starting video conversion...");
            AddLog($"🎹 Input: {Path.GetFileName(filename)}");
            AddLog($"⏱️ Duration: {FormatTime(timeEnd - timeStart)}");
            AddLog($"📊 Frames: {totalFrames} @ {frameRate} fps");
            AddLog("╚════════════════════════════════════");
            SetupEnvironment();
            CreatePrefab(); // CRÉATION AVANT LA CONVERSION (Comme Leviant)
            string extension = saveInJPEG ? "jpeg" : "png";
            string quality = saveInJPEG ? ("-qscale:v " + QuatityToQScale(imageQuality)) : "";
            string outDirFull = AssetPathToFullPath(outputDirectory);
            string outPattern = Path.Combine(outDirFull, outputName + " Atlas %d." + extension);
            string arguments = String.Format(CultureInfo.InvariantCulture, "-nostdin -y -ss {1} -to {2} -i \"{0}\" -filter_complex \"fps=fps={5}, format=pix_fmts=rgb24, scale={3}x{4}:flags=area:out_range=full\" -f image2 -start_number 0 -frames {6} {7} \"{8}\"", filename, timeStart, timeEnd, frameSize.x, frameSize.y, frameRate, totalFrames, quality, outPattern);
            
            string ffmpegExe = Path.Combine(ffmpegPath, "ffmpeg.exe");
            if(!File.Exists(ffmpegExe))
                ffmpegExe = Path.Combine(ffmpegPath, "ffmpeg");
            
            ffmpeg = new Process();
            ffmpeg.StartInfo.FileName = ffmpegExe;
            ffmpeg.StartInfo.Arguments = arguments;
            ffmpeg.StartInfo.CreateNoWindow = true;
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.StartInfo.RedirectStandardError = true;
            ffmpeg.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            ffmpeg.EnableRaisingEvents = true;
            ffmpeg.ErrorDataReceived += OnFFMPEG_Error;
            ffmpeg.Exited += OnFFMPEG_Split_Exited;
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();
            isEncoding = true;
        }
        
        void CreateAtlasWithFFMPEG(string filename)
        {
            log = new StringBuilder();
            AddLog("╔════════════════════════════════════");
            AddLog("🚀 Starting video conversion...");
            AddLog($"🎹 Input: {Path.GetFileName(filename)}");
            AddLog($"⏱️ Duration: {FormatTime(timeEnd - timeStart)}");
            AddLog($"📊 Frames: {totalFrames} @ {frameRate} fps");
            AddLog("╚════════════════════════════════════");
            SetupEnvironment();
            CreatePrefab(); // CRÉATION AVANT LA CONVERSION (Comme Leviant)
            string arguments = String.Format(CultureInfo.InvariantCulture, "-nostdin -ss {1} -to {2} -i \"{0}\" -filter_complex \"fps=fps={5}, format=pix_fmts=rgb24, scale={3}x{4}:flags=area:out_range=full, vflip\" -f rawvideo -frames {6} pipe:1", filename, timeStart, timeEnd, frameSize.x, frameSize.y, frameRate, totalFrames);
            
            string ffmpegExe = Path.Combine(ffmpegPath, "ffmpeg.exe");
            if(!File.Exists(ffmpegExe))
                ffmpegExe = Path.Combine(ffmpegPath, "ffmpeg");
            
            ffmpeg = new Process();
            ffmpeg.StartInfo.FileName = ffmpegExe;
            ffmpeg.StartInfo.Arguments = arguments;
            ffmpeg.StartInfo.CreateNoWindow = true;
            ffmpeg.StartInfo.UseShellExecute = false;
            ffmpeg.StartInfo.RedirectStandardOutput = true;
            ffmpeg.StartInfo.RedirectStandardError = true;
            ffmpeg.StartInfo.WorkingDirectory = Environment.CurrentDirectory;
            ffmpeg.EnableRaisingEvents = true;
            ffmpeg.ErrorDataReceived += OnFFMPEG_Error;
            ffmpeg.Exited += OnFFMPEG_Exited;
            ffmpeg.Start();
            ffmpeg.BeginErrorReadLine();

            outputTexture = new Texture2D(atlasSize.x, atlasSize.y, TextureFormat.RGB24, false);
            ClearTexture();

            BeginReadPipe(ffmpeg.StandardOutput.BaseStream);
            AddLog("▶ Atlas conversion started...");
        }
        
        void BeginReadPipe(Stream stream)
        {
            frame = new Color32[frameSize.x * frameSize.y];
            imageData = new byte[frameSize.x * frameSize.y * 3];
            pipe = stream;
            currentAtlas = 0;
            currentFrame = -1;
            isEncoding = true;
            EditorApplication.update += EditorUpdateFFMPEG;
        }
        
        void OnFFMPEG_Error(object sender, DataReceivedEventArgs e)
        {
            if(e.Data != null)
            {
                lock(logLock)
                {
                    if(log != null)
                        log.AppendLine(e.Data);
                }
            }
        }
        
        void OnFFMPEG_Exited(object sender, EventArgs e)
        {
            isEncoding = false;
            EditorApplication.update -= EditorUpdateFFMPEG;
        }
        
        void OnFFMPEG_Split_Exited(object sender, EventArgs e)
        {
            StopEncoding();
            EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                AddLog("✅ Frame conversion completed!");
                ShowSuccessMessage();
            };
        }
        
        void StopEncoding()
        {
            isEncoding = false;
            currentFrame = 0;
            
            EditorApplication.update -= EditorUpdateFFMPEG;
            
            if(ffmpeg != null)
            {
                try
                {
                    if(!ffmpeg.HasExited)
                        ffmpeg.Kill();
                    ffmpeg.Dispose();
                }
                catch { }
                ffmpeg = null;
            }
            
            if(pipe != null)
            {
                try
                {
                    pipe.Dispose();
                }
                catch { }
                pipe = null;
            }
            
            if(outputTexture != null)
            {
                DestroyImmediate(outputTexture);
                outputTexture = null;
            }
        }
        
        private string FormatTime(float seconds)
        {
            TimeSpan t = TimeSpan.FromSeconds(seconds);
            return $"{t.Minutes:D2}:{t.Seconds:D2}.{t.Milliseconds:D3}";
        }
        
        [Serializable]
        class TextureIDs
        {
            public GUIContent[] textures;
            public int[] propertyIDs;
            public TextureIDs()
            {
                textures = new GUIContent[0];
                propertyIDs = new int[0];
            }
        }
        
        [Serializable]
        class MediaInfo
        {
            public float Duration { get; set; }
            public float Framerate { get; set; }
            public Vector2Int FrameSize { get; set; }
            public long Frames { get; set; }
            public int Bitrate { get; set; }
            public float AspectRatio { get; set; }
            public bool IsEmpty { get; set; }

            public MediaStreamFormat format = null;
            public MediaStreamInfo[] streams = null;
            
            public MediaInfo()
            {
                IsEmpty = true;
            }
            
            public MediaInfo(VideoClip clip)
            {
                if(clip == null)
                    return;

                Duration = (float)clip.length;
                Framerate = (float)clip.frameRate;
                FrameSize = new Vector2Int((int)clip.width, (int)clip.height);
                Frames = (long)clip.frameCount;
                Bitrate = 0;
                IsEmpty = false;
            }
            
            [Serializable]
            public class MediaStreamFormat
            {
                public float Duration { get; set; }
                public int Bitrate { get; set; }
                [SerializeField] string duration = null;
                [SerializeField] string bit_rate = null;

                internal void Refresh()
                {
                    if(!String.IsNullOrEmpty(duration))
                    {
                        float d;
                        ParseFloat(duration, out d);
                        Duration = d;
                    }
                    if(!String.IsNullOrEmpty(bit_rate))
                        Bitrate = int.Parse(bit_rate, CultureInfo.InvariantCulture);
                }
            }
            
            [Serializable]
            public class MediaStreamInfo
            {
                public enum StreamType
                {
                    Other, Video, Audio
                }
                public StreamType Content { get; set; }
                public int Index { get { return index; } }
                public int Bitrate { get { return _bitrate; } }
                public long Frames { get { return _frames; } }
                public float Duration { get { return _duration; } }
                public float Framerate { get; set; }
                public float AspectRatio { get; set; }
                public Vector2Int FrameSize { get; set; }
                [SerializeField] int _bitrate = 0;
                [SerializeField] long _frames = 0;
                [SerializeField] float _duration = 0;
                [SerializeField] int index = 0;
                [SerializeField] string codec_type = null;
                [SerializeField] int width = 0;
                [SerializeField] int height = 0;
                [SerializeField] string display_aspect_ratio = null;
                [SerializeField] string r_frame_rate = null;
                [SerializeField] string duration = null;
                [SerializeField] string bit_rate = null;
                [SerializeField] string nb_frames = null;
                
                internal void Refresh()
                {
                    try
                    {
                        Content = (StreamType)Enum.Parse(typeof(StreamType), codec_type, true);

                        ParseFloat(duration, out _duration);
                        long.TryParse(nb_frames, out _frames);
                        int.TryParse(bit_rate, out _bitrate);

                        if(Content == StreamType.Video)
                        {
                            FrameSize = new Vector2Int(width, height);
                            string[] rd = r_frame_rate.Split('/');
                            float r, d;
                            ParseFloat(rd[0], out r);
                            if(rd.Length > 1 && ParseFloat(rd[1], out d))
                                Framerate = r / d;
                            else
                                Framerate = r;

                            if(!String.IsNullOrEmpty(display_aspect_ratio))
                            {
                                string[] xy = display_aspect_ratio.Split(':');
                                float x, y;
                                ParseFloat(xy[0], out x);
                                if(xy.Length > 1 && ParseFloat(xy[1], out y))
                                    AspectRatio = x / y;
                                else
                                    AspectRatio = x;
                            }
                            else
                                AspectRatio = (float)FrameSize.x / FrameSize.y;
                        }
                    }
                    catch(ArgumentException) { }
                }
            }
            
            public void Refresh()
            {
                if(format != null)
                    format.Refresh();
                Duration = format != null ? format.Duration : 0;
                Bitrate = format != null ? format.Bitrate : 0;
                if(streams != null)
                {
                    foreach(var s in streams)
                    {
                        s.Refresh();
                        if(s.Content == MediaStreamInfo.StreamType.Video)
                        {
                            AspectRatio = s.AspectRatio;
                            FrameSize = s.FrameSize;
                            Framerate = s.Framerate;
                            Frames = s.Frames;
                        }
                    }
                }
                IsEmpty = FrameSize.x * FrameSize.y == 0;
            }
            
            static bool ParseFloat(string str, out float value)
            {
                return float.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture.NumberFormat, out value);
            }
        }
    }
}

