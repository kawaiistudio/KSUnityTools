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
    public class GLBtoFBXConverter : EditorWindow
    {
        // Configuration
        private string blenderPath = "";
        private string glbFilePath = "";
        private string outputFolder = "";
        private Vector2 scrollPosition;
        private string consoleOutput = "";
        private bool isConverting = false;
        private Process blenderProcess;
        
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

        private void InitializeStyles()
        {
            if (stylesInitialized) return;

            // Créer les textures de couleur
            purpleTexture = MakeTex(2, 2, new Color(0.486f, 0.227f, 0.929f, 1f)); // #7c3aed
            redTexture = MakeTex(2, 2, new Color(1f, 0.278f, 0.341f, 1f)); // #ff4757
            blackTexture = MakeTex(2, 2, new Color(0.039f, 0.039f, 0.059f, 1f)); // #0a0a0f

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

        private void OnGUI()
        {
            InitializeStyles();

            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), new Color(0.102f, 0.059f, 0.122f, 1f));

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
            GUILayout.Label("Blender Path:", labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            blenderPath = EditorGUILayout.TextField(blenderPath, textFieldStyle);
            
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select Blender.exe", "", "exe");
                if (!string.IsNullOrEmpty(path))
                {
                    blenderPath = path;
                    EditorPrefs.SetString(PREFS_BLENDER_PATH, blenderPath);
                    AddLog($"✓ Blender path set: {blenderPath}");
                }
            }
            
            if (GUILayout.Button("Auto-Detect", GUILayout.Width(100)))
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
            GUILayout.Label("GLB File:", labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            glbFilePath = EditorGUILayout.TextField(glbFilePath, textFieldStyle);
            
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFilePanel("Select GLB File", "", "glb");
                if (!string.IsNullOrEmpty(path))
                {
                    glbFilePath = path;
                    AddLog($"✓ GLB file selected: {Path.GetFileName(glbFilePath)}");
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
            GUILayout.Label("Output Folder:", labelStyle, GUILayout.Width(100));
            
            GUIStyle textFieldStyle = new GUIStyle(EditorStyles.textField)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            outputFolder = EditorGUILayout.TextField(outputFolder, textFieldStyle);
            
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Output Folder", "", "");
                if (!string.IsNullOrEmpty(path))
                {
                    outputFolder = path;
                    AddLog($"✓ Output folder set: {outputFolder}");
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
            
            if (GUILayout.Button("🚀 CONVERT GLB TO FBX", buttonStyle, GUILayout.Width(400)))
            {
                StartConversion();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUI.enabled = true;
        }

        private void DrawStatus()
        {
            string statusText = isConverting ? "⚡ Converting..." : "● READY";
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
            GUILayout.Label("[ CONSOLE OUTPUT ]", consoleLabelStyle);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, consoleStyle, GUILayout.Height(250));
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
            scrollPosition = new Vector2(0, float.MaxValue);
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
                                                AddLog($"    ✓ Found in registry: {blenderExe}");
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
                            AddLog($"    ✓ Found in PATH: {blenderExe}");
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
                            AddLog($"    ✓ Found: {file}");
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
                    AddLog($"    ✓ Found in Steam: {blenderExe}");
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

            isConverting = true;
            consoleOutput = "";
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🚀 Starting conversion...");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            string assetName = Path.GetFileNameWithoutExtension(glbFilePath);
            string finalFolder = Path.Combine(outputFolder, $"{assetName} Converted to FBX");
            string texturesFolder = Path.Combine(finalFolder, "Textures");
            Directory.CreateDirectory(texturesFolder);

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
            blenderProcess.Exited += (sender, e) => {
                EditorApplication.delayCall += () => {
                    AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    AddLog("✅ CONVERSION COMPLETED!");
                    AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
                    isConverting = false;
                    EditorUtility.DisplayDialog("Success! 🎉", 
                        $"Conversion completed successfully!\n\nOutput: {finalFolder}", "OK");
                };
            };

            blenderProcess.Start();
            blenderProcess.BeginOutputReadLine();
            blenderProcess.BeginErrorReadLine();
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
            if (blenderProcess != null && !blenderProcess.HasExited)
            {
                blenderProcess.Kill();
            }
        }
    }
}
