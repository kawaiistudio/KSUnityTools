// Kawaii Studio Manager v2.2 - Version corrigée
// Amélioration de la comparaison de versions et de la détection des mises à jour

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
        public string currentVersion;
        public string latestVersion;
        public bool updateAvailable;
        public bool isInstalled;
        public DateTime lastChecked;
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
        // Version
        private const string VERSION = "2.2";
        
        // Configuration
        private const string GITHUB_BASE_URL = "https://raw.githubusercontent.com/kawaiistudio/KSUnityTools/refs/heads/main/";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string TELEGRAM_URL = "https://t.me/kawaiistudio";
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        private const string LOGO_URL = "https://github.com/kawaiistudio/KSUnityTools/blob/main/logo_v2.png?raw=true";
        private const string LANGUAGES_FOLDER = "Assets/Kawaii Studio/Languages";
        
        // Tools à gérer
        private List<ToolInfo> tools = new List<ToolInfo>
        {
            new ToolInfo
            {
                name = "Video Animator",
                fileName = "Kawaii Studio Video Animator.cs",
                githubRawUrl = GITHUB_BASE_URL + "Kawaii%20Studio%20Video%20Animator.cs",
                localPath = "Assets/Kawaii Studio/Editor/Kawaii Studio Video Animator.cs"
            },
            new ToolInfo
            {
                name = "GLB to FBX Converter",
                fileName = "Kawaii Studio GLB to FBX.cs",
                githubRawUrl = GITHUB_BASE_URL + "Kawaii%20Studio%20GLB%20to%20FBX.cs",
                localPath = "Assets/Kawaii Studio/Editor/Kawaii Studio GLB to FBX.cs"
            },
            new ToolInfo
            {
                name = "Prefab Optimizer",
                fileName = "Kawaii Studio Prefab Optimizer.cs",
                githubRawUrl = GITHUB_BASE_URL + "Kawaii%20Studio%20Prefab%20Optimizer.cs",
                localPath = "Assets/Kawaii Studio/Editor/Kawaii Studio Prefab Optimizer.cs"
            }
        };
        
        // Langues disponibles
        private string[] availableLanguages = { "English", "Русский", "中文", "日本語", "Español", "Français", "Deutsch" };
        private string[] languageCodes = { "en", "ru", "zh", "ja", "es", "fr", "de" };
        private int selectedLanguage = 0;
        private Dictionary<string, Dictionary<string, string>> translations = new Dictionary<string, Dictionary<string, string>>();
        
        // UI State
        private Vector2 scrollPosition;
        private Vector2 logScrollPosition;
        private string logOutput = "";
        private bool isCheckingUpdates = false;
        private bool showTools = true;
        private bool showLinks = true;
        private bool showSettings = true;
        private bool showDebugInfo = false;
        private Texture2D logoTexture;
        
        // UI Styles
        private GUIStyle headerStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle buttonStyle;
        private GUIStyle logStyle;
        private GUIStyle linkButtonStyle;
        private Texture2D purpleTexture;
        private Texture2D redTexture;
        private Texture2D blackTexture;
        private Texture2D greenTexture;
        private Texture2D discordTexture;
        private Texture2D telegramTexture;
        private bool stylesInitialized = false;

        [MenuItem("Kawaii Studio/Studio Manager", priority = 0)]
        public static void ShowWindow()
        {
            KawaiiStudioManager window = GetWindow<KawaiiStudioManager>("Kawaii Studio Manager");
            window.minSize = new Vector2(700, 650);
            window.Show();
        }

        private void OnEnable()
        {
            LoadPreferences();
            LoadTranslationsFromJSON();
            CheckInstalledTools();
            DownloadLogo();
            EditorApplication.update += AutoCheckUpdates;
        }

        private void OnDisable()
        {
            SavePreferences();
            EditorApplication.update -= AutoCheckUpdates;
        }

        private void LoadTranslationsFromJSON()
        {
            translations.Clear();
            
            if (!Directory.Exists(LANGUAGES_FOLDER))
            {
                AddLog($"⚠️ Languages folder not found: {LANGUAGES_FOLDER}");
                LoadFallbackTranslations();
                return;
            }

            foreach (string langCode in languageCodes)
            {
                string jsonPath = Path.Combine(LANGUAGES_FOLDER, $"{langCode}.json");
                
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
                                {
                                    langDict[entry.key] = entry.value;
                                }
                            }
                            
                            translations[langCode] = langDict;
                            AddLog($"✅ Loaded {langDict.Count} translations for {langCode}");
                        }
                    }
                    catch (Exception e)
                    {
                        AddLog($"❌ Error loading {langCode}.json: {e.Message}");
                    }
                }
            }
            
            if (translations.Count == 0)
            {
                LoadFallbackTranslations();
            }
        }

        private void LoadFallbackTranslations()
        {
            translations["en"] = new Dictionary<string, string>
            {
                { "title", "KAWAII STUDIO MANAGER" },
                { "subtitle", "Manage all your Kawaii Studio tools" },
                { "tools", "INSTALLED TOOLS" },
                { "check_updates", "CHECK FOR UPDATES" },
                { "update_all", "UPDATE ALL" },
                { "community", "COMMUNITY LINKS" },
                { "settings", "SETTINGS" },
                { "language", "Language" },
                { "version", "Version" },
                { "status", "Status" },
                { "installed", "Installed" },
                { "not_installed", "Not Installed" },
                { "update_available", "Update Available" },
                { "up_to_date", "Up to Date" },
                { "install", "INSTALL" },
                { "update", "UPDATE" },
                { "checking", "Checking..." },
                { "log", "LOG OUTPUT" },
                { "discord_join", "Join Discord" },
                { "telegram_join", "Join Telegram" },
                { "github_view", "View on GitHub" },
                { "language_change_title", "Update Scripts Language?" },
                { "language_change_message", "Do you want to update all installed scripts to use this language?" },
                { "yes", "Yes" },
                { "no", "No" },
                { "updating_scripts", "Updating scripts language..." },
                { "scripts_updated", "Scripts updated successfully!" }
            };
        }

        private void DownloadLogo()
        {
            if (logoTexture != null) return;
            
            UnityWebRequest request = UnityWebRequestTexture.GetTexture(LOGO_URL);
            var operation = request.SendWebRequest();
            
            operation.completed += (op) =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    logoTexture = DownloadHandlerTexture.GetContent(request);
                    Repaint();
                }
                request.Dispose();
            };
        }

        private void AutoCheckUpdates()
        {
            if (EditorApplication.timeSinceStartup % 3600 < 1 && !isCheckingUpdates)
            {
                CheckForUpdates();
            }
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            purpleTexture = MakeTex(2, 2, new Color(0.486f, 0.227f, 0.929f, 1f));
            redTexture = MakeTex(2, 2, new Color(1f, 0.278f, 0.341f, 1f));
            blackTexture = MakeTex(2, 2, new Color(0.039f, 0.039f, 0.059f, 1f));
            greenTexture = MakeTex(2, 2, new Color(0f, 1f, 0.255f, 1f));
            discordTexture = MakeTex(2, 2, new Color(0.345f, 0.396f, 0.949f, 1f));
            telegramTexture = MakeTex(2, 2, new Color(0.133f, 0.588f, 0.918f, 1f));

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };

            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { background = redTexture, textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(1f, 0.42f, 0.506f, 1f)), textColor = Color.white },
                active = { background = redTexture, textColor = Color.white },
                padding = new RectOffset(15, 15, 8, 8),
                fixedHeight = 35
            };

            linkButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { background = discordTexture, textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(0.4f, 0.45f, 1f, 1f)), textColor = Color.white },
                active = { background = discordTexture, textColor = Color.white },
                padding = new RectOffset(12, 12, 6, 6),
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

        private string T(string key)
        {
            string langCode = languageCodes[selectedLanguage];
            
            if (translations.ContainsKey(langCode) && translations[langCode].ContainsKey(key))
            {
                return translations[langCode][key];
            }
            
            if (translations.ContainsKey("en") && translations["en"].ContainsKey(key))
            {
                return translations["en"][key];
            }
            
            return key;
        }

        private void LoadPreferences()
        {
            string savedLang = EditorPrefs.GetString(PREFS_LANGUAGE, "en");
            selectedLanguage = Array.IndexOf(languageCodes, savedLang);
            if (selectedLanguage < 0) selectedLanguage = 0;
        }

        private void SavePreferences()
        {
            EditorPrefs.SetString(PREFS_LANGUAGE, languageCodes[selectedLanguage]);
        }

        private void CheckInstalledTools()
        {
            foreach (var tool in tools)
            {
                tool.isInstalled = File.Exists(tool.localPath);
                if (tool.isInstalled)
                {
                    tool.currentVersion = ExtractVersionFromFile(tool.localPath);
                }
            }
        }

        private string ExtractVersionFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return "Unknown";
                }

                string content = File.ReadAllText(filePath);
                return ExtractVersionFromContent(content);
            }
            catch (Exception e)
            {
                AddLog($"⚠️ Error reading version from {Path.GetFileName(filePath)}: {e.Message}");
                return "Unknown";
            }
        }

        private string ExtractVersionFromContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            try
            {
                var patterns = new[]
                {
                    @"private\s+const\s+string\s+VERSION\s*=\s*""([^""]+)""",
                    @"public\s+const\s+string\s+VERSION\s*=\s*""([^""]+)""",
                    @"const\s+string\s+VERSION\s*=\s*""([^""]+)""",
                    @"private\s+static\s+readonly\s+string\s+VERSION\s*=\s*""([^""]+)""",
                    @"public\s+static\s+readonly\s+string\s+VERSION\s*=\s*""([^""]+)""",
                    @"VERSION\s*=\s*""([^""]+)"""
                };

                foreach (var pattern in patterns)
                {
                    var match = System.Text.RegularExpressions.Regex.Match(content, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        string version = match.Groups[1].Value.Trim();
                        if (IsValidVersion(version))
                        {
                            return version;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AddLog($"⚠️ Error extracting version: {e.Message}");
            }
            
            return null;
        }

        private bool IsValidVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return false;

            var versionPattern = @"^v?\d+(\.\d+)*$";
            return System.Text.RegularExpressions.Regex.IsMatch(version, versionPattern);
        }

        private bool IsNewerVersion(string currentVersion, string latestVersion)
        {
            if (string.IsNullOrEmpty(currentVersion) || string.IsNullOrEmpty(latestVersion))
            {
                return false;
            }

            currentVersion = currentVersion.TrimStart('v', 'V');
            latestVersion = latestVersion.TrimStart('v', 'V');

            try
            {
                var currentParts = currentVersion.Split('.').Select(p =>
                {
                    int.TryParse(p, out int val);
                    return val;
                }).ToArray();

                var latestParts = latestVersion.Split('.').Select(p =>
                {
                    int.TryParse(p, out int val);
                    return val;
                }).ToArray();

                int maxLength = Math.Max(currentParts.Length, latestParts.Length);
                
                for (int i = 0; i < maxLength; i++)
                {
                    int current = i < currentParts.Length ? currentParts[i] : 0;
                    int latest = i < latestParts.Length ? latestParts[i] : 0;

                    if (latest > current)
                    {
                        return true;
                    }
                    else if (latest < current)
                    {
                        return false;
                    }
                }

                return false;
            }
            catch (Exception e)
            {
                AddLog($"⚠️ Error comparing versions {currentVersion} vs {latestVersion}: {e.Message}");
                return currentVersion != latestVersion;
            }
        }

        private string CalculateFileHash(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    using (var stream = File.OpenRead(filePath))
                    {
                        byte[] hash = md5.ComputeHash(stream);
                        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

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
            
            DrawToolsSection();
            GUILayout.Space(10);
            
            DrawCommunitySection();
            GUILayout.Space(10);
            
            DrawSettingsSection();
            GUILayout.Space(10);
            
            DrawLogSection();
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
            }
        }

        private void DrawHeader()
        {
            GUILayout.Space(logoTexture != null ? 50 : 0);
            GUILayout.Label($"⚡ {T("title")} v{VERSION} ⚡", headerStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.278f, 0.341f, 1f) }
            };
            GUILayout.Label(T("subtitle"), subtitleStyle);
            
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 2), new Color(0.486f, 0.227f, 0.929f, 1f));
        }

        private void DrawToolsSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.BeginHorizontal();
            showTools = EditorGUILayout.Foldout(showTools, $"🔧 {T("tools")}", true, subHeaderStyle);
            GUILayout.FlexibleSpace();
            
            GUI.enabled = !isCheckingUpdates;
            if (GUILayout.Button(T("check_updates"), GUILayout.Width(180), GUILayout.Height(25)))
            {
                CheckForUpdates();
            }
            
            int updatesAvailable = tools.Count(t => t.updateAvailable);
            GUI.enabled = updatesAvailable > 0 && !isCheckingUpdates;
            if (GUILayout.Button($"{T("update_all")} ({updatesAvailable})", GUILayout.Width(150), GUILayout.Height(25)))
            {
                UpdateAllTools();
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
            
            if (showTools)
            {
                GUILayout.Space(10);
                
                foreach (var tool in tools)
                {
                    DrawToolItem(tool);
                    GUILayout.Space(5);
                }
            }
            
            GUILayout.EndVertical();
        }

        private void DrawToolItem(ToolInfo tool)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter
            };
            
            string statusIcon = tool.isInstalled ? (tool.updateAvailable ? "🔄" : "✅") : "⬇️";
            GUILayout.Label(statusIcon, statusStyle, GUILayout.Width(30));
            
            GUILayout.BeginVertical();
            
            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            GUILayout.Label(tool.name, nameStyle);
            
            GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f, 1f) }
            };
            
            string statusText = tool.isInstalled ? T("installed") : T("not_installed");
            if (tool.isInstalled && tool.updateAvailable)
            {
                statusText = T("update_available");
            }
            else if (tool.isInstalled && !tool.updateAvailable && !string.IsNullOrEmpty(tool.latestVersion))
            {
                statusText = T("up_to_date");
            }
            
            string versionInfo = $"{T("version")}: {tool.currentVersion}";
            if (!string.IsNullOrEmpty(tool.latestVersion) && tool.latestVersion != tool.currentVersion)
            {
                versionInfo += $" → {tool.latestVersion}";
            }
            
            GUILayout.Label($"{T("status")}: {statusText} | {versionInfo}", infoStyle);
            
            if (showDebugInfo && tool.lastChecked != DateTime.MinValue)
            {
                GUILayout.Label($"Last checked: {tool.lastChecked:yyyy-MM-dd HH:mm:ss}", infoStyle);
            }
            
            GUILayout.EndVertical();
            
            GUILayout.FlexibleSpace();
            
            GUI.enabled = !isCheckingUpdates;
            if (!tool.isInstalled)
            {
                if (GUILayout.Button(T("install"), buttonStyle, GUILayout.Width(120)))
                {
                    InstallTool(tool);
                }
            }
            else if (tool.updateAvailable)
            {
                if (GUILayout.Button(T("update"), buttonStyle, GUILayout.Width(120)))
                {
                    UpdateTool(tool);
                }
            }
            GUI.enabled = true;
            
            GUILayout.EndHorizontal();
        }

        private void DrawCommunitySection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            showLinks = EditorGUILayout.Foldout(showLinks, $"🌐 {T("community")}", true, subHeaderStyle);
            
            if (showLinks)
            {
                GUILayout.Space(10);
                GUILayout.BeginHorizontal();
                
                GUIStyle discordStyle = new GUIStyle(linkButtonStyle)
                {
                    normal = { background = discordTexture, textColor = Color.white }
                };
                if (GUILayout.Button($"💬 {T("discord_join")}", discordStyle, GUILayout.Height(40)))
                {
                    Application.OpenURL(DISCORD_URL);
                    AddLog($"🔗 Opening Discord: {DISCORD_URL}");
                }
                
                GUIStyle telegramStyle = new GUIStyle(linkButtonStyle)
                {
                    normal = { background = telegramTexture, textColor = Color.white }
                };
                if (GUILayout.Button($"✈️ {T("telegram_join")}", telegramStyle, GUILayout.Height(40)))
                {
                    Application.OpenURL(TELEGRAM_URL);
                    AddLog($"🔗 Opening Telegram: {TELEGRAM_URL}");
                }
                
                if (GUILayout.Button($"🔙 {T("github_view")}", linkButtonStyle, GUILayout.Height(40)))
                {
                    Application.OpenURL(GITHUB_URL);
                    AddLog($"🔗 Opening GitHub: {GITHUB_URL}");
                }
                
                GUILayout.EndHorizontal();
            }
            
            GUILayout.EndVertical();
        }

        private void DrawSettingsSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            showSettings = EditorGUILayout.Foldout(showSettings, $"⚙️ {T("settings")}", true, subHeaderStyle);
            
            if (showSettings)
            {
                GUILayout.Space(10);
                
                GUILayout.BeginHorizontal();
                GUILayout.Label(T("language"), GUILayout.Width(100));
                
                EditorGUI.BeginChangeCheck();
                selectedLanguage = EditorGUILayout.Popup(selectedLanguage, availableLanguages);
                if (EditorGUI.EndChangeCheck())
                {
                    SavePreferences();
                    AddLog($"🌐 Language changed to: {availableLanguages[selectedLanguage]}");
                    
                    if (EditorUtility.DisplayDialog(
                        T("language_change_title"),
                        T("language_change_message"),
                        T("yes"),
                        T("no")))
                    {
                        UpdateAllScriptsLanguage();
                    }
                    
                    Repaint();
                }
                
                GUILayout.EndHorizontal();
                
                GUILayout.Space(10);
                
                if (GUILayout.Button("🔄 Reload Translations from JSON", GUILayout.Height(30)))
                {
                    LoadTranslationsFromJSON();
                    Repaint();
                }
                
                GUILayout.Space(5);
                
                showDebugInfo = EditorGUILayout.Toggle("Show Debug Info", showDebugInfo);
            }
            
            GUILayout.EndVertical();
        }

        private void DrawLogSection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.Label($"📋 {T("log")}", subHeaderStyle);
            GUILayout.Space(5);
            
            logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(120));
            GUILayout.TextArea(logOutput, logStyle, GUILayout.ExpandHeight(true));
            GUILayout.EndScrollView();
            
            GUILayout.EndVertical();
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

        private void AddLog(string message)
        {
            logOutput += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private void CheckForUpdates()
        {
            if (isCheckingUpdates) return;
            
            isCheckingUpdates = true;
            AddLog("🔍 " + T("checking"));
            
            EditorApplication.delayCall += () =>
            {
                StartCoroutine(CheckAllToolsForUpdates());
            };
        }

        private IEnumerator CheckAllToolsForUpdates()
        {
            foreach (var tool in tools)
            {
                yield return CheckToolForUpdate(tool);
            }
            
            isCheckingUpdates = false;
            
            int updates = tools.Count(t => t.updateAvailable);
            if (updates > 0)
            {
                AddLog($"✅ Found {updates} update(s) available!");
            }
            else
            {
                AddLog("✅ All tools are up to date!");
            }
            
            Repaint();
        }

        private IEnumerator CheckToolForUpdate(ToolInfo tool)
        {
            Ad