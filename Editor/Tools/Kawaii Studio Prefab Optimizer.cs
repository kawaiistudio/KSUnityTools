using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using Unity.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KawaiiStudio
{
    /// <summary>
    /// Prefab Optimizer 4 — the same optimizer as the Kawaii Studio editor, for a prefab or avatar in your project.
    ///
    /// Simple on top: Light / Balanced / Maximum, three sliders in % (texture size, audio quality, polygons), one switch
    /// (unused blendshapes), the texture memory, triangles and audio it gives shown live, and a per-item list where every
    /// texture, mesh and sound can be set on its own.
    ///
    /// Faithful underneath:
    ///  - Textures: the importer's max size becomes a % of the SOURCE image (never twice), uncompressed ones get
    ///    compressed, crunch makes the download smaller. Ramps, lookup tables and small textures (256 px and less) are
    ///    left alone.
    ///  - Audio: Vorbis at the chosen quality / rate (mono for Medium and Low), only ever lower than it is now.
    ///  - Meshes: polygon reduction that never invents a vertex (each step folds an edge onto an existing vertex, so
    ///    every vertex left keeps its exact position, normal, UVs, skin weights and blendshape deltas). Faces and
    ///    everything a blendshape moves, UV seams, open borders and material edges are never touched, and a mesh stops
    ///    before its shape would change. Unused blendshapes (not animated, not a viseme / eyelid, no weight, not "vrc."
    ///    nor MMD) can go too.
    ///  - Nothing of yours is overwritten: the reduced meshes are new assets in "Assets/Kawaii Studio Optimized/…",
    ///    used by a COPY of the avatar named "… (Optimized)". Texture / audio import settings are saved first and
    ///    "Restore settings" puts them back.
    /// </summary>
    public class PrefabOptimizer : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;
        private const string OutRoot = "Assets/Kawaii Studio Optimized";
        private const int TextureFloor = 256;

        private static readonly string[] TextureLabels = { "100 %", "50 %", "25 %", "12.5 %" };
        private static readonly float[] TextureScales = { 1f, 0.5f, 0.25f, 0.125f };
        private static readonly string[] MeshLabels = { "100 %", "75 %", "50 %", "25 %" };
        private static readonly float[] MeshKeeps = { 1f, 0.75f, 0.5f, 0.25f };
        private static readonly string[] AudioLabels = { "Original", "High", "Medium", "Low" };

        private enum Preset { Light, Balanced, Maximum }

        // ---- settings
        private GameObject prefab;
        private int texStep = 1, audioStep = 2, meshStep = 1;
        private bool removeShapes = true;

        // ---- scan
        private bool scanned;
        private bool shapeSourcesFound;
        private readonly List<TexItem> textures = new List<TexItem>();
        private readonly List<MeshItem> meshes = new List<MeshItem>();
        private readonly List<ClipItem> clips = new List<ClipItem>();

        // ---- ui
        private int itemTab;
        private bool showAllItems, showLog;
        private Vector2 scroll, logScroll;
        private readonly StringBuilder log = new StringBuilder();
        private string resultMessage;

        private class TexItem
        {
            public Texture2D Tex; public string Path; public TextureImporter Importer;
            public int SrcMax, CurMax, Width, Height; public bool Alpha, Mips, Compressed, Crunched;
            public string Why; public int Step = -1;
        }

        private class MeshItem
        {
            public Mesh Mesh; public string Path; public int Tris, Verts, Locked, Interior, ShapeCount;
            public List<string> Unused = new List<string>(); public string Why; public int Step = -1;
        }

        private class ClipItem
        {
            public AudioClip Clip; public string Path; public AudioImporter Importer;
            public float Length; public int Channels, Frequency; public bool Mono; public float Quality;
            public AudioCompressionFormat Format; public string Why; public int Step = -1;
        }

        [Serializable] private class BackupFile { public List<BackupEntry> entries = new List<BackupEntry>(); }
        [Serializable] private class BackupEntry
        {
            public string path; public string kind;
            public int maxSize; public int compression; public bool crunch; public int crunchQuality;
            public bool mono; public int format; public float quality; public int rateSetting; public int rate; public int loadType;
            public List<string> platforms = new List<string>(); public List<int> platformMax = new List<int>();
        }

        [MenuItem("Kawaii Studio/Prefab Optimizer")]
        public static void ShowWindow()
        {
            PrefabOptimizer window = GetWindow<PrefabOptimizer>("Prefab Optimizer");
            window.minSize = new Vector2(760, 640);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("Prefab Optimizer");
            KawaiiStudioLocalization.Reload();
        }

        private static string T(string key) => KawaiiStudioLocalization.T(key);
        private static string TF(string key, params object[] args)
        {
            try { return string.Format(T(key), args); } catch { return string.Format(key, args); }
        }
        private static string Size(long bytes) => KawaiiStudioUtil.FormatBytes(bytes);

        // ================================================================== GUI
        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginVertical();
            KawaiiStudioGUI.DrawBanner("Prefab Optimizer", T("Make your avatar lighter without breaking it"), VERSION, KawaiiStudioBranding.Logo, KawaiiStudioBranding.Banner);
            GUILayout.Space(KawaiiStudioGUI.Space2);

            DrawSource();
            if (scanned && prefab != null)
            {
                DrawNumbers();
                DrawPresets();
                DrawSettings();
                DrawItems();
                DrawApply();
                DrawLog();
            }

            KawaiiStudioGUI.DrawFooter();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawSource()
        {
            KawaiiStudioGUI.DrawSection(T("Avatar or prefab"), () =>
            {
                var picked = (GameObject)EditorGUILayout.ObjectField(T("Drag it here"), prefab, typeof(GameObject), true);
                if (picked != prefab)
                {
                    prefab = picked;
                    scanned = false;
                    resultMessage = null;
                    textures.Clear(); meshes.Clear(); clips.Clear();
                    log.Length = 0;
                    if (prefab != null) Scan();
                }
                GUILayout.Space(KawaiiStudioGUI.Space2);
                if (prefab == null)
                    KawaiiStudioGUI.Banner(T("Drag a prefab or an avatar from your scene into the field above."), KawaiiStudioGUI.MessageKind.Info);
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    KawaiiStudioGUI.KeyValueRow(T("Selected"), prefab.name, KawaiiStudioGUI.SuccessColor);
                    GUILayout.FlexibleSpace();
                    if (KawaiiStudioGUI.SecondaryButton(T("Scan again"), GUILayout.Width(140f))) Scan();
                    EditorGUILayout.EndHorizontal();
                }
            });
        }

        private void DrawNumbers()
        {
            long vramBefore = 0, vramAfter = 0, audioBefore = 0, audioAfter = 0, trisBefore = 0, trisAfter = 0;
            foreach (var t in textures) { var e = EstimateTexture(t); vramBefore += e.Key; vramAfter += e.Value; }
            foreach (var c in clips) { var e = EstimateClip(c); audioBefore += e.Key; audioAfter += e.Value; }
            foreach (var m in meshes) { trisBefore += m.Tris; trisAfter += EstimateTriangles(m); }
            EditorGUILayout.BeginHorizontal();
            Tile(T("Texture memory"), Size(vramBefore) + "  →  " + Size(vramAfter), vramBefore, vramAfter);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            Tile(T("Triangles"), trisBefore.ToString("N0") + "  →  ≈ " + trisAfter.ToString("N0"), trisBefore, trisAfter);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            Tile(T("Audio"), "≈ " + Size(audioBefore) + "  →  ≈ " + Size(audioAfter), audioBefore, audioAfter);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(KawaiiStudioGUI.Space3);
        }

        private static void Tile(string caption, string value, long before, long after)
        {
            double pct = before > 0 ? (before - after) * 100.0 / before : 0;
            string tail = pct >= 0.5 ? "   −" + pct.ToString("F0") + " %" : "";
            KawaiiStudioGUI.StatTile(value + tail, caption, pct >= 0.5 ? KawaiiStudioGUI.SuccessColor : KawaiiStudioGUI.SubTextColor);
        }

        private Preset? CurrentPreset()
        {
            if (textures.Any(t => t.Step >= 0) || meshes.Any(m => m.Step >= 0) || clips.Any(c => c.Step >= 0)) return null;
            foreach (Preset p in Enum.GetValues(typeof(Preset)))
            {
                var s = PresetSteps(p);
                if (s[0] == texStep && s[1] == audioStep && s[2] == meshStep && removeShapes) return p;
            }
            return null;
        }

        private static int[] PresetSteps(Preset p)
        {
            if (p == Preset.Light) return new[] { 0, 1, 0 };
            if (p == Preset.Maximum) return new[] { 2, 3, 2 };
            return new[] { 1, 2, 1 };
        }

        private void DrawPresets()
        {
            var cur = CurrentPreset();
            string[] labels = { T("Light — no visible change"), T("Balanced — recommended"), T("Maximum — smallest") };
            int sel = cur.HasValue ? (int)cur.Value : -1;
            int picked = KawaiiStudioGUI.Tabs(sel, labels);
            if (picked != sel && picked >= 0)
            {
                var s = PresetSteps((Preset)picked);
                texStep = s[0]; audioStep = s[1]; meshStep = s[2]; removeShapes = true;
                foreach (var t in textures) t.Step = -1;
                foreach (var m in meshes) m.Step = -1;
                foreach (var c in clips) c.Step = -1;
            }
            GUILayout.Space(KawaiiStudioGUI.Space2);
        }

        private void DrawSettings()
        {
            KawaiiStudioGUI.DrawSection(T("Settings"), () =>
            {
                // textures
                SettingHeader(T("Textures"), texStep == 0 ? T("Full size — only uncompressed textures get compressed")
                    : texStep == 1 ? T("Half size — 4096 → 2048, 2048 → 1024, smaller download (crunch)")
                    : texStep == 2 ? T("Quarter size — 4096 → 1024, 2048 → 512, smaller download (crunch)")
                    : T("Eighth size — 4096 → 512, 2048 → 256, smaller download (crunch)"));
                texStep = StepSlider(texStep, TextureLabels);
                int texChanging = textures.Count(t => TexturePlan(t, Effective(t)) != null);
                Sub(TF("{0} of {1} textures get lighter. Small ones (256 px and less), ramps and lookup tables are never touched.", texChanging, textures.Count));

                // audio
                SettingHeader(T("Audio"), audioStep == 0 ? T("Sounds stay exactly as they are")
                    : audioStep == 1 ? T("High — Vorbis, stereo kept")
                    : audioStep == 2 ? T("Medium — Vorbis, mono, 22 kHz") : T("Low — Vorbis, mono, 11 kHz"));
                audioStep = StepSlider(audioStep, AudioLabels.Select(T).ToArray());
                int clipChanging = clips.Count(c => ClipPlan(c, Effective(c)) != null);
                Sub(clips.Count == 0 ? T("No sound on this avatar.") : TF("{0} of {1} sounds get lighter. A sound is only ever made smaller, never better or bigger.", clipChanging, clips.Count));

                // polygons
                SettingHeader(T("Polygons"), meshStep == 0 ? T("Every triangle kept")
                    : meshStep == 1 ? T("Up to a quarter fewer triangles — no visible change")
                    : meshStep == 2 ? T("Up to half the triangles") : T("Up to three quarters fewer triangles"));
                meshStep = StepSlider(meshStep, MeshLabels);
                long tb = meshes.Sum(m => (long)m.Tris), ta = meshes.Sum(m => (long)EstimateTriangles(m));
                Sub(meshes.Count == 0 ? T("No mesh on this avatar.") : TF("{0} → ≈ {1} triangles. Faces and blendshapes, UV seams, edges and material borders are never touched; a mesh stops before its shape would change.", tb.ToString("N0"), ta.ToString("N0")));

                // blendshapes
                GUILayout.Space(KawaiiStudioGUI.Space2);
                removeShapes = KawaiiStudioGUI.DrawToggle(T("Remove unused blendshapes"), removeShapes);
                int unused = meshes.Sum(m => m.Unused.Count), total = meshes.Sum(m => m.ShapeCount);
                Sub(!shapeSourcesFound ? T("No animator controller found: nothing can be proven unused, every blendshape is kept.")
                    : total == 0 ? T("No blendshape on this avatar.")
                    : TF("{0} of {1} blendshapes are unused. Visemes, eyelids, animated, weighted, MMD and \"vrc.\" ones are always kept.", unused, total));
            });
        }

        private static void SettingHeader(string name, string hint)
        {
            GUILayout.Space(KawaiiStudioGUI.Space2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(name, KawaiiStudioGUI.H3);
            GUILayout.FlexibleSpace();
            var st = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = KawaiiStudioGUI.AccentColor }, fontStyle = FontStyle.Bold };
            GUILayout.Label(hint, st);
            EditorGUILayout.EndHorizontal();
        }

        private static void Sub(string text)
        {
            var st = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = KawaiiStudioGUI.SubTextColor } };
            GUILayout.Label(text, st);
        }

        /// <summary>A stepped slider: a filled track, a dot per stop, a knob, a label under each stop. Click or drag.</summary>
        private int StepSlider(int value, string[] labels)
        {
            Rect r = GUILayoutUtility.GetRect(10f, 40f, GUILayout.ExpandWidth(true));
            int n = labels.Length;
            float pad = 14f;
            var track = new Rect(r.x + pad, r.y + 10f, r.width - 2 * pad, 4f);
            float f = n > 1 ? (float)value / (n - 1) : 0f;
            Color accent = KawaiiStudioGUI.AccentColor;
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(track, new Color(0.5f, 0.5f, 0.55f, 0.25f));
                EditorGUI.DrawRect(new Rect(track.x, track.y, track.width * f, track.height), accent);
                for (int i = 0; i < n; i++)
                {
                    float x = track.x + track.width * i / Mathf.Max(1, n - 1);
                    EditorGUI.DrawRect(new Rect(x - 3f, track.y - 2f, 6f, 8f), i <= value ? accent : new Color(0.45f, 0.45f, 0.5f, 0.8f));
                }
                float kx = track.x + track.width * f;
                EditorGUI.DrawRect(new Rect(kx - 7f, track.y - 6f, 14f, 16f), Color.white);
                EditorGUI.DrawRect(new Rect(kx - 5f, track.y - 4f, 10f, 12f), accent);
                for (int i = 0; i < n; i++)
                {
                    float x = track.x + track.width * i / Mathf.Max(1, n - 1);
                    var st = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = i == 0 ? TextAnchor.UpperLeft : i == n - 1 ? TextAnchor.UpperRight : TextAnchor.UpperCenter,
                        fontStyle = i == value ? FontStyle.Bold : FontStyle.Normal,
                        normal = { textColor = i == value ? KawaiiStudioGUI.TextColor : KawaiiStudioGUI.SubTextColor }
                    };
                    float w = 90f;
                    float lx = i == 0 ? track.x - 6f : i == n - 1 ? track.x + track.width - w + 6f : x - w / 2;
                    GUI.Label(new Rect(lx, track.y + 9f, w, 16f), labels[i], st);
                }
            }
            int id = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            EditorGUIUtility.AddCursorRect(r, MouseCursor.Link);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                value = Pick(e.mousePosition.x);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == id)
            {
                value = Pick(e.mousePosition.x);
                e.Use(); Repaint();
            }
            else if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }
            return value;

            int Pick(float x) => Mathf.Clamp(Mathf.RoundToInt((x - track.x) / Mathf.Max(1f, track.width) * (n - 1)), 0, n - 1);
        }

        private void DrawItems()
        {
            KawaiiStudioGUI.DrawSection(T("Each item"), () =>
            {
                EditorGUILayout.BeginHorizontal();
                int t = KawaiiStudioGUI.Tabs(itemTab, new[]
                {
                    TF("Textures ({0})", textures.Count), TF("Meshes ({0})", meshes.Count), TF("Audio ({0})", clips.Count)
                });
                if (t != itemTab) { itemTab = t; showAllItems = false; }
                EditorGUILayout.EndHorizontal();
                if (GUILayout.Button(T("All on Auto"), EditorStyles.miniButton, GUILayout.Width(110f)))
                {
                    foreach (var x in textures) x.Step = -1;
                    foreach (var x in meshes) x.Step = -1;
                    foreach (var x in clips) x.Step = -1;
                }
                Sub(T("Auto follows the sliders above. Pick a value to set one item on its own. Before → after is estimated, the biggest first."));
                GUILayout.Space(KawaiiStudioGUI.Space1);

                const int Shown = 40;
                int count = 0;
                if (itemTab == 0)
                {
                    var list = textures.OrderByDescending(x => EstimateTexture(x).Key).ToList();
                    foreach (var x in showAllItems ? list : list.Take(Shown))
                    {
                        var e = EstimateTexture(x);
                        string detail = $"{x.Width}×{x.Height} · {(x.Tex != null ? x.Tex.format.ToString() : "?")}{(x.Crunched ? " · crunch" : "")}";
                        x.Step = ItemRow(x.Tex, x.Tex != null ? x.Tex.name : x.Path, detail, e.Key, e.Value, x.Why, x.Step, texStep, TextureLabels);
                    }
                    count = list.Count;
                }
                else if (itemTab == 1)
                {
                    var list = meshes.OrderByDescending(x => x.Tris).ToList();
                    foreach (var x in showAllItems ? list : list.Take(Shown))
                    {
                        int after = EstimateTriangles(x);
                        string detail = TF("{0} triangles", x.Tris.ToString("N0")) + (after < x.Tris ? " → ≈ " + after.ToString("N0") : "")
                                        + (removeShapes && x.Unused.Count > 0 ? " · " + TF("{0} unused blendshape(s) removed", x.Unused.Count) : "");
                        x.Step = ItemRow(x.Mesh, x.Mesh != null ? x.Mesh.name : x.Path, detail, x.Tris, after, x.Why, x.Step, meshStep, MeshLabels, count: true);
                    }
                    count = list.Count;
                }
                else
                {
                    var list = clips.OrderByDescending(x => EstimateClip(x).Key).ToList();
                    foreach (var x in showAllItems ? list : list.Take(Shown))
                    {
                        var e = EstimateClip(x);
                        string detail = $"{x.Format} · {(x.Mono || x.Channels == 1 ? "mono" : "stereo")} · {x.Frequency / 1000f:0.#} kHz · {x.Length:0.0} s";
                        x.Step = ItemRow(x.Clip, x.Clip != null ? x.Clip.name : x.Path, detail, e.Key, e.Value, x.Why, x.Step, audioStep, AudioLabels.Select(T).ToArray());
                    }
                    count = list.Count;
                }
                if (count == 0) Sub(T("Nothing of this kind on this avatar."));
                if (!showAllItems && count > Shown && GUILayout.Button(TF("Show all {0}", count), EditorStyles.miniButton, GUILayout.Width(140f))) showAllItems = true;
            });
        }

        private int ItemRow(UnityEngine.Object obj, string name, string detail, long before, long after, string why, int step, int global, string[] labels, bool count = false)
        {
            KawaiiStudioGUI.BeginWell();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(200f));
            if (GUILayout.Button(name, EditorStyles.boldLabel) && obj != null) EditorGUIUtility.PingObject(obj);
            GUILayout.Label(detail, EditorStyles.miniLabel);
            if (why != null)
            {
                var ws = new GUIStyle(EditorStyles.wordWrappedMiniLabel) { normal = { textColor = KawaiiStudioGUI.WarningColor } };
                GUILayout.Label(T(why), ws);
            }
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            double pct = before > 0 ? (before - after) * 100.0 / before : 0;
            string a = count ? after.ToString("N0") : Size(after), b = count ? before.ToString("N0") : Size(before);
            EditorGUILayout.BeginVertical(GUILayout.Width(170f));
            GUILayout.Label(pct >= 0.5 ? b + " → ≈ " + a : b, EditorStyles.label);
            var ps = new GUIStyle(EditorStyles.miniBoldLabel) { normal = { textColor = pct >= 0.5 ? KawaiiStudioGUI.SuccessColor : KawaiiStudioGUI.SubTextColor } };
            GUILayout.Label(pct >= 0.5 ? "−" + pct.ToString("F0") + " %" : T("unchanged"), ps);
            EditorGUILayout.EndVertical();
            if (why == null)
            {
                var options = new[] { T("Auto") + " · " + labels[Mathf.Clamp(global, 0, labels.Length - 1)] }.Concat(labels).ToArray();
                int sel = EditorGUILayout.Popup(step < 0 ? 0 : step + 1, options, GUILayout.Width(130f));
                step = sel == 0 ? -1 : sel - 1;
            }
            else GUILayout.Space(134f);
            EditorGUILayout.EndHorizontal();
            KawaiiStudioGUI.EndWell();
            return step;
        }

        private void DrawApply()
        {
            GUILayout.Space(KawaiiStudioGUI.Space2);
            KawaiiStudioGUI.Banner(TF("Your avatar is never modified: the lighter meshes are new files in \"{0}\", used by a copy named \"{1} (Optimized)\". Texture and sound import settings change, and \"Restore settings\" puts them back.", OutFolder(), prefab.name), KawaiiStudioGUI.MessageKind.Info);
            GUILayout.Space(KawaiiStudioGUI.Space2);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (KawaiiStudioGUI.PrimaryButton(T("OPTIMIZE"), GUILayout.Width(260f), GUILayout.Height(36f))) Optimize();
            if (File.Exists(BackupPath()) && KawaiiStudioGUI.SecondaryButton(T("Restore settings"), GUILayout.Width(160f), GUILayout.Height(36f))) RestoreSettings();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(resultMessage))
            {
                GUILayout.Space(KawaiiStudioGUI.Space2);
                KawaiiStudioGUI.Banner(resultMessage, KawaiiStudioGUI.MessageKind.Success);
            }
        }

        private void DrawLog()
        {
            if (log.Length == 0) return;
            GUILayout.Space(KawaiiStudioGUI.Space2);
            showLog = EditorGUILayout.Foldout(showLog, T("Details"), true);
            if (!showLog) return;
            logScroll = EditorGUILayout.BeginScrollView(logScroll, GUILayout.Height(180f));
            EditorGUILayout.TextArea(log.ToString(), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }

        private void Log(string line) => log.AppendLine(line);

        // ================================================================== scan
        /// <param name="keepReport">After OPTIMIZE: the result message and the details stay on screen.</param>
        private void Scan(bool keepReport = false)
        {
            textures.Clear(); meshes.Clear(); clips.Clear();
            if (!keepReport) { log.Length = 0; resultMessage = null; }
            if (prefab == null) return;
            var renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var seenTex = new HashSet<Texture>();
            var seenMesh = new HashSet<Mesh>();
            foreach (var r in renderers)
            {
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat == null || mat.shader == null) continue;
                    int pc = ShaderUtil.GetPropertyCount(mat.shader);
                    for (int i = 0; i < pc; i++)
                    {
                        if (ShaderUtil.GetPropertyType(mat.shader, i) != ShaderUtil.ShaderPropertyType.TexEnv) continue;
                        string prop = ShaderUtil.GetPropertyName(mat.shader, i);
                        var tex = mat.GetTexture(prop);
                        if (tex == null || !seenTex.Add(tex)) continue;
                        var item = ScanTexture(tex, prop);
                        if (item != null) textures.Add(item);
                    }
                }
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else { var mf = r.GetComponent<MeshFilter>(); if (mf != null) mesh = mf.sharedMesh; }
                if (mesh != null && seenMesh.Add(mesh)) meshes.Add(ScanMesh(mesh));
            }
            var seenClip = new HashSet<AudioClip>();
            foreach (var src in prefab.GetComponentsInChildren<AudioSource>(true))
                if (src.clip != null && seenClip.Add(src.clip)) clips.Add(ScanClip(src.clip));
            FindUnusedShapes();
            scanned = true;
            if (!keepReport) Log($"Scan: {textures.Count} texture(s), {meshes.Count} mesh(es), {clips.Count} sound(s)");
        }

        private static readonly string[] RampWords = { "ramp", "lut", "gradation", "gradient", "matcap" };

        private TexItem ScanTexture(Texture tex, string prop)
        {
            string path = AssetDatabase.GetAssetPath(tex);
            var item = new TexItem { Tex = tex as Texture2D, Path = path, Width = tex.width, Height = tex.height };
            item.Importer = AssetImporter.GetAtPath(path) as TextureImporter;
            item.CurMax = Mathf.Max(tex.width, tex.height);
            item.SrcMax = item.CurMax;
            if (item.Tex == null) item.Why = "not a 2D texture";
            else if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) item.Why = "a built-in or package texture: left as it is";
            else if (item.Importer == null) item.Why = "not an imported image";
            if (item.Importer != null)
            {
                int w, h;
                if (SourceSize(item.Importer, out w, out h)) item.SrcMax = Mathf.Max(w, h);
                item.Mips = item.Importer.mipmapEnabled;
                item.Compressed = item.Importer.textureCompression != TextureImporterCompression.Uncompressed;
                item.Crunched = item.Importer.crunchedCompression;
                item.Alpha = item.Importer.DoesSourceTextureHaveAlpha() && item.Importer.alphaSource != TextureImporterAlphaSource.None;
                string low = (tex.name + " " + prop).ToLowerInvariant();
                bool thin = Mathf.Max(tex.width, tex.height) >= 8 * Mathf.Min(tex.width, tex.height) && Mathf.Min(tex.width, tex.height) <= 16;
                if (item.Why == null && (RampWords.Any(low.Contains) || thin)) item.Why = "ramp or lookup texture: left as it is";
                if (item.Why == null && item.Importer.textureType == TextureImporterType.Sprite) item.Why = "sprite: left as it is";
            }
            return item;
        }

        private static bool SourceSize(TextureImporter imp, out int w, out int h)
        {
#if UNITY_2021_2_OR_NEWER
            imp.GetSourceTextureWidthAndHeight(out w, out h);
            return w > 0 && h > 0;
#else
            w = h = 0;
            var m = typeof(TextureImporter).GetMethod("GetWidthAndHeight", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (m == null) return false;
            object[] args = { 0, 0 };
            m.Invoke(imp, args);
            w = (int)args[0]; h = (int)args[1];
            return w > 0 && h > 0;
#endif
        }

        private MeshItem ScanMesh(Mesh mesh)
        {
            var item = new MeshItem { Mesh = mesh, Path = AssetDatabase.GetAssetPath(mesh), ShapeCount = mesh.blendShapeCount };
            try
            {
                item.Verts = mesh.vertexCount;
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    if (mesh.GetTopology(s) != MeshTopology.Triangles) { item.Why = "not made of triangles (lines or points)"; continue; }
                    item.Tris += (int)(mesh.GetIndexCount(s) / 3);
                }
                if (string.IsNullOrEmpty(item.Path) || item.Path.StartsWith("Library/") || item.Path.StartsWith("Resources/")) item.Why = item.Why ?? "a built-in mesh: left as it is";
                if (item.Why == null && item.Tris < 64) item.Why = "too few triangles to reduce";
                if (item.Why == null)
                {
                    var locked = LockedVertices(mesh);
                    item.Locked = locked.Count(x => x);
                    item.Interior = CountInterior(mesh, locked);
                }
            }
            catch (Exception e) { item.Why = "can't be read: " + e.Message; }
            return item;
        }

        private ClipItem ScanClip(AudioClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            var item = new ClipItem { Clip = clip, Path = path, Length = clip.length, Channels = clip.channels, Frequency = clip.frequency };
            item.Importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (item.Importer == null || !path.StartsWith("Assets/")) { item.Why = "not an imported sound in Assets"; return item; }
            var s = item.Importer.defaultSampleSettings;
            item.Format = s.compressionFormat;
            item.Quality = s.quality;
            item.Mono = item.Importer.forceToMono;
            if (s.sampleRateSetting == AudioSampleRateSetting.OverrideSampleRate) item.Frequency = Mathf.Min(item.Frequency, (int)s.sampleRateOverride);
            return item;
        }

        /// <summary>Vertices a blendshape moves: never removed (faces, expressions, toggles stay exact).</summary>
        private static bool[] LockedVertices(Mesh mesh)
        {
            int n = mesh.vertexCount;
            var locked = new bool[n];
            if (mesh.blendShapeCount == 0) return locked;
            var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
            for (int s = 0; s < mesh.blendShapeCount; s++)
                for (int f = 0; f < mesh.GetBlendShapeFrameCount(s); f++)
                {
                    mesh.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    for (int i = 0; i < n; i++)
                        if (!locked[i] && (dv[i].sqrMagnitude > 0 || dn[i].sqrMagnitude > 0 || dt[i].sqrMagnitude > 0)) locked[i] = true;
                }
            return locked;
        }

        /// <summary>Vertices the polygon reduction may remove at all: every edge shared by two triangles, one material, not locked.</summary>
        private static int CountInterior(Mesh mesh, bool[] locked)
        {
            int n = mesh.vertexCount;
            var sub = new int[n];
            for (int i = 0; i < n; i++) sub[i] = -1;
            var bad = (bool[])locked.Clone();
            var edges = new Dictionary<long, int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                var tri = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < tri.Length; t += 3)
                    for (int k = 0; k < 3; k++)
                    {
                        int a = tri[t + k], b = tri[t + (k + 1) % 3];
                        if (sub[a] == -1) sub[a] = s; else if (sub[a] != s) bad[a] = true;
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        edges.TryGetValue(key, out int c);
                        edges[key] = c + 1;
                    }
            }
            foreach (var kv in edges)
                if (kv.Value != 2) { bad[(int)(kv.Key >> 32)] = true; bad[(int)(kv.Key & 0xFFFFFFFF)] = true; }
            int interior = 0;
            for (int i = 0; i < n; i++) if (!bad[i] && sub[i] >= 0) interior++;
            return interior;
        }

        // ---- which blendshapes are used
        private void FindUnusedShapes()
        {
            var used = new HashSet<string>();
            var keepIndex = new Dictionary<Mesh, HashSet<int>>();
            int controllers = 0;
            var ctrls = new List<RuntimeAnimatorController>();
            foreach (var anim in prefab.GetComponentsInChildren<Animator>(true))
                if (anim.runtimeAnimatorController != null) ctrls.Add(anim.runtimeAnimatorController);
            bool sdk2 = false;
            foreach (var comp in prefab.GetComponentsInChildren<Component>(true))
            {
                if (comp == null) continue;
                string tn = comp.GetType().Name;
                if (tn != "VRCAvatarDescriptor" && tn != "VRC_AvatarDescriptor") continue;
                sdk2 |= tn == "VRC_AvatarDescriptor";
                var so = new SerializedObject(comp);
                foreach (string layers in new[] { "baseAnimationLayers", "specialAnimationLayers" })
                {
                    var arr = so.FindProperty(layers);
                    if (arr == null || !arr.isArray) continue;
                    for (int i = 0; i < arr.arraySize; i++)
                    {
                        var c = arr.GetArrayElementAtIndex(i).FindPropertyRelative("animatorController");
                        if (c != null && c.objectReferenceValue is RuntimeAnimatorController rac) ctrls.Add(rac);
                    }
                }
                var vis = so.FindProperty("VisemeBlendShapes");
                if (vis != null && vis.isArray) for (int i = 0; i < vis.arraySize; i++) used.Add(vis.GetArrayElementAtIndex(i).stringValue);
                var mouth = so.FindProperty("MouthOpenBlendShapeName");
                if (mouth != null) used.Add(mouth.stringValue);
                var eyeMesh = so.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh");
                var eyeIdx = so.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
                if (eyeMesh != null && eyeMesh.objectReferenceValue is SkinnedMeshRenderer es && es.sharedMesh != null && eyeIdx != null && eyeIdx.isArray)
                {
                    var hs = new HashSet<int>();
                    for (int i = 0; i < eyeIdx.arraySize; i++) hs.Add(eyeIdx.GetArrayElementAtIndex(i).intValue);
                    keepIndex[es.sharedMesh] = hs;
                }
                // SDK2 animations live in override controllers
                var ov = so.FindProperty("CustomStandingAnims");
                if (ov != null && ov.objectReferenceValue is RuntimeAnimatorController o1) ctrls.Add(o1);
                var ov2 = so.FindProperty("CustomSittingAnims");
                if (ov2 != null && ov2.objectReferenceValue is RuntimeAnimatorController o2) ctrls.Add(o2);
            }
            foreach (var rac in ctrls.Distinct())
            {
                controllers++;
                foreach (var clip in rac.animationClips)
                {
                    if (clip == null) continue;
                    foreach (var b in AnimationUtility.GetCurveBindings(clip))
                        if (b.propertyName.StartsWith("blendShape.")) used.Add(b.propertyName.Substring("blendShape.".Length));
                }
            }
            shapeSourcesFound = controllers > 0;
            var weighted = new Dictionary<Mesh, HashSet<int>>();
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                HashSet<int> hs;
                if (!weighted.TryGetValue(smr.sharedMesh, out hs)) weighted[smr.sharedMesh] = hs = new HashSet<int>();
                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++) if (smr.GetBlendShapeWeight(i) != 0) hs.Add(i);
            }
            foreach (var m in meshes)
            {
                m.Unused.Clear();
                if (!shapeSourcesFound || m.Mesh == null) continue;
                HashSet<int> w, eye;
                weighted.TryGetValue(m.Mesh, out w);
                keepIndex.TryGetValue(m.Mesh, out eye);
                for (int i = 0; i < m.Mesh.blendShapeCount; i++)
                {
                    string name = m.Mesh.GetBlendShapeName(i);
                    if (used.Contains(name) || (w != null && w.Contains(i)) || (eye != null && eye.Contains(i))) continue;
                    if (name.StartsWith("vrc.", StringComparison.OrdinalIgnoreCase) || IsMmd(name) || (sdk2 && i < 4)) continue;
                    m.Unused.Add(name);
                }
            }
        }

        private static bool IsMmd(string n)
        {
            foreach (char c in n)
                if ((c >= '぀' && c <= 'ヿ') || (c >= '一' && c <= '鿿') || (c >= 'ｦ' && c <= 'ﾟ')) return true;
            return n.Trim() == "▲" || n.Trim() == "∧" || n.Trim() == "□" || n.Trim() == "ω";
        }

        // ================================================================== plans + estimates
        private int Effective(TexItem t) => t.Step >= 0 ? t.Step : texStep;
        private int Effective(MeshItem m) => m.Step >= 0 ? m.Step : meshStep;
        private int Effective(ClipItem c) => c.Step >= 0 ? c.Step : audioStep;

        private class TexPlanData { public int MaxSize; public bool Compress; public int Crunch; }

        /// <summary>What a texture gets at this step, null = nothing to change.</summary>
        private static TexPlanData TexturePlan(TexItem t, int step)
        {
            if (t.Why != null || t.Importer == null) return null;
            float scale = TextureScales[Mathf.Clamp(step, 0, TextureScales.Length - 1)];
            int max = t.CurMax;
            if (scale < 1f && t.SrcMax > TextureFloor)
            {
                int target = Mathf.Max(TextureFloor, Mathf.RoundToInt(t.SrcMax * scale));
                int pot = 32;
                while (pot * 2 <= target) pot *= 2;
                max = Mathf.Min(t.CurMax, pot);
            }
            int crunch = step <= 0 ? 0 : step == 1 ? 75 : 50;
            var p = new TexPlanData { MaxSize = max, Compress = !t.Compressed, Crunch = t.Crunched ? 0 : crunch };
            if (p.MaxSize >= t.CurMax && !p.Compress && p.Crunch == 0) return null;
            return p;
        }

        private static float BytesPerPixel(TextureFormat f)
        {
            switch (f)
            {
                case TextureFormat.DXT1: case TextureFormat.DXT1Crunched: case TextureFormat.BC4: case TextureFormat.ETC_RGB4: case TextureFormat.ETC2_RGB: return 0.5f;
                case TextureFormat.DXT5: case TextureFormat.DXT5Crunched: case TextureFormat.BC5: case TextureFormat.BC7: case TextureFormat.BC6H: case TextureFormat.ETC2_RGBA8: return 1f;
                case TextureFormat.Alpha8: case TextureFormat.R8: return 1f;
                case TextureFormat.RGB24: return 3f;
                case TextureFormat.RGBAHalf: return 8f;
                case TextureFormat.RGBAFloat: return 16f;
                default: return 4f;
            }
        }

        /// <summary>(VRAM now, VRAM after) for a texture at its current setting.</summary>
        private KeyValuePair<long, long> EstimateTexture(TexItem t)
        {
            float bpp = t.Tex != null ? BytesPerPixel(t.Tex.format) : 4f;
            float mip = t.Mips ? 4f / 3f : 1f;
            long now = (long)(t.Width * (long)t.Height * bpp * mip);
            var p = TexturePlan(t, Effective(t));
            if (p == null) return new KeyValuePair<long, long>(now, now);
            float s = Mathf.Min(1f, (float)p.MaxSize / Mathf.Max(1, t.CurMax));
            float bppAfter = p.Compress ? (t.Alpha ? 1f : 0.5f) : bpp;
            long after = (long)(t.Width * s * t.Height * s * bppAfter * mip);
            return new KeyValuePair<long, long>(now, Math.Min(now, after));
        }

        private class ClipPlanData { public bool Mono; public int Rate; public float Quality; }

        /// <summary>What a sound gets at this step: never a better quality, rate or channel count than it has.</summary>
        private static ClipPlanData ClipPlan(ClipItem c, int step)
        {
            if (c.Why != null || step <= 0) return null;
            float q = step == 1 ? 0.7f : step == 2 ? 0.5f : 0.3f;
            int rate = step == 1 ? c.Frequency : step == 2 ? 22050 : 11025;
            bool mono = step >= 2 || c.Mono;
            var p = new ClipPlanData
            {
                Mono = mono || c.Mono,
                Rate = Mathf.Min(rate, c.Frequency),
                Quality = c.Format == AudioCompressionFormat.Vorbis ? Mathf.Min(q, c.Quality) : q
            };
            bool same = c.Format == AudioCompressionFormat.Vorbis && Mathf.Approximately(p.Quality, c.Quality) && p.Rate >= c.Frequency && p.Mono == c.Mono;
            return same ? null : p;
        }

        private static long ClipBytes(float length, int channels, int rate, AudioCompressionFormat fmt, float q)
        {
            if (fmt == AudioCompressionFormat.PCM) return (long)(length * rate * channels * 2);
            if (fmt == AudioCompressionFormat.ADPCM) return (long)(length * rate * channels * 0.5f);
            float kbpsPerChannel = (32f + 96f * Mathf.Clamp01(q)) * Mathf.Clamp(rate / 44100f, 0.3f, 1.1f);
            return (long)(length * kbpsPerChannel * 1000f / 8f * channels);
        }

        private KeyValuePair<long, long> EstimateClip(ClipItem c)
        {
            int ch = c.Mono ? 1 : c.Channels;
            long now = ClipBytes(c.Length, ch, c.Frequency, c.Format, c.Quality);
            var p = ClipPlan(c, Effective(c));
            if (p == null) return new KeyValuePair<long, long>(now, now);
            long after = ClipBytes(c.Length, p.Mono ? 1 : c.Channels, p.Rate, AudioCompressionFormat.Vorbis, p.Quality);
            return new KeyValuePair<long, long>(now, Math.Min(now, after));
        }

        private int EstimateTriangles(MeshItem m)
        {
            float keep = MeshKeeps[Mathf.Clamp(Effective(m), 0, MeshKeeps.Length - 1)];
            if (m.Why != null || keep >= 0.999f) return m.Tris;
            int canRemove = (int)(m.Interior * 0.85f);
            return Mathf.Max((int)(m.Tris * keep), m.Tris - 2 * canRemove);
        }

        // ================================================================== optimize
        private string OutFolder() => OutRoot + "/" + Safe(prefab != null ? prefab.name : "Avatar");
        private string BackupPath() => OutFolder() + "/import-settings-backup.json";
        private static string Safe(string s) => string.Concat((s ?? "Avatar").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();

        private void Optimize()
        {
            if (prefab == null) return;
            log.Length = 0;
            resultMessage = null;
            EnsureFolder(OutFolder());
            var backup = LoadBackup();
            int texDone = 0, clipDone = 0, meshDone = 0;
            long trisBefore = meshes.Sum(m => (long)m.Tris), trisAfter = 0;
            var replaced = new Dictionary<Mesh, Mesh>();
            try
            {
                AssetDatabase.StartAssetEditing();
                try
                {
                    // textures: import settings
                    for (int i = 0; i < textures.Count; i++)
                    {
                        var t = textures[i];
                        EditorUtility.DisplayProgressBar("Prefab Optimizer", t.Tex != null ? t.Tex.name : t.Path, 0.3f * i / Mathf.Max(1, textures.Count));
                        var p = TexturePlan(t, Effective(t));
                        if (p == null) continue;
                        Backup(backup, t.Path, t.Importer, null);
                        if (p.MaxSize < t.CurMax)
                        {
                            t.Importer.maxTextureSize = p.MaxSize;
                            foreach (string plat in new[] { "Standalone", "Android" })
                            {
                                var ps = t.Importer.GetPlatformTextureSettings(plat);
                                if (ps.overridden && ps.maxTextureSize > p.MaxSize) { ps.maxTextureSize = p.MaxSize; t.Importer.SetPlatformTextureSettings(ps); }
                            }
                        }
                        if (p.Compress) t.Importer.textureCompression = TextureImporterCompression.Compressed;
                        if (p.Crunch > 0) { t.Importer.crunchedCompression = true; t.Importer.compressionQuality = p.Crunch; }
                        EditorUtility.SetDirty(t.Importer);
                        t.Importer.SaveAndReimport();
                        texDone++;
                        var parts = new List<string>();
                        if (p.MaxSize < t.CurMax) parts.Add($"{t.CurMax} → {p.MaxSize} px");
                        if (p.Compress) parts.Add("compressed");
                        if (p.Crunch > 0) parts.Add($"crunch {p.Crunch} (smaller download)");
                        Log($"✓ {t.Tex.name}: {string.Join(", ", parts)}");
                    }
                    // audio: import settings
                    for (int i = 0; i < clips.Count; i++)
                    {
                        var c = clips[i];
                        EditorUtility.DisplayProgressBar("Prefab Optimizer", c.Clip != null ? c.Clip.name : c.Path, 0.3f + 0.1f * i / Mathf.Max(1, clips.Count));
                        var p = ClipPlan(c, Effective(c));
                        if (p == null) continue;
                        Backup(backup, c.Path, null, c.Importer);
                        c.Importer.forceToMono = p.Mono;
                        var s = c.Importer.defaultSampleSettings;
                        s.compressionFormat = AudioCompressionFormat.Vorbis;
                        s.quality = p.Quality;
                        if (p.Rate < c.Clip.frequency) { s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate; s.sampleRateOverride = (uint)p.Rate; }
                        if (s.loadType == AudioClipLoadType.DecompressOnLoad && c.Length > 10f) s.loadType = AudioClipLoadType.CompressedInMemory;
                        c.Importer.defaultSampleSettings = s;
                        EditorUtility.SetDirty(c.Importer);
                        c.Importer.SaveAndReimport();
                        clipDone++;
                        Log($"✓ {c.Clip.name}: Vorbis {p.Quality:0.0}{(p.Mono ? ", mono" : "")}, {p.Rate / 1000f:0.#} kHz");
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                SaveBackup(backup);

                // meshes: new, lighter copies
                for (int i = 0; i < meshes.Count; i++)
                {
                    var m = meshes[i];
                    EditorUtility.DisplayProgressBar("Prefab Optimizer", m.Mesh != null ? m.Mesh.name : m.Path, 0.4f + 0.5f * i / Mathf.Max(1, meshes.Count));
                    float keep = MeshKeeps[Mathf.Clamp(Effective(m), 0, MeshKeeps.Length - 1)];
                    var drop = removeShapes ? new HashSet<string>(m.Unused) : new HashSet<string>();
                    if (m.Mesh == null || m.Why != null || (keep >= 0.999f && drop.Count == 0)) { trisAfter += m.Tris; continue; }
                    try
                    {
                        int after;
                        var nm = BuildMesh(m.Mesh, keep, drop, out after);
                        if (nm == null) { trisAfter += m.Tris; Log($"○ {m.Mesh.name}: nothing to remove without changing its shape"); continue; }
                        string path = AssetDatabase.GenerateUniqueAssetPath(OutFolder() + "/" + Safe(m.Mesh.name) + ".asset");
                        AssetDatabase.CreateAsset(nm, path);
                        replaced[m.Mesh] = nm;
                        trisAfter += after;
                        meshDone++;
                        Log($"✓ {m.Mesh.name}: triangles {m.Tris:N0} → {after:N0}{(drop.Count > 0 ? $", {drop.Count} unused blendshape(s) removed" : "")} → {path}");
                    }
                    catch (Exception e) { trisAfter += m.Tris; Log($"✗ {m.Mesh.name}: {e.Message} — left as it was"); }
                }

                string copyName = null;
                if (replaced.Count > 0)
                {
                    EditorUtility.DisplayProgressBar("Prefab Optimizer", T("Saving the optimized copy"), 0.95f);
                    copyName = MakeOptimizedCopy(replaced);
                }
                AssetDatabase.SaveAssets();
                resultMessage = TF("Done: {0} texture(s), {1} sound(s), {2} mesh(es) made lighter. Triangles {3} → {4}.", texDone, clipDone, meshDone, trisBefore.ToString("N0"), trisAfter.ToString("N0"))
                                + (copyName != null ? "\n" + TF("Use \"{0}\" for your upload: it has the lighter meshes.", copyName) : "");
                Log(resultMessage);
            }
            catch (Exception e)
            {
                resultMessage = null;
                Log("✗ " + e);
                EditorUtility.DisplayDialog("Prefab Optimizer", e.Message, "OK");
            }
            finally { EditorUtility.ClearProgressBar(); }
            showLog = true;
            Scan(keepReport: true);
        }

        /// <summary>
        /// A lighter copy of the mesh: the vertices the reduction keeps (and every blendshape-driven one), each with its
        /// exact data — positions, normals, tangents, colours, every UV channel, bone weights, bind poses — new triangles,
        /// and the blendshapes still used. Null when nothing would change.
        /// </summary>
        private static Mesh BuildMesh(Mesh src, float keep, HashSet<string> dropShapes, out int trisAfter)
        {
            int n = src.vertexCount;
            var pos = src.vertices;
            var subs = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++) subs.Add(src.GetTriangles(s));
            int before = subs.Sum(x => x.Length / 3);
            var locked = LockedVertices(src);

            float[] P = new float[n * 3];
            for (int i = 0; i < n; i++) { P[i * 3] = pos[i].x; P[i * 3 + 1] = pos[i].y; P[i * 3 + 2] = pos[i].z; }
            float[] W = null; int[] B = null;
            var bpv = src.GetBonesPerVertex();
            var all = src.GetAllBoneWeights();
            if (bpv.Length == n && all.Length > 0)
            {
                W = new float[n * 4]; B = new int[n * 4];
                int at = 0;
                for (int i = 0; i < n; i++)
                {
                    int c = bpv[i];
                    for (int k = 0; k < c; k++)
                        if (k < 4) { W[i * 4 + k] = all[at + k].weight; B[i * 4 + k] = all[at + k].boneIndex; }
                    at += c;
                }
            }

            List<int[]> outSubs = subs;
            var used = new bool[n];
            if (keep < 0.999f)
            {
                float maxError = keep >= 0.74f ? 0.005f : keep >= 0.49f ? 0.01f : 0.02f;
                var r = KSMeshSimplify.Run(new KSMeshSimplify.Input { Positions = P, SubMeshes = subs, Locked = locked, Weights = W, Bones = B, MaxError = maxError },
                    Mathf.RoundToInt(before * keep));
                outSubs = r.SubMeshes;
                used = r.Used;
            }
            else foreach (var sm in subs) foreach (int i in sm) used[i] = true;
            trisAfter = outSubs.Sum(x => x.Length / 3);
            if (trisAfter >= before && dropShapes.Count == 0) return null;

            var map = new int[n];
            int nn = 0;
            for (int i = 0; i < n; i++) map[i] = used[i] || locked[i] ? nn++ : -1;
            var keepIdx = new List<int>(nn);
            for (int i = 0; i < n; i++) if (map[i] >= 0) keepIdx.Add(i);

            var m = new Mesh { name = src.name, indexFormat = nn > 65535 ? IndexFormat.UInt32 : src.indexFormat };
            m.SetVertices(keepIdx.Select(i => pos[i]).ToList());
            if (src.HasVertexAttribute(VertexAttribute.Normal)) { var a = src.normals; m.SetNormals(keepIdx.Select(i => a[i]).ToList()); }
            if (src.HasVertexAttribute(VertexAttribute.Tangent)) { var a = src.tangents; m.SetTangents(keepIdx.Select(i => a[i]).ToList()); }
            if (src.HasVertexAttribute(VertexAttribute.Color)) { var a = new List<Color>(); src.GetColors(a); m.SetColors(keepIdx.Select(i => a[i]).ToList()); }
            for (int ch = 0; ch < 8; ch++)
            {
                var attr = (VertexAttribute)((int)VertexAttribute.TexCoord0 + ch);
                if (!src.HasVertexAttribute(attr)) continue;
                int dim = src.GetVertexAttributeDimension(attr);
                var uv = new List<Vector4>(); src.GetUVs(ch, uv);
                if (dim <= 2) m.SetUVs(ch, keepIdx.Select(i => (Vector2)uv[i]).ToList());
                else if (dim == 3) m.SetUVs(ch, keepIdx.Select(i => (Vector3)uv[i]).ToList());
                else m.SetUVs(ch, keepIdx.Select(i => uv[i]).ToList());
            }
            m.bindposes = src.bindposes;
            if (bpv.Length == n && all.Length > 0)
            {
                var offsets = new int[n + 1];
                for (int i = 0; i < n; i++) offsets[i + 1] = offsets[i] + bpv[i];
                var nb = new NativeArray<byte>(nn, Allocator.Temp);
                var nw = new List<BoneWeight1>();
                int j = 0;
                foreach (int i in keepIdx)
                {
                    nb[j++] = bpv[i];
                    for (int k = offsets[i]; k < offsets[i + 1]; k++) nw.Add(all[k]);
                }
                var nwa = new NativeArray<BoneWeight1>(nw.ToArray(), Allocator.Temp);
                m.SetBoneWeights(nb, nwa);
                nb.Dispose(); nwa.Dispose();
            }
            m.subMeshCount = outSubs.Count;
            for (int s = 0; s < outSubs.Count; s++) m.SetTriangles(outSubs[s].Select(i => map[i]).ToArray(), s, false);
            // blendshapes, frame by frame, on the kept vertices
            var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
            for (int s = 0; s < src.blendShapeCount; s++)
            {
                string name = src.GetBlendShapeName(s);
                if (dropShapes.Contains(name)) continue;
                for (int f = 0; f < src.GetBlendShapeFrameCount(s); f++)
                {
                    src.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    m.AddBlendShapeFrame(name, src.GetBlendShapeFrameWeight(s, f),
                        keepIdx.Select(i => dv[i]).ToArray(), keepIdx.Select(i => dn[i]).ToArray(), keepIdx.Select(i => dt[i]).ToArray());
                }
            }
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A copy of the avatar using the lighter meshes (blendshape weights and eyelid indices follow by name).
        /// A prefab becomes a new prefab next to it; a scene avatar gets a copy beside it. Returns its name.</summary>
        private string MakeOptimizedCopy(Dictionary<Mesh, Mesh> replaced)
        {
            bool isAsset = PrefabUtility.IsPartOfPrefabAsset(prefab);
            GameObject copy;
            if (isAsset)
            {
                copy = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            else
            {
                copy = Instantiate(prefab, prefab.transform.parent);
                copy.transform.SetPositionAndRotation(prefab.transform.position, prefab.transform.rotation);
                copy.transform.localScale = prefab.transform.localScale;
                if (PrefabUtility.IsPartOfPrefabInstance(copy)) PrefabUtility.UnpackPrefabInstance(copy, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
            copy.name = prefab.name + " (Optimized)";
            foreach (var smr in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh nm;
                if (smr.sharedMesh == null || !replaced.TryGetValue(smr.sharedMesh, out nm)) continue;
                var old = smr.sharedMesh;
                var weights = new Dictionary<string, float>();
                for (int i = 0; i < old.blendShapeCount; i++) weights[old.GetBlendShapeName(i)] = smr.GetBlendShapeWeight(i);
                smr.sharedMesh = nm;
                for (int i = 0; i < nm.blendShapeCount; i++)
                {
                    float w;
                    smr.SetBlendShapeWeight(i, weights.TryGetValue(nm.GetBlendShapeName(i), out w) ? w : 0f);
                }
                FixEyelids(copy, smr, old, nm);
            }
            foreach (var mf in copy.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh nm;
                if (mf.sharedMesh != null && replaced.TryGetValue(mf.sharedMesh, out nm)) mf.sharedMesh = nm;
            }
            if (isAsset)
            {
                string dir = Path.GetDirectoryName(AssetDatabase.GetAssetPath(prefab)).Replace('\\', '/');
                string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + Safe(copy.name) + ".prefab");
                var saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
                DestroyImmediate(copy);
                Selection.activeObject = saved;
                EditorGUIUtility.PingObject(saved);
                return Path.GetFileNameWithoutExtension(path);
            }
            Undo.RegisterCreatedObjectUndo(copy, "Optimized copy");
            Selection.activeObject = copy;
            return copy.name;
        }

        /// <summary>The VRChat descriptor's eyelid blendshapes are INDICES into the eyelid mesh: re-point them by name.</summary>
        private static void FixEyelids(GameObject root, SkinnedMeshRenderer smr, Mesh oldMesh, Mesh newMesh)
        {
            foreach (var comp in root.GetComponentsInChildren<Component>(true))
            {
                if (comp == null || comp.GetType().Name != "VRCAvatarDescriptor") continue;
                var so = new SerializedObject(comp);
                var eyeMesh = so.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh");
                var idx = so.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
                if (eyeMesh == null || idx == null || !idx.isArray || eyeMesh.objectReferenceValue != smr) continue;
                for (int i = 0; i < idx.arraySize; i++)
                {
                    var e = idx.GetArrayElementAtIndex(i);
                    int o = e.intValue;
                    if (o < 0 || o >= oldMesh.blendShapeCount) continue;
                    e.intValue = newMesh.GetBlendShapeIndex(oldMesh.GetBlendShapeName(o));
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        // ================================================================== import settings backup / restore
        private BackupFile LoadBackup()
        {
            try { if (File.Exists(BackupPath())) return JsonUtility.FromJson<BackupFile>(File.ReadAllText(BackupPath())) ?? new BackupFile(); }
            catch { }
            return new BackupFile();
        }

        private void SaveBackup(BackupFile b)
        {
            if (b.entries.Count == 0) return;
            File.WriteAllText(BackupPath(), JsonUtility.ToJson(b, true));
            AssetDatabase.ImportAsset(BackupPath());
        }

        /// <summary>The settings as they were before the FIRST optimization (a later one never overwrites them).</summary>
        private static void Backup(BackupFile b, string path, TextureImporter ti, AudioImporter ai)
        {
            if (b.entries.Any(e => e.path == path)) return;
            var e = new BackupEntry { path = path };
            if (ti != null)
            {
                e.kind = "texture";
                e.maxSize = ti.maxTextureSize; e.compression = (int)ti.textureCompression; e.crunch = ti.crunchedCompression; e.crunchQuality = ti.compressionQuality;
                foreach (string plat in new[] { "Standalone", "Android" })
                {
                    var ps = ti.GetPlatformTextureSettings(plat);
                    if (ps.overridden) { e.platforms.Add(plat); e.platformMax.Add(ps.maxTextureSize); }
                }
            }
            if (ai != null)
            {
                e.kind = "audio";
                var s = ai.defaultSampleSettings;
                e.mono = ai.forceToMono; e.format = (int)s.compressionFormat; e.quality = s.quality; e.rateSetting = (int)s.sampleRateSetting; e.rate = (int)s.sampleRateOverride; e.loadType = (int)s.loadType;
            }
            b.entries.Add(e);
        }

        private void RestoreSettings()
        {
            var b = LoadBackup();
            int n = 0;
            foreach (var e in b.entries)
            {
                if (e.kind == "texture" && AssetImporter.GetAtPath(e.path) is TextureImporter ti)
                {
                    ti.maxTextureSize = e.maxSize; ti.textureCompression = (TextureImporterCompression)e.compression; ti.crunchedCompression = e.crunch; ti.compressionQuality = e.crunchQuality;
                    for (int i = 0; i < e.platforms.Count; i++)
                    {
                        var ps = ti.GetPlatformTextureSettings(e.platforms[i]);
                        ps.maxTextureSize = e.platformMax[i];
                        ti.SetPlatformTextureSettings(ps);
                    }
                    ti.SaveAndReimport(); n++;
                }
                else if (e.kind == "audio" && AssetImporter.GetAtPath(e.path) is AudioImporter ai)
                {
                    ai.forceToMono = e.mono;
                    var s = ai.defaultSampleSettings;
                    s.compressionFormat = (AudioCompressionFormat)e.format; s.quality = e.quality; s.sampleRateSetting = (AudioSampleRateSetting)e.rateSetting; s.sampleRateOverride = (uint)e.rate; s.loadType = (AudioClipLoadType)e.loadType;
                    ai.defaultSampleSettings = s;
                    ai.SaveAndReimport(); n++;
                }
            }
            AssetDatabase.DeleteAsset(BackupPath());
            resultMessage = TF("{0} texture / sound setting(s) restored.", n);
            Scan(keepReport: true);
        }
    }
}
