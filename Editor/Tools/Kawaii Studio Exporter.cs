using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    public class KawaiiExporterWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;

        private UnityEngine.Object exportTarget;
        private bool includeDependencies = true;
        private bool recurseFolders = true;
        private bool openFolderAfterExport = true;

        private string exportFileName = "KawaiiExport.unitypackage";
        private string exportDirectory = "";

        private string status = "";
        private MessageType statusType = MessageType.Info;

        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;

        // Localization
        private const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        // Resolved at runtime: the old hardcoded literal broke any install under Packages/.
        private static string LANGUAGES_FOLDER => KawaiiStudioPaths.Languages;
        private string[] languageCodes = { "en", "ru", "zh", "ja", "es", "fr", "de" };
        private int selectedLanguage = 0;
        private Dictionary<string, Dictionary<string, string>> translations = new Dictionary<string, Dictionary<string, string>>();

        [MenuItem("Kawaii Studio/Universal Tools/Kawaii Exporter")]
        public static void ShowWindow()
        {
            var window = GetWindow<KawaiiExporterWindow>("Kawaii Exporter");
            window.minSize = new Vector2(650, 650);
            window.Show();
        }

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
            LoadPreferences();
            LoadTranslationsFromJSON();

            if (logoTexture == null || bannerTexture == null)
            {
                logoTexture = KawaiiStudioBranding.Logo;
                bannerTexture = KawaiiStudioBranding.Banner;
            }

            // Default export directory: project root
            try
            {
                exportDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
            catch
            {
                exportDirectory = "";
            }
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            KawaiiStudioGUI.DrawWindowBackground(position);

            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });

            KawaiiStudioGUI.DrawBanner(
                T("exporter_title"),
                T("exporter_subtitle"),
                VERSION,
                logoTexture,
                bannerTexture
            );

            GUILayout.Space(10);

            KawaiiStudioGUI.DrawSection(T("about_title"), () =>
            {
                EditorGUILayout.LabelField(T("exporter_about_1"), KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField(T("exporter_about_2"), KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField(T("exporter_about_3"), KawaiiStudioGUI.InfoLabelStyle);
            });

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection(T("target_title"), () =>
            {
                EditorGUILayout.LabelField(T("target_label"), KawaiiStudioGUI.LabelStyle);

                using (new EditorGUILayout.HorizontalScope())
                {
                    exportTarget = EditorGUILayout.ObjectField(exportTarget, typeof(UnityEngine.Object), true, GUILayout.Height(28));

                    if (GUILayout.Button(T("use_selection"), GUILayout.Width(120), GUILayout.Height(28)))
                    {
                        exportTarget = Selection.activeObject;
                    }
                }

                string assetPath = ResolveTargetAssetPath(exportTarget);
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(T("resolved_path"), assetPath ?? "");
                }

                if (string.IsNullOrEmpty(assetPath))
                {
                    EditorGUILayout.HelpBox(T("exporter_help_select"), MessageType.Warning);
                }
                else if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) && assetPath != "Assets")
                {
                    EditorGUILayout.HelpBox(T("exporter_help_assets"), MessageType.Error);
                }
            });

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection(T("options_title"), () =>
            {
                includeDependencies = KawaiiStudioGUI.DrawToggle(T("opt_dependencies"), includeDependencies);
                recurseFolders = KawaiiStudioGUI.DrawToggle(T("opt_recurse"), recurseFolders);
                openFolderAfterExport = KawaiiStudioGUI.DrawToggle(T("opt_open_folder"), openFolderAfterExport);
            });

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection(T("output_title"), () =>
            {
                // Standard export settings
                EditorGUILayout.LabelField(T("file_name"), KawaiiStudioGUI.LabelStyle);
                exportFileName = EditorGUILayout.TextField(exportFileName);
                if (!exportFileName.EndsWith(".unitypackage", StringComparison.OrdinalIgnoreCase))
                    exportFileName += ".unitypackage";

                EditorGUILayout.LabelField(T("directory"), KawaiiStudioGUI.LabelStyle);
                using (new EditorGUILayout.HorizontalScope())
                {
                    exportDirectory = EditorGUILayout.TextField(exportDirectory);
                    if (GUILayout.Button(T("browse"), GUILayout.Width(90)))
                    {
                        string picked = EditorUtility.SaveFolderPanel("Choose export folder", string.IsNullOrEmpty(exportDirectory) ? "" : exportDirectory, "");
                        if (!string.IsNullOrEmpty(picked))
                            exportDirectory = picked;
                    }
                }

                GUILayout.Space(6);
                EditorGUILayout.HelpBox(T("smart_export_info"), MessageType.Info);
            });

            GUILayout.Space(12);

            DrawExportButtons();

            if (!string.IsNullOrEmpty(status))
            {
                GUILayout.Space(8);
                EditorGUILayout.HelpBox(status, statusType);
            }

            GUILayout.Space(18);
            KawaiiStudioGUI.DrawFooter();

            EditorGUILayout.EndVertical();
        }

        private void DrawExportButtons()
        {
            string assetPath = ResolveTargetAssetPath(exportTarget);
            bool canExportSmart = !string.IsNullOrEmpty(assetPath)
                                  && (assetPath == "Assets" || assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase));
            bool canExportStandard = canExportSmart
                                     && !string.IsNullOrEmpty(exportDirectory)
                                     && Directory.Exists(exportDirectory)
                                     && !string.IsNullOrEmpty(exportFileName);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            GUIStyle bigButtonStyle = new GUIStyle(KawaiiStudioGUI.ButtonStyle)
            {
                fontSize = 13,
                fixedHeight = 45,
                fixedWidth = 260
            };

            // Standard button
            using (new EditorGUI.DisabledScope(!canExportStandard))
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = canExportStandard ? KawaiiStudioGUI.SuccessColor : Color.gray;
                if (GUILayout.Button(T("export_btn"), bigButtonStyle))
                    ExportStandard(assetPath);
                GUI.backgroundColor = oldBg;
            }

            GUILayout.Space(10);

            // Smart button
            using (new EditorGUI.DisabledScope(!canExportSmart))
            {
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = canExportSmart ? KawaiiStudioGUI.SuccessColor : Color.gray;
                if (GUILayout.Button(T("smart_export_btn"), bigButtonStyle))
                    ExportSmart(assetPath);
                GUI.backgroundColor = oldBg;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void ExportStandard(string targetAssetPath)
        {
            try
            {
                string outPath = Path.Combine(exportDirectory, exportFileName);
                var assetPaths = BuildExportAssetList(targetAssetPath, includeDependencies, recurseFolders);
                
                if (assetPaths.Count == 0)
                    throw new InvalidOperationException("Nothing to export.");

                var options = ExportPackageOptions.Interactive;
                if (includeDependencies) options |= ExportPackageOptions.IncludeDependencies;
                if (recurseFolders) options |= ExportPackageOptions.Recurse;

                statusType = MessageType.Info;
                status = string.Format(T("status_exporting"), assetPaths.Count, outPath);
                Repaint();

                AssetDatabase.ExportPackage(assetPaths.ToArray(), outPath, options);

                // ExportPackageOptions.Interactive opens Unity's own modal dialog, which
                // the user can cancel. The success status and RevealInFinder used to fire
                // regardless, pointing at a file that was never written.
                if (!File.Exists(outPath))
                {
                    statusType = MessageType.Warning;
                    status = T("status_failed") + "export cancelled, no file was written.";
                    return;
                }

                statusType = MessageType.Info;
                status = string.Format(T("status_complete"), outPath, assetPaths.Count);

                if (openFolderAfterExport)
                {
                    try { EditorUtility.RevealInFinder(outPath); } catch { }
                }
            }
            catch (Exception e)
            {
                statusType = MessageType.Error;
                status = T("status_failed") + e.Message;
            }
        }

        private void ExportSmart(string targetAssetPath)
        {
            try
            {
                // 1. Determine destination
                string targetName = Path.GetFileNameWithoutExtension(targetAssetPath);
                string baseExportFolder = "Assets/Kawaii Studio/KS Exported";
                string specificExportFolder = $"{baseExportFolder}/{targetName}_KS_exported";

                if (!Directory.Exists(baseExportFolder)) Directory.CreateDirectory(baseExportFolder);
                if (!Directory.Exists(specificExportFolder)) Directory.CreateDirectory(specificExportFolder);

                // 2. Collect files
                var assetPaths = BuildExportAssetList(targetAssetPath, includeDependencies, recurseFolders);
                if (assetPaths.Count == 0) throw new InvalidOperationException("Nothing to export.");

                int copiedCount = 0;

                // 3. Copy files
                foreach (string srcPath in assetPaths)
                {
                    // Skip scripts to avoid duplication errors (class already exists)
                    if (srcPath.EndsWith(".cs") || srcPath.EndsWith(".dll")) continue;

                    string ext = Path.GetExtension(srcPath).ToLower();
                    string subFolder = "Others";

                    if (IsTexture(ext)) subFolder = "Textures";
                    else if (IsMaterial(ext)) subFolder = "Materials";
                    else if (IsModel(ext)) subFolder = "Models";
                    else if (IsAudio(ext)) subFolder = "Audio";
                    else if (IsAnimation(ext)) subFolder = "Animations";
                    else if (IsShader(ext)) subFolder = "Shaders";
                    else if (IsPrefab(ext)) subFolder = "Prefabs";

                    string destFolder = Path.Combine(specificExportFolder, subFolder);
                    if (!Directory.Exists(destFolder)) Directory.CreateDirectory(destFolder);

                    string fileName = Path.GetFileName(srcPath);
                    string destPath = Path.Combine(destFolder, fileName);

                    // Copy asset
                    AssetDatabase.CopyAsset(srcPath, destPath);
                    copiedCount++;
                }

                AssetDatabase.Refresh();

                statusType = MessageType.Info;
                status = string.Format(T("status_complete"), specificExportFolder, copiedCount);

                if (openFolderAfterExport)
                {
                    UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(specificExportFolder);
                    EditorGUIUtility.PingObject(obj);
                }
            }
            catch (Exception e)
            {
                statusType = MessageType.Error;
                status = T("status_failed") + e.Message;
                Debug.LogException(e);
            }
        }

        private bool IsTexture(string ext) => ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".tga" || ext == ".psd";
        private bool IsMaterial(string ext) => ext == ".mat";
        private bool IsModel(string ext) => ext == ".fbx" || ext == ".obj" || ext == ".blend" || ext == ".glb";
        private bool IsAudio(string ext) => ext == ".mp3" || ext == ".wav" || ext == ".ogg";
        private bool IsAnimation(string ext) => ext == ".anim" || ext == ".controller";
        private bool IsShader(string ext) => ext == ".shader" || ext == ".shadergraph";
        private bool IsPrefab(string ext) => ext == ".prefab";

        // --- Localization Helpers ---

        private void LoadPreferences()
        {
            string savedLang = EditorPrefs.GetString(PREFS_LANGUAGE, "en");
            selectedLanguage = Array.IndexOf(languageCodes, savedLang);
            if (selectedLanguage < 0) selectedLanguage = 0;
        }

        private void LoadTranslationsFromJSON()
        {
            translations.Clear();
            string languagesPath = GetLanguagesFolderPath();
            if (!Directory.Exists(languagesPath)) return;

            foreach (string langCode in languageCodes)
            {
                string jsonPath = Path.Combine(languagesPath, $"{langCode}.json");
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        string jsonContent = File.ReadAllText(jsonPath);
                        // Shared localization model. It used to live in the Studio Manager
                        // (as TranslationFile); that file is now a pure launcher, so this
                        // reads the canonical KSTranslationFile from the Core layer.
                        KSTranslationFile translationFile = JsonUtility.FromJson<KSTranslationFile>(jsonContent);
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
                    catch { }
                }
            }
        }

        private static string GetLanguagesFolderPath()
        {
            try
            {
                string abs = Path.Combine(Application.dataPath, "Kawaii Studio", "Languages");
                if (Directory.Exists(abs)) return abs;
            }
            catch { }

            try
            {
                if (Directory.Exists(LANGUAGES_FOLDER)) return LANGUAGES_FOLDER;
                string relFromCwd = Path.Combine(Directory.GetCurrentDirectory(), LANGUAGES_FOLDER);
                if (Directory.Exists(relFromCwd)) return relFromCwd;
            }
            catch { }

            return LANGUAGES_FOLDER;
        }

        private string T(string key)
        {
            if (translations.Count == 0) LoadTranslationsFromJSON();
            string langCode = languageCodes[selectedLanguage];
            if (translations.ContainsKey(langCode) && translations[langCode].ContainsKey(key))
                return translations[langCode][key];
            if (translations.ContainsKey("en") && translations["en"].ContainsKey(key))
                return translations["en"][key];
            return key;
        }

        // --- Core Logic ---

        private static string ResolveTargetAssetPath(UnityEngine.Object obj)
        {
            if (obj == null) return null;

            string path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path))
                return path.Replace('\\', '/');

            if (obj is GameObject go)
            {
                var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(go);
                if (prefabAsset != null)
                {
                    path = AssetDatabase.GetAssetPath(prefabAsset);
                    if (!string.IsNullOrEmpty(path))
                        return path.Replace('\\', '/');
                }
            }

            return null;
        }

        private static List<string> BuildExportAssetList(string targetAssetPath, bool includeDeps, bool recurse)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrEmpty(targetAssetPath))
                return results.ToList();

            bool isFolder = AssetDatabase.IsValidFolder(targetAssetPath);
            if (isFolder)
            {
                var options = includeDeps ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                if (!recurse) options = SearchOption.TopDirectoryOnly;

                string fullFolderPath = AssetPathToFullPath(targetAssetPath);
                if (Directory.Exists(fullFolderPath))
                {
                    foreach (string file in Directory.GetFiles(fullFolderPath, "*.*", options))
                    {
                        if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                        string assetPath = FullPathToAssetPath(file);
                        if (!string.IsNullOrEmpty(assetPath) && assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                            results.Add(assetPath);
                    }
                }
            }
            else
            {
                results.Add(targetAssetPath);
            }

            if (includeDeps)
            {
                string[] deps = AssetDatabase.GetDependencies(results.ToArray(), true);
                foreach (string dep in deps)
                {
                    if (string.IsNullOrEmpty(dep)) continue;
                    string p = dep.Replace('\\', '/');
                    if (p.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                        results.Add(p);
                }
            }

            return results
                .Where(p => !string.IsNullOrEmpty(p))
                .Where(p => !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string GetProjectRootFullPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string AssetPathToFullPath(string assetPath)
        {
            string root = GetProjectRootFullPath();
            string normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(root, normalized));
        }

        private static string FullPathToAssetPath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath)) return null;
            string root = GetProjectRootFullPath();
            string normRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normFull = Path.GetFullPath(fullPath);
            if (!normFull.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
                return null;

            string rel = normFull.Substring(normRoot.Length);
            return rel.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
