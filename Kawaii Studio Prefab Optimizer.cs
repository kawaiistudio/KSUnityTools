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
        
        // Lists
        private List<TextureItem> textureItems = new List<TextureItem>();
        private List<MeshItem> meshItems = new List<MeshItem>();
        
        // UI State
        private Vector2 scrollPosition;
        private Vector2 textureScrollPosition;
        private Vector2 meshScrollPosition;
        private string logOutput = "";
        private bool showTextures = false;
        private bool showMeshes = false;
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
            PrefabOptimizer window = GetWindow<PrefabOptimizer>("Prefab Optimizer v1.1");
            window.minSize = new Vector2(900, 750);
            window.Show();
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
                GUILayout.Space(15);

                // Optimize Button
                DrawOptimizeButton();
                GUILayout.Space(10);
            }

            // Log Output
            DrawLog();
            GUILayout.Space(10);

            // Footer
            DrawFooter();
            GUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            GUILayout.Label("⚡ PREFAB OPTIMIZER v1.1 ⚡", headerStyle);
            
            GUIStyle subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(1f, 0.278f, 0.341f, 1f) }
            };
            GUILayout.Label("Texture & Mesh Compression Manager", subtitleStyle);
            
            EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 40, 2), new Color(0.486f, 0.227f, 0.929f, 1f));
        }

        private void DrawAvatarSelection()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label("🎭 PREFAB", labelStyle);
            
            GUILayout.Space(5);
            GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField("Drag Prefab Here:", prefab, typeof(GameObject), true);
            
            if (newPrefab != prefab)
            {
                prefab = newPrefab;
                scanned = false;
                textureItems.Clear();
                meshItems.Clear();
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
            
            if (GUILayout.Button("🔍 SCAN PREFAB", buttonStyle, GUILayout.Width(300)))
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
            GUILayout.Label("🎨 TEXTURE COMPRESSION SETTINGS", labelStyle);
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Max Texture Size:", GUILayout.Width(150));
            maxTextureSize = EditorGUILayout.IntPopup(maxTextureSize, 
                new string[] { "128", "256", "512", "1024", "2048", "4096" },
                new int[] { 128, 256, 512, 1024, 2048, 4096 });
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Compression:", GUILayout.Width(150));
            compressionQuality = (TextureImporterCompression)EditorGUILayout.EnumPopup(compressionQuality);
            GUILayout.EndHorizontal();
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Use Crunch Compression:", GUILayout.Width(150));
            useCrunchCompression = EditorGUILayout.Toggle(useCrunchCompression);
            GUILayout.EndHorizontal();
            
            if (useCrunchCompression)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(20);
                GUILayout.Label("Crunch Quality:", GUILayout.Width(130));
                crunchCompressionQuality = EditorGUILayout.IntSlider(crunchCompressionQuality, 0, 100);
                GUILayout.EndHorizontal();
            }
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Generate Mipmaps:", GUILayout.Width(150));
            generateMipmaps = EditorGUILayout.Toggle(generateMipmaps);
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        private void DrawTextureList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.BeginHorizontal();
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            
            string arrow = showTextures ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} TEXTURES FOUND: {textureItems.Count}", EditorStyles.boldLabel))
            {
                showTextures = !showTextures;
            }
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Select All", GUILayout.Width(80)))
            {
                foreach (var item in textureItems) item.selected = true;
            }
            
            if (GUILayout.Button("Select None", GUILayout.Width(80)))
            {
                foreach (var item in textureItems) item.selected = false;
            }
            
            GUILayout.EndHorizontal();
            
            if (showTextures && textureItems.Count > 0)
            {
                GUILayout.Space(5);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 60, 1), new Color(0.486f, 0.227f, 0.929f, 0.5f));
                GUILayout.Space(5);
                
                textureScrollPosition = GUILayout.BeginScrollView(textureScrollPosition, GUILayout.Height(200));
                
                GUIStyle textureStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0f, 1f, 0.255f, 1f) }
                };
                
                GUIStyle optimizedStyle = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.5f, 0.8f, 1f, 1f) }
                };
                
                GUIStyle headerColumnStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) },
                    fontStyle = FontStyle.Bold
                };
                
                // Column headers
                GUILayout.BeginHorizontal();
                GUILayout.Space(20); // Checkbox space
                GUILayout.Label("", GUILayout.Width(200)); // Texture preview space
                GUILayout.Label("Resolution", headerColumnStyle, GUILayout.Width(100));
                GUILayout.Label("Original Size", headerColumnStyle, GUILayout.Width(100));
                GUILayout.Label("Size After Opt.", headerColumnStyle, GUILayout.Width(100));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                
                GUILayout.Space(3);
                
                foreach (var item in textureItems)
                {
                    GUILayout.BeginHorizontal();
                    
                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20));
                    
                    // Texture preview with tooltip
                    Rect textureRect = GUILayoutUtility.GetRect(200, 18);
                    EditorGUI.ObjectField(textureRect, item.texture, typeof(Texture), false);
                    
                    // Show compression info on hover
                    if (textureRect.Contains(Event.current.mousePosition))
                    {
                        string tooltip = $"{item.texture.name}\n" +
                                       $"{item.resolution.x}x{item.resolution.y} {item.compressionFormat}\n" +
                                       $"Mipmaps: {(item.hasMipmaps ? "Yes" : "No")}\n" +
                                       $"File Size: {FormatBytes(item.originalSize)}\n" +
                                       $"Memory Size: {FormatBytes(item.originalMemorySize)}";
                        GUI.tooltip = tooltip;
                    }
                    
                    GUILayout.Label($"{item.resolution.x}x{item.resolution.y}", textureStyle, GUILayout.Width(100));
                    
                    // Show original FILE size (on disk)
                    GUILayout.Label($"{FormatBytes(item.originalSize)}", textureStyle, GUILayout.Width(100));
                    
                    // Show optimized MEMORY size if optimization has been performed
                    if (item.optimizedMemorySize > 0)
                    {
                        GUILayout.Label($"{FormatBytes(item.optimizedMemorySize)}", optimizedStyle, GUILayout.Width(100));
                        
                        // Calculate and show percentage saved based on memory
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
            GUILayout.Label("🔧 MESH COMPRESSION SETTINGS", labelStyle);
            
            GUILayout.Space(5);
            
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mesh Compression:", GUILayout.Width(150));
            meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(meshCompression);
            GUILayout.EndHorizontal();
            
            GUILayout.EndVertical();
        }

        private void DrawMeshList()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            
            GUILayout.BeginHorizontal();
            GUIStyle labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            
            string arrow = showMeshes ? "▼" : "▶";
            if (GUILayout.Button($"{arrow} FBX MESHES FOUND: {meshItems.Count}", EditorStyles.boldLabel))
            {
                showMeshes = !showMeshes;
            }
            
            GUILayout.FlexibleSpace();
            
            if (GUILayout.Button("Select All", GUILayout.Width(80)))
            {
                foreach (var item in meshItems) item.selected = true;
            }
            
            if (GUILayout.Button("Select None", GUILayout.Width(80)))
            {
                foreach (var item in meshItems) item.selected = false;
            }
            
            GUILayout.EndHorizontal();
            
            if (showMeshes && meshItems.Count > 0)
            {
                GUILayout.Space(5);
                EditorGUI.DrawRect(GUILayoutUtility.GetRect(position.width - 60, 1), new Color(0.486f, 0.227f, 0.929f, 0.5f));
                GUILayout.Space(5);
                
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

        private void DrawOptimizeButton()
        {
            int selectedTextures = textureItems.Count(t => t.selected);
            int selectedMeshes = meshItems.Count(m => m.selected);
            
            GUI.enabled = selectedTextures > 0 || selectedMeshes > 0;
            
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            
            string buttonText = $"🚀 OPTIMIZE ({selectedTextures} Textures, {selectedMeshes} Meshes)";
            if (GUILayout.Button(buttonText, buttonStyle, GUILayout.Width(500)))
            {
                OptimizeAvatar();
            }
            
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            
            GUI.enabled = true;
        }

        private void DrawLog()
        {
            GUIStyle logLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = new Color(0.486f, 0.227f, 0.929f, 1f) }
            };
            GUILayout.Label("[ OPTIMIZATION LOG ]", logLabelStyle);
            
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, logStyle, GUILayout.Height(150));
            GUILayout.Label(logOutput, logStyle);
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
            logOutput += message + "\n";
            scrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private void ScanPrefab()
        {
            if (prefab == null) return;

            logOutput = "";
            textureItems.Clear();
            meshItems.Clear();

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
                if (meshFilter != null) mesh = meshFilter.sharedMesh;
                else if (skinnedMesh != null) mesh = skinnedMesh.sharedMesh;

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

            AddLog($"✓ Found {textureItems.Count} texture(s)");
            AddLog($"✓ Found {meshItems.Count} FBX mesh(es)");
            AddLog("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog("✅ Scan completed! Review and optimize.");

            scanned = true;
            showTextures = true;
            showMeshes = true;
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
                
                // Force a complete refresh after all textures are optimized
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                
                // Update memory sizes after refresh
                AddLog("\n📊 Updating memory sizes...");
                foreach (var item in selectedTextures)
                {
                    Texture2D reloadedTex = AssetDatabase.LoadAssetAtPath<Texture2D>(item.path);
                    if (reloadedTex != null)
                    {
                        item.optimizedMemorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(reloadedTex);
                        AddLog($"   → {item.texture.name}: {FormatBytes(item.optimizedMemorySize)}");
                    }
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

            AddLog("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            AddLog($"✅ OPTIMIZATION COMPLETED!");
            AddLog($"   Textures optimized: {texturesOptimized}/{selectedTextures.Count}");
            AddLog($"   Meshes optimized: {meshesOptimized}/{selectedMeshes.Count}");
            
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

            // Force UI repaint to show updated values
            Repaint();

            EditorUtility.DisplayDialog("Success! 🎉", 
                $"Optimization completed!\n\n" +
                $"Textures: {texturesOptimized}/{selectedTextures.Count}\n" +
                $"Meshes: {meshesOptimized}/{selectedMeshes.Count}\n\n" +
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
                
                fileInfo = new FileInfo(item.path);
                long newSize = fileInfo.Exists ? fileInfo.Length : 0;
                item.optimizedSize = newSize;
                optimizedSize += newSize;
                
                AddLog($"   ✓ {item.texture.name} ({item.resolution.x}x{item.resolution.y}) - Reimporting...");
                return true;
            }
            else
            {
                item.optimizedSize = fileInfo.Length;
                item.optimizedMemorySize = item.originalMemorySize;
                optimizedSize += fileInfo.Length;
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