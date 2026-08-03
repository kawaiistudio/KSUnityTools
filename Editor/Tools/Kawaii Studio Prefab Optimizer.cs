using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System;

namespace KawaiiStudio
{
    // PrefabTranslationEntry / PrefabTranslationFile removed: they were byte-for-byte
    // duplicates of the pairs in the Manager and the GLB converter, all reading the
    // same JSON. See KSTranslationEntry in Editor/Core/KawaiiStudioLocalization.cs.

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
        // True once this texture has actually been through an optimization pass,
        // so the UI can tell "0 bytes measured" apart from "not measured yet".
        public bool hasOptimizationResult;
    }

    public class MeshItem
    {
        public Mesh mesh;
        public bool selected;
        public string path;
        public ModelImporterMeshCompression compression;
        public int vertexCount;
        public int triangleCount;
        // When true the global mesh compression setting is applied to this mesh.
        // Without this flag a per-mesh value of "Off" is indistinguishable from
        // "unset" and can never actually be applied.
        public bool useGlobalCompression = true;
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
        private const string VERSION = KawaiiStudioVersion.Current;
        
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
        
        // Language state lives in KawaiiStudioLocalization. The hardcoded
        // "Assets/Kawaii Studio/Languages" literal that used to sit here also broke
        // any install under Packages/.

        // Lists
        private List<TextureItem> textureItems = new List<TextureItem>();
        private List<MeshItem> meshItems = new List<MeshItem>();
        private List<AudioItem> audioItems = new List<AudioItem>();
        
        // UI State
        private Vector2 scrollPosition;
        // Which results table is showing (0 textures, 1 meshes, 2 audio).
        private int resultsTab = 0;
        private Vector2 logScrollPosition;
        private Vector2 textureScrollPosition;
        private Vector2 meshScrollPosition;
        private Vector2 audioScrollPosition;
        private string logOutput = "";
        private readonly StringBuilder logBuilder = new StringBuilder();
        // showTextures / showMeshes / showAudio removed: the three foldouts were
        // replaced by the Textures / Meshes / Audio tabs.
        private bool scanned = false;
        
        // All styling now comes from KawaiiStudioGUI: one palette, one spacing
        // scale, and it follows the dark/light editor skin. The hand-rolled styles
        // and their leaked Texture2D allocations that used to live here are gone.

        // Stats
        private long originalSize = 0;
        private long optimizedSize = 0;

        [MenuItem("Kawaii Studio/Prefab Optimizer")]
        public static void ShowWindow()
        {
            PrefabOptimizer window = GetWindow<PrefabOptimizer>("Prefab Optimizer");
            window.minSize = new Vector2(760, 620);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Prefab Optimizer");
            LoadLanguage();
        }

        // Translations are shared by the whole toolset now. This tool used to carry
        // its own [Serializable] entry/file pair, its own dictionary, its own JSON
        // reader and a 20-key hardcoded English fallback - all duplicated in the
        // other tools and all reading the very same files.
        private void LoadLanguage() => KawaiiStudioLocalization.Reload();

        private string T(string key) => KawaiiStudioLocalization.T(key);

        private string FormatBytes(long bytes) => KawaiiStudioUtil.FormatBytes(bytes);

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            GUILayout.BeginVertical();

            KawaiiStudioGUI.DrawBanner(
                "Prefab Optimizer",
                "Compress textures, meshes and audio on a prefab or avatar",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner);

            GUILayout.Space(KawaiiStudioGUI.Space2);

            DrawSourceSection();

            if (!scanned || prefab == null)
            {
                DrawScanButton();
            }
            else
            {
                DrawSummary();
                DrawResults();
                DrawOptimizeButton();
                DrawLogOutput();
            }

            KawaiiStudioGUI.DrawFooter();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        /// <summary>Step 1: pick the prefab, with inline validation rather than a dialog.</summary>
        private void DrawSourceSection()
        {
            KawaiiStudioGUI.DrawSection(T("prefab"), () =>
            {
                GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(
                    T("drag_prefab"), prefab, typeof(GameObject), true);

                if (newPrefab != prefab)
                {
                    prefab = newPrefab;
                    scanned = false;
                    textureItems.Clear();
                    meshItems.Clear();
                    audioItems.Clear();
                    ClearLog();
                }

                GUILayout.Space(KawaiiStudioGUI.Space2);

                // Inline validation: the scan button used to be silently disabled with
                // no indication of why.
                if (prefab == null)
                {
                    KawaiiStudioGUI.Banner(
                        "Drag a prefab or a scene avatar into the field above to begin.",
                        KawaiiStudioGUI.MessageKind.Info);
                }
                else
                {
                    KawaiiStudioGUI.KeyValueRow("Selected", prefab.name, KawaiiStudioGUI.SuccessColor);
                    if (scanned)
                    {
                        KawaiiStudioGUI.KeyValueRow("Status",
                            $"{textureItems.Count + meshItems.Count + audioItems.Count} assets found",
                            KawaiiStudioGUI.SubTextColor);
                    }
                }
            });
        }

        private void DrawScanButton()
        {
            GUILayout.Space(KawaiiStudioGUI.Space2);
            using (new EditorGUI.DisabledScope(prefab == null))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (KawaiiStudioGUI.PrimaryButton(T("scan_prefab"), GUILayout.Width(280f)))
                {
                    ScanPrefab();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }
            GUILayout.Space(KawaiiStudioGUI.Space2);
        }

        /// <summary>Scan results at a glance, before any of the detail tables.</summary>
        private void DrawSummary()
        {
            long originalMemory = 0;
            long projectedMemory = 0;
            foreach (TextureItem item in textureItems)
            {
                originalMemory += item.originalMemorySize;
                projectedMemory += GetEffectiveOptimizedTextureMemory(item);
            }
            long saved = originalMemory - projectedMemory;

            EditorGUILayout.BeginHorizontal();
            KawaiiStudioGUI.StatTile(textureItems.Count.ToString(), "Textures", KawaiiStudioGUI.AccentColor);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            KawaiiStudioGUI.StatTile(meshItems.Count.ToString(), "Meshes", KawaiiStudioGUI.AccentColor);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            KawaiiStudioGUI.StatTile(audioItems.Count.ToString(), "Audio clips", KawaiiStudioGUI.AccentColor);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            KawaiiStudioGUI.StatTile(
                saved > 0 ? FormatBytes(saved) : "—",
                "Memory saved",
                saved > 0 ? KawaiiStudioGUI.SuccessColor : KawaiiStudioGUI.SubTextColor);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(KawaiiStudioGUI.Space3);
        }

        /// <summary>
        /// Textures / Meshes / Audio as tabs. They used to be six stacked sections
        /// in one long scroll, which made the results very hard to read.
        /// </summary>
        private void DrawResults()
        {
            resultsTab = KawaiiStudioGUI.Tabs(resultsTab, new[]
            {
                $"Textures ({textureItems.Count})",
                $"Meshes ({meshItems.Count})",
                $"Audio ({audioItems.Count})"
            });

            GUILayout.Space(KawaiiStudioGUI.Space2);

            switch (resultsTab)
            {
                case 1:
                    DrawMeshSettings();
                    DrawMeshList();
                    break;
                case 2:
                    DrawAudioSettings();
                    DrawAudioList();
                    break;
                default:
                    DrawTextureSettings();
                    DrawTextureList();
                    break;
            }
        }

        private void DrawTextureSettings()
        {
            KawaiiStudioGUI.DrawSection(T("texture_settings"), () =>
            {
                EditorGUIUtility.labelWidth = 170f;

                maxTextureSize = EditorGUILayout.IntPopup(T("max_size"), maxTextureSize,
                    new[] { "32", "64", "128", "256", "512", "1024", "2048", "4096", "8192" },
                    new[] { 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192 });

                compressionQuality = (TextureImporterCompression)EditorGUILayout.EnumPopup(
                    T("compression"), compressionQuality);

                useCrunchCompression = EditorGUILayout.Toggle(T("crunch_compression"), useCrunchCompression);

                using (new EditorGUI.DisabledScope(!useCrunchCompression))
                {
                    crunchCompressionQuality = EditorGUILayout.IntSlider(
                        T("quality"), crunchCompressionQuality, 0, 100);
                }

                generateMipmaps = EditorGUILayout.Toggle(T("generate_mipmaps"), generateMipmaps);

                EditorGUIUtility.labelWidth = 0f;

                if (compressionQuality == TextureImporterCompression.Uncompressed)
                {
                    GUILayout.Space(KawaiiStudioGUI.Space2);
                    KawaiiStudioGUI.Banner(
                        "Uncompressed keeps full quality but uses far more VRAM. VRChat avatars normally want Compressed.",
                        KawaiiStudioGUI.MessageKind.Warning);
                }
            });
        }

        /// <summary>Select all / none header shared by the three result tables.</summary>
        private void DrawSelectionToolbar(int count, int selected, Action<bool> setAll)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{selected} of {count} selected", KawaiiStudioGUI.InfoLabelStyle);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(count == 0))
            {
                if (KawaiiStudioGUI.SecondaryButton(T("select_all"), GUILayout.Width(90f))) setAll(true);
                if (KawaiiStudioGUI.SecondaryButton(T("none"), GUILayout.Width(70f))) setAll(false);
            }
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(KawaiiStudioGUI.Space2);
        }

        /// <summary>Column header row over a hairline.</summary>
        private static void DrawTableHeader(params (string label, float width)[] columns)
        {
            EditorGUILayout.BeginHorizontal();
            foreach ((string label, float width) in columns)
            {
                if (width > 0f) GUILayout.Label(label, KawaiiStudioGUI.H3, GUILayout.Width(width));
                else GUILayout.Label(label, KawaiiStudioGUI.H3);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            KawaiiStudioGUI.Separator();
            GUILayout.Space(KawaiiStudioGUI.Space1);
        }

        private void DrawTextureList()
        {
            KawaiiStudioGUI.DrawSection("Textures", () =>
            {
                if (textureItems.Count == 0)
                {
                    KawaiiStudioGUI.EmptyState("No textures found",
                        "Nothing on this prefab references a texture asset that can be re-imported.");
                    return;
                }

                DrawSelectionToolbar(textureItems.Count, textureItems.Count(i => i.selected),
                    on => { foreach (TextureItem i in textureItems) i.selected = on; });

                DrawTableHeader(("", 20f), ("Texture", 190f), ("Size", 80f), ("Format", 140f),
                    ("Mips", 44f), ("Memory", 80f), ("After", 80f), ("Saved", 70f));

                textureScrollPosition = GUILayout.BeginScrollView(textureScrollPosition, GUILayout.Height(220f));

                GUIStyle cell = KawaiiStudioGUI.Mono;
                var resultStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = KawaiiStudioGUI.SuccessColor },
                    fontStyle = FontStyle.Bold
                };

                foreach (TextureItem item in textureItems)
                {
                    EditorGUILayout.BeginHorizontal();

                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20f));
                    EditorGUILayout.ObjectField(item.texture, typeof(Texture), false, GUILayout.Width(190f));

                    GUILayout.Label($"{item.resolution.x}×{item.resolution.y}", cell, GUILayout.Width(80f));
                    GUILayout.Label(item.compressionFormat, cell, GUILayout.Width(140f));
                    GUILayout.Label(item.hasMipmaps ? "Yes" : "No", cell, GUILayout.Width(44f));
                    GUILayout.Label(FormatBytes(item.originalMemorySize), cell, GUILayout.Width(80f));

                    if (item.hasOptimizationResult)
                    {
                        long after = GetEffectiveOptimizedTextureMemory(item);
                        GUILayout.Label(FormatBytes(after), cell, GUILayout.Width(80f));

                        if (item.originalMemorySize > after && item.originalMemorySize > 0)
                        {
                            float pct = (item.originalMemorySize - after) / (float)item.originalMemorySize * 100f;
                            GUILayout.Label($"−{pct:F0}%", resultStyle, GUILayout.Width(70f));
                        }
                        else
                        {
                            GUILayout.Label("no change", cell, GUILayout.Width(70f));
                        }
                    }
                    else
                    {
                        GUILayout.Label("—", cell, GUILayout.Width(80f));
                        GUILayout.Label("", cell, GUILayout.Width(70f));
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            });
        }

        private void DrawMeshSettings()
        {
            KawaiiStudioGUI.DrawSection(T("mesh_compression"), () =>
            {
                EditorGUIUtility.labelWidth = 170f;
                meshCompression = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(
                    T("mesh_compression_level"), meshCompression);
                EditorGUIUtility.labelWidth = 0f;

                GUILayout.Space(KawaiiStudioGUI.Space1);
                GUILayout.Label(
                    "Applies to every mesh with \"Global\" ticked below. Untick it on a row to override that mesh.",
                    KawaiiStudioGUI.InfoLabelStyle);
            });
        }

        private void DrawMeshList()
        {
            KawaiiStudioGUI.DrawSection("Meshes", () =>
            {
                if (meshItems.Count == 0)
                {
                    KawaiiStudioGUI.EmptyState("No compressible meshes found",
                        "Only meshes that come from an .fbx can have their import settings changed.");
                    return;
                }

                DrawSelectionToolbar(meshItems.Count, meshItems.Count(i => i.selected),
                    on => { foreach (MeshItem i in meshItems) i.selected = on; });

                DrawTableHeader(("", 20f), ("Mesh", 190f), ("Verts", 70f), ("Tris", 70f), ("Compression", 190f));

                meshScrollPosition = GUILayout.BeginScrollView(meshScrollPosition, GUILayout.Height(220f));

                GUIStyle cell = KawaiiStudioGUI.Mono;

                foreach (MeshItem item in meshItems)
                {
                    EditorGUILayout.BeginHorizontal();

                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20f));
                    EditorGUILayout.ObjectField(item.mesh, typeof(Mesh), false, GUILayout.Width(190f));

                    GUILayout.Label(item.vertexCount.ToString("N0"), cell, GUILayout.Width(70f));
                    GUILayout.Label(item.triangleCount.ToString("N0"), cell, GUILayout.Width(70f));

                    // "Global" follows the global setting above; unticking it makes the
                    // per-mesh dropdown authoritative, including a value of Off.
                    item.useGlobalCompression = EditorGUILayout.ToggleLeft(
                        "Global", item.useGlobalCompression, GUILayout.Width(66f));
                    using (new EditorGUI.DisabledScope(item.useGlobalCompression))
                    {
                        // While "Global" is ticked the popup previews the global value but
                        // must not write it back, or the mesh's own choice is lost the
                        // moment the user unticks the box.
                        var shown = item.useGlobalCompression ? meshCompression : item.compression;
                        var picked = (ModelImporterMeshCompression)EditorGUILayout.EnumPopup(shown, GUILayout.Width(110f));
                        if (!item.useGlobalCompression) item.compression = picked;
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            });
        }

        private void DrawAudioSettings()
        {
            KawaiiStudioGUI.DrawSection(T("audio_compression"), () =>
            {
                EditorGUIUtility.labelWidth = 170f;

                audioLoadType = (AudioClipLoadType)EditorGUILayout.EnumPopup(T("load_type"), audioLoadType);
                audioCompressionFormat = (AudioCompressionFormat)EditorGUILayout.EnumPopup(
                    T("compression_format"), audioCompressionFormat);

                audioQuality = EditorGUILayout.Slider(T("quality"), audioQuality, 0.01f, 1f);

                audioSampleRate = EditorGUILayout.IntPopup(T("sample_rate"), audioSampleRate,
                    new[] { "8000 Hz", "11025 Hz", "22050 Hz", "44100 Hz", "48000 Hz" },
                    new[] { 8000, 11025, 22050, 44100, 48000 });

                forceToMono = EditorGUILayout.Toggle(T("force_to_mono"), forceToMono);

                EditorGUIUtility.labelWidth = 0f;

                GUILayout.Space(KawaiiStudioGUI.Space2);
                KawaiiStudioGUI.KeyValueRow("Estimated bitrate",
                    $"≈ {Mathf.RoundToInt(audioQuality * 320)} kbps",
                    KawaiiStudioGUI.AccentColor);
            });
        }

        private void DrawAudioList()
        {
            KawaiiStudioGUI.DrawSection("Audio clips", () =>
            {
                if (audioItems.Count == 0)
                {
                    KawaiiStudioGUI.EmptyState("No audio clips found",
                        "Only clips referenced by an AudioSource on this prefab are listed.");
                    return;
                }

                DrawSelectionToolbar(audioItems.Count, audioItems.Count(i => i.selected),
                    on => { foreach (AudioItem i in audioItems) i.selected = on; });

                DrawTableHeader(("", 20f), ("Clip", 180f), ("Length", 60f), ("Ch", 34f),
                    ("Rate", 74f), ("Format", 90f), ("Size", 80f), ("Est.", 80f), ("Change", 70f));

                audioScrollPosition = GUILayout.BeginScrollView(audioScrollPosition, GUILayout.Height(220f));

                GUIStyle cell = KawaiiStudioGUI.Mono;
                var goodStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = KawaiiStudioGUI.SuccessColor },
                    fontStyle = FontStyle.Bold
                };
                var badStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = KawaiiStudioGUI.WarningColor },
                    fontStyle = FontStyle.Bold
                };

                foreach (AudioItem item in audioItems)
                {
                    EditorGUILayout.BeginHorizontal();

                    item.selected = EditorGUILayout.Toggle(item.selected, GUILayout.Width(20f));
                    EditorGUILayout.ObjectField(item.audioClip, typeof(AudioClip), false, GUILayout.Width(180f));

                    GUILayout.Label($"{item.length:F1}s", cell, GUILayout.Width(60f));
                    GUILayout.Label($"{item.channels}", cell, GUILayout.Width(34f));
                    GUILayout.Label($"{item.frequency}", cell, GUILayout.Width(74f));
                    GUILayout.Label(item.compressionFormat.ToString(), cell, GUILayout.Width(90f));
                    GUILayout.Label(FormatBytes(item.originalSize), cell, GUILayout.Width(80f));

                    long estimated = CalculateEstimatedAudioSize(item);
                    GUILayout.Label(FormatBytes(estimated), cell, GUILayout.Width(80f));

                    if (item.originalSize > 0)
                    {
                        float pct = (item.originalSize - estimated) / (float)item.originalSize * 100f;
                        if (pct > 0f) GUILayout.Label($"−{pct:F0}%", goodStyle, GUILayout.Width(70f));
                        else GUILayout.Label($"+{-pct:F0}%", badStyle, GUILayout.Width(70f));
                    }
                    else
                    {
                        GUILayout.Label("—", cell, GUILayout.Width(70f));
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            });
        }

        private void DrawOptimizeButton()
        {
            int selectedCount = textureItems.Count(t => t.selected)
                              + meshItems.Count(m => m.selected)
                              + audioItems.Count(a => a.selected);

            GUILayout.Space(KawaiiStudioGUI.Space3);

            // Inline explanation instead of a mysteriously dead button.
            if (selectedCount == 0)
            {
                KawaiiStudioGUI.Banner(
                    "Nothing selected. Tick at least one texture, mesh or audio clip to optimize.",
                    KawaiiStudioGUI.MessageKind.Warning);
            }

            using (new EditorGUI.DisabledScope(selectedCount == 0))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (KawaiiStudioGUI.PrimaryButton($"{T("optimize")}  ({selectedCount})", GUILayout.Width(280f)))
                {
                    OptimizeAvatar();
                }
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();
            }

            GUILayout.Space(KawaiiStudioGUI.Space2);
        }

        private void DrawLogOutput()
        {
            if (string.IsNullOrEmpty(logOutput)) return;

            KawaiiStudioGUI.DrawSection(T("log"), () =>
            {
                KawaiiStudioGUI.BeginWell();
                logScrollPosition = GUILayout.BeginScrollView(logScrollPosition, GUILayout.Height(160f));

                // SelectableLabel, not TextArea: the log is output, and TextArea let
                // the user type into it while silently discarding the edit.
                EditorGUILayout.SelectableLabel(
                    logOutput,
                    KawaiiStudioGUI.Mono,
                    GUILayout.ExpandHeight(true),
                    GUILayout.ExpandWidth(true));

                GUILayout.EndScrollView();
                KawaiiStudioGUI.EndWell();

                GUILayout.Space(KawaiiStudioGUI.Space1);
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (KawaiiStudioGUI.SecondaryButton("Copy log", GUILayout.Width(110f)))
                {
                    EditorGUIUtility.systemCopyBuffer = logOutput;
                }
                if (KawaiiStudioGUI.SecondaryButton("Clear", GUILayout.Width(80f)))
                {
                    ClearLog();
                }
                EditorGUILayout.EndHorizontal();
            });
        }

        private void ClearLog()
        {
            logBuilder.Length = 0;
            logOutput = "";
        }

        private void AddLog(string message)
        {
            // Plain "logOutput += ..." is O(n^2) over a few hundred assets and was
            // visibly stalling the optimize pass on large avatars.
            logBuilder.Append(message).Append('\n');
            logOutput = logBuilder.ToString();
            logScrollPosition = new Vector2(0, float.MaxValue);
            Repaint();
        }

        private void ScanPrefab()
        {
            if (prefab == null) return;

            ClearLog();
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
                        // A material whose shader failed to compile / was deleted has a null
                        // shader; ShaderUtil.GetPropertyCount(null) throws and aborted the scan.
                        if (shader == null)
                        {
                            AddLog($"   ⚠ Skipped material '{mat.name}' (missing shader)");
                            continue;
                        }
                        int propertyCount = ShaderUtil.GetPropertyCount(shader);
                        for (int i = 0; i < propertyCount; i++)
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
                                        long fileSize = GetFileSizeFromAssetPath(path);
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
                                            originalSize = fileSize,
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
                        long fileSize = GetFileSizeFromAssetPath(path);
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
                            originalSize = fileSize,
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
            // Land on the tab that actually has something in it.
            resultsTab = textureItems.Count > 0 ? 0 : (meshItems.Count > 0 ? 1 : 2);
        }

        private void OptimizeAvatar()
        {
            ClearLog();
            originalSize = 0;
            optimizedSize = 0;

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

            // try/finally: a throw inside any Optimize* call used to leave the modal
            // progress bar on screen, which locks the whole editor until restart.
            try
            {
                // Optimize Textures
                if (selectedTextures.Count > 0)
                {
                    AddLog($"\n🎨 Optimizing {selectedTextures.Count} texture(s)...");

                    foreach (var item in selectedTextures)
                    {
                        EditorUtility.DisplayProgressBar("Prefab Optimizer",
                            $"Texture: {(item.texture != null ? item.texture.name : item.path)}",
                            totalItems > 0 ? (float)processed / totalItems : 1f);
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
                            $"Mesh: {(item.mesh != null ? item.mesh.name : item.path)}",
                            totalItems > 0 ? (float)processed / totalItems : 1f);
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
                            $"Audio: {(item.audioClip != null ? item.audioClip.name : item.path)}",
                            totalItems > 0 ? (float)processed / totalItems : 1f);
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
                // Textures that were already optimal contribute their original size, not 0,
                // which used to inflate "Memory saved" by the whole size of every skipped texture.
                totalOptimizedMemory += GetEffectiveOptimizedTextureMemory(item);
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
            // new FileInfo(path).Length throws FileNotFoundException when the asset has
            // been moved/deleted since the scan; GetFileSizeFromAssetPath returns 0.
            originalSize += GetFileSizeFromAssetPath(item.path);
            // Captured before the reimport: item.texture can be replaced/destroyed by it.
            string textureName = item.texture != null ? item.texture.name : Path.GetFileName(item.path);

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

                RefreshTextureMetrics(item, importer);
                optimizedSize += GetFileSizeFromAssetPath(item.path);

                AddLog($"   ✓ {textureName} ({FormatBytes(item.originalMemorySize)} → {FormatBytes(item.optimizedMemorySize)})");
                return true;
            }
            else
            {
                RefreshTextureMetrics(item, importer);
                AddLog($"   ○ {textureName} (already optimized)");
                return false;
            }
        }

        // After SaveAndReimport the previously held Texture2D instance is stale (Unity
        // recreates it), so the "optimized" memory read used to measure the old object
        // and report a bogus saving. Reload from the asset path instead.
        private void RefreshTextureMetrics(TextureItem item, TextureImporter importer)
        {
            Texture2D reloadedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(item.path);
            if (reloadedTexture != null)
            {
                item.texture = reloadedTexture;
                item.resolution = new Vector2Int(reloadedTexture.width, reloadedTexture.height);
                item.optimizedMemorySize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(reloadedTexture);
            }
            else
            {
                item.optimizedMemorySize = item.originalMemorySize;
            }

            if (importer != null)
            {
                item.compressionFormat = GetCompressionFormat(importer);
                item.hasMipmaps = importer.mipmapEnabled;
            }

            item.hasOptimizationResult = true;
        }

        private static long GetEffectiveOptimizedTextureMemory(TextureItem item)
        {
            if (item == null) return 0;
            return item.hasOptimizationResult ? item.optimizedMemorySize : item.originalMemorySize;
        }

        // Shared with every other tool via KawaiiStudioUtil; also handles assets that
        // live under Packages/ rather than Assets/.
        private static long GetFileSizeFromAssetPath(string assetPath) => KawaiiStudioUtil.GetFileSize(assetPath);

        private bool OptimizeMesh(MeshItem item)
        {
            ModelImporter modelImporter = AssetImporter.GetAtPath(item.path) as ModelImporter;
            if (modelImporter == null) return false;

            bool modified = false;

            // Use individual compression setting or global setting.
            // The old test was "item.compression != Off ? item.compression : meshCompression",
            // which made a deliberate per-mesh choice of "Off" impossible to apply.
            ModelImporterMeshCompression targetCompression = item.useGlobalCompression
                ? meshCompression
                : item.compression;

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

            // Was "!= PreserveSampleRate", which (a) never applied the chosen rate to clips
            // set to Preserve and (b) reported "modified" on every subsequent run because the
            // condition stayed true once overridden. Compare against the desired target.
            if (settings.sampleRateSetting != AudioSampleRateSetting.OverrideSampleRate ||
                settings.sampleRateOverride != (uint)audioSampleRate)
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

            string clipName = item.audioClip != null ? item.audioClip.name : Path.GetFileName(item.path);

            if (modified)
            {
                importer.defaultSampleSettings = settings;
                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();

                // Use the estimation formula: changing importer settings does NOT rewrite the
                // source file, so reading its size back always reported a 0-byte saving.
                item.loadType = settings.loadType;
                item.compressionFormat = settings.compressionFormat;
                item.quality = settings.quality;
                item.frequency = (int)settings.sampleRateOverride;
                item.channels = forceToMono ? 1 : (item.audioClip != null ? item.audioClip.channels : item.channels);
                item.estimatedSize = CalculateEstimatedAudioSize(item);

                AddLog($"   ✓ {clipName} ({FormatBytes(item.originalSize)} → ~{FormatBytes(item.estimatedSize)})");
                return true;
            }
            else
            {
                item.estimatedSize = CalculateEstimatedAudioSize(item);
                AddLog($"   ○ {clipName} (already optimized)");
                return false;
            }
        }

        private long CalculateEstimatedAudioSize(AudioItem item)
        {
            // Formule approximative: (bitrate * durée * channels) / 8
            // Le bitrate est basé sur la qualité (0-1 → 0-320 kbps)
            int estimatedBitrate = Mathf.RoundToInt(audioQuality * 320000); // en bits/sec
            // Mathf.Max guards clips that report 0 channels, which produced a 0-byte estimate
            // and a bogus "-100%" reduction in the list.
            int channelCount = forceToMono ? 1 : Mathf.Max(1, item.channels);
            
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
