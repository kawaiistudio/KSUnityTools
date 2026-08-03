using UnityEngine;
using UnityEditor;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Win32;
using System;

namespace KawaiiStudio
{
    [Serializable]
    public class GLBTranslationEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class GLBTranslationFile
    {
        public List<GLBTranslationEntry> entries;
    }

    public class GLBtoFBXConverter : EditorWindow
    {
        // Version
        private const string VERSION = KawaiiStudioVersion.Current;
        
        // Configuration
        private string blenderPath = "";
        private string glbFilePath = "";
        private string outputFolder = "";
        private Vector2 scrollPosition;
        private Vector2 consoleScrollPosition;
        private string consoleOutput = "";
        private bool isConverting = false;
        private Process blenderProcess;
        
        // Language
        // Resolved at runtime: the old hardcoded literal broke any install under Packages/.
        private static string LANGUAGES_FOLDER => KawaiiStudioPaths.Languages;
        private const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        private Dictionary<string, string> translations = new Dictionary<string, string>();
        private string currentLanguage = "en";
        
        // UI Styles
        private GUIStyle headerStyle;
        private GUIStyle buttonStyle;
        private GUIStyle consoleStyle;
        private GUIStyle statusStyle;
        private Texture2D purpleTexture;
        private Texture2D redTexture;
        private Texture2D blackTexture;
        private bool stylesInitialized = false;

        private const string PREFS_BLENDER_PATH = "KawaiiStudio_BlenderPath";

        [MenuItem("Kawaii Studio/GLB to FBX Converter")]
        public static void ShowWindow()
        {
            GLBtoFBXConverter window = GetWindow<GLBtoFBXConverter>("Kawaii Studio Converter");
            window.minSize = new Vector2(800, 600);
            window.Show();
        }

        private void OnEnable()
        {
            // Charger la langue
            LoadLanguage();
            
            // Charger le chemin Blender sauvegardé
            blenderPath = EditorPrefs.GetString(PREFS_BLENDER_PATH, "");
            
            // Auto-détecter Blender si pas de chemin sauvegardé
            if (string.IsNullOrEmpty(blenderPath) || !File.Exists(blenderPath))
            {
                AddLog("🔍 Auto-detecting Blender...");
                blenderPath = FindBlenderUltra();
                if (!string.IsNullOrEmpty(blenderPath))
                {
                    EditorPrefs.SetString(PREFS_BLENDER_PATH, blenderPath);
                    AddLog($"✅ Blender found: {blenderPath}");
                }
                else
                {
                    AddLog("⚠️ Blender not found automatically. Please select manually.");
                }
            }
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
                    GLBTranslationFile translationFile = JsonUtility.FromJson<GLBTranslationFile>(jsonContent);
                    
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
                    UnityEngine.Debug.LogWarning($"Failed to load translations: {e.Message}");
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
                { "blender_path", "Blender Path:" },
                { "glb_file", "GLB File:" },
                { "output_folder", "Output Folder:" },
                { "browse", "Browse" },
                { "auto_detect", "Auto-Detect" },
                { "convert_glb", "CONVERT GLB TO FBX" },
                { "ready", "READY" },
                { "converting", "Converting..." },
                { "console_output", "CONSOLE OUTPUT" }
            };
        }

        private string T(string key)
        {
            if (translations.ContainsKey(key))
                return translations[key];
            return key;
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            // Créer les textures de couleur
            purpleTexture = MakeTex(2, 2, new Color(0.486f, 0.227f, 0.929f, 1f));
            redTexture = MakeTex(2, 2, new Color(1f, 0.278f, 0.341f, 1f));
            blackTexture = MakeTex(2, 2, new Color(0.039f, 0.039f, 0.059f, 1f));

            // Header Style
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };

