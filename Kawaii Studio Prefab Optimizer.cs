using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System;

namespace KawaiiStudio
{
    [Serializable]
    public class PrefabTranslationEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class PrefabTranslationFile
    {
        public List<PrefabTranslationEntry> entries;
    }

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
        // Version
        private const string VERSION = "2.0";
        
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
        
        // Language
        private const string LANGUAGES_FOLDER = "Assets/Kawaii Studio/Languages";
        private const string PREFS_LANGUAGE = "KawaiiStudio.Language";
        private Dictionary<string, string> translations = new Dictionary<string, string>();
        private string currentLanguage = "en";
        
        // Lists
        private List<TextureItem> textureItems = new List<TextureItem>();
        private List<MeshItem> meshItems = new List<MeshItem>();
        private List<AudioItem> audioItems = new List<AudioItem>();
        
        // UI State
        private Vector2 scrollPosition;
        private Vector2 logScrollPosition;
        private Vector2 textureScrollPosition;
        private Vector2 meshScrollPosition;
        private Vector2 audioScrollPosition;
        private string logOutput = "";
        private bool showTextures = false;
        private bool showMeshes = false;
        private bool showAudio = false;
        private bool scanned = false;
        
        // UI Styles
        private GUIStyle headerStyle;
        private GUIStyle buttonStyle;
        private GUIStyle logStyle;
        private GUIStyle toggleStyle;
        private Texture2D purpleTexture;
        private Texture2D redTexture;
        private Texture2D blackTexture;
        private Texture2D greenTexture;
        private bool stylesInitialized = false;
        
        // Stats
        private long originalSize = 0;
        private long optimizedSize = 0;

        [MenuItem("Kawaii Studio/Prefab Optimizer")]
        public static void ShowWindow()
        {
            PrefabOptimizer window = GetWindow<PrefabOptimizer>("Prefab Optimizer");
            window.minSize = new Vector2(900, 750);
            window.Show();
        }

        private void OnEnable()
        {
            LoadLanguage();
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
                    PrefabTranslationFile translationFile = JsonUtility.FromJson<PrefabTranslationFile>(jsonContent);
                    
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
                { "prefab", "PREFAB" },
                { "drag_prefab", "Drag Prefab Here:" },
                { "scan_prefab", "SCAN PREFAB" },
                { "texture_settings", "TEXTURE COMPRESSION SETTINGS" },
                { "max_size", "Max Texture Size:" },
                { "compression", "Compression Quality:" },
                { "crunch_compression", "Use Crunch Compression:" },
                { "generate_mipmaps", "Generate Mipmaps:" },
                { "mesh_compression", "MESH COMPRESSION SETTINGS" },
                { "mesh_compression_level", "Mesh Compression:" },
                { "audio_compression", "AUDIO COMPRESSION SETTINGS" },
                { "load_type", "Load Type:" },
                { "compression_format", "Compression Format:" },
                { "quality", "Quality:" },
                { "sample_rate", "Sample Rate:" },
                { "force_to_mono", "Force To Mono:" },
                { "optimize", "OPTIMIZE" },
                { "select_all", "Select All" },
                { "none", "None" },
                { "log", "LOG OUTPUT" }
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

            purpleTexture = MakeTex(2, 2, new Color(0.486f, 0.227f, 0.929f, 1f));
            redTexture = MakeTex(2, 2, new Color(1f, 0.278f, 0.341f, 1f));
            blackTexture = MakeTex(2, 2, new Color(0.039f, 0.039f, 0.059f, 1f));
            greenTexture = MakeTex(2, 2, new Color(0f, 1f, 0.255f, 1f));

            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 20,
                alignment = TextAnchor.MiddleCenter,
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
                fixedHeight = 50
            };

            logStyle = new GUIStyle(EditorStyles.textArea)
            {
                normal = { background = blackTexture, textColor = new Color(0f, 1f, 0.255f, 1f) },
                fontSize = 10,
                wordWrap = true
            };

