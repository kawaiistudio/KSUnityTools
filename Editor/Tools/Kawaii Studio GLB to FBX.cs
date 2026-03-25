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
        
        // UI Assets
        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
        private static bool isDownloadingAssets = false;
        
        private const string PREFS_BLENDER_PATH = "KawaiiStudio_BlenderPath";
        private const string VERSION = "1.4";

        [MenuItem("Kawaii Studio/Universal Tools/GLB to FBX Converter")]
        public static void ShowWindow()
        {
            GLBtoFBXConverter window = GetWindow<GLBtoFBXConverter>("GLB → FBX Converter");
            window.minSize = new Vector2(800, 700);
            window.Show();
        }

        private void OnEnable()
        {
            blenderPath = EditorPrefs.GetString(PREFS_BLENDER_PATH, "");
            
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
            
            if (logoTexture == null || bannerTexture == null)
                DownloadAssets();
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            KawaiiStudioGUI.DrawWindowBackground(position);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });
            
            // Banner
            KawaiiStudioGUI.DrawBanner("GLB → FBX CONVERTER", "Material Processing & Auto-Setup", VERSION, logoTexture, bannerTexture);
            
            GUILayout.Space(10);
            
            // Info Section
            KawaiiStudioGUI.DrawSection("ℹ️ About This Tool", () => {
                EditorGUILayout.LabelField("• Converts GLB models to VRChat-ready FBX format", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("• Automatically processes materials and textures", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("• Extracts textures to organized folders", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("• Requires Blender 2.8+ installed", KawaiiStudioGUI.InfoLabelStyle);
            });
            
            GUILayout.Space(10);
            
            // Blender Path Section
            KawaiiStudioGUI.DrawSection("🔧 BLENDER SETUP", () => {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Blender Executable Path:", KawaiiStudioGUI.LabelStyle);
                
                EditorGUILayout.BeginHorizontal();
                blenderPath = EditorGUILayout.TextField(blenderPath);
                
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
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
                GUI.backgroundColor = oldBg;
                EditorGUILayout.EndHorizontal();
                
                if (!string.IsNullOrEmpty(blenderPath) && File.Exists(blenderPath))
                {
                    GUILayout.Space(5);
                    EditorGUILayout.LabelField($"✓ Blender found: {Path.GetFileName(blenderPath)}", KawaiiStudioGUI.InfoLabelStyle);
                }
            });
            
            GUILayout.Space(10);
            
            // File Selection Section
            KawaiiStudioGUI.DrawSection("📁 FILE SELECTION", () => {
                GUILayout.Space(5);
                
                EditorGUILayout.LabelField("GLB File:", KawaiiStudioGUI.LabelStyle);
                EditorGUILayout.BeginHorizontal();
                glbFilePath = EditorGUILayout.TextField(glbFilePath);
                Color oldBg = GUI.backgroundColor;
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFilePanel("Select GLB File", "", "glb");
                    if (!string.IsNullOrEmpty(path))
                    {
                        glbFilePath = path;
                        AddLog($"✓ GLB file selected: {Path.GetFileName(glbFilePath)}");
                    }
                }
                GUI.backgroundColor = oldBg;
                EditorGUILayout.EndHorizontal();
                
                GUILayout.Space(10);
                
                EditorGUILayout.LabelField("Output Folder:", KawaiiStudioGUI.LabelStyle);
                EditorGUILayout.BeginHorizontal();
                outputFolder = EditorGUILayout.TextField(outputFolder);
                oldBg = GUI.backgroundColor;
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("Browse", GUILayout.Width(80)))
                {
                    string path = EditorUtility.OpenFolderPanel("Select Output Folder", "", "");
                    if (!string.IsNullOrEmpty(path))
                    {
                        outputFolder = path;
                        AddLog($"✓ Output folder set: {outputFolder}");
                    }
                }
                GUI.backgroundColor = oldBg;
                EditorGUILayout.EndHorizontal();
            });
            
            GUILayout.Space(15);
            
            // Convert Button
            DrawConvertButton();
            
            GUILayout.Space(10);
            
            // Status
            if (isConverting)
            {
                KawaiiStudioGUI.DrawSection("⚡ STATUS", () => {
                    EditorGUILayout.LabelField("Converting... Please wait.", KawaiiStudioGUI.InfoLabelStyle);
                });
                GUILayout.Space(10);
            }
            
            // Console Output
            KawaiiStudioGUI.DrawSection("📋 CONSOLE OUTPUT", () => {
                GUILayout.Space(5);
                Vector2 scrollPos = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));
                GUIStyle consoleStyle = new GUIStyle(EditorStyles.textArea)
                {
                    normal = { 
                        background = KawaiiStudioGUI.GetRoundedTexture(KawaiiStudioGUI.FieldBackground, Color.clear, 5, 0),
                        textColor = KawaiiStudioGUI.SuccessColor 
                    },
                    fontSize = 10,
                    wordWrap = true,
                    richText = true
                };
                GUILayout.Label(consoleOutput, consoleStyle);
                EditorGUILayout.EndScrollView();
            });
            
            GUILayout.Space(20);
            
            // Footer
            KawaiiStudioGUI.DrawFooter();
            
            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawConvertButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.enabled = !isConverting && !string.IsNullOrEmpty(blenderPath) && 
                          !string.IsNullOrEmpty(glbFilePath) && !string.IsNullOrEmpty(outputFolder);
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = isConverting ? Color.gray : KawaiiStudioGUI.SuccessColor;
            
            string btnText = isConverting ? "CONVERTING..." : "🚀 CONVERT GLB TO FBX";
            
            GUIStyle bigButtonStyle = new GUIStyle(KawaiiStudioGUI.ButtonStyle)
            {
                fontSize = 14,
                fixedHeight = 45,
                fixedWidth = 300
            };
            
            if (GUILayout.Button(btnText, bigButtonStyle))
            {
                StartConversion();
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void AddLog(string message)
        {
            consoleOutput += message + "\n";
            scrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private void DownloadAssets()
        {
            // Branding is loaded locally from Assets/Kawaii Studio/References (no network).
            logoTexture = KawaiiStudioBranding.Logo;
            bannerTexture = KawaiiStudioBranding.Banner;
            isDownloadingAssets = false;
            Repaint();
        }

        private string FindBlenderUltra()
        {
            List<string> possiblePaths = new List<string>();
            
            try
            {
                AddLog("  → Scanning Windows Registry...");
                possiblePaths.AddRange(SearchRegistry());
                AddLog("  → Scanning PATH...");
                possiblePaths.AddRange(SearchPath());
                AddLog("  → Scanning standard locations...");
                possiblePaths.AddRange(SearchStandardLocations());
                AddLog("  → Scanning Steam...");
                possiblePaths.AddRange(SearchSteam());
                
                possiblePaths = possiblePaths.Distinct().Where(File.Exists).ToList();
                
                if (possiblePaths.Count > 0)
                {
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
