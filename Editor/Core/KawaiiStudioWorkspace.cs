using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// The workspace installer: "Assets/Kawaii Studio" is created and kept in step with the
    /// package, so the package can live read-only under Packages/ and the user still gets
    /// real, editable, exportable shaders and materials in the project.
    ///
    /// What it does, every time the editor loads (cheap once installed) and whenever a
    /// tool asks for a shader, a material or an output folder:
    ///   1. creates the workspace folders (Shaders, Materials, Videos, Animations, Prefabs,
    ///      Models, KS Exported, Optimized, Protected, ThirdParty/FFMPEG);
    ///   2. copies the package's shader and material SOURCES (Shaders~ / Materials~, which
    ///      Unity never imports) into the workspace together with their .meta files -- the
    ///      GUIDs are the ones published since 3.0.0, so every material that already points
    ///      at a Kawaii Studio shader keeps working;
    ///   3. on a package update, replaces the installed shaders with the new ones (the
    ///      previous files go to Backups~/&lt;version&gt;/) -- but only the shaders it installed
    ///      itself and that nobody edited since (their hash is recorded in workspace.json); a
    ///      shader the user changed, or one that was already there before the package, is kept
    ///      and reported, until "Repair / Update" is asked for. Materials are never replaced:
    ///      they are the user's (the Video Animator writes the video size into one);
    ///   4. writes a stamp (workspace.json) with the package version it was synced to;
    ///   5. on a fresh install or an update, looks INSIDE the project for what older installs
    ///      left behind (<see cref="KawaiiStudioProjectRepair"/>: old tool scripts, duplicate
    ///      shaders, materials on a missing shader, stray output folders, FFMPEG in the old
    ///      place) and offers to repair it.
    /// Nothing of the user's is ever deleted.
    /// </summary>
    [InitializeOnLoad]
    public static class KawaiiStudioWorkspace
    {
        public const string StampFileName = "workspace.json";

        /// <summary>Shader file name -> the name declared inside it (Shader "..."): what Shader.Find takes.</summary>
        private static readonly Dictionary<string, string> KnownShaderNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "BloodKiller", "Kawaii Studio/BloodKiller" },
            { "KSHairRealistic", "Kawaii Studio/KSHairRealistic" },
            { "KSScreenShader", "Kawaii Studio/KSScreenShader" },
            { "KSVRStereoDisplay", "Kawaii Studio/VR Stereo Display Debug" },
            { "KSVampireCrimsonEye", "Kawaii Studio/KSVampireCrimsonEye" },
            { "KSVideoDecoder", "Kawaii Studio/KSVideoDecoder" },
            { "KS_ScreenLine", "Kawaii Studio/KS_ScreenLine" },
            { "screen_overlay", "rireal/screen overlay" },
        };

        /// <summary>The tool scripts of the pre-package layout: compiled from Assets/, they double the package's.</summary>
        internal static readonly string[] LegacyScriptNames =
        {
            "Kawaii Studio Manager.cs", "Kawaii Studio Exporter.cs", "Kawaii Studio Video Animator.cs",
            "Kawaii Studio GLB to FBX.cs", "Kawaii Studio NSFW Detector.cs", "Kawaii Studio Prefab Optimizer.cs",
            "Kawaii Studio Tail Animator to PhysBones.cs", "Kawaii Studio Controller To Anim.cs", "Kawaii Studio Obfuscator.cs",
            "Kawaii Studio Polygon Reducer.cs", "Kawaii Studio Menu Colorizer.cs", "ContactScannerMenu.cs", "UltimateConstraintTool.cs",
            "KawaiiStudioGUI.cs", "KawaiiStudioBranding.cs", "KawaiiStudioPaths.cs", "KawaiiStudioUtil.cs", "KawaiiStudioVersion.cs",
            "KawaiiStudioLocalization.cs", "KawaiiStudioWorkspace.cs", "KawaiiStudioProjectRepair.cs",
            "KSHairRealisticGUI.cs", "KS_BloodKillerGUI.cs", "KS_ScreenLineGUI.cs", "KS_VampireEyeGUI.cs",
        };

        public sealed class Report
        {
            public bool Ok = true;
            public string PackageVersion = KawaiiStudioVersion.Current;
            public string PreviousVersion = "";
            public bool Fresh;                      // the workspace did not exist
            public List<string> FoldersCreated = new List<string>();
            public List<string> Installed = new List<string>();   // files copied in
            public List<string> Updated = new List<string>();     // files replaced (previous one in Backups~)
            public List<string> Kept = new List<string>();        // differing files left alone (edited, or there before us)
            public KawaiiStudioProjectRepair.ScanResult Project;  // what the project scan found (fresh / update / forced runs)
            public List<string> Errors = new List<string>();

            public override string ToString()
            {
                var sb = new StringBuilder();
                sb.Append(Ok ? "[Kawaii Studio] workspace " : "[Kawaii Studio] workspace FAILED ");
                sb.Append(Fresh ? "created" : "checked").Append(" at ").Append(KawaiiStudioPaths.Workspace);
                sb.Append(" (package ").Append(PackageVersion).Append(')');
                if (FoldersCreated.Count > 0) sb.Append(" · ").Append(FoldersCreated.Count).Append(" folder(s) created");
                if (Installed.Count > 0) sb.Append(" · installed: ").Append(string.Join(", ", Installed));
                if (Updated.Count > 0) sb.Append(" · updated: ").Append(string.Join(", ", Updated));
                if (Kept.Count > 0) sb.Append(" · kept as they were: ").Append(string.Join(", ", Kept));
                if (Project != null && !Project.Clean) sb.Append(" · ").Append(Project.Summary());
                if (Errors.Count > 0) sb.Append(" · errors: ").Append(string.Join(" | ", Errors));
                return sb.ToString();
            }
        }

        private static bool _checkedThisSession;
        private static bool _running;
        private static bool _repairAsked;

        public static Report LastReport { get; private set; }

        static KawaiiStudioWorkspace()
        {
            // Once per editor load / domain reload. delayCall: the AssetDatabase is not
            // usable from a static constructor during import.
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                try { EnsureInstalled(); }
                catch (Exception e) { Debug.LogWarning("[Kawaii Studio] workspace check failed: " + e.Message); }
            };
        }

        /// <summary>The workspace folder exists and its stamp is readable.</summary>
        public static bool IsInstalled => AssetDatabase.IsValidFolder(KawaiiStudioPaths.Workspace) && ReadStamp() != null;

        /// <summary>The package version the workspace was last synced to ("" when never).</summary>
        public static string InstalledVersion => ReadStamp()?.package ?? "";

        /// <summary>The name a shader file declares (Shader "..."), for Shader.Find; null when unknown.</summary>
        public static string DeclaredShaderName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            string key = fileName.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ? fileName.Substring(0, fileName.Length - 7) : fileName;
            return KnownShaderNames.TryGetValue(key, out var n) ? n : null;
        }

        /// <summary>
        /// Create / refresh the workspace. Cheap when it is already in step with the package
        /// (one stamp read per session). force = re-copy every shader and material, backing
        /// up what differs, and scan the project again.
        /// </summary>
        public static Report EnsureInstalled(bool force = false)
        {
            if (_running) return LastReport ?? new Report();
            if (!force && _checkedThisSession && LastReport != null && LastReport.Ok) return LastReport;
            _running = true;
            var r = new Report();
            var record = new Dictionary<string, string>();
            try
            {
                Stamp stamp = ReadStamp();
                r.Fresh = !AssetDatabase.IsValidFolder(KawaiiStudioPaths.Workspace);
                r.PreviousVersion = stamp?.package ?? "";
                bool upgrade = force || stamp == null || stamp.package != KawaiiStudioVersion.Current;
                var recorded = ParseFiles(stamp);

                foreach (string folder in new[]
                {
                    KawaiiStudioPaths.Workspace, KawaiiStudioPaths.Shaders, KawaiiStudioPaths.Materials,
                    KawaiiStudioPaths.Videos, KawaiiStudioPaths.Animations, KawaiiStudioPaths.Prefabs,
                    KawaiiStudioPaths.Models, KawaiiStudioPaths.Exported, KawaiiStudioPaths.Optimized,
                    KawaiiStudioPaths.Protected, KawaiiStudioPaths.FFmpeg,
                })
                {
                    if (AssetDatabase.IsValidFolder(folder)) continue;
                    KawaiiStudioPaths.EnsureFolder(folder);
                    r.FoldersCreated.Add(folder);
                }

                // the legacy layout IS the workspace: the sources are already in place
                if (!KawaiiStudioPaths.IsLegacyLayout)
                {
                    SyncFolder(KawaiiStudioPaths.ShaderSources, KawaiiStudioPaths.Shaders, "*.shader", upgrade, force, true, recorded, record, r);
                    var touched = new List<string>();
                    SyncFolder(KawaiiStudioPaths.MaterialSources, KawaiiStudioPaths.Materials, "*.mat", upgrade, force, false, recorded, record, r, touched);
                    // A material the package ships references our shaders by their PUBLISHED GUIDs. The
                    // installed shader may carry another one (it was in the project before the package,
                    // with the project's own GUID): point the fresh copy at what is actually installed.
                    var guidMap = InstalledShaderGuidMap();
                    foreach (string mat in touched)
                    {
                        try { RemapShaderGuids(mat, guidMap); }
                        catch (Exception e) { r.Errors.Add($"{Path.GetFileName(mat)}: {e.Message}"); }
                    }
                }
                else
                {
                    string shaders = KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Shaders);
                    if (shaders != null && Directory.Exists(shaders))
                        foreach (string f in Directory.EnumerateFiles(shaders, "*.shader"))
                            record[Path.GetFileName(f)] = Md5(f);
                }

                if (r.Installed.Count > 0 || r.Updated.Count > 0 || r.FoldersCreated.Count > 0)
                    AssetDatabase.Refresh();

                WriteStamp(new Stamp
                {
                    package = KawaiiStudioVersion.Current,
                    installed = DateTime.UtcNow.ToString("o"),
                    layout = KawaiiStudioPaths.IsLegacyLayout ? "unitypackage" : "vpm",
                    files = record.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value).ToArray(),
                });

                // the project itself: what older installs left behind
                if (r.Fresh || upgrade) r.Project = KawaiiStudioProjectRepair.Scan();

                r.Ok = r.Errors.Count == 0;
            }
            catch (Exception e)
            {
                r.Ok = false;
                r.Errors.Add(e.GetType().Name + ": " + e.Message);
            }
            finally
            {
                _running = false;
                _checkedThisSession = true;
                LastReport = r;
            }
            bool findings = r.Project != null && !r.Project.Clean;
            if (r.Fresh || r.Installed.Count > 0 || r.Updated.Count > 0 || r.Kept.Count > 0 || !r.Ok || findings)
                Debug.Log(r.ToString() + (findings ? "\n" + r.Project : ""));
            if (r.Project != null && r.Project.FixableCount > 0 && !Application.isBatchMode) OfferProjectRepair(r.Project);
            return r;
        }

        /// <summary>Re-copy every shader and material from the package (what differs goes to Backups~) and rescan the project.</summary>
        public static Report Reinstall() => EnsureInstalled(force: true);

        [MenuItem("Kawaii Studio/Workspace/Install or Repair", priority = 20)]
        public static void MenuInstall()
        {
            var r = Reinstall();
            Debug.Log(r.ToString());
            EditorUtility.DisplayDialog("Kawaii Studio", r.Ok
                ? $"Workspace ready at {KawaiiStudioPaths.Workspace}\n\n{r.Installed.Count} file(s) installed, {r.Updated.Count} updated, {r.Kept.Count} kept." +
                  (r.Project != null ? "\n\n" + r.Project.Summary() : "")
                : "Workspace install failed:\n\n" + string.Join("\n", r.Errors), "OK");
        }

        [MenuItem("Kawaii Studio/Workspace/Open Folder", priority = 21)]
        public static void MenuOpenFolder()
        {
            EnsureInstalled();
            var folder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(KawaiiStudioPaths.Workspace);
            if (folder != null) { Selection.activeObject = folder; EditorGUIUtility.PingObject(folder); }
        }

        /// <summary>
        /// Headless entry point: Unity.exe -batchmode -executeMethod KawaiiStudio.KawaiiStudioWorkspace.CommandLineInstall
        /// Installs, repairs the project, writes workspace-report.txt next to the project and exits non-zero on failure.
        /// </summary>
        public static void CommandLineInstall()
        {
            var r = EnsureInstalled(force: true);
            var lines = new List<string> { r.ToString() };
            if (r.Project != null)
            {
                lines.Add(r.Project.ToString());
                if (r.Project.FixableCount > 0)
                {
                    var rep = KawaiiStudioProjectRepair.Repair(r.Project);
                    lines.Add(rep.ToString());
                    lines.Add("after repair: " + KawaiiStudioProjectRepair.Scan().Summary());
                }
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (string f in Directory.EnumerateFiles(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Shaders), "*.shader"))
            {
                string asset = KawaiiStudioPaths.Shaders + "/" + Path.GetFileName(f);
                var sh = AssetDatabase.LoadAssetAtPath<Shader>(asset);
                lines.Add($"shader {Path.GetFileName(f)} guid={AssetDatabase.AssetPathToGUID(asset)} loaded={(sh != null ? sh.name : "NULL")}");
            }
            foreach (string f in Directory.EnumerateFiles(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Materials), "*.mat"))
            {
                string asset = KawaiiStudioPaths.Materials + "/" + Path.GetFileName(f);
                var m = AssetDatabase.LoadAssetAtPath<Material>(asset);
                lines.Add($"material {Path.GetFileName(f)} guid={AssetDatabase.AssetPathToGUID(asset)} shader={(m == null ? "NULL" : m.shader == null ? "NONE" : m.shader.name)}");
            }
            File.WriteAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "workspace-report.txt"), string.Join("\n", lines));
            if (!r.Ok) EditorApplication.Exit(1);
        }

        // ------------------------------------------------------------------ sync

        private static void SyncFolder(string sourceFolder, string destFolder, string pattern, bool upgrade, bool force, bool replaceOnUpgrade,
                                       Dictionary<string, string> recorded, Dictionary<string, string> record, Report r, List<string> touched = null)
        {
            string src = KawaiiStudioPaths.ToAbsolute(sourceFolder);
            string dst = KawaiiStudioPaths.ToAbsolute(destFolder);
            if (src == null || !Directory.Exists(src)) { r.Errors.Add("package sources missing: " + sourceFolder); return; }
            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase)) return;
            Directory.CreateDirectory(dst);

            foreach (string file in Directory.EnumerateFiles(src, pattern))
            {
                string name = Path.GetFileName(file);
                string target = Path.Combine(dst, name);
                try
                {
                    if (!File.Exists(target))
                    {
                        File.Copy(file, target);
                        CopyMeta(file, target);
                        r.Installed.Add(name);
                        record[name] = Md5(target);
                        touched?.Add(target);
                        continue;
                    }
                    if (SameContent(file, target))
                    {
                        CopyMeta(file, target, onlyIfMissing: true);
                        record[name] = Md5(target);
                        continue;
                    }

                    // The installed file differs from the package's. Ours and untouched since
                    // (hash recorded) -> an update replaces it. Edited, or there before us -> kept,
                    // unless the user asked for a forced repair.
                    bool untouched = recorded.TryGetValue(name, out string hash) && hash == Md5(target);
                    bool replace = force || (upgrade && replaceOnUpgrade && untouched);
                    if (!replace) { r.Kept.Add(name); continue; }

                    Backup(target, r.PreviousVersion);
                    File.Copy(file, target, true);
                    CopyMeta(file, target, onlyIfMissing: true);   // the installed .meta keeps its GUID
                    r.Updated.Add(name);
                    record[name] = Md5(target);
                    touched?.Add(target);
                }
                catch (Exception e)
                {
                    r.Errors.Add($"{destFolder}/{name}: {e.Message}");
                }
            }
        }

        /// <summary>Published shader GUID (the package's Shaders~/*.meta) -> GUID of the shader installed under the same name.</summary>
        private static Dictionary<string, string> InstalledShaderGuidMap()
        {
            var map = new Dictionary<string, string>();
            string src = KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.ShaderSources);
            string dst = KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Shaders);
            if (src == null || dst == null || !Directory.Exists(src) || !Directory.Exists(dst)) return map;
            foreach (string meta in Directory.EnumerateFiles(src, "*.shader.meta"))
            {
                string published = ReadGuid(meta);
                string installed = ReadGuid(Path.Combine(dst, Path.GetFileName(meta)));
                if (published != null && installed != null && published != installed) map[published] = installed;
            }
            return map;
        }

        private static string ReadGuid(string metaFile)
        {
            if (!File.Exists(metaFile)) return null;
            foreach (string line in File.ReadLines(metaFile))
                if (line.StartsWith("guid: ", StringComparison.Ordinal)) return line.Substring(6).Trim();
            return null;
        }

        private static void RemapShaderGuids(string materialFile, Dictionary<string, string> map)
        {
            if (map.Count == 0 || !File.Exists(materialFile)) return;
            string text = File.ReadAllText(materialFile), updated = text;
            foreach (var kv in map) updated = updated.Replace("guid: " + kv.Key + ",", "guid: " + kv.Value + ",");
            if (updated != text) File.WriteAllText(materialFile, updated, new UTF8Encoding(false));
        }

        private static void CopyMeta(string sourceFile, string targetFile, bool onlyIfMissing = false)
        {
            string srcMeta = sourceFile + ".meta", dstMeta = targetFile + ".meta";
            if (!File.Exists(srcMeta)) return;
            if (onlyIfMissing && File.Exists(dstMeta)) return;
            File.Copy(srcMeta, dstMeta, true);
        }

        private static bool SameContent(string a, string b)
        {
            var fa = new FileInfo(a); var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            byte[] ba = File.ReadAllBytes(a), bb = File.ReadAllBytes(b);
            for (int i = 0; i < ba.Length; i++) if (ba[i] != bb[i]) return false;
            return true;
        }

        private static string Md5(string file)
        {
            using (var md5 = MD5.Create())
            using (var fs = File.OpenRead(file))
                return BitConverter.ToString(md5.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        private static void Backup(string file, string version)
        {
            string dir = Path.Combine(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Backups), string.IsNullOrEmpty(version) ? "unknown" : version);
            Directory.CreateDirectory(dir);
            string dest = Path.Combine(dir, Path.GetFileName(file));
            int n = 1;
            while (File.Exists(dest)) dest = Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + $" ({n++})" + Path.GetExtension(file));
            File.Copy(file, dest, true);
        }

        // ------------------------------------------------------------------ project repair

        private static void OfferProjectRepair(KawaiiStudioProjectRepair.ScanResult scan)
        {
            if (_repairAsked) return;
            _repairAsked = true;
            string preview = string.Join("\n", scan.Findings.Where(f => f.Fixable).Take(10).Select(f => "• " + f.Path));
            if (scan.FixableCount > 10) preview += $"\n… and {scan.FixableCount - 10} more (see the Console)";
            bool go = EditorUtility.DisplayDialog("Kawaii Studio",
                "Kawaii Studio looked through the project and found things an older install left behind:\n\n" +
                scan.Summary() + "\n\n" + preview +
                "\n\nRepair now? Old files are moved to Backups~ or into the workspace; nothing is deleted.",
                "Repair", "Not now");
            if (go) KawaiiStudioProjectRepair.Repair(scan);
        }

        /// <summary>
        /// Park the given pre-package scripts (absolute paths, with their .meta) in
        /// Backups~/legacy-editor-&lt;stamp&gt;/ and refresh. Returns how many moved. Nothing is deleted.
        /// </summary>
        public static int MoveLegacyScripts(IEnumerable<string> files)
        {
            string dir = Path.Combine(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Backups), "legacy-editor-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(dir);
            int moved = 0;
            foreach (string f in files.ToList())
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    string dest = Path.Combine(dir, Path.GetFileName(f));
                    int n = 1;
                    while (File.Exists(dest)) dest = Path.Combine(dir, Path.GetFileNameWithoutExtension(f) + $" ({n++})" + Path.GetExtension(f));
                    File.Move(f, dest);
                    if (File.Exists(f + ".meta")) File.Move(f + ".meta", dest + ".meta");
                    moved++;
                }
                catch (Exception e) { Debug.LogWarning("[Kawaii Studio] could not move " + f + ": " + e.Message); }
            }
            if (moved > 0) AssetDatabase.Refresh();
            return moved;
        }

        // ------------------------------------------------------------------ stamp

        [Serializable]
        private sealed class Stamp
        {
            public string package;
            public string installed;
            public string layout;
            public string[] files;   // "<file name>=<md5>" of every shader / material the installer put there
        }

        private static string StampPath => KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Workspace + "/" + StampFileName);

        private static Dictionary<string, string> ParseFiles(Stamp s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s?.files == null) return d;
            foreach (string e in s.files)
            {
                int i = e.LastIndexOf('=');
                if (i > 0) d[e.Substring(0, i)] = e.Substring(i + 1);
            }
            return d;
        }

        private static Stamp ReadStamp()
        {
            try
            {
                string p = StampPath;
                if (p == null || !File.Exists(p)) return null;
                var s = JsonUtility.FromJson<Stamp>(File.ReadAllText(p));
                return s != null && !string.IsNullOrEmpty(s.package) ? s : null;
            }
            catch { return null; }
        }

        private static void WriteStamp(Stamp s)
        {
            string p = StampPath;
            if (p == null) return;
            string json = JsonUtility.ToJson(s, true);
            if (File.Exists(p) && File.ReadAllText(p) == json) return;
            File.WriteAllText(p, json);
        }
    }
}
