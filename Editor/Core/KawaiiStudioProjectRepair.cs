using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Looks INSIDE the project for everything Kawaii Studio-related that an older install, a
    /// hand copy or a broken import left behind, and puts it right. Two steps, so the user
    /// always sees what will happen first: <see cref="Scan"/> is read-only, <see cref="Repair"/>
    /// applies the fixes. Nothing is ever deleted: what moves goes to Backups~.
    ///
    /// What it finds:
    ///  * DuplicateShader  -- a byte-identical copy of one of our shaders outside the workspace
    ///                        (an old .unitypackage import, a drag-and-drop). Two shaders with the
    ///                        same name make Shader.Find pick one at random. Fix: every material on
    ///                        the copy is repointed to the workspace shader, the copy is parked.
    ///  * ModifiedShaderCopy -- same, but the copy differs from ours: it is the user's variant.
    ///                        Reported, never touched.
    ///  * BrokenMaterial   -- a material whose shader GUID resolves to nothing (the "Hidden/
    ///                        InternalErrorShader" pink). When its saved properties, or a GUID we
    ///                        know from an older install, identify one of our shaders beyond doubt,
    ///                        the material is repointed; otherwise it is reported only.
    ///  * LegacyScript     -- a script of a pre-package install (the tools' own .cs files under
    ///                        Assets/). They compile next to the package and double every menu item.
    ///                        Fix: parked in Backups~.
    ///  * StrayFolder      -- outputs older tools wrote outside the workspace
    ///                        ("Assets/Kawaii Studio Optimized", "Assets/KS_Protected_*"). Fix: moved
    ///                        into the workspace through the AssetDatabase, so references hold.
    ///  * LegacyFfmpeg     -- FFMPEG in the pre-3.4 location "Assets/ThirdParty/FFMPEG" while the
    ///                        workspace has none. Fix: moved to Assets/Kawaii Studio/ThirdParty/FFMPEG.
    /// </summary>
    public static class KawaiiStudioProjectRepair
    {
        public enum Kind { DuplicateShader, ModifiedShaderCopy, BrokenMaterial, LegacyScript, StrayFolder, LegacyFfmpeg }

        public sealed class Finding
        {
            public Kind Kind;
            public string Path;      // the asset concerned
            public string Target;    // where it goes / what it is repointed to (asset path or guid)
            public string Detail;    // one line for the user
            public bool Fixable;
            public override string ToString() => $"{Kind}: {Path} -- {Detail}" + (Fixable ? "" : " (not changed)");
        }

        public sealed class ScanResult
        {
            public DateTime When = DateTime.Now;
            public List<Finding> Findings = new List<Finding>();
            public List<string> Errors = new List<string>();
            public int FixableCount => Findings.Count(f => f.Fixable);
            public int ReportOnlyCount => Findings.Count(f => !f.Fixable);
            public bool Clean => Findings.Count == 0;

            public string Summary()
            {
                if (Clean) return "Project scan: nothing to repair.";
                var byKind = Findings.GroupBy(f => f.Kind).Select(g => $"{g.Count()} {Describe(g.Key, g.Count())}");
                return $"Project scan: {FixableCount} fixable, {ReportOnlyCount} to look at -- " + string.Join(", ", byKind);
            }

            public override string ToString()
            {
                var sb = new StringBuilder(Summary());
                foreach (var f in Findings) sb.Append('\n').Append("  ").Append(f);
                foreach (var e in Errors) sb.Append('\n').Append("  error: ").Append(e);
                return sb.ToString();
            }
        }

        public sealed class RepairResult
        {
            public List<string> Done = new List<string>();
            public List<string> Errors = new List<string>();
            public override string ToString() =>
                $"Project repair: {Done.Count} change(s)" + (Errors.Count > 0 ? $", {Errors.Count} error(s)" : "") +
                string.Concat(Done.Select(d => "\n  " + d)) + string.Concat(Errors.Select(e => "\n  error: " + e));
        }

        /// <summary>Shader GUIDs of older installs that never shipped: material -> shader file name.</summary>
        private static readonly Dictionary<string, string> KnownLegacyShaderGuids = new Dictionary<string, string>
        {
            { "bc67626dd7e67eb4fa52947fbd59f075", "KSVampireCrimsonEye" },   // the author's project GUID, in EYE SHADER.mat up to 3.3.0
        };

        private static readonly (string Folder, string WorkspaceSub)[] StrayFolders =
        {
            ("Assets/Kawaii Studio Optimized", "Optimized"),
        };

        private static readonly Regex ShaderRef = new Regex(@"m_Shader: \{fileID: 4800000, guid: ([0-9a-f]{32}), type: 3\}", RegexOptions.Compiled);
        private static readonly Regex NoShaderRef = new Regex(@"m_Shader: \{fileID: 0\}", RegexOptions.Compiled);
        private static readonly Regex SavedProperty = new Regex(@"^\s+- (_[A-Za-z0-9_]+):", RegexOptions.Compiled | RegexOptions.Multiline);

        public static ScanResult LastScan { get; private set; }
        public static RepairResult LastRepair { get; private set; }

        private sealed class OurShader
        {
            public string FileName;      // "KSVideoDecoder"
            public string AssetPath;     // workspace path
            public string Guid;
            public string DeclaredName;  // Shader "..."
            public HashSet<string> Properties;
            public string SourceText;    // normalized, for the duplicate test
        }

        // ------------------------------------------------------------------ scan

        /// <summary>Read-only. Looks through Assets/ (never Packages/) and lists what Repair would do.</summary>
        public static ScanResult Scan()
        {
            var res = new ScanResult();
            try
            {
                var ours = LoadOurShaders(res);
                ScanShaderCopies(ours, res);
                ScanMaterials(ours, res);
                ScanLegacyScripts(res);
                ScanStrayFolders(res);
                ScanLegacyFfmpeg(res);
            }
            catch (Exception e)
            {
                res.Errors.Add(e.GetType().Name + ": " + e.Message);
            }
            LastScan = res;
            return res;
        }

        private static List<OurShader> LoadOurShaders(ScanResult res)
        {
            var list = new List<OurShader>();
            string dir = KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Shaders);
            if (dir == null || !Directory.Exists(dir)) return list;
            foreach (string file in Directory.EnumerateFiles(dir, "*.shader"))
            {
                string asset = KawaiiStudioPaths.Shaders + "/" + Path.GetFileName(file);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(asset);
                if (shader == null) { res.Errors.Add("workspace shader not imported: " + asset); continue; }
                var props = new HashSet<string>();
                int n = ShaderUtil.GetPropertyCount(shader);
                for (int i = 0; i < n; i++) props.Add(ShaderUtil.GetPropertyName(shader, i));
                list.Add(new OurShader
                {
                    FileName = Path.GetFileNameWithoutExtension(file),
                    AssetPath = asset,
                    Guid = AssetDatabase.AssetPathToGUID(asset),
                    DeclaredName = shader.name,
                    Properties = props,
                    SourceText = Normalize(File.ReadAllText(file)),
                });
            }
            return list;
        }

        private static void ScanShaderCopies(List<OurShader> ours, ScanResult res)
        {
            if (ours.Count == 0) return;
            string workspaceShaders = KawaiiStudioPaths.Shaders + "/";
            var byName = ours.ToDictionary(o => o.DeclaredName, o => o);
            foreach (string guid in AssetDatabase.FindAssets("t:Shader"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUserAsset(path) || path.StartsWith(workspaceShaders, StringComparison.OrdinalIgnoreCase)) continue;
                if (!path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase)) continue;
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null || !byName.TryGetValue(shader.name, out var mine)) continue;

                string abs = KawaiiStudioPaths.ToAbsolute(path);
                string text;
                try { text = Normalize(File.ReadAllText(abs)); } catch { continue; }
                bool identical = text == mine.SourceText;
                res.Findings.Add(new Finding
                {
                    Kind = identical ? Kind.DuplicateShader : Kind.ModifiedShaderCopy,
                    Path = path,
                    Target = mine.AssetPath,
                    Fixable = identical,
                    Detail = identical
                        ? $"copy of {mine.FileName}; materials will use {mine.AssetPath}, the copy goes to Backups~"
                        : $"a modified copy of {mine.FileName} (yours): kept, materials on it stay on it",
                });
            }
        }

        private static void ScanMaterials(List<OurShader> ours, ScanResult res)
        {
            var byGuid = ours.ToDictionary(o => o.Guid, o => o);
            var byFile = ours.ToDictionary(o => o.FileName, o => o);
            // the GUIDs of the duplicate copies that Repair will retire
            var retiring = res.Findings.Where(f => f.Kind == Kind.DuplicateShader)
                .ToDictionary(f => AssetDatabase.AssetPathToGUID(f.Path), f => f.Target);

            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUserAsset(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                string abs = KawaiiStudioPaths.ToAbsolute(path);
                string text;
                try { text = File.ReadAllText(abs); } catch { continue; }

                var m = ShaderRef.Match(text);
                string shaderGuid = m.Success ? m.Groups[1].Value : null;

                if (shaderGuid != null && retiring.TryGetValue(shaderGuid, out string replacement))
                {
                    // covered by the duplicate's repair; listed so the user sees the material move
                    res.Findings.Add(new Finding { Kind = Kind.BrokenMaterial, Path = path, Target = replacement, Fixable = true,
                        Detail = $"uses the duplicate shader; will use {replacement}" });
                    continue;
                }

                bool missing = shaderGuid != null
                    ? string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(shaderGuid))
                    : NoShaderRef.IsMatch(text);
                if (!missing) continue;

                OurShader target = null;
                string why;
                if (shaderGuid != null && KnownLegacyShaderGuids.TryGetValue(shaderGuid, out string known) && byFile.TryGetValue(known, out target))
                {
                    why = $"shader {shaderGuid} is the pre-3.4 GUID of {target.FileName}";
                }
                else
                {
                    target = MatchByProperties(text, ours, out why);
                }

                res.Findings.Add(new Finding
                {
                    Kind = Kind.BrokenMaterial,
                    Path = path,
                    Target = target?.AssetPath,
                    Fixable = target != null,
                    Detail = target != null ? $"{why}; will use {target.AssetPath}" : "shader missing, and no Kawaii Studio shader matches its properties",
                });
            }
        }

        /// <summary>
        /// The one of our shaders a shader-less material was made for, from the property names it
        /// saved -- only when the match is beyond doubt: it covers at least 80 % of the material's
        /// properties and owns one of them that no other shader of ours has.
        /// </summary>
        private static OurShader MatchByProperties(string materialText, List<OurShader> ours, out string why)
        {
            why = null;
            var props = new HashSet<string>(SavedProperty.Matches(materialText).Cast<Match>().Select(x => x.Groups[1].Value));
            if (props.Count == 0 || ours.Count == 0) return null;

            OurShader best = null; double bestScore = 0; int ties = 0;
            foreach (var o in ours)
            {
                double score = props.Count(p => o.Properties.Contains(p)) / (double)props.Count;
                if (score > bestScore + 1e-9) { best = o; bestScore = score; ties = 0; }
                else if (Math.Abs(score - bestScore) < 1e-9 && score > 0) ties++;
            }
            if (best == null || bestScore < 0.8 || ties > 0) return null;

            var signature = props.Where(p => best.Properties.Contains(p) && !ours.Any(o => o != best && o.Properties.Contains(p))).ToList();
            if (signature.Count == 0) return null;
            why = $"its properties match {best.FileName} ({bestScore:P0}, e.g. {signature[0]})";
            return best;
        }

        private static void ScanLegacyScripts(ScanResult res)
        {
            var names = new HashSet<string>(KawaiiStudioWorkspace.LegacyScriptNames, StringComparer.OrdinalIgnoreCase);
            string packageRoot = KawaiiStudioPaths.PackageRoot + "/";
            string oldEditor = KawaiiStudioPaths.Workspace + "/Editor/";
            foreach (string guid in AssetDatabase.FindAssets("t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUserAsset(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) continue;
                if (path.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase)) continue;   // the .unitypackage install itself

                bool known = names.Contains(Path.GetFileName(path));
                bool inOldEditor = path.StartsWith(oldEditor, StringComparison.OrdinalIgnoreCase);
                if (!known && !inOldEditor) continue;
                if (!known)
                {
                    // under the old install's Editor folder only scripts of the toolset's own namespace count
                    string text;
                    try { text = File.ReadAllText(KawaiiStudioPaths.ToAbsolute(path)); } catch { continue; }
                    if (!Regex.IsMatch(text, @"namespace\s+KawaiiStudio\b")) continue;
                }
                res.Findings.Add(new Finding
                {
                    Kind = Kind.LegacyScript, Path = path, Target = KawaiiStudioPaths.Backups, Fixable = true,
                    Detail = known ? "a tool script of an older install; the package has it now" : "an older Kawaii Studio script; the package replaces it",
                });
            }
        }

        private static void ScanStrayFolders(ScanResult res)
        {
            foreach (var (folder, sub) in StrayFolders)
                if (AssetDatabase.IsValidFolder(folder))
                    res.Findings.Add(new Finding { Kind = Kind.StrayFolder, Path = folder, Target = KawaiiStudioPaths.Workspace + "/" + sub, Fixable = true,
                        Detail = $"output of an older version; its content moves into {KawaiiStudioPaths.Workspace}/{sub}" });

            foreach (string f in AssetDatabase.GetSubFolders("Assets"))
                if (Path.GetFileName(f).StartsWith("KS_Protected_", StringComparison.Ordinal))
                    res.Findings.Add(new Finding { Kind = Kind.StrayFolder, Path = f, Target = KawaiiStudioPaths.Protected + "/" + Path.GetFileName(f).Substring("KS_Protected_".Length), Fixable = true,
                        Detail = $"Obfuscator output of an older version; moves into {KawaiiStudioPaths.Protected}" });
        }

        private static void ScanLegacyFfmpeg(ScanResult res)
        {
            const string legacy = "Assets/ThirdParty/FFMPEG";
            if (!AssetDatabase.IsValidFolder(legacy)) return;
            if (!HasFfmpeg(KawaiiStudioPaths.ToAbsolute(legacy))) return;
            if (HasFfmpeg(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.FFmpeg))) return;
            res.Findings.Add(new Finding { Kind = Kind.LegacyFfmpeg, Path = legacy, Target = KawaiiStudioPaths.FFmpeg, Fixable = true,
                Detail = $"FFMPEG in the pre-3.4 location; moves to {KawaiiStudioPaths.FFmpeg}" });
        }

        // ------------------------------------------------------------------ repair

        /// <summary>Apply every fixable finding of a scan. Moves go to Backups~ or into the workspace; nothing is deleted.</summary>
        public static RepairResult Repair(ScanResult scan)
        {
            var r = new RepairResult();
            if (scan == null) return r;
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                // 1. old scripts first: fewer compile passes
                var scripts = scan.Findings.Where(f => f.Kind == Kind.LegacyScript && f.Fixable).Select(f => KawaiiStudioPaths.ToAbsolute(f.Path)).ToList();
                if (scripts.Count > 0)
                {
                    int moved = KawaiiStudioWorkspace.MoveLegacyScripts(scripts);
                    r.Done.Add($"{moved} old script(s) moved to {KawaiiStudioPaths.Backups}");
                }

                // 2. stray output folders and FFMPEG: AssetDatabase moves keep every GUID, so references hold
                foreach (var f in scan.Findings.Where(f => (f.Kind == Kind.StrayFolder || f.Kind == Kind.LegacyFfmpeg) && f.Fixable))
                {
                    try
                    {
                        KawaiiStudioPaths.EnsureFolder(f.Target);
                        int n = MoveChildren(f.Path, f.Target);
                        if (AssetDatabase.FindAssets("", new[] { f.Path }).Length == 0) AssetDatabase.DeleteAsset(f.Path);   // the now-empty folder only
                        r.Done.Add($"{f.Path}: {n} item(s) moved into {f.Target}");
                    }
                    catch (Exception e) { r.Errors.Add($"{f.Path}: {e.Message}"); }
                }

                // 3. duplicate shaders: repoint their materials to the workspace shader, park the copy
                var dupes = scan.Findings.Where(f => f.Kind == Kind.DuplicateShader && f.Fixable).ToList();
                foreach (var f in dupes)
                {
                    try
                    {
                        string oldGuid = AssetDatabase.AssetPathToGUID(f.Path);
                        string newGuid = AssetDatabase.AssetPathToGUID(f.Target);
                        if (string.IsNullOrEmpty(oldGuid) || string.IsNullOrEmpty(newGuid)) { r.Errors.Add($"{f.Path}: guid not found"); continue; }
                        int n = RepointMaterials(oldGuid, newGuid);
                        ParkAsset(f.Path, Path.Combine(KawaiiStudioPaths.ToAbsolute(KawaiiStudioPaths.Backups), "duplicate-shaders-" + stamp));
                        r.Done.Add($"{f.Path}: {n} material(s) now use {f.Target}; copy parked in Backups~");
                    }
                    catch (Exception e) { r.Errors.Add($"{f.Path}: {e.Message}"); }
                }

                // 4. broken materials
                foreach (var f in scan.Findings.Where(f => f.Kind == Kind.BrokenMaterial && f.Fixable && !string.IsNullOrEmpty(f.Target)))
                {
                    try
                    {
                        string newGuid = AssetDatabase.AssetPathToGUID(f.Target);
                        if (string.IsNullOrEmpty(newGuid)) { r.Errors.Add($"{f.Path}: {f.Target} not imported"); continue; }
                        if (RepointMaterial(f.Path, newGuid)) r.Done.Add($"{f.Path}: now uses {f.Target}");
                    }
                    catch (Exception e) { r.Errors.Add($"{f.Path}: {e.Message}"); }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                r.Errors.Add(e.GetType().Name + ": " + e.Message);
            }
            LastRepair = r;
            Debug.Log("[Kawaii Studio] " + r);
            return r;
        }

        /// <summary>Scan, then repair everything fixable. What the Manager's button and the headless install run.</summary>
        public static RepairResult ScanAndRepair()
        {
            var scan = Scan();
            return scan.FixableCount == 0 ? new RepairResult() : Repair(scan);
        }

        [MenuItem("Kawaii Studio/Workspace/Scan and Repair Project", priority = 22)]
        public static void MenuScanAndRepair()
        {
            KawaiiStudioWorkspace.EnsureInstalled();
            var scan = Scan();
            Debug.Log("[Kawaii Studio] " + scan);
            if (scan.Clean)
            {
                EditorUtility.DisplayDialog("Kawaii Studio", "Project scan: nothing to repair.", "OK");
                return;
            }
            string preview = string.Join("\n", scan.Findings.Take(12).Select(f => "• " + f.Path + (f.Fixable ? "" : "  (left as is)")));
            if (scan.Findings.Count > 12) preview += $"\n… and {scan.Findings.Count - 12} more (see the Console)";
            bool go = scan.FixableCount > 0 && EditorUtility.DisplayDialog("Kawaii Studio",
                scan.Summary() + "\n\n" + preview + "\n\nRepair now? Moved files go to Backups~ or into the workspace; nothing is deleted.",
                "Repair", "Not now");
            if (!go)
            {
                if (scan.FixableCount == 0) EditorUtility.DisplayDialog("Kawaii Studio", scan.Summary() + "\n\n" + preview, "OK");
                return;
            }
            var rep = Repair(scan);
            EditorUtility.DisplayDialog("Kawaii Studio", rep.ToString(), "OK");
        }

        // ------------------------------------------------------------------ helpers

        private static string Describe(Kind k, int n)
        {
            switch (k)
            {
                case Kind.DuplicateShader: return n == 1 ? "duplicate shader" : "duplicate shaders";
                case Kind.ModifiedShaderCopy: return n == 1 ? "modified shader copy (kept)" : "modified shader copies (kept)";
                case Kind.BrokenMaterial: return n == 1 ? "material on a missing shader" : "materials on a missing shader";
                case Kind.LegacyScript: return n == 1 ? "old script" : "old scripts";
                case Kind.StrayFolder: return n == 1 ? "stray output folder" : "stray output folders";
                case Kind.LegacyFfmpeg: return "FFMPEG in the old place";
                default: return k.ToString();
            }
        }

        private static bool IsUserAsset(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);

        private static string Normalize(string s) =>
            string.Join("\n", s.Replace("\r\n", "\n").Split('\n').Select(l => l.TrimEnd())).Trim();

        private static bool HasFfmpeg(string dir) =>
            dir != null && Directory.Exists(dir) &&
            (File.Exists(Path.Combine(dir, "ffmpeg.exe")) || File.Exists(Path.Combine(dir, "ffmpeg")) ||
             File.Exists(Path.Combine(dir, "bin", "ffmpeg.exe")) || File.Exists(Path.Combine(dir, "bin", "ffmpeg")));

        private static int MoveChildren(string fromFolder, string toFolder)
        {
            int n = 0;
            string abs = KawaiiStudioPaths.ToAbsolute(fromFolder);
            foreach (string entry in Directory.EnumerateFileSystemEntries(abs))
            {
                string name = Path.GetFileName(entry);
                if (name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string src = fromFolder + "/" + name;
                string dst = AssetDatabase.GenerateUniqueAssetPath(toFolder + "/" + name);
                string err = AssetDatabase.MoveAsset(src, dst);
                if (!string.IsNullOrEmpty(err)) throw new IOException(err);
                n++;
            }
            return n;
        }

        private static int RepointMaterials(string oldGuid, string newGuid)
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsUserAsset(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) continue;
                string abs = KawaiiStudioPaths.ToAbsolute(path);
                string text;
                try { text = File.ReadAllText(abs); } catch { continue; }
                if (!text.Contains("guid: " + oldGuid)) continue;
                if (RepointMaterial(path, newGuid)) n++;
            }
            return n;
        }

        private static bool RepointMaterial(string path, string newGuid)
        {
            string abs = KawaiiStudioPaths.ToAbsolute(path);
            string text = File.ReadAllText(abs);
            string updated;
            if (ShaderRef.IsMatch(text))
                updated = ShaderRef.Replace(text, "m_Shader: {fileID: 4800000, guid: " + newGuid + ", type: 3}", 1);
            else if (NoShaderRef.IsMatch(text))
                updated = NoShaderRef.Replace(text, "m_Shader: {fileID: 4800000, guid: " + newGuid + ", type: 3}", 1);
            else
                return false;
            if (updated == text) return false;
            File.WriteAllText(abs, updated, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return true;
        }

        private static void ParkAsset(string assetPath, string backupDir)
        {
            Directory.CreateDirectory(backupDir);
            string abs = KawaiiStudioPaths.ToAbsolute(assetPath);
            string dest = Path.Combine(backupDir, Path.GetFileName(abs));
            int i = 1;
            while (File.Exists(dest)) dest = Path.Combine(backupDir, Path.GetFileNameWithoutExtension(abs) + $" ({i++})" + Path.GetExtension(abs));
            File.Move(abs, dest);
            if (File.Exists(abs + ".meta")) File.Move(abs + ".meta", dest + ".meta");
        }
    }
}
