using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace KawaiiStudio
{
    public class TextureItem
    {
        public Texture texture;
        public bool selected;
        public string path;
        public long originalSize;
        public long optimizedSize;
        public long originalMemorySize;
        public long optimizedMemorySize;
        public Vector2Int resolution;
        public string compressionFormat;
        public bool hasMipmaps;
    }

    public class MeshItem
    {
        public Mesh mesh;
        public bool selected;
        public string path;
        public ModelImporterMeshCompression compression;
        public int vertexCount;
        public int triangleCount;
    }

    public class AudioItem
    {
        public AudioClip audioClip;
        public bool selected;
        public string path;
        public long originalSize;
        public long estimatedSize;
        public AudioClipLoadType loadType;
        public AudioCompressionFormat compressionFormat;
        public float quality;
        public int frequency;
        public float length;
        public int channels;
    }

    public class PrefabOptimizer : EditorWindow
    {
        // Configuration
        private GameObject prefab;
        private int maxTextureSize = 2048;
        private TextureImporterCompression compressionQuality = TextureImporterCompression.Compressed;
        private bool useCrunchCompression = true;
        private int crunchCompressionQuality = 100;
        private bool generateMipmaps = true;
        private ModelImporterMeshCompression meshCompression = ModelImporterMeshCompression.High;
        
        // Audio Settings
        private AudioClipLoadType audioLoadType = AudioClipLoadType.CompressedInMemory;
        private AudioCompressionFormat audioCompressionFormat = AudioCompressionFormat.Vorbis;
        private float audioQuality = 0.7f;
        private bool forceToMono = false;
        private int audioSampleRate = 44100;
        
        // Lists
        private List<TextureItem> textureItems = new List<TextureItem>();
        private List<MeshItem> meshItems = new List<MeshItem>();
        private List<AudioItem> audioItems = new List<AudioItem>();
        
        // UI State
        private Vector2 scrollPosition;
        private Vector2 textureScrollPosition;
        private Vector2 meshScrollPosition;
        private Vector2 audioScrollPosition;
        private Vector2 logScrollPosition;
        private string logOutput = "";
        private bool scanned = false;
        
        // UI Styles & Assets
        private static Texture2D logoTexture;
        private static Texture2D bannerTexture;
        
        // Cached GUIStyles (avoid per-frame allocation)
        private GUIStyle _itemStyle;
        private GUIStyle _optimizedItemStyle;
        
        private const string VERSION = "1.5";

        [MenuItem("Kawaii Studio/Universal Tools/Prefab Optimizer")]
        public static void ShowWindow()
        {
            PrefabOptimizer window = GetWindow<PrefabOptimizer>("Prefab Optimizer");
            window.minSize = new Vector2(900, 750);
            window.Show();
        }

        private void OnEnable()
        {
            if (logoTexture == null || bannerTexture == null)
                LoadBranding();
        }

        private void LoadBranding()
        {
            // Branding is loaded locally from Assets/Kawaii Studio/References (no network).
            logoTexture = KawaiiStudioBranding.Logo;
            bannerTexture = KawaiiStudioBranding.Banner;
        }

        private void EnsureStyles()
        {
            if (_itemStyle == null)
            {
                _itemStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = KawaiiStudioGUI.SuccessColor }
                };
            }
            if (_optimizedItemStyle == null)
            {
                _optimizedItemStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(1f, 0.647f, 0f, 1f) },
                    fontStyle = FontStyle.Bold
                };
            }
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            EnsureStyles();
            KawaiiStudioGUI.DrawWindowBackground(position);
            
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });
            
            // Banner
            KawaiiStudioGUI.DrawBanner("PREFAB OPTIMIZER", "Texture, Mesh & Audio Compression", VERSION, logoTexture, bannerTexture);
            
            GUILayout.Space(10);
            
            // Info Section
            KawaiiStudioGUI.DrawSection("ℹ️ About This Tool", () => {
                EditorGUILayout.LabelField("• Optimize textures, meshes, and audio for VRChat", GetInfoStyle());
                EditorGUILayout.LabelField("• Reduce file sizes by 50-80% while maintaining quality", GetInfoStyle());
                EditorGUILayout.LabelField("• Improve VRChat performance ranking", GetInfoStyle());
                EditorGUILayout.LabelField("• Batch process multiple assets at once", GetInfoStyle());
            });
            
            GUILayout.Space(10);
            
            // Avatar Selection
            KawaiiStudioGUI.DrawSection("🎭 TARGET PREFAB", () => {
                GUILayout.Space(5);
                EditorGUILayout.LabelField("Drop your avatar prefab here:", GetLabelStyle());
                
                GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(prefab, typeof(GameObject), true, GUILayout.Height(30));
                
                if (newPrefab != prefab)
                {
                    prefab = newPrefab;
                    scanned = false;
                    textureItems.Clear();
                    meshItems.Clear();
                    audioItems.Clear();
                }
                
                if (prefab != null)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.LabelField($"✓ Selected: {prefab.name}", GetInfoStyle());
                }
            });
            
            GUILayout.Space(10);
            
            // Scan Button
            if (!scanned || prefab == null)
            {
                DrawScanButton();
            }

            if (scanned && prefab != null)
            {
                GUILayout.Space(10);
                
                // Texture Settings
                DrawTextureSettings();
                GUILayout.Space(10);

                // Texture List
                DrawTextureList();
                GUILayout.Space(10);

                // Mesh Settings
                DrawMeshSettings();
                GUILayout.Space(10);

                // Mesh List
                DrawMeshList();
                GUILayout.Space(10);

                // Audio Settings
                DrawAudioSettings();
                GUILayout.Space(10);

                // Audio List
                DrawAudioList();
                GUILayout.Space(10);

                // Optimize Button
                DrawOptimizeButton();
                GUILayout.Space(10);

                // Log Output
                DrawLogOutput();
            }
            
            GUILayout.Space(20);
            
            // Footer
            KawaiiStudioGUI.DrawFooter();
            
            GUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private GUIStyle GetLabelStyle() => KawaiiStudioGUI.LabelStyle;
        private GUIStyle GetInfoStyle() => KawaiiStudioGUI.InfoLabelStyle;


        private void DrawScanButton()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.enabled = prefab != null;
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = prefab != null ? KawaiiStudioGUI.AccentColor : Color.gray;
            
            GUIStyle bigButtonStyle = new GUIStyle(KawaiiStudioGUI.ButtonStyle)
            {
                fontSize = 14,
                fixedHeight = 45,
                fixedWidth = 300
            };
            
            if (GUILayout.Button("🔍 SCAN PREFAB", bigButtonStyle))
            {
                ScanPrefab();
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTextureSettings()
        {
            KawaiiStudioGUI.DrawSection("🎨 TEXTURE COMPRESSION SETTINGS", () => {
                GUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Max Texture Size:", GetLabelStyle(), GUILayout.Width(150));
                maxTextureSize = EditorGUILayout.IntPopup(maxTextureSize, 
                    new string[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" },
                    new int[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 });
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Compression Quality:", GetLabelStyle(), GUILayout.Width(150));
                compressionQuality = (TextureImporterCompression)EditorGUILayout.EnumPopup(compressionQuality);
                EditorGUILayout.EndHorizontal();
                
                useCrunchCompression = DrawToggle("Use Crunch Compression", useCrunchCompression);
                
                if (useCrunchCompression)
                {
                    EditorGUILayout.BeginHorizontal();
                    GUILayout.Space(20);
                    EditorGUILayout.LabelField("Crunch Quality:", GetLabelStyle(), GUILayout.Width(130));
                    crunchCompressionQuality = EditorGUILayout.IntSlider(crunchCompressionQuality, 0, 100);
                    EditorGUILayout.EndHorizontal();
                }
                
                generateMipmaps = DrawToggle("Generate Mipmaps", generateMipmaps);
            });
        }

        private bool DrawToggle(string label, bool value)
        {
            return KawaiiStudioGUI.DrawToggle(label, value);
        }

        private void DrawTextureList()
        {
            KawaiiStudioGUI.DrawSection($"🖼️ TEXTURES ({textureItems.Count})", () => {
                if (textureItems.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select All", GUILayout.Width(80)))
                    {
                        foreach (var item in textureItems) item.selected = true;
                    }
                    if (GUILayout.Button("None", GUILayout.Width(60)))
                    {
                        foreach (var item in textureItems) item.selected = false;
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(5);
                }
            
                if (textureItems.Count > 0)
                {
                    // Header
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("", GUILayout.Width(20));
                    GUILayout.Label("Texture", GetLabelStyle(), GUILayout.Width(200));
                    GUILayout.Label("Resolution", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Format", GetLabelStyle(), GUILayout.Width(150));
                    GUILayout.Label("Mipmaps", GetLabelStyle(), GUILayout.Width(70));
                    GUILayout.Label("Memory (Original)", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Memory (Optimized)", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.EndHorizontal();
                    
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 60, 1), KawaiiStudioGUI.AccentColor);
                    
                    textureScrollPosition = EditorGUILayout.BeginScrollView(textureScrollPosition, GUILayout.Height(200));
                    
                    foreach (var item in textureItems)
                    {
                        GUILayout.BeginHorizontal();
                        
                        item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                        
                        EditorGUILayout.ObjectField(item.texture, typeof(Texture), false, GUILayout.Width(200));
                        
                        GUILayout.Label($"{item.resolution.x}x{item.resolution.y}", _itemStyle, GUILayout.Width(100));
                        GUILayout.Label(item.compressionFormat, _itemStyle, GUILayout.Width(150));
                        GUILayout.Label(item.hasMipmaps ? "Yes" : "No", _itemStyle, GUILayout.Width(70));
                        GUILayout.Label($"{FormatBytes(item.originalMemorySize)}", _itemStyle, GUILayout.Width(100));
                        
                        if (item.optimizedMemorySize > 0)
                        {
                            GUILayout.Label($"{FormatBytes(item.optimizedMemorySize)}", _optimizedItemStyle, GUILayout.Width(100));
                            
                            if (item.originalMemorySize > item.optimizedMemorySize)
                            {
                                float percentSaved = ((float)(item.originalMemorySize - item.optimizedMemorySize) / item.originalMemorySize) * 100f;
                                GUILayout.Label($"(-{percentSaved:F1}%)", _optimizedItemStyle, GUILayout.Width(70));
                            }
                            else if (item.originalMemorySize == item.optimizedMemorySize)
                            {
                                GUILayout.Label("(No change)", _optimizedItemStyle, GUILayout.Width(70));
                            }
                        }
                        else
                        {
                            GUILayout.Label("-", _itemStyle, GUILayout.Width(100));
                        }
                        
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
            });
        }

        private void DrawMeshSettings()
        {
            KawaiiStudioGUI.DrawSection("🔧 MESH COMPRESSION SETTINGS", () => {
                GUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Mesh Compression:", GetLabelStyle(), GUILayout.Width(150));
                meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(meshCompression);
                EditorGUILayout.EndHorizontal();
            });
        }

        private void DrawMeshList()
        {
            KawaiiStudioGUI.DrawSection($"📐 MESHES ({meshItems.Count})", () => {
                if (meshItems.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select All", GUILayout.Width(80)))
                    {
                        foreach (var item in meshItems) item.selected = true;
                    }
                    if (GUILayout.Button("None", GUILayout.Width(60)))
                    {
                        foreach (var item in meshItems) item.selected = false;
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(5);
                    
                    // Header
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("", GUILayout.Width(20));
                    GUILayout.Label("Mesh", GetLabelStyle(), GUILayout.Width(200));
                    GUILayout.Label("Vertices", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Triangles", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Compression", GetLabelStyle(), GUILayout.Width(200));
                    GUILayout.EndHorizontal();
                    
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 60, 1), KawaiiStudioGUI.AccentColor);
                    
                    meshScrollPosition = EditorGUILayout.BeginScrollView(meshScrollPosition, GUILayout.Height(200));
                    
                    foreach (var item in meshItems)
                    {
                        GUILayout.BeginHorizontal();
                        
                        item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                        
                        EditorGUILayout.ObjectField(item.mesh, typeof(Mesh), false, GUILayout.Width(200));
                        
                        GUILayout.Label($"Verts: {item.vertexCount}", _itemStyle, GUILayout.Width(100));
                        GUILayout.Label($"Tris: {item.triangleCount}", _itemStyle, GUILayout.Width(100));
                        
                        GUILayout.Label("Compression:", _itemStyle, GUILayout.Width(90));
                        item.compression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(item.compression, GUILayout.Width(100));
                        
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
            });
        }

        private void DrawAudioSettings()
        {
            KawaiiStudioGUI.DrawSection("🔊 AUDIO COMPRESSION SETTINGS", () => {
                GUILayout.Space(5);
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Load Type:", GetLabelStyle(), GUILayout.Width(150));
                audioLoadType = (AudioClipLoadType)EditorGUILayout.EnumPopup(audioLoadType);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Compression Format:", GetLabelStyle(), GUILayout.Width(150));
                audioCompressionFormat = (AudioCompressionFormat)EditorGUILayout.EnumPopup(audioCompressionFormat);
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"Quality: {(audioQuality * 100):F0}%", GetLabelStyle(), GUILayout.Width(150));
                audioQuality = EditorGUILayout.Slider(audioQuality, 0.01f, 1f);
                EditorGUILayout.EndHorizontal();
                
                int estimatedBitrate = Mathf.RoundToInt(audioQuality * 320);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(20);
                EditorGUILayout.LabelField($"≈ {estimatedBitrate} kbps", GetInfoStyle());
                EditorGUILayout.EndHorizontal();
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Sample Rate:", GetLabelStyle(), GUILayout.Width(150));
                audioSampleRate = EditorGUILayout.IntPopup(audioSampleRate, 
                    new string[] { "8000 Hz", "11025 Hz", "22050 Hz", "44100 Hz", "48000 Hz" },
                    new int[] { 8000, 11025, 22050, 44100, 48000 });
                EditorGUILayout.EndHorizontal();
                
                forceToMono = DrawToggle("Force To Mono", forceToMono);
            });
        }

        private void DrawAudioList()
        {
            KawaiiStudioGUI.DrawSection($"🔊 AUDIO CLIPS ({audioItems.Count})", () => {
                if (audioItems.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Select All", GUILayout.Width(80)))
                    {
                        foreach (var item in audioItems) item.selected = true;
                    }
                    if (GUILayout.Button("None", GUILayout.Width(60)))
                    {
                        foreach (var item in audioItems) item.selected = false;
                    }
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(5);
                    
                    // Header
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("", GUILayout.Width(20));
                    GUILayout.Label("Audio Clip", GetLabelStyle(), GUILayout.Width(200));
                    GUILayout.Label("Length", GetLabelStyle(), GUILayout.Width(80));
                    GUILayout.Label("Channels", GetLabelStyle(), GUILayout.Width(70));
                    GUILayout.Label("Frequency", GetLabelStyle(), GUILayout.Width(80));
                    GUILayout.Label("Format", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Original Size", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Estimated Size", GetLabelStyle(), GUILayout.Width(100));
                    GUILayout.Label("Reduction", GetLabelStyle(), GUILayout.Width(80));
                    GUILayout.EndHorizontal();
                    
                    EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 60, 1), KawaiiStudioGUI.AccentColor);
                    
                    audioScrollPosition = EditorGUILayout.BeginScrollView(audioScrollPosition, GUILayout.Height(200));
                    
                    foreach (var item in audioItems)
                    {
                        GUILayout.BeginHorizontal();
                        
                        item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                        
                        EditorGUILayout.ObjectField(item.audioClip, typeof(AudioClip), false, GUILayout.Width(200));
                        
                        GUILayout.Label($"{item.length:F2}s", _itemStyle, GUILayout.Width(80));
                        GUILayout.Label($"{item.channels}ch", _itemStyle, GUILayout.Width(70));
                        GUILayout.Label($"{item.frequency} Hz", _itemStyle, GUILayout.Width(80));
                        GUILayout.Label($"{item.compressionFormat}", _itemStyle, GUILayout.Width(100));
                        GUILayout.Label($"{FormatBytes(item.originalSize)}", _itemStyle, GUILayout.Width(100));
                        
                        long estimatedSize = CalculateEstimatedAudioSize(item);
                        GUILayout.Label($"{FormatBytes(estimatedSize)}", _optimizedItemStyle, GUILayout.Width(100));
                        
                        if (item.originalSize > 0)
                        {
                            float percentSaved = ((float)(item.originalSize - estimatedSize) / item.originalSize) * 100f;
                            if (percentSaved > 0)
                            {
                                GUILayout.Label($"(-{percentSaved:F1}%)", _optimizedItemStyle, GUILayout.Width(80));
                            }
                            else
                            {
                                GUILayout.Label("(+)", _itemStyle, GUILayout.Width(80));
                            }
                        }
                        
                        GUILayout.FlexibleSpace();
                        GUILayout.EndHorizontal();
                    }
                    
                    EditorGUILayout.EndScrollView();
                }
            });
        }

        private void DrawOptimizeButton()
        {
            int selectedCount = textureItems.Count(t => t.selected) + meshItems.Count(m => m.selected) + audioItems.Count(a => a.selected);
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            GUI.enabled = selectedCount > 0;
            
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = selectedCount > 0 ? KawaiiStudioGUI.SuccessColor : Color.gray;
            
            GUIStyle bigButtonStyle = new GUIStyle(KawaiiStudioGUI.ButtonStyle)
            {
                fontSize = 14,
                fixedHeight = 45,
                fixedWidth = 300
            };
            
            if (GUILayout.Button($"⚡ OPTIMIZE ({selectedCount} items)", bigButtonStyle))
            {
                OptimizeAvatar();
            }
            
            GUI.backgroundColor = oldBg;
            GUI.enabled = true;
            
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLogOutput()
        {
            KawaiiStudioGUI.DrawSection("📋 LOG OUTPUT", () => {
                GUILayout.Space(5);
                logScrollPosition = EditorGUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(150));
                GUIStyle logStyle = new GUIStyle(EditorStyles.textArea)
                {
                    normal = { 
                        background = KawaiiStudioGUI.GetRoundedTexture(KawaiiStudioGUI.FieldBackground, Color.clear, 5, 0),
                        textColor = KawaiiStudioGUI.SuccessColor 
                    },
                    fontSize = 10,
                    wordWrap = true
                };
                GUILayout.Label(logOutput, logStyle);
                EditorGUILayout.EndScrollView();
            });
        }


        private void AddLog(string message)
        {
            logOutput += message + "\n";
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private void ScanPrefab()
        {
            if (prefab == null) return;

            logOutput = "";
            textureItems.Clear();
            meshItems.Clear();
            audioItems.Clear();

            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🔍 Scanning Prefab...");
            AddLog($"📦 Prefab: {prefab.name}");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            // Trouver tous les renderers
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            HashSet<Texture> texturesFound = new HashSet<Texture>();
            HashSet<Mesh> meshesFound = new HashSet<Mesh>();

            AddLog($"\n📊 Found {renderers.Length} renderer(s)");

            // Collecter textures et meshes
            foreach (Renderer renderer in renderers)
            {
                // Textures
                foreach (Material mat in renderer.sharedMaterials)
                {
                    if (mat != null)
                    {
                        Shader shader = mat.shader;
                        for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                        {
                            if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                            {
                                string propertyName = ShaderUtil.GetPropertyName(shader, i);
                                Texture tex = mat.GetTexture(propertyName);
                                if (tex != null && !texturesFound.Contains(tex))
                                {
                                    texturesFound.Add(tex);
                                    
                                    string path = AssetDatabase.GetAssetPath(tex);
                                    if (!string.IsNullOrEmpty(path))
                                    {
                                        FileInfo fileInfo = new FileInfo(path);
                                        Texture2D tex2D = tex as Texture2D;
                                        TextureImporter texImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                                        
                                        string compressionInfo = "Unknown";
                                        bool mipmaps = false;
                                        long memorySize = 0;
                                        
                                        if (texImporter != null)
                                        {
                                            compressionInfo = GetCompressionFormat(texImporter);
                                            mipmaps = texImporter.mipmapEnabled;
                                        }
                                        
                                        if (tex2D != null)
                                        {
                                            memorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex2D);
                                        }
                                        
                                        textureItems.Add(new TextureItem
                                        {
                                            texture = tex,
                                            selected = true,
                                            path = path,
                                            originalSize = fileInfo.Exists ? fileInfo.Length : 0,
                                            optimizedSize = 0,
                                            originalMemorySize = memorySize,
                                            optimizedMemorySize = 0,
                                            resolution = tex2D != null ? new Vector2Int(tex2D.width, tex2D.height) : Vector2Int.zero,
                                            compressionFormat = compressionInfo,
                                            hasMipmaps = mipmaps
                                        });
                                    }
                                }
                            }
                        }
                    }
                }

                // Meshes
                MeshFilter meshFilter = renderer.GetComponent<MeshFilter>();
                SkinnedMeshRenderer skinnedMesh = renderer as SkinnedMeshRenderer;
                
                Mesh mesh = null;
                if (meshFilter != null)
                    mesh = meshFilter.sharedMesh;
                else if (skinnedMesh != null)
                    mesh = skinnedMesh.sharedMesh;

                if (mesh != null && !meshesFound.Contains(mesh))
                {
                    meshesFound.Add(mesh);
                    
                    string path = AssetDatabase.GetAssetPath(mesh);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase))
                    {
                        ModelImporter modelImporter = AssetImporter.GetAtPath(path) as ModelImporter;
                        
                        meshItems.Add(new MeshItem
                        {
                            mesh = mesh,
                            selected = true,
                            path = path,
                            compression = modelImporter != null ? modelImporter.meshCompression : ModelImporterMeshCompression.Off,
                            vertexCount = mesh.vertexCount,
                            triangleCount = mesh.triangles.Length / 3
                        });
                    }
                }
            }

            // Scan Audio Clips
            AudioSource[] audioSources = prefab.GetComponentsInChildren<AudioSource>(true);
            HashSet<AudioClip> audioClipsFound = new HashSet<AudioClip>();
            
            AddLog($"\n🔊 Found {audioSources.Length} audio source(s)");
            
            foreach (AudioSource audioSource in audioSources)
            {
                if (audioSource.clip != null && !audioClipsFound.Contains(audioSource.clip))
                {
                    audioClipsFound.Add(audioSource.clip);
                    
                    string path = AssetDatabase.GetAssetPath(audioSource.clip);
                    if (!string.IsNullOrEmpty(path))
                    {
                        FileInfo fileInfo = new FileInfo(path);
                        AudioImporter audioImporter = AssetImporter.GetAtPath(path) as AudioImporter;
                        
                        AudioClipLoadType loadType = AudioClipLoadType.CompressedInMemory;
                        AudioCompressionFormat format = AudioCompressionFormat.Vorbis;
                        float quality = 1f;
                        
                        if (audioImporter != null)
                        {
                            AudioImporterSampleSettings settings = audioImporter.defaultSampleSettings;
                            loadType = settings.loadType;
                            format = settings.compressionFormat;
                            quality = settings.quality;
                        }
                        
                        audioItems.Add(new AudioItem
                        {
                            audioClip = audioSource.clip,
                            selected = true,
                            path = path,
                            originalSize = fileInfo.Exists ? fileInfo.Length : 0,
                            estimatedSize = 0,
                            loadType = loadType,
                            compressionFormat = format,
                            quality = quality,
                            frequency = audioSource.clip.frequency,
                            length = audioSource.clip.length,
                            channels = audioSource.clip.channels
                        });
                    }
                }
            }

            AddLog($"✓ Found {textureItems.Count} texture(s)");
            AddLog($"✓ Found {meshItems.Count} FBX mesh(es)");
            AddLog($"✓ Found {audioItems.Count} audio clip(s)");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("✅ Scan completed! Review and optimize.");

            scanned = true;
        }

        private void OptimizeAvatar()
        {
            logOutput = "";

            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🚀 Starting Optimization...");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            int texturesOptimized = 0;
            int meshesOptimized = 0;
            int audiosOptimized = 0;

            var selectedTextures = textureItems.Where(t => t.selected).ToList();
            var selectedMeshes = meshItems.Where(m => m.selected).ToList();
            var selectedAudios = audioItems.Where(x => x.selected).ToList();
            int totalItems = selectedTextures.Count + selectedMeshes.Count + selectedAudios.Count;
            int processed = 0;

            try
            {
                // Optimize Textures
                if (selectedTextures.Count > 0)
                {
                    AddLog($"\n🎨 Optimizing {selectedTextures.Count} texture(s)...");
                    
                    foreach (var item in selectedTextures)
                    {
                        EditorUtility.DisplayProgressBar("Prefab Optimizer", 
                            $"Texture: {item.texture.name}", (float)processed / totalItems);
                        if (OptimizeTexture(item))
                            texturesOptimized++;
                        processed++;
                    }
                }

                // Optimize Meshes
                if (selectedMeshes.Count > 0)
                {
                    AddLog($"\n🔧 Optimizing {selectedMeshes.Count} mesh(es)...");
                    
                    foreach (var item in selectedMeshes)
                    {
                        EditorUtility.DisplayProgressBar("Prefab Optimizer", 
                            $"Mesh: {item.mesh.name}", (float)processed / totalItems);
                        if (OptimizeMesh(item))
                            meshesOptimized++;
                        processed++;
                    }
                }

                // Optimize Audio
                if (selectedAudios.Count > 0)
                {
                    AddLog($"\n🔊 Optimizing {selectedAudios.Count} audio clip(s)...");
                    
                    long totalOriginalAudioSize = 0;
                    long totalOptimizedAudioSize = 0;
                    
                    foreach (var item in selectedAudios)
                    {
                        EditorUtility.DisplayProgressBar("Prefab Optimizer", 
                            $"Audio: {item.audioClip.name}", (float)processed / totalItems);
                        if (OptimizeAudio(item))
                        {
                            audiosOptimized++;
                            totalOriginalAudioSize += item.originalSize;
                            totalOptimizedAudioSize += item.estimatedSize;
                        }
                        processed++;
                    }
                    
                    AddLog($"✓ Audio optimization: {audiosOptimized}/{selectedAudios.Count}");
                    if (totalOriginalAudioSize > 0)
                    {
                        long savedAudio = totalOriginalAudioSize - totalOptimizedAudioSize;
                        AddLog($"💾 Estimated audio size reduction: {FormatBytes(savedAudio)}");
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AddLog("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog($"✅ OPTIMIZATION COMPLETED!");
            AddLog($"   Textures optimized: {texturesOptimized}/{selectedTextures.Count}");
            AddLog($"   Meshes optimized: {meshesOptimized}/{selectedMeshes.Count}");
            AddLog($"   Audio optimized: {audiosOptimized}/{selectedAudios.Count}");
            
            // Calculate total memory saved
            long totalOriginalMemory = 0;
            long totalOptimizedMemory = 0;
            foreach (var item in selectedTextures)
            {
                totalOriginalMemory += item.originalMemorySize;
                totalOptimizedMemory += item.optimizedMemorySize;
            }
            
            if (totalOriginalMemory > 0)
            {
                AddLog($"   Memory saved: {FormatBytes(totalOriginalMemory - totalOptimizedMemory)}");
            }
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // Force UI repaint to show updated values
            Repaint();

            EditorUtility.DisplayDialog("Success! 🎉", 
                $"Optimization completed!\n\n" +
                $"Textures: {texturesOptimized}/{selectedTextures.Count}\n" +
                $"Meshes: {meshesOptimized}/{selectedMeshes.Count}\n" +
                $"Audio: {audiosOptimized}/{selectedAudios.Count}\n\n" +
                $"Memory saved: {FormatBytes(totalOriginalMemory - totalOptimizedMemory)}", 
                "OK");
        }

        private bool OptimizeTexture(TextureItem item)
        {
            TextureImporter importer = AssetImporter.GetAtPath(item.path) as TextureImporter;
            if (importer == null) return false;

            bool modified = false;

            if (importer.maxTextureSize != maxTextureSize)
            {
                importer.maxTextureSize = maxTextureSize;
                modified = true;
            }

            if (importer.textureCompression != compressionQuality)
            {
                importer.textureCompression = compressionQuality;
                modified = true;
            }

            if (importer.crunchedCompression != useCrunchCompression)
            {
                importer.crunchedCompression = useCrunchCompression;
                modified = true;
            }

            if (useCrunchCompression && importer.compressionQuality != crunchCompressionQuality)
            {
                importer.compressionQuality = crunchCompressionQuality;
                modified = true;
            }

            if (importer.mipmapEnabled != generateMipmaps)
            {
                importer.mipmapEnabled = generateMipmaps;
                modified = true;
            }

            if (modified)
            {
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                Texture2D tex2D = item.texture as Texture2D;
                if (tex2D != null)
                {
                    item.optimizedMemorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex2D);
                }

                AddLog($"   ✓ {item.texture.name} ({FormatBytes(item.originalMemorySize)} → {FormatBytes(item.optimizedMemorySize)})");
                return true;
            }
            else
            {
                AddLog($"   ○ {item.texture.name} (already optimized)");
                return false;
            }
        }

        private bool OptimizeMesh(MeshItem item)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(item.path) as ModelImporter;
            if (modelImporter == null) return false;

            bool modified = false;

            // Use individual compression setting or global setting
            ModelImporterMeshCompression targetCompression = item.compression != ModelImporterMeshCompression.Off 
                ? item.compression 
                : meshCompression;

            if (modelImporter.meshCompression != targetCompression)
            {
                modelImporter.meshCompression = targetCompression;
                modified = true;
            }

            if (!modelImporter.optimizeMeshPolygons)
            {
                modelImporter.optimizeMeshPolygons = true;
                modified = true;
            }

            if (!modelImporter.optimizeMeshVertices)
            {
                modelImporter.optimizeMeshVertices = true;
                modified = true;
            }

            if (modified)
            {
                EditorUtility.SetDirty(modelImporter);
                modelImporter.SaveAndReimport();
                AddLog($"   ✓ {item.mesh.name} (Verts: {item.vertexCount}, Compression: {targetCompression})");
                return true;
            }
            else
            {
                AddLog($"   ○ {item.mesh.name} (already optimized)");
                return false;
            }
        }

        private bool OptimizeAudio(AudioItem item)
        {
            AudioImporter importer = AssetImporter.GetAtPath(item.path) as AudioImporter;
            if (importer == null) return false;

            bool modified = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;

            if (settings.loadType != audioLoadType)
            {
                settings.loadType = audioLoadType;
                modified = true;
            }

            if (settings.compressionFormat != audioCompressionFormat)
            {
                settings.compressionFormat = audioCompressionFormat;
                modified = true;
            }

            if (Mathf.Abs(settings.quality - audioQuality) > 0.01f)
            {
                settings.quality = audioQuality;
                modified = true;
            }

            if (settings.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate)
            {
                settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                settings.sampleRateOverride = (uint)audioSampleRate;
                modified = true;
            }

            if (importer.forceToMono != forceToMono)
            {
                importer.forceToMono = forceToMono;
                modified = true;
            }

            if (modified)
            {
                importer.defaultSampleSettings = settings;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                
                // Use estimation formula — source file size on disk doesn't change after reimport
                item.estimatedSize = CalculateEstimatedAudioSize(item);
                
                AddLog($"   ✓ {item.audioClip.name} ({FormatBytes(item.originalSize)} → ~{FormatBytes(item.estimatedSize)})");
                return true;
            }
            else
            {
                AddLog($"   ○ {item.audioClip.name} (already optimized)");
                return false;
            }
        }

        private long CalculateEstimatedAudioSize(AudioItem item)
        {
            // Formule approximative: (bitrate * durée * channels) / 8
            // Le bitrate est basé sur la qualité (0-1 → 0-320 kbps)
            int estimatedBitrate = Mathf.RoundToInt(audioQuality * 320000); // en bits/sec
            int channelCount = forceToMono ? 1 : item.channels;
            
            // Si c'est du PCM non compressé
            if (audioCompressionFormat == AudioCompressionFormat.PCM)
            {
                // PCM: sampleRate * bitDepth * channels * length / 8
                return (long)(audioSampleRate * 16 * channelCount * item.length / 8);
            }
            
            // Pour les formats compressés (Vorbis, MP3, ADPCM)
            long estimatedSize = (long)((estimatedBitrate * item.length * channelCount) / 8);
            
            // Ajouter un overhead pour les métadonnées (environ 5%)
            estimatedSize = (long)(estimatedSize * 1.05f);
            
            return estimatedSize;
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

        private string GetCompressionFormat(TextureImporter importer)
        {
            if (importer == null) return "Unknown";
            
            TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
            
            if (settings.format != TextureImporterFormat.Automatic)
            {
                return settings.format.ToString();
            }
            
            bool hasAlpha = importer.DoesSourceTextureHaveAlpha();
            
            if (importer.textureCompression == TextureImporterCompression.Uncompressed)
            {
                return hasAlpha ? "RGBA Uncompressed" : "RGB Uncompressed";
            }
            else if (importer.crunchedCompression)
            {
                return hasAlpha ? "RGBA DXT5/BC3 Crunch" : "RGB DXT1/BC1 Crunch";
            }
            else
            {
                return hasAlpha ? "RGBA Compressed DXT5/BC3" : "RGB Compressed DXT1/BC1";
            }
        }
    }
}
