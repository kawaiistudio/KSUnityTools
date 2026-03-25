// Kawaii Studio Manager v1.4 - Complete and improved version
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace KawaiiStudio
{
    [Serializable]
    public class ToolInfo
    {
        public string name;
        public string fileName;
        public string githubRawUrl;
        public string localPath;
        public string menuItemPath;
        public string currentVersion;
        public string latestVersion;
        public bool updateAvailable;
        public bool isInstalled;
        public DateTime lastChecked;
        public string description;
        public string releaseNotes;
    }

    [Serializable]
    public class ShaderInfo
    {
        public string name;
        public string path;
        public bool isInstalled;
    }

    [Serializable]
    public class GitHubRelease
    {
        public string tag_name;
        public string name;
        public string body;
        public string published_at;
        public GitHubReleaseAsset[] assets;
    }

    [Serializable]
    public class GitHubReleaseAsset
    {
        public string name;
        public string browser_download_url;
        public int size;
    }

    [Serializable]
    public class TranslationEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class TranslationFile
    {
        public List<TranslationEntry> entries;
    }

    public class KawaiiStudioManager : EditorWindow
    {
        // Local version is stored in Assets/Kawaii Studio/VERSION.md (single line, e.g. 1.4)
        private const string VERSION_FILE_PATH = "Assets/Kawaii Studio/VERSION.md";
        private const string DEFAULT_LOCAL_VERSION = "0.0";

        private const string GITHUB_RELEASES_API = "https://api.github.com/repos/kawaiistudio/KSUnityTools/releases/latest";
        private const string GITHUB_BASE_URL = "https://raw.githubusercontent.com/kawaiistudio/KSUnityTools/main/";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        private const string LANGUAGES_FOLDER = "Assets/Kawaii Studio/Languages";

        // Package update (GitHub release .unitypackage)
        private const string KS_ROOT_FOLDER = "Assets/Kawaii Studio";
        private const string UPDATE_DOWNLOAD_FOLDER = "Library/KawaiiStudioUpdates";

        private bool isInstallingPackage = false;
        private float packageDownloadProgress = 0f;
        private string packageStatus = "";

        private enum InstalledView
        {
            Tools = 0,
            Shaders = 1
        }

        private InstalledView installedView = InstalledView.Tools;
        private readonly List<ShaderInfo> shaders = new List<ShaderInfo>();
        private bool shadersScanned = false;

        private static string GetLanguagesFolderPath()
        {
            // Absolute path (robust)
            try
            {
                string abs = Path.Combine(Application.dataPath, "Kawaii Studio", "Languages");
                if (Directory.Exists(abs)) return abs;
            }
            catch { }

            // Relative fallback (Unity usually runs from project root)
            try
            {
                if (Directory.Exists(LANGUAGES_FOLDER)) return LANGUAGES_FOLDER;
                string relFromCwd = Path.Combine(Directory.GetCurrentDirectory(), LANGUAGES_FOLDER);
                if (Directory.Exists(relFromCwd)) return relFromCwd;
            }
            catch { }

            return LANGUAGES_FOLDER;
        }
        
        private List<ToolInfo> tools = new List<ToolInfo>
        {
            new ToolInfo { name = "Studio Manager", fileName = "Kawaii Studio Manager.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20Manager.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Manager.cs",
                menuItemPath = "Kawaii Studio/Studio Manager",
                description = "Manage and update all Kawaii Studio tools" },
            new ToolInfo { name = "Kawaii Exporter", fileName = "Kawaii Studio Exporter.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20Exporter.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Exporter.cs",
                menuItemPath = "Kawaii Studio/Universal Tools/Kawaii Exporter",
                description = "Export assets/prefabs as .unitypackage (with dependencies)" },
            new ToolInfo { name = "Video Animator", fileName = "Kawaii Studio Video Animator.cs", 
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20Video%20Animator.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Video Animator.cs",
                menuItemPath = "Kawaii Studio/Universal Tools/Video Animator",
                description = "Convert videos to optimized Unity texture animations" },
            new ToolInfo { name = "GLB to FBX Converter", fileName = "Kawaii Studio GLB to FBX.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20GLB%20to%20FBX.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio GLB to FBX.cs",
                menuItemPath = "Kawaii Studio/Universal Tools/GLB to FBX Converter",
                description = "Convert GLB models to VRChat-ready FBX with material setup" },
            new ToolInfo { name = "NSFW Detector", fileName = "Kawaii Studio NSFW Detector.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20NSFW%20Detector.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio NSFW Detector.cs",
                menuItemPath = "Kawaii Studio/VRC/NSFW Detector",
                description = "Scan avatars for potentially NSFW content and SDK tag recommendations" },
            new ToolInfo { name = "Prefab Optimizer", fileName = "Kawaii Studio Prefab Optimizer.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20Prefab%20Optimizer.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Prefab Optimizer.cs",
                menuItemPath = "Kawaii Studio/Universal Tools/Prefab Optimizer",
                description = "Optimize avatars with texture, mesh, and audio compression" },
            new ToolInfo { name = "Tail Animator to PhysBones", fileName = "Kawaii Studio Tail Animator to PhysBones.cs",
                githubRawUrl = GITHUB_BASE_URL + "Tools/Kawaii%20Studio%20Tail%20Animator%20to%20PhysBones.cs",
                localPath = "Assets/Kawaii Studio/Editor/Tools/Kawaii Studio Tail Animator to PhysBones.cs",
                menuItemPath = "Kawaii Studio/Universal Tools/Tail to PhysBones Converter",
                description = "Convert Tail Animator to VRChat PhysBones" }
        };
        
        private string[] availableLanguages = { "English", "Русский", "中文", "日本語", "Español", "Français", "Deutsch" };
        private string[] languageCodes = { "en", "ru", "zh", "ja", "es", "fr", "de" };
        private int selectedLanguage = 0;
        private Dictionary<string, Dictionary<string, string>> translations = new Dictionary<string, Dictionary<string, string>>();
        private Vector2 scrollPosition;
        private Vector2 logScrollPosition;
        private string logOutput = "";
        private bool isCheckingUpdates = false;
        private bool showTools = true;
        private bool showLinks = true;
        private bool showSettings = true;
        private bool showReleaseInfo = true;
        private bool showDebugInfo = false;
        
        // State
        private GitHubRelease latestRelease;
        private bool isLoadingRelease = false;
        private bool releaseUpdateAvailable = false;
        private Texture2D logoTexture;
        private Texture2D bannerTexture;
        private static bool isDownloadingBranding = false;
        private string cachedLocalVersion = null;
        
        // RGB Wave Animation
        private float animTime = 0f;

        [MenuItem("Kawaii Studio/Studio Manager", priority = 0)]
        public static void ShowWindow()
        {
            KawaiiStudioManager window = GetWindow<KawaiiStudioManager>("Kawaii Studio Manager");
            window.minSize = new Vector2(800, 700);
            window.Show();
        }

        void OnEnable()
        {
            LoadPreferences();
            LoadTranslationsFromJSON();
            LoadLocalVersion(true);
            CheckInstalledTools();
            ScanShaders();
            DownloadBrandingAssets();
            LoadLatestRelease();
            EditorApplication.update += AutoCheckUpdates;
            EditorApplication.update += OnEditorUpdate;
        }
        
        void OnEditorUpdate()
        {
            animTime += 0.016f; // ~60 FPS
            if (animTime > 100f) animTime = 0f;
            Repaint();
        }
        
        Color GetRGBWaveColor(float offset = 0f)
        {
            float time = animTime * 2f + offset;
            float r = Mathf.Sin(time * 0.5f) * 0.5f + 0.5f;
            float g = Mathf.Sin(time * 0.5f + 2.0f) * 0.5f + 0.5f;
            float b = Mathf.Sin(time * 0.5f + 4.0f) * 0.5f + 0.5f;
            return new Color(r * 0.8f + 0.2f, g * 0.8f + 0.2f, b * 0.8f + 0.2f);
        }

        void LoadLatestRelease()
        {
            if (isLoadingRelease) return;
            isLoadingRelease = true;
            EditorApplication.delayCall += () => { StartCoroutine(FetchLatestRelease()); };
        }

        IEnumerator FetchLatestRelease()
        {
            UnityWebRequest request = UnityWebRequest.Get(GITHUB_RELEASES_API);
            request.SetRequestHeader("User-Agent", "KawaiiStudioManager/" + GetLocalVersion());
            var operation = request.SendWebRequest();
            while (!operation.isDone) yield return null;
            
            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    latestRelease = JsonUtility.FromJson<GitHubRelease>(request.downloadHandler.text);
                    releaseUpdateAvailable = latestRelease != null && IsNewerVersion(GetLocalVersion(), latestRelease.tag_name);
                    AddLog($"✅ Latest release loaded: {latestRelease?.tag_name ?? "Unknown"}");
                }
                catch (Exception e)
                {
                    AddLog($"⚠️ Failed to parse release info: {e.Message}");
                }
            }
            else
            {
                AddLog($"⚠️ Failed to fetch release info: {request.error}");
            }
            isLoadingRelease = false;
            request.Dispose();
            Repaint();
        }

        void OnDisable()
        {
            SavePreferences();
            EditorApplication.update -= AutoCheckUpdates;
            EditorApplication.update -= OnEditorUpdate;
        }

        void LoadTranslationsFromJSON()
        {
            translations.Clear();
            string languagesPath = GetLanguagesFolderPath();
            if (!Directory.Exists(languagesPath))
            {
                AddLog($"⚠️ Languages folder not found: {languagesPath}");
                LoadFallbackTranslations();
                return;
            }
            foreach (string langCode in languageCodes)
            {
                string jsonPath = Path.Combine(languagesPath, $"{langCode}.json");
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonPath);
                        TranslationFile translationFile = JsonUtility.FromJson<TranslationFile>(jsonContent);
                        if (translationFile != null && translationFile.entries != null)
                        {
                            Dictionary<string, string> langDict = new Dictionary<string, string>();
                            foreach (var entry in translationFile.entries)
                            {
                                if (!string.IsNullOrEmpty(entry.key) && !string.IsNullOrEmpty(entry.value))
                                    langDict[entry.key] = entry.value;
                            }
                            translations[langCode] = langDict;
                        }
                    }
                    catch (Exception e)
                    {
                        AddLog($"❌ Error loading {langCode}.json: {e.Message}");
                    }
                }
            }
            if (translations.Count == 0)
                LoadFallbackTranslations();
        }

        void LoadFallbackTranslations()
        {
            translations["en"] = new Dictionary<string, string>
            {
                { "title", "KAWAII STUDIO MANAGER" }, { "subtitle", "Manage all your Kawaii Studio tools" },
                { "tools", "INSTALLED TOOLS" }, { "check_updates", "CHECK FOR UPDATES" }, { "update_all", "UPDATE ALL" },
                { "community", "COMMUNITY LINKS" }, { "settings", "SETTINGS" }, { "language", "Language" },
                { "version", "Version" }, { "status", "Status" }, { "installed", "Installed" },
                { "not_installed", "Not Installed" }, { "update_available", "Update Available" },
                { "up_to_date", "Up to Date" }, { "install", "INSTALL" }, { "update", "UPDATE" },
                { "checking", "Checking..." }, { "log", "LOG OUTPUT" }, { "discord_join", "Join Discord" },
                { "telegram_join", "Join Telegram" }, { "github_view", "View on GitHub" }
            };
        }

        void DownloadBrandingAssets()
        {
            if (isDownloadingBranding) return;
            if (logoTexture != null && bannerTexture != null) return;

            // Branding is loaded locally from Assets/Kawaii Studio/References (no network).
            isDownloadingBranding = true;
            if (logoTexture == null) logoTexture = KawaiiStudioBranding.Logo;
            if (bannerTexture == null) bannerTexture = KawaiiStudioBranding.Banner;
            isDownloadingBranding = false;
        }

        void AutoCheckUpdates()
        {
            if (EditorApplication.timeSinceStartup % 3600 < 1 && !isCheckingUpdates)
                CheckForUpdates();
        }

        string T(string key)
        {
            string langCode = languageCodes[selectedLanguage];
            if (translations.ContainsKey(langCode) && translations[langCode].ContainsKey(key))
                return translations[langCode][key];
            if (translations.ContainsKey("en") && translations["en"].ContainsKey(key))
                return translations["en"][key];
            return key;
        }

        void LoadPreferences()
        {
            string savedLang = EditorPrefs.GetString(PREFS_LANGUAGE, "en");
            selectedLanguage = Array.IndexOf(languageCodes, savedLang);
            if (selectedLanguage < 0) selectedLanguage = 0;
        }

        void SavePreferences()
        {
            EditorPrefs.SetString(PREFS_LANGUAGE, languageCodes[selectedLanguage]);
        }

        void CheckInstalledTools()
        {
            foreach (var tool in tools)
            {
                tool.isInstalled = File.Exists(tool.localPath);
                if (tool.isInstalled)
                    tool.currentVersion = ExtractVersionFromFile(tool.localPath);

                // The Manager itself doesn't necessarily expose a VERSION constant; display the package version instead.
                // Also avoids false positives like DEFAULT_LOCAL_VERSION being interpreted as "VERSION".
                if (tool.name == "Studio Manager")
                    tool.currentVersion = GetLocalVersion();
            }
        }

        void ScanShaders()
        {
            shaders.Clear();
            shadersScanned = true;

            try
            {
                // Scan all Shader assets inside Kawaii Studio folder
                string[] guids = AssetDatabase.FindAssets("t:Shader", new[] { KS_ROOT_FOLDER });
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;

                    Shader s = AssetDatabase.LoadAssetAtPath<Shader>(path);
                    if (s == null) continue;

                    shaders.Add(new ShaderInfo
                    {
                        name = s.name,
                        path = path,
                        isInstalled = true
                    });
                }

                shaders.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                AddLog($"⚠️ Shader scan failed: {e.Message}");
            }
        }

        string ExtractVersionFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return "Unknown";
                string content = File.ReadAllText(filePath);
                return ExtractVersionFromContent(content);
            }
            catch
            {
                return "Unknown";
            }
        }

        string ExtractVersionFromContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return null;
            try
            {
                var patterns = new[]
                {
                    // Match the VERSION identifier only (avoid DEFAULT_LOCAL_VERSION, VERSION_FILE_PATH, etc.)
                    @"\bprivate\s+const\s+string\s+VERSION\b\s*=\s*""([^""]+)""",
                    @"\bpublic\s+const\s+string\s+VERSION\b\s*=\s*""([^""]+)""",
                    @"\bconst\s+string\s+VERSION\b\s*=\s*""([^""]+)""",
                    @"\bVERSION\b\s*=\s*""([^""]+)"""
                };
                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(content, pattern, 
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string version = match.Groups[1].Value.Trim();
                        if (IsValidVersion(version)) return version;
                    }
                }
            }
            catch { }
            return null;
        }

        bool IsValidVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return false;
            return System.Text.RegularExpressions.Regex.IsMatch(version, @"^v?\d+(\.\d+)*$");
        }

        bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion)) return false;
            currentVersion = currentVersion.TrimStart('v', 'V');
            latestVersion = latestVersion.TrimStart('v', 'V');
            try
            {
                var currentParts = currentVersion.Split('.').Select(p => { int.TryParse(p, out int val); return val; }).ToArray();
                var latestParts = latestVersion.Split('.').Select(p => { int.TryParse(p, out int val); return val; }).ToArray();
                int maxLength = Math.Max(currentParts.Length, latestParts.Length);
                for (int i = 0; i < maxLength; i++)
                {
                    int current = i < currentParts.Length ? currentParts[i] : 0;
                    int latest = i < latestParts.Length ? latestParts[i] : 0;
                    if (latest > current) return true;
                    else if (latest < current) return false;
                }
                return false;
            }
            catch
            {
                return currentVersion != latestVersion;
            }
        }

        void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            
            KawaiiStudioGUI.DrawWindowBackground(position);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(20, 20, 15, 15) });
            
            DownloadBrandingAssets();
            KawaiiStudioGUI.DrawBanner(T("title"), T("subtitle"), GetLocalVersion(), logoTexture, bannerTexture);
            GUILayout.Space(25);

            // Put release information first (most important action: update).
            DrawReleaseInfo();
            GUILayout.Space(15);

            DrawToolsSection();
            GUILayout.Space(15);
            
            DrawCommunitySection();
            GUILayout.Space(15);
            
            DrawSettingsSection();
            GUILayout.Space(15);
            
            DrawLogSection();
            GUILayout.Space(25);
            
            KawaiiStudioGUI.DrawFooter();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }
        
        void DrawToolsSection()
        {
            DrawRGBSection("📦 INSTALLED", () => {
                // Top row: selector
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();

                GUIStyle label = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 10,
                    normal = { textColor = new Color(0.65f, 0.65f, 0.75f) }
                };
                GUILayout.Label("View:", label, GUILayout.Width(40));

                GUIStyle popup = new GUIStyle(EditorStyles.popup)
                {
                    fixedHeight = 24,
                    fontSize = 11
                };

                installedView = (InstalledView)EditorGUILayout.Popup((int)installedView, new[] { "Tools", "Shaders" }, popup, GUILayout.Width(160));

                GUILayout.FlexibleSpace();

                if (installedView == InstalledView.Shaders)
                {
                    if (GUILayout.Button("🔄 Rescan", GUILayout.Height(24), GUILayout.Width(90)))
                        ScanShaders();
                }

                GUILayout.EndHorizontal();

                GUILayout.Space(10);

                if (installedView == InstalledView.Tools)
                {
                    // Stats row (Tools)
                    GUILayout.BeginHorizontal();

                    int installedCount = tools.Count(t => t.isInstalled);
                    int updatesAvailable = releaseUpdateAvailable ? 1 : 0;

                    DrawMiniStatCard("✓", installedCount.ToString(), T("installed"), new Color(0.2f, 0.8f, 0.4f));
                    GUILayout.Space(8);
                    DrawMiniStatCard("↻", updatesAvailable.ToString(), T("update_available"), new Color(1f, 0.7f, 0.2f));
                    GUILayout.Space(8);
                    DrawMiniStatCard("📦", tools.Count.ToString(), "Total", new Color(0.4f, 0.6f, 1f));

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.Space(15);

                    // Tools list
                    for (int i = 0; i < tools.Count; i++)
                    {
                        DrawBeautifulToolCard(tools[i], i);
                        GUILayout.Space(8);
                    }
                }
                else
                {
                    if (!shadersScanned) ScanShaders();

                    // Stats row (Shaders)
                    GUILayout.BeginHorizontal();
                    DrawMiniStatCard("✓", shaders.Count.ToString(), "Installed", new Color(0.2f, 0.8f, 0.4f));
                    GUILayout.Space(8);
                    DrawMiniStatCard("🎨", shaders.Count.ToString(), "Shaders", new Color(0.4f, 0.6f, 1f));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUILayout.Space(12);

                    if (shaders.Count == 0)
                    {
                        EditorGUILayout.HelpBox("No shaders found under Assets/Kawaii Studio.", MessageType.Info);
                        return;
                    }

                    for (int i = 0; i < shaders.Count; i++)
                    {
                        DrawShaderCard(shaders[i], i);
                        GUILayout.Space(6);
                    }
                }
            }, 2.0f);
        }

        void DrawShaderCard(ShaderInfo info, int index)
        {
            Color rgbAccent = GetRGBWaveColor(index * 0.25f);
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(52));

            Rect accentRect = GUILayoutUtility.GetRect(4, 52, GUILayout.Width(4));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(accentRect, rgbAccent);

            GUILayout.Space(10);

            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                normal = { textColor = Color.white }
            };

            EditorGUILayout.BeginVertical();
            GUILayout.Space(8);
            GUILayout.Label(info.name, nameStyle);

            GUIStyle pathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.65f, 0.65f, 0.7f) }
            };
            GUILayout.Label(info.path, pathStyle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            // Ping button
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 28,
                padding = new RectOffset(10, 10, 6, 6)
            };
            if (GUILayout.Button("Find", btnStyle, GUILayout.Width(70)))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Shader>(info.path);
                if (obj != null)
                {
                    EditorGUIUtility.PingObject(obj);
                    Selection.activeObject = obj;
                }
            }

            GUILayout.Space(8);
            EditorGUILayout.EndHorizontal();
        }
        
        void DrawMiniStatCard(string icon, string value, string label, Color baseColor)
        {
            Color rgb = GetRGBWaveColor(label.GetHashCode() * 0.1f);
            
            EditorGUILayout.BeginVertical(GUILayout.Width(95));
            
            // Icon + Value
            EditorGUILayout.BeginHorizontal();
            
            GUIStyle iconStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = rgb }
            };
            GUILayout.Label(icon, iconStyle, GUILayout.Width(20));
            
            GUIStyle valueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = baseColor }
            };
            GUILayout.Label(value, valueStyle);
            
            EditorGUILayout.EndHorizontal();
            
            // Label
            GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.6f, 0.6f, 0.7f) }
            };
            GUILayout.Label(label, labelStyle);
            
            EditorGUILayout.EndVertical();
        }
        
        void DrawBeautifulToolCard(ToolInfo tool, int toolIndex = 0)
        {
            // Status color
            Color statusColor = tool.isInstalled 
                ? new Color(0.3f, 0.85f, 0.5f)
                : new Color(0.5f, 0.7f, 1f);
            
            Color rgbAccent = GetRGBWaveColor(toolIndex * 0.4f);
            
            // Card container
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox, GUILayout.Height(55));
            
            // RGB accent bar on left
            Rect accentRect = GUILayoutUtility.GetRect(4, 55, GUILayout.Width(4));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(accentRect, rgbAccent);
            
            GUILayout.Space(10);

            // Heart icon (left of each tool)
            GUIStyle heartStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = rgbAccent }
            };
            GUILayout.Label("❤️", heartStyle, GUILayout.Width(22), GUILayout.Height(55));

            GUILayout.Space(4);

            // Open button (directly left of the checkmark)
            bool canOpen = tool.isInstalled && !string.IsNullOrEmpty(tool.menuItemPath);
            GUI.enabled = canOpen;
            Color oldBgBtn = GUI.backgroundColor;
            GUI.backgroundColor = canOpen ? new Color(0.22f, 0.22f, 0.26f) : new Color(0.18f, 0.18f, 0.2f);
            GUIStyle openBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                fixedHeight = 26,
                fixedWidth = 60,
                padding = new RectOffset(8, 8, 5, 5)
            };
            // Center the button vertically inside the 55px card height.
            EditorGUILayout.BeginVertical(GUILayout.Width(60), GUILayout.Height(55));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OPEN", openBtnStyle))
            {
                bool ok = EditorApplication.ExecuteMenuItem(tool.menuItemPath);
                if (!ok)
                    EditorUtility.DisplayDialog("Open failed", $"Menu item not found:\n{tool.menuItemPath}", "OK");
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = oldBgBtn;
            GUI.enabled = true;
            
            // Icon
            string icon = tool.isInstalled ? "✓" : "↓";
            GUIStyle iconStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = statusColor }
            };
            GUILayout.Label(icon, iconStyle, GUILayout.Width(28), GUILayout.Height(55));
            
            GUILayout.Space(8);
            
            // Info column
            EditorGUILayout.BeginVertical();
            GUILayout.Space(8);
            
            // Name
            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = Color.white }
            };
            GUILayout.Label(tool.name, nameStyle);
            
            // Status + Version
            EditorGUILayout.BeginHorizontal();
            
            string status = tool.isInstalled 
                ? $"● {T("up_to_date")}" 
                : $"● {T("not_installed")}";
            
            GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                normal = { textColor = statusColor }
            };
            GUILayout.Label(status, statusStyle);
            
            GUILayout.Space(8);
            
            string ver = $"v{tool.currentVersion ?? "?"}";
            
            GUIStyle verStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                normal = { textColor = new Color(0.6f, 0.6f, 0.65f) }
            };
            GUILayout.Label(ver, verStyle);
            
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            
            // Button
            EditorGUILayout.BeginVertical();
            GUILayout.Space(12);
            
            GUI.enabled = !isCheckingUpdates && !isLoadingRelease;
            Color oldBg = GUI.backgroundColor;
            
            GUIStyle btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedHeight = 28,
                padding = new RectOffset(12, 12, 6, 6)
            };
            
            if (!tool.isInstalled)
            {
                // Install is managed via the GitHub release package now.
                GUI.backgroundColor = releaseUpdateAvailable ? new Color(0.3f, 0.8f, 0.5f) : new Color(0.3f, 0.3f, 0.32f);
                if (GUILayout.Button(releaseUpdateAvailable ? "⬇️ Install" : "—", btnStyle, GUILayout.MinWidth(110)))
                    StartPackageUpdateFromLatestRelease();
            }
            else
            {
                GUI.enabled = false;
                GUI.backgroundColor = new Color(0.3f, 0.3f, 0.32f);
                GUILayout.Button("✓", btnStyle, GUILayout.MinWidth(110));
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            EditorGUILayout.EndVertical();
            GUILayout.Space(8);
            
            EditorGUILayout.EndHorizontal();
        }

        void DrawReleaseInfo()
        {
            if (latestRelease == null) return;
            
            DrawRGBSection("📦 LATEST RELEASE", () => {
                GUILayout.BeginHorizontal();
                
                // Version badge
                GUIStyle versionStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 18,
                    normal = { textColor = new Color(0.3f, 0.8f, 0.5f) }
                };
                GUILayout.Label($"🎉 {latestRelease.tag_name}", versionStyle);
                
                GUILayout.FlexibleSpace();
                
                bool releaseUpdateAvailable = IsNewerVersion(GetLocalVersion(), latestRelease.tag_name);

                // Check updates (GitHub release) - placed right next to release info
                Color oldBg = GUI.backgroundColor;
                GUIStyle smallBtn = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 28,
                    padding = new RectOffset(12, 12, 6, 6)
                };

                GUI.enabled = !isCheckingUpdates && !isLoadingRelease;
                GUI.backgroundColor = (isCheckingUpdates || isLoadingRelease) ? Color.grey : new Color(0.3f, 0.7f, 1f);
                if (GUILayout.Button((isCheckingUpdates || isLoadingRelease) ? "⏳" : $"🔍 {T("check_updates")}", smallBtn, GUILayout.Width(170)))
                    CheckForUpdates();
                GUI.enabled = true;
                GUI.backgroundColor = oldBg;

                GUILayout.Space(8);

                if (releaseUpdateAvailable)
                {
                    GUIStyle warnStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        fontSize = 11,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(1f, 0.7f, 0.2f) }
                    };
                    GUILayout.Label("⬆️ Update available please download", warnStyle);
                    GUILayout.Space(8);
                }

                // GitHub button
                oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.5f);
                GUIStyle githubBtn = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 28,
                    padding = new RectOffset(12, 12, 6, 6)
                };
                if (GUILayout.Button("🔗 View on GitHub", githubBtn, GUILayout.Width(140)))
                {
                    Application.OpenURL($"{GITHUB_URL}/releases/tag/{latestRelease.tag_name}");
                }
                GUI.backgroundColor = oldBg;
                
                GUILayout.EndHorizontal();
                
                GUILayout.Space(8);

                // Package update button + progress
                if (IsNewerVersion(GetLocalVersion(), latestRelease.tag_name))
                {
                    GUILayout.BeginHorizontal();
                    GUI.enabled = !isInstallingPackage;
                    Color old = GUI.backgroundColor;
                    GUI.backgroundColor = isInstallingPackage ? Color.gray : new Color(0.35f, 0.8f, 0.5f);
                    if (GUILayout.Button(isInstallingPackage ? "⏳ Downloading..." : "⬇️ Download (.unitypackage)", GUILayout.Height(30), GUILayout.MinWidth(260)))
                    {
                        StartPackageUpdateFromLatestRelease();
                    }
                    GUI.backgroundColor = old;
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();

                    if (isInstallingPackage)
                    {
                        Rect r = GUILayoutUtility.GetRect(10, 18, GUILayout.ExpandWidth(true));
                        EditorGUI.ProgressBar(r, packageDownloadProgress, string.IsNullOrEmpty(packageStatus) ? "Working..." : packageStatus);
                        GUILayout.Space(4);
                    }

                    GUILayout.Space(8);
                }
                
                // Name
                if (!string.IsNullOrEmpty(latestRelease.name))
                {
                    GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        normal = { textColor = new Color(0.85f, 0.85f, 0.9f) }
                    };
                    GUILayout.Label(latestRelease.name, nameStyle);
                    GUILayout.Space(5);
                }
                
                // Description
                if (!string.IsNullOrEmpty(latestRelease.body))
                {
                    string preview = latestRelease.body.Length > 180 
                        ? latestRelease.body.Substring(0, 180) + "..." 
                        : latestRelease.body;
                    
                    GUIStyle bodyStyle = new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 10,
                        wordWrap = true,
                        normal = { textColor = new Color(0.65f, 0.65f, 0.7f) }
                    };
                    GUILayout.Label(preview, bodyStyle);
                }
            }, 3.0f);
        }

        private GitHubReleaseAsset FindUnityPackageAsset(GitHubRelease rel)
        {
            if (rel?.assets == null || rel.assets.Length == 0) return null;
            // Prefer .unitypackage assets
            var pkg = rel.assets.FirstOrDefault(a => !string.IsNullOrEmpty(a?.name) && a.name.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase));
            if (pkg != null) return pkg;
            // Some releases might use .zip - we only support unitypackage for now
            return null;
        }

        private void StartPackageUpdateFromLatestRelease()
        {
            if (latestRelease == null) return;
            if (isInstallingPackage) return;

            // Refresh local version before prompting
            LoadLocalVersion(true);

            var pkg = FindUnityPackageAsset(latestRelease);
            if (pkg == null || string.IsNullOrEmpty(pkg.browser_download_url))
            {
                EditorUtility.DisplayDialog("No package found",
                    "This GitHub release doesn't include a .unitypackage asset.\n\nPlease upload a .unitypackage to the release assets.",
                    "OK");
                return;
            }

            string updateMsg =
                $"🎉 New version available!\n\n" +
                $"═══════════════════════════════\n" +
                $"📦 Current Version: v{GetLocalVersion()}\n" +
                $"✨ Latest Version:  {latestRelease.tag_name}\n" +
                $"═══════════════════════════════\n\n" +
                $"UPDATE PROCESS:\n\n" +
                $"1️⃣  Download .unitypackage (automatic)\n" +
                $"2️⃣  Close Kawaii Studio windows (you)\n" +
                $"3️⃣  Delete Assets/Kawaii Studio (you)\n" +
                $"4️⃣  Import the package (you)\n" +
                $"5️⃣  Finalization & success message (automatic)\n\n" +
                $"Ready to download?";

            if (!EditorUtility.DisplayDialog("🔄 Kawaii Studio Update Available", updateMsg, "Download Now", "Cancel"))
                return;

            isInstallingPackage = true;
            packageDownloadProgress = 0f;
            packageStatus = "Preparing...";
            Repaint();

            EditorApplication.delayCall += () => { StartCoroutine(DownloadAndInstallUnityPackage(pkg)); };
        }

        private void ShowManualInstallDialog(string pkgPath, string version)
        {
            if (string.IsNullOrEmpty(pkgPath)) return;

            // Save update state so hooks can finalize after manual import
            EditorPrefs.SetString("KawaiiStudio.PendingPackagePath", pkgPath);
            EditorPrefs.SetString("KawaiiStudio.PendingVersion", version);

            // Copy path for convenience
            try { EditorGUIUtility.systemCopyBuffer = pkgPath; } catch { }

            string msg =
                $"✅ Package downloaded successfully!\n" +
                $"Version: {version}\n\n" +
                "═══════════════════════════════\n" +
                "INSTALLATION STEPS:\n" +
                "═══════════════════════════════\n\n" +
                "1️⃣  Close all Kawaii Studio windows\n" +
                "     (including this one)\n\n" +
                "2️⃣  Delete the folder:\n" +
                "     Assets/Kawaii Studio\n\n" +
                "3️⃣  Import the .unitypackage:\n" +
                $"     {pkgPath}\n" +
                "     (Path copied to clipboard)\n\n" +
                "4️⃣  After import completes:\n" +
                "     • VERSION.md will update automatically\n" +
                "     • Success message will appear\n" +
                "     • Package file will be deleted\n\n" +
                "═══════════════════════════════\n" +
                "Click 'Open Folder' to see the package file.";

            int choice = EditorUtility.DisplayDialogComplex(
                "🔄 Update Ready - Manual Installation Required",
                msg,
                "Open Folder",
                "OK - I'll do it now",
                "Copy Path Again"
            );

            // 0 = Open folder, 1 = OK, 2 = Copy path
            if (choice == 0)
            {
                try { EditorUtility.RevealInFinder(pkgPath); } catch { }
            }
            else if (choice == 2)
            {
                try { EditorGUIUtility.systemCopyBuffer = pkgPath; } catch { }
                EditorUtility.DisplayDialog("Path Copied", $"Path copied to clipboard:\n\n{pkgPath}", "OK");
            }
        }

        private IEnumerator DownloadAndInstallUnityPackage(GitHubReleaseAsset pkg)
        {
            string updateDir = Path.Combine(Directory.GetCurrentDirectory(), UPDATE_DOWNLOAD_FOLDER);
            string pkgPath = Path.Combine(updateDir, pkg.name);

            // NOTE: In iterator methods, you cannot `yield return` inside a try block that has a catch.
            // So we only use try/catch in non-yield sections and rely on explicit error checks.

            // Prepare Library/ update folder (no yield here)
            try
            {
                Directory.CreateDirectory(updateDir);
            }
            catch (Exception e)
            {
                FailPackageUpdate("Failed to create update folder: " + e.Message);
                yield break;
            }

            // Download (yield loop is NOT inside a try/catch)
            packageStatus = "Downloading package...";
            packageDownloadProgress = 0.01f;
            Repaint();

            string url = pkg.browser_download_url;
            if (string.IsNullOrEmpty(url))
            {
                FailPackageUpdate("Download failed: empty download URL.");
                yield break;
            }

            const int maxRedirects = 5;
            for (int redirect = 0; redirect <= maxRedirects; redirect++)
            {
                UnityWebRequest request = null;
                UnityWebRequestAsyncOperation op = null;

                // Create request (no yield inside try/catch)
                try
                {
                    request = UnityWebRequest.Get(url);
                    request.SetRequestHeader("User-Agent", "KawaiiStudioManager/" + GetLocalVersion());
                    // Helps some endpoints that serve binary assets.
                    request.SetRequestHeader("Accept", "application/octet-stream");

                    request.redirectLimit = 16;
                    request.timeout = 300; // seconds

#if UNITY_2020_2_OR_NEWER
                    var dh = new DownloadHandlerFile(pkgPath) { removeFileOnAbort = true };
                    request.downloadHandler = dh;
#else
                    request.downloadHandler = new DownloadHandlerFile(pkgPath);
#endif
                    op = request.SendWebRequest();
                }
                catch (Exception e)
                {
                    request?.Dispose();
                    FailPackageUpdate("Download failed: " + e.Message);
                    yield break;
                }

                // Progress loop (no try/catch here)
                ulong totalBytes = pkg.size > 0 ? (ulong)pkg.size : 0UL;
                ulong lastBytes = 0UL;
                double lastTime = EditorApplication.timeSinceStartup;

                while (!op.isDone)
                {
                    // Optional cancel (modal progress bar) - prevents "stuck forever" feeling.
                    string sizeLabel = totalBytes > 0
                        ? $"{(request.downloadedBytes / (1024f * 1024f)):0.0}/{(totalBytes / (1024f * 1024f)):0.0} MB"
                        : $"{(request.downloadedBytes / (1024f * 1024f)):0.0} MB";

                    double now = EditorApplication.timeSinceStartup;
                    double dt = Math.Max(0.001, now - lastTime);
                    double speedMBs = ((request.downloadedBytes - lastBytes) / (1024.0 * 1024.0)) / dt;
                    lastBytes = request.downloadedBytes;
                    lastTime = now;

                    packageStatus = $"Downloading... {sizeLabel} ({speedMBs:0.0} MB/s)";
                    packageDownloadProgress = Mathf.Clamp01(request.downloadProgress * 0.9f);
                    Repaint();

                    bool cancel = EditorUtility.DisplayCancelableProgressBar(
                        "Kawaii Studio Update",
                        packageStatus,
                        packageDownloadProgress
                    );
                    if (cancel)
                    {
                        request.Abort();
                        request.Dispose();
                        EditorUtility.ClearProgressBar();
                        FailPackageUpdate("Download cancelled.");
                        yield break;
                    }

                    yield return null;
                }

                EditorUtility.ClearProgressBar();

                // Success
                if (request.result == UnityWebRequest.Result.Success)
                {
                    request.Dispose();
                    // Manual install flow: do NOT import automatically.
                    packageStatus = "Downloaded";
                    packageDownloadProgress = 1f;
                    isInstallingPackage = false;
                    Repaint();

                    AddLog("✅ Package downloaded: " + pkgPath);
                    
                    // Pass version for hooks to update VERSION.md after manual import
                    string version = latestRelease?.tag_name ?? "";
                    ShowManualInstallDialog(pkgPath, version);
                    yield break;
                }

                // Handle redirects manually if Unity didn't follow (common with GitHub asset URLs on older Unity)
                long code = request.responseCode;
                string location = request.GetResponseHeader("Location");
                string err = request.error;
                request.Dispose();

                bool isRedirect = code == 301 || code == 302 || code == 303 || code == 307 || code == 308;
                if (isRedirect && !string.IsNullOrEmpty(location) && redirect < maxRedirects)
                {
                    url = location;
                    continue;
                }

                FailPackageUpdate($"Download failed (HTTP {code}): {err}");
                yield break;
            }
        }

        private void FailPackageUpdate(string message)
        {
            AddLog("❌ Package update failed: " + message);
            packageStatus = "Failed";
            isInstallingPackage = false;
            packageDownloadProgress = 0f;
            EditorUtility.DisplayDialog("Update failed", message, "OK");
            Repaint();
        }

        // Note: backup/move-based update removed because Windows file locks make it unreliable.

        void DrawRGBSection(string title, System.Action content, float offsetMultiplier = 1f)
        {
            Color sectionColor = GetRGBWaveColor(offsetMultiplier);
            
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            
            // Title bar with RGB accent
            EditorGUILayout.BeginHorizontal();
            
            // RGB accent bar
            Rect barRect = GUILayoutUtility.GetRect(4, 20, GUILayout.Width(4));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(barRect, sectionColor);
            
            GUILayout.Space(10);
            
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal = { textColor = sectionColor }
            };
            GUILayout.Label(title, titleStyle);
            
            EditorGUILayout.EndHorizontal();
            
            GUILayout.Space(8);
            
            // Content
            content?.Invoke();
            
            GUILayout.Space(5);
            EditorGUILayout.EndVertical();
            
            GUILayout.Space(8);
        }

        void DrawCommunitySection()
        {
            DrawRGBSection($"🌐 {T("community")}", () => {
                GUILayout.BeginHorizontal();
                
                Color oldBg = GUI.backgroundColor;
                
                GUIStyle socialBtn = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 45,
                    padding = new RectOffset(12, 12, 10, 10)
                };
                
                // Discord
                GUI.backgroundColor = new Color(0.345f, 0.396f, 0.949f);
                if (GUILayout.Button($"💬 {T("discord_join")}", socialBtn))
                {
                    Application.OpenURL(DISCORD_URL);
                    AddLog($"🔗 Discord opened");
                }
                
                GUILayout.Space(8);
                
                // Telegram
                GUI.backgroundColor = new Color(0.133f, 0.588f, 0.918f);
                if (GUILayout.Button($"✈️ {T("telegram_join")}", socialBtn))
                {
                    Application.OpenURL(TELEGRAM_URL);
                    AddLog($"🔗 Telegram opened");
                }
                
                GUILayout.Space(8);
                
                // GitHub
                GUI.backgroundColor = new Color(0.2f, 0.2f, 0.24f);
                if (GUILayout.Button($"⭐ {T("github_view")}", socialBtn))
                {
                    Application.OpenURL(GITHUB_URL);
                    AddLog($"🔗 GitHub opened");
                }
                
                GUI.backgroundColor = oldBg;
                
                GUILayout.EndHorizontal();
                
                GUILayout.Space(8);
                
                GUIStyle communityMsg = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    fontSize = 10,
                    normal = { textColor = new Color(0.6f, 0.6f, 0.65f) },
                    alignment = TextAnchor.MiddleCenter
                };
                GUILayout.Label("Join our community for support and updates", communityMsg);
            }, 3.5f);
        }

        void DrawSettingsSection()
        {
            DrawRGBSection($"⚙️ {T("settings")}", () => {
                // Language selector avec style
                GUILayout.BeginHorizontal();
                
                GUIStyle langLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    normal = { textColor = new Color(0.9f, 0.9f, 0.95f) }
                };
                GUILayout.Label($"🌐 {T("language")}", langLabelStyle, GUILayout.Width(120));
                
                EditorGUI.BeginChangeCheck();
                GUIStyle popupStyle = new GUIStyle(EditorStyles.popup)
                {
                    fontSize = 11,
                    fixedHeight = 28
                };
                selectedLanguage = EditorGUILayout.Popup(selectedLanguage, availableLanguages, popupStyle);
                if (EditorGUI.EndChangeCheck())
                {
                    SavePreferences();
                    LoadTranslationsFromJSON();
                    AddLog($"🌐 Language changed to: {availableLanguages[selectedLanguage]}");
                    Repaint();
                }
                
                GUILayout.Space(10);
                
                // Reload button compact
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
                GUIStyle reloadBtn = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    fixedHeight = 28,
                    fixedWidth = 140
                };
                if (GUILayout.Button("🔄 Reload", reloadBtn))
                {
                    LoadTranslationsFromJSON();
                    Repaint();
                }
                GUI.backgroundColor = oldBg;
                
                GUILayout.EndHorizontal();
                
                GUILayout.Space(12);
                
                // Debug toggle avec style
                showDebugInfo = KawaiiStudioGUI.DrawToggle("🔍 Debug Mode", showDebugInfo);
            }, 4.0f);
        }

        void DrawLogSection()
        {
            DrawRGBSection($"📋 {T("log")}", () => {
                GUIStyle logBox = new GUIStyle(GUI.skin.box)
                {
                    padding = new RectOffset(10, 10, 8, 8),
                    normal = { background = Texture2D.whiteTexture } // Use pure color via GUI.backgroundColor
                };
                
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.1f, 0.1f, 0.12f);
                
                GUILayout.BeginVertical(logBox);
                GUI.backgroundColor = oldBg;
                
                logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(100));
                
                GUIStyle logStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 10,
                    wordWrap = true,
                    richText = true,
                    normal = { textColor = new Color(0.7f, 0.85f, 0.7f) }
                };
                
                // Coloriser les logs
                string coloredLog = logOutput
                    .Replace("✅", "<color=#4ade80>✅</color>")
                    .Replace("❌", "<color=#f87171>❌</color>")
                    .Replace("⚠️", "<color=#fbbf24>⚠️</color>")
                    .Replace("🔍", "<color=#60a5fa>🔍</color>")
                    .Replace("🔄", "<color=#a78bfa>🔄</color>")
                    .Replace("📥", "<color=#34d399>📥</color>")
                    .Replace("🔗", "<color=#60a5fa>🔗</color>");
                
                GUILayout.Label(coloredLog, logStyle);
                
                EditorGUILayout.EndScrollView();
                
                GUILayout.EndVertical();
            }, 4.5f);
        }

        void AddLog(string message)
        {
            logOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        void CheckForUpdates()
        {
            // GitHub release check (not per-script)
            if (isCheckingUpdates || isLoadingRelease) return;
            isCheckingUpdates = true;
            AddLog("🔍 " + T("checking"));
            EditorApplication.delayCall += () => { StartCoroutine(CheckLatestReleaseForUpdates()); };
        }

        IEnumerator CheckLatestReleaseForUpdates()
        {
            // Reuse the same endpoint as LoadLatestRelease, but keep the "checking" UI state.
            isLoadingRelease = true;
            LoadLocalVersion(true);
            UnityWebRequest request = UnityWebRequest.Get(GITHUB_RELEASES_API);
            request.SetRequestHeader("User-Agent", "KawaiiStudioManager/" + GetLocalVersion());
            var operation = request.SendWebRequest();
            while (!operation.isDone) yield return null;

            if (request.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    latestRelease = JsonUtility.FromJson<GitHubRelease>(request.downloadHandler.text);
                    releaseUpdateAvailable = latestRelease != null && IsNewerVersion(GetLocalVersion(), latestRelease.tag_name);
                    if (releaseUpdateAvailable) AddLog($"🔄 Update available: v{GetLocalVersion()} → {latestRelease.tag_name}");
                    else AddLog($"✅ Up to date (v{GetLocalVersion()})");

                    // Popup result + optional install
                    EditorApplication.delayCall += () =>
                    {
                        ShowReleaseUpdatePopup();
                    };
                }
                catch (Exception e)
                {
                    AddLog($"⚠️ Failed to parse release info: {e.Message}");
                }
            }
            else
            {
                AddLog($"❌ Failed to check release: {request.error}");
            }

            request.Dispose();
            isLoadingRelease = false;
            isCheckingUpdates = false;
            Repaint();
        }

        private void ShowReleaseUpdatePopup()
        {
            // Only show popup when we have a parsed release
            if (latestRelease == null || string.IsNullOrEmpty(latestRelease.tag_name)) return;

            if (!releaseUpdateAvailable)
            {
                EditorUtility.DisplayDialog(
                    "No update available",
                    $"You already have the latest update.\n\nCurrent: v{GetLocalVersion()}\nLatest: {latestRelease.tag_name}",
                    "OK");
                return;
            }

            bool install = EditorUtility.DisplayDialog(
                "Update available",
                $"An update is available.\n\nCurrent: v{GetLocalVersion()}\nLatest: {latestRelease.tag_name}\n\nDo you want to download and install it now?",
                "Yes, update",
                "No");

            if (install)
            {
                StartPackageUpdateFromLatestRelease();
            }
        }

        private void LoadLocalVersion(bool force = false)
        {
            if (!force && !string.IsNullOrEmpty(cachedLocalVersion)) return;

            try
            {
                // Read from project root relative path
                if (File.Exists(VERSION_FILE_PATH))
                {
                    string raw = File.ReadAllText(VERSION_FILE_PATH);
                    cachedLocalVersion = NormalizeVersion(raw);
                    if (string.IsNullOrEmpty(cachedLocalVersion)) cachedLocalVersion = DEFAULT_LOCAL_VERSION;
                    return;
                }

                // Fallback using absolute path
                string abs = Path.Combine(Directory.GetCurrentDirectory(), VERSION_FILE_PATH);
                if (File.Exists(abs))
                {
                    string raw = File.ReadAllText(abs);
                    cachedLocalVersion = NormalizeVersion(raw);
                    if (string.IsNullOrEmpty(cachedLocalVersion)) cachedLocalVersion = DEFAULT_LOCAL_VERSION;
                    return;
                }
            }
            catch { }

            cachedLocalVersion = DEFAULT_LOCAL_VERSION;
        }

        private string GetLocalVersion()
        {
            LoadLocalVersion(false);
            return string.IsNullOrEmpty(cachedLocalVersion) ? DEFAULT_LOCAL_VERSION : cachedLocalVersion;
        }

        private static string NormalizeVersion(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            string s = input.Trim();
            // Try to extract the first x.y(.z...) pattern from any string (e.g. "v1.4", "KSUnityToolsV1.4")
            try
            {
                var m = System.Text.RegularExpressions.Regex.Match(s, @"\d+(\.\d+)+");
                if (m.Success) return m.Value;
            }
            catch { }
            // Fallback: trim leading v/V and whitespace
            s = s.Trim().TrimStart('v', 'V');
            return s;
        }

        void StartCoroutine(IEnumerator routine)
        {
            EditorApplication.update += UpdateCoroutine;
            void UpdateCoroutine()
            {
                try
                {
                    if (!routine.MoveNext())
                        EditorApplication.update -= UpdateCoroutine;
                }
                catch (Exception e)
                {
                    EditorApplication.update -= UpdateCoroutine;
                    Debug.LogException(e);
                    FailPackageUpdate("Update coroutine crashed: " + e.Message);
                }
            }
        }
    }

    // Hook qui détecte quand un import Unity est terminé et met à jour VERSION.md
    [InitializeOnLoad]
    internal static class KawaiiStudioImportHook
    {
        private const string VERSION_FILE = "Assets/Kawaii Studio/VERSION.md";
        
        static KawaiiStudioImportHook()
        {
            AssetDatabase.importPackageCompleted += OnImportCompleted;
        }

        private static void OnImportCompleted(string packageName)
        {
            // Vérifie si on a une version en attente (= update manuel en cours)
            string pkgPath = EditorPrefs.GetString("KawaiiStudio.PendingPackagePath", "");
            string version = EditorPrefs.GetString("KawaiiStudio.PendingVersion", "");

            if (string.IsNullOrEmpty(version)) return; // Pas d'update en cours

            try
            {
                // Normalise la version (enlève "v" prefix si présent)
                string normalized = version.Trim().TrimStart('v', 'V');
                
                // Met à jour VERSION.md
                if (!string.IsNullOrEmpty(normalized))
                {
                    File.WriteAllText(VERSION_FILE, normalized);
                    Debug.Log($"[Kawaii Studio] VERSION.md updated to {normalized}");
                }

                // Supprime le .unitypackage téléchargé (best effort)
                if (!string.IsNullOrEmpty(pkgPath) && File.Exists(pkgPath))
                {
                    try { File.Delete(pkgPath); } catch { }
                }

                // Nettoie les prefs
                EditorPrefs.DeleteKey("KawaiiStudio.PendingPackagePath");
                EditorPrefs.DeleteKey("KawaiiStudio.PendingVersion");

                // Refresh pour que Unity voit le nouveau VERSION.md
                AssetDatabase.Refresh();

                // Affiche un message de succès
                EditorApplication.delayCall += () =>
                {
                    EditorUtility.DisplayDialog(
                        "✅ Update Successfully Imported",
                        $"═══════════════════════════════\n" +
                        $"Kawaii Studio has been updated!\n" +
                        $"═══════════════════════════════\n\n" +
                        $"📦 New Version: v{normalized}\n\n" +
                        $"✅ VERSION.md updated\n" +
                        $"✅ Package file cleaned up\n" +
                        $"✅ Assets refreshed\n\n" +
                        $"You can now use the updated tools!",
                        "Perfect! 🎉"
                    );
                };
            }
            catch (Exception e)
            {
                Debug.LogError($"[Kawaii Studio] Failed to finalize update: {e.Message}");
            }
        }
    }
}