            // Button Style
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { background = redTexture, textColor = Color.white },
                hover = { background = MakeTex(2, 2, new Color(1f, 0.42f, 0.506f, 1f)), textColor = Color.white },
                active = { background = redTexture, textColor = Color.white },
                padding = new RectOffset(20, 20, 10, 10),
                fixedHeight = 50
            };

            // Console Style
            consoleStyle = new GUIStyle(EditorStyles.textArea)
            {
                normal = { background = blackTexture, textColor = new Color(0f, 1f, 0.255f, 1f) },
                fontSize = 11,
                fontStyle = FontStyle.Normal,
                wordWrap = true,
                richText = true
            };

            // Status Style
            statusStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };

            stylesInitialized = true;
        }

        // Shared cached implementation. The local copy allocated a brand new Texture2D
        // on every style rebuild, without HideAndDontSave and without ever destroying
        // it, so Unity reported leaked textures on each assembly reload.
        private Texture2D MakeTex(int width, int height, Color col) => KawaiiStudioUtil.MakeTex(width, height, col);

        private void OnGUI()
        {
            InitializeStyles();

            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.102f, 0.059f, 0.122f, 1f));

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // Header
            DrawHeader();
            
            GUILayout.Space(20);

            // Blender Path Section
            DrawBlenderPathSection();

            GUILayout.Space(10);

            // GLB File Selection
            DrawGLBFileSection();

            GUILayout.Space(10);

            // Output Folder Selection
            DrawOutputFolderSection();

            GUILayout.Space(20);

            // Convert Button
            DrawConvertButton();

            GUILayout.Space(10);

            // Status
            DrawStatus();

            GUILayout.Space(10);

            // Console Output
            DrawConsole();

            GUILayout.Space(10);

            // Footer
            DrawFooter();

            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            GUILayout.Label("⚡ GLB → FBX CONVERTER ⚡", headerStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.278f, 0.341f, 1f) }
            };
            GUILayout.Label("Material Processing & Auto-Setup", subtitleStyle);
            
            // Separator
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 2), new Color(0.486f, 0.227f, 0.929f, 1f));
        }

        private void DrawBlenderPathSection()
        {
            GUILayout.BeginHorizontal();
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label(T("blender_path"), labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            blenderPath = EditorGUILayout.TextField(blenderPath, textFieldStyle);
            
            if (GUILayout.Button(T("browse"), GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select Blender.exe", "", "exe");
                if (!string.IsNullOrEmpty(path))
                {
                    blenderPath = path;
                    EditorPrefs.SetString(PREFS_BLENDER_PATH, blenderPath);
                    AddLog($"✔ Blender path set: {blenderPath}");
                }
            }
            
            if (GUILayout.Button(T("auto_detect"), GUILayout.Width(100)))
            {
                AddLog("🔍 Starting auto-detection...");
                string detected = FindBlenderUltra();
                if (!string.IsNullOrEmpty(detected))
                {
                    blenderPath = detected;
                    EditorPrefs.SetString(PREFS_BLENDER_PATH, blenderPath);
                    AddLog($"✅ Blender found: {blenderPath}");
                }
                else
                {
                    AddLog("❌ Blender not found automatically");
                    EditorUtility.DisplayDialog("Not Found", "Blender was not found automatically. Please select it manually.", "OK");
                }
            }
            
            GUILayout.EndHorizontal();
        }

        private void DrawGLBFileSection()
        {
            GUILayout.BeginHorizontal();
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label(T("glb_file"), labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            glbFilePath = EditorGUILayout.TextField(glbFilePath, textFieldStyle);
            
            if (GUILayout.Button(T("browse"), GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select GLB File", "", "glb");
                if (!string.IsNullOrEmpty(path))
                {
                    glbFilePath = path;
                    AddLog($"✔ GLB file selected: {Path.GetFileName(glbFilePath)}");
                }
            }
            
            GUILayout.EndHorizontal();
        }

        private void DrawOutputFolderSection()
        {
            GUILayout.BeginHorizontal();
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label(T("output_folder"), labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            outputFolder = EditorGUILayout.TextField(outputFolder, textFieldStyle);
            
            if (GUILayout.Button(T("browse"), GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", "", "");
                if (!string.IsNullOrEmpty(path))
                {
                    outputFolder = path;
                    AddLog($"✔ Output folder set: {outputFolder}");
                }
            }
            
            GUILayout.EndHorizontal();
        }

        private void DrawConvertButton()
        {
            GUI.enabled = !isConverting && !string.IsNullOrEmpty(blenderPath) && 
                          !string.IsNullOrEmpty(glbFilePath) && !string.IsNullOrEmpty(outputFolder);
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button($"🚀 {T("convert_glb")}", buttonStyle, GUILayout.Width(400)))
            {
                StartConversion();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUI.enabled = true;
        }

        private void DrawStatus()
        {
            string statusText = isConverting ? $"⚡ {T("converting")}" : $"● {T("ready")}";
            statusStyle.normal.textColor = isConverting ? 
                new Color(1f, 0.278f, 0.341f, 1f) : 
                new Color(0f, 1f, 0.255f, 1f);
            
            GUILayout.Label(statusText, statusStyle);
        }

        private void DrawConsole()
        {
            GUIStyle consoleLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"[ {T("console_output")} ]", consoleLabelStyle);
            
            consoleScrollPosition = GUILayout.BeginScrollView(consoleScrollPosition, consoleStyle, GUILayout.Height(250));
            GUILayout.Label(consoleOutput, consoleStyle);
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

        private void AddLog(string message)
        {
            consoleOutput += message + "\n";
            consoleScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private string FindBlenderUltra()
        {
            List<string> possiblePaths = new List<string>();

            try
            {
                // 1. Registry Windows
                AddLog("  → Scanning Windows Registry...");
                possiblePaths.AddRange(SearchRegistry());

                // 2. PATH Environment
                AddLog("  → Scanning PATH...");
                possiblePaths.AddRange(SearchPath());

                // 3. Standard locations
                AddLog("  → Scanning standard locations...");
                possiblePaths.AddRange(SearchStandardLocations());

                // 4. Steam
                AddLog("  → Scanning Steam...");
                possiblePaths.AddRange(SearchSteam());

                // Dédupliquer
                possiblePaths = possiblePaths.Distinct().Where(File.Exists).ToList();

                if (possiblePaths.Count > 0)
                {
                    // Trier par date de modification (plus récent en premier)
                    possiblePaths.Sort((a, b) => File.GetLastWriteTime(b).CompareTo(File.GetLastWriteTime(a)));
                    AddLog($"✅ Found {possiblePaths.Count} Blender installation(s)");
                    return possiblePaths[0];
                }
            }
            catch (Exception e)
            {
                AddLog($"⚠️ Error during detection: {e.Message}");
            }

            return null;
        }

        private List<string> SearchRegistry()
        {
            List<string> paths = new List<string>();
            
            try
            {
                string[] keyPaths = new string[]
                {
                    @"SOFTWARE\BlenderFoundation\Blender",
                    @"SOFTWARE\WOW6432Node\BlenderFoundation\Blender"
                };

                foreach (string keyPath in keyPaths)
                {
                    try
                    {
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(keyPath))
                        {
                            if (key != null)
                            {
                                foreach (string subKeyName in key.GetSubKeyNames())
                                {
                                    using (RegistryKey subKey = key.OpenSubKey(subKeyName))
                                    {
                                        object installDir = subKey?.GetValue("InstallDir");
                                        if (installDir != null)
                                        {
                                            string blenderExe = Path.Combine(installDir.ToString(), "blender.exe");
                                            if (File.Exists(blenderExe))
                                            {
                                                paths.Add(blenderExe);
                                                AddLog($"    ✔ Found in registry: {blenderExe}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return paths;
        }

        private List<string> SearchPath()
        {
            List<string> paths = new List<string>();
            
            try
            {
                string pathEnv = Environment.GetEnvironmentVariable("PATH");
                if (!string.IsNullOrEmpty(pathEnv))
                {
                    string[] pathDirs = pathEnv.Split(';');
                    foreach (string dir in pathDirs)
                    {
                        string blenderExe = Path.Combine(dir, "blender.exe");
                        if (File.Exists(blenderExe))
                        {
                            paths.Add(blenderExe);
                            AddLog($"    ✔ Found in PATH: {blenderExe}");
                        }
                    }
                }
            }
            catch { }

            return paths;
        }

        private List<string> SearchStandardLocations()
        {
            List<string> paths = new List<string>();
            
            string[] standardLocations = new string[]
            {
                @"C:\Program Files\Blender Foundation",
                @"C:\Program Files (x86)\Blender Foundation",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Blender Foundation"),
                @"C:\Blender"
            };

            foreach (string location in standardLocations)
            {
                if (Directory.Exists(location))
                {
                    try
                    {
                        string[] files = Directory.GetFiles(location, "blender.exe", SearchOption.AllDirectories);
                        foreach (string file in files)
                        {
                            paths.Add(file);
                            AddLog($"    ✔ Found: {file}");
                        }
                    }
                    catch { }
                }
            }

            return paths;
        }

        private List<string> SearchSteam()
        {
            List<string> paths = new List<string>();
            
            string[] steamPaths = new string[]
            {
                @"C:\Program Files (x86)\Steam\steamapps\common\Blender",
                @"C:\Program Files\Steam\steamapps\common\Blender"
            };

            foreach (string steamPath in steamPaths)
            {
                string blenderExe = Path.Combine(steamPath, "blender.exe");
                if (File.Exists(blenderExe))
                {
                    paths.Add(blenderExe);
                    AddLog($"    ✔ Found in Steam: {blenderExe}");
                }
            }

            return paths;
        }

        private void StartConversion()
        {
            if (string.IsNullOrEmpty(blenderPath) || !File.Exists(blenderPath))
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid Blender executable.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(glbFilePath) || !File.Exists(glbFilePath))
            {
                EditorUtility.DisplayDialog("Error", "Please select a valid GLB file.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(outputFolder))
            {
                EditorUtility.DisplayDialog("Error", "Please select an output folder.", "OK");
                return;
            }

            string assetName = Path.GetFileNameWithoutExtension(glbFilePath);
            string finalFolder = Path.Combine(outputFolder, $"{assetName} Converted to FBX");
            string texturesFolder = Path.Combine(finalFolder, "Textures");

            // Create the output folders BEFORE flipping isConverting: an IO failure here
            // used to escape through OnGUI and leave the window stuck in "converting".
            try
            {
                Directory.CreateDirectory(texturesFolder);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error",
                    $"Could not create the output folder:\n{texturesFolder}\n\n{ex.Message}", "OK");
                return;
            }

            isConverting = true;
            consoleOutput = "";
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🚀 Starting conversion...");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            string blenderScript = GenerateBlenderScript(glbFilePath, finalFolder, texturesFolder, assetName);
            
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = blenderPath,
                Arguments = $"--background --python-expr \"{blenderScript}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            // Release any previous run's process handle before overwriting the field.
            if (blenderProcess != null)
            {
                try { blenderProcess.Dispose(); } catch (Exception) { }
                blenderProcess = null;
            }

            blenderProcess = new Process { StartInfo = startInfo };
            blenderProcess.OutputDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    UnityEngine.Debug.Log(e.Data);
                    EditorApplication.delayCall += () => AddLog(e.Data);
                }
            };
            blenderProcess.ErrorDataReceived += (sender, e) => {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    UnityEngine.Debug.LogError(e.Data);
                    EditorApplication.delayCall += () => AddLog($"ERROR: {e.Data}");
                }
            };

            blenderProcess.EnableRaisingEvents = true;
            string expectedFbx = Path.Combine(finalFolder, $"{assetName}_converted_FBX.fbx");
            blenderProcess.Exited += (sender, e) => {
                // Read the exit code on the event thread; the Process may be disposed
                // by the time delayCall runs.
                int exitCode = -1;
                try { exitCode = blenderProcess.ExitCode; } catch (Exception) { /* already released */ }

                EditorApplication.delayCall += () => {
                    // Previously this branch unconditionally logged "CONVERSION COMPLETED"
                    // and showed the Success dialog, so a Blender crash or a Python error
                    // was reported to the user as a success.
                    isConverting = false;
                    bool succeeded = exitCode == 0 && File.Exists(expectedFbx);

                    AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    if (succeeded)
                    {
                        AddLog("✅ CONVERSION COMPLETED!");
                        AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        // Make the result visible if the user exported inside the project.
                        string normalized = finalFolder.Replace("\\", "/");
                        if (normalized.StartsWith(Application.dataPath.Replace("\\", "/"), StringComparison.OrdinalIgnoreCase))
                            AssetDatabase.Refresh();
                        EditorUtility.DisplayDialog("Success! 🎉",
                            $"Conversion completed successfully!\n\nOutput: {finalFolder}", "OK");
                    }
                    else
                    {
                        AddLog($"❌ CONVERSION FAILED (Blender exit code {exitCode})");
                        AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                        EditorUtility.DisplayDialog("Conversion failed",
                            $"Blender exited with code {exitCode} and no FBX was produced.\n\nCheck the log for the Blender error.", "OK");
                    }
                };
            };

            try
            {
                blenderProcess.Start();
                blenderProcess.BeginOutputReadLine();
                blenderProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                // A failed Start() used to escape through OnGUI leaving isConverting == true
                // forever, which permanently greyed out the Convert button.
                isConverting = false;
                blenderProcess.Dispose();
                blenderProcess = null;
                AddLog($"❌ Could not start Blender: {ex.Message}");
                EditorUtility.DisplayDialog("Error", $"Could not start Blender:\n{ex.Message}", "OK");
            }
        }

        private string GenerateBlenderScript(string glbPath, string folder, string texturesFolder, string assetName)
        {
            glbPath = glbPath.Replace("\\", "/");
            folder = folder.Replace("\\", "/");
            texturesFolder = texturesFolder.Replace("\\", "/");

            return $@"
import bpy, os

glb_path = r'{glbPath}'
folder = r'{folder}'
textures_folder = r'{texturesFolder}'
asset_name = r'{assetName}'

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb_path)

texture_index = 0
for mat in bpy.data.materials:
    if not mat.node_tree:
        continue
    nodes = mat.node_tree.nodes
    links = mat.node_tree.links

    principled = None
    for node in nodes:
        if node.type == 'BSDF_PRINCIPLED':
            principled = node
            break
    if not principled:
        principled = nodes.new('ShaderNodeBsdfPrincipled')
        output = None
        for node in nodes:
            if node.type == 'OUTPUT_MATERIAL':
                output = node
                break
        if output:
            links.new(principled.outputs['BSDF'], output.inputs['Surface'])

    for node in nodes:
        if node.type == 'TEX_IMAGE' and node.image:
            image = node.image
            base_name = os.path.basename(image.filepath)
            if not base_name or base_name.endswith(('/', '\\\\\\\\')):
                base_name = f'texture_{{texture_index:03d}}.png'
                texture_index += 1
            save_path = os.path.join(textures_folder, base_name)
            image.filepath_raw = save_path
            image.file_format = 'PNG'
            image.save()

            lower_name = node.name.lower()
            if 'basecolor' in lower_name or 'diffuse' in lower_name:
                links.new(node.outputs['Color'], principled.inputs['Base Color'])
            elif 'normal' in lower_name:
                normal_node = nodes.new('ShaderNodeNormalMap')
                links.new(node.outputs['Color'], normal_node.inputs['Color'])
                links.new(normal_node.outputs['Normal'], principled.inputs['Normal'])
            elif 'metallic' in lower_name or 'roughness' in lower_name:
                links.new(node.outputs['Color'], principled.inputs['Metallic'])
                links.new(node.outputs['Color'], principled.inputs['Roughness'])
            elif 'emissive' in lower_name:
                links.new(node.outputs['Color'], principled.inputs['Emission'])

fbx_output_path = os.path.join(folder, f'{{asset_name}}_converted_FBX.fbx')
bpy.ops.export_scene.fbx(filepath=fbx_output_path, path_mode='COPY', embed_textures=True)
print('Conversion completed!')
";
        }

        private void OnDestroy()
        {
            // HasExited throws InvalidOperationException when the process was never
            // started (e.g. Start() failed), and the handle was never disposed.
            if (blenderProcess != null)
            {
                try
                {
                    if (!blenderProcess.HasExited)
                    {
                        blenderProcess.Kill();
                        blenderProcess.WaitForExit(2000);
                    }
                }
                catch (Exception) { /* never started, or already gone */ }
                finally
                {
                    blenderProcess.Dispose();
                    blenderProcess = null;
                }
            }
        }
    }
}