            toggleStyle = new GUIStyle(EditorStyles.toggle)
            {
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

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical();
            GUILayout.Space(10);

            // Header
            DrawHeader();
            GUILayout.Space(15);

            // Avatar Selection
            DrawAvatarSelection();
            GUILayout.Space(10);

            // Scan Button
            if (!scanned || prefab == null)
            {
                DrawScanButton();
            }

            if (scanned && prefab != null)
            {
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

            GUILayout.Space(10);
            DrawFooter();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            GUILayout.Label($"✨ {T("prefab").ToUpper()} OPTIMIZER ✨", headerStyle);
        }

        private void DrawAvatarSelection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"🎭 {T("prefab")}", labelStyle);
            
            GUILayout.Space(5);
            GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(T("drag_prefab"), prefab, typeof(GameObject), true);
            
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
                GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                GUILayout.Label($"✓ Selected: {prefab.name}", infoStyle);
            }
            
            GUILayout.EndVertical();
        }

        private void DrawScanButton()
        {
            GUI.enabled = prefab != null;
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button($"🔍 {T("scan_prefab")}", buttonStyle, GUILayout.Width(300)))
            {
                ScanPrefab();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUI.enabled = true;
        }

        private void DrawTextureSettings()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"🎨 {T("texture_settings")}", labelStyle);
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("max_size"), GUILayout.Width(150));
            maxTextureSize = EditorGUILayout.IntPopup(maxTextureSize, 
                new string[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" },
                new int[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 });
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("compression"), GUILayout.Width(150));
            compressionQuality = (TextureImporterCompression)EditorGUILayout.EnumPopup(compressionQuality);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("crunch_compression"), GUILayout.Width(150));
            useCrunchCompression = EditorGUILayout.Toggle(useCrunchCompression);
            GUILayout.EndHorizontal();
            
            if (useCrunchCompression)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.Label(T("quality"), GUILayout.Width(130));
                crunchCompressionQuality = EditorGUILayout.IntSlider(crunchCompressionQuality, 0, 100);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("generate_mipmaps"), GUILayout.Width(150));
            generateMipmaps = EditorGUILayout.Toggle(generateMipmaps);
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        private void DrawTextureList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            
            GUILayout.BeginHorizontal();
            showTextures = EditorGUILayout.Foldout(showTextures, $"🖼️ TEXTURES ({textureItems.Count})", true, labelStyle);
            
            if (textureItems.Count > 0)
            {
                if (GUILayout.Button(T("select_all"), GUILayout.Width(80)))
                {
                    foreach (var item in textureItems) item.selected = true;
                }
                if (GUILayout.Button(T("none"), GUILayout.Width(60)))
                {
                    foreach (var item in textureItems) item.selected = false;
                }
            }
            
            GUILayout.EndHorizontal();
            
            if (showTextures && textureItems.Count > 0)
            {
                GUILayout.Space(5);
                
                // Header
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(20));
                GUILayout.Label("Texture", GUILayout.Width(200));
                GUILayout.Label("Resolution", GUILayout.Width(100));
                GUILayout.Label("Format", GUILayout.Width(150));
                GUILayout.Label("Mipmaps", GUILayout.Width(70));
                GUILayout.Label("Memory (Original)", GUILayout.Width(100));
                GUILayout.Label("Memory (Optimized)", GUILayout.Width(100));
                GUILayout.EndHorizontal();
                
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 1), new Color(0.486f, 0.227f, 0.929f, 1f));
                
                textureScrollPosition = GUILayout.BeginScrollView(textureScrollPosition, GUILayout.Height(200));
                
                GUIStyle textureStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                
                GUIStyle optimizedStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(1f, 0.647f, 0f, 1f) },
                    fontStyle = FontStyle.Bold
                };
                
                foreach (var item in textureItems)
                {
                    GUILayout.BeginHorizontal();
                    
                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                    
                    EditorGUILayout.ObjectField(item.texture, typeof(Texture), false, GUILayout.Width(200));
                    
                    GUILayout.Label($"{item.resolution.x}x{item.resolution.y}", textureStyle, GUILayout.Width(100));
                    GUILayout.Label(item.compressionFormat, textureStyle, GUILayout.Width(150));
                    GUILayout.Label(item.hasMipmaps ? "Yes" : "No", textureStyle, GUILayout.Width(70));
                    GUILayout.Label($"{FormatBytes(item.originalMemorySize)}", textureStyle, GUILayout.Width(100));
                    
                    if (item.optimizedMemorySize > 0)
                    {
                        GUILayout.Label($"{FormatBytes(item.optimizedMemorySize)}", optimizedStyle, GUILayout.Width(100));
                        
                        if (item.originalMemorySize > item.optimizedMemorySize)
                        {
                            float percentSaved = ((float)(item.originalMemorySize - item.optimizedMemorySize) / item.originalMemorySize) * 100f;
                            GUILayout.Label($"(-{percentSaved:F1}%)", optimizedStyle, GUILayout.Width(70));
                        }
                        else if (item.originalMemorySize == item.optimizedMemorySize)
                        {
                            GUILayout.Label("(No change)", optimizedStyle, GUILayout.Width(70));
                        }
                    }
                    else
                    {
                        GUILayout.Label("-", textureStyle, GUILayout.Width(100));
                    }
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.EndScrollView();
            }
            
            GUILayout.EndVertical();
        }

        private void DrawMeshSettings()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"🔧 {T("mesh_compression")}", labelStyle);
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("mesh_compression_level"), GUILayout.Width(150));
            meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(meshCompression);
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        private void DrawMeshList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            
            GUILayout.BeginHorizontal();
            showMeshes = EditorGUILayout.Foldout(showMeshes, $"📐 MESHES ({meshItems.Count})", true, labelStyle);
            
            if (meshItems.Count > 0)
            {
                if (GUILayout.Button(T("select_all"), GUILayout.Width(80)))
                {
                    foreach (var item in meshItems) item.selected = true;
                }
                if (GUILayout.Button(T("none"), GUILayout.Width(60)))
                {
                    foreach (var item in meshItems) item.selected = false;
                }
            }
            
            GUILayout.EndHorizontal();
            
            if (showMeshes && meshItems.Count > 0)
            {
                GUILayout.Space(5);
                
                // Header
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(20));
                GUILayout.Label("Mesh", GUILayout.Width(200));
                GUILayout.Label("Vertices", GUILayout.Width(100));
                GUILayout.Label("Triangles", GUILayout.Width(100));
                GUILayout.Label("Compression", GUILayout.Width(200));
                GUILayout.EndHorizontal();
                
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 1), new Color(0.486f, 0.227f, 0.929f, 1f));
                
                meshScrollPosition = GUILayout.BeginScrollView(meshScrollPosition, GUILayout.Height(200));
                
                GUIStyle meshStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                
                foreach (var item in meshItems)
                {
                    GUILayout.BeginHorizontal();
                    
                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                    
                    EditorGUILayout.ObjectField(item.mesh, typeof(Mesh), false, GUILayout.Width(200));
                    
                    GUILayout.Label($"Verts: {item.vertexCount}", meshStyle, GUILayout.Width(100));
                    GUILayout.Label($"Tris: {item.triangleCount}", meshStyle, GUILayout.Width(100));
                    
                    GUILayout.Label("Compression:", meshStyle, GUILayout.Width(90));
                    item.compression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(item.compression, GUILayout.Width(100));
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.EndScrollView();
            }
            
            GUILayout.EndVertical();
        }

        private void DrawAudioSettings()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"🔊 {T("audio_compression")}", labelStyle);
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("load_type"), GUILayout.Width(150));
            audioLoadType = (AudioClipLoadType)EditorGUILayout.EnumPopup(audioLoadType);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("compression_format"), GUILayout.Width(150));
            audioCompressionFormat = (AudioCompressionFormat)EditorGUILayout.EnumPopup(audioCompressionFormat);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{T("quality")} {(audioQuality * 100):F0}%", GUILayout.Width(150));
            audioQuality = EditorGUILayout.Slider(audioQuality, 0.01f, 1f);
            GUILayout.EndHorizontal();
            
            // Info sur le bitrate estimé
            int estimatedBitrate = Mathf.RoundToInt(audioQuality * 320);
            GUILayout.BeginHorizontal();
            GUILayout.Space(20);
            GUIStyle infoStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
            };
            GUILayout.Label($"≈ {estimatedBitrate} kbps", infoStyle);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("sample_rate"), GUILayout.Width(150));
            audioSampleRate = EditorGUILayout.IntPopup(audioSampleRate, 
                new string[] { "8000 Hz", "11025 Hz", "22050 Hz", "44100 Hz", "48000 Hz" },
                new int[] { 8000, 11025, 22050, 44100, 48000 });
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("force_to_mono"), GUILayout.Width(150));
            forceToMono = EditorGUILayout.Toggle(forceToMono);
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        private void DrawAudioList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            
            GUILayout.BeginHorizontal();
            showAudio = EditorGUILayout.Foldout(showAudio, $"🔊 AUDIO CLIPS ({audioItems.Count})", true, labelStyle);
            
            if (audioItems.Count > 0)
            {
                if (GUILayout.Button(T("select_all"), GUILayout.Width(80)))
                {
                    foreach (var item in audioItems) item.selected = true;
                }
                if (GUILayout.Button(T("none"), GUILayout.Width(60)))
                {
                    foreach (var item in audioItems) item.selected = false;
                }
            }
            
            GUILayout.EndHorizontal();
            
            if (showAudio && audioItems.Count > 0)
            {
                GUILayout.Space(5);
                
                // Header
                GUILayout.BeginHorizontal();
                GUILayout.Label("", GUILayout.Width(20));
                GUILayout.Label("Audio Clip", GUILayout.Width(200));
                GUILayout.Label("Length", GUILayout.Width(80));
                GUILayout.Label("Channels", GUILayout.Width(70));
                GUILayout.Label("Frequency", GUILayout.Width(80));
                GUILayout.Label("Format", GUILayout.Width(100));
                GUILayout.Label("Original Size", GUILayout.Width(100));
                GUILayout.Label("Estimated Size", GUILayout.Width(100));
                GUILayout.Label("Reduction", GUILayout.Width(80));
                GUILayout.EndHorizontal();
                
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 1), new Color(0.486f, 0.227f, 0.929f, 1f));
                
                audioScrollPosition = GUILayout.BeginScrollView(audioScrollPosition, GUILayout.Height(200));
                
                GUIStyle audioStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                
                GUIStyle optimizedStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(1f, 0.647f, 0f, 1f) },
                    fontStyle = FontStyle.Bold
                };
                
                foreach (var item in audioItems)
                {
                    GUILayout.BeginHorizontal();
                    
                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                    
                    EditorGUILayout.ObjectField(item.audioClip, typeof(AudioClip), false, GUILayout.Width(200));
                    
                    GUILayout.Label($"{item.length:F2}s", audioStyle, GUILayout.Width(80));
                    GUILayout.Label($"{item.channels}ch", audioStyle, GUILayout.Width(70));
                    GUILayout.Label($"{item.frequency} Hz", audioStyle, GUILayout.Width(80));
                    GUILayout.Label($"{item.compressionFormat}", audioStyle, GUILayout.Width(100));
                    GUILayout.Label($"{FormatBytes(item.originalSize)}", audioStyle, GUILayout.Width(100));
                    
                    // Calculer la taille estimée après compression
                    long estimatedSize = CalculateEstimatedAudioSize(item);
                    GUILayout.Label($"{FormatBytes(estimatedSize)}", optimizedStyle, GUILayout.Width(100));
                    
                    // Afficher la réduction estimée
                    if (item.originalSize > 0)
                    {
                        float percentSaved = ((float)(item.originalSize - estimatedSize) / item.originalSize) * 100f;
                        if (percentSaved > 0)
                        {
                            GUILayout.Label($"(-{percentSaved:F1}%)", optimizedStyle, GUILayout.Width(80));
                        }
                        else
                        {
                            GUILayout.Label("(+)", audioStyle, GUILayout.Width(80));
                        }
                    }
                    
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                
                GUILayout.EndScrollView();
            }
            
            GUILayout.EndVertical();
        }

        private void DrawOptimizeButton()
        {
            int selectedCount = textureItems.Count(t => t.selected) + meshItems.Count(m => m.selected) + audioItems.Count(a => a.selected);
            GUI.enabled = selectedCount > 0;
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button($"⚡ {T("optimize")} ({selectedCount} items)", buttonStyle, GUILayout.Width(300)))
            {
                OptimizeAvatar();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUI.enabled = true;
        }

        private void DrawLogOutput()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label($"📋 {T("log")}", labelStyle);
            
            GUILayout.Space(5);
            
            logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(150));
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
            showTextures = true;
            showMeshes = true;
            showAudio = true;
        }

        private void OptimizeAvatar()
        {
            logOutput = "";
            originalSize = 0;
            optimizedSize = 0;

            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("🚀 Starting Optimization...");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

            int texturesOptimized = 0;
            int meshesOptimized = 0;
            int audiosOptimized = 0;

            // Optimize Textures
            var selectedTextures = textureItems.Where(t => t.selected).ToList();
            if (selectedTextures.Count > 0)
            {
                AddLog($"\n🎨 Optimizing {selectedTextures.Count} texture(s)...");
                
                foreach (var item in selectedTextures)
                {
                    if (OptimizeTexture(item))
                        texturesOptimized++;
                }
            }

            // Optimize Meshes
            var selectedMeshes = meshItems.Where(m => m.selected).ToList();
            if (selectedMeshes.Count > 0)
            {
                AddLog($"\n🔧 Optimizing {selectedMeshes.Count} mesh(es)...");
                
                foreach (var item in selectedMeshes)
                {
                    if (OptimizeMesh(item))
                        meshesOptimized++;
                }
            }

            // Optimize Audio
            var selectedAudios = audioItems.Where(x => x.selected).ToList();
            if (selectedAudios.Count > 0)
            {
                AddLog($"\n🔊 Optimizing {selectedAudios.Count} audio clip(s)...");
                
                long totalOriginalAudioSize = 0;
                long totalOptimizedAudioSize = 0;
                
                foreach (var item in selectedAudios)
                {
                    if (OptimizeAudio(item))
                    {
                        audiosOptimized++;
                        totalOriginalAudioSize += item.originalSize;
                        totalOptimizedAudioSize += item.estimatedSize;
                    }
                }
                
                AddLog($"✓ Audio optimization: {audiosOptimized}/{selectedAudios.Count}");
                if (totalOriginalAudioSize > 0)
                {
                    long savedAudio = totalOriginalAudioSize - totalOptimizedAudioSize;
                    AddLog($"💾 Estimated audio size reduction: {FormatBytes(savedAudio)}");
                }
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
            FileInfo fileInfo = new FileInfo(item.path);
            originalSize += fileInfo.Length;

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

                // Get updated memory size after reimport
                Texture2D tex2D = item.texture as Texture2D;
                if (tex2D != null)
                {
                    item.optimizedMemorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex2D);
                }

                FileInfo newFileInfo = new FileInfo(item.path);
                optimizedSize += newFileInfo.Length;

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
                
                // Recalculer la taille après optimisation
                FileInfo fileInfo = new FileInfo(item.path);
                item.estimatedSize = fileInfo.Exists ? fileInfo.Length : CalculateEstimatedAudioSize(item);
                
                AddLog($"   ✓ {item.audioClip.name} ({FormatBytes(item.originalSize)} → {FormatBytes(item.estimatedSize)})");
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
            
            string format = "RGB";
            if (settings.format != TextureImporterFormat.Automatic)
            {
                format = settings.format.ToString();
            }
            else
            {
                // Try to determine based on compression settings
                if (importer.textureCompression == TextureImporterCompression.Uncompressed)
                {
                    format = "RGB Uncompressed";
                }
                else if (importer.crunchedCompression)
                {
                    format = $"RGB Compressed DXT1/BC1 Crunch";
                }
                else
                {
                    format = "RGB Compressed DXT1/BC1";
                }
            }
            
            return format;
        }
    }
}
