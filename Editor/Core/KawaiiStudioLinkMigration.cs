using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// 3.20.0: the Kawaii Studio Link is part of KS Unity Tools again (its own assembly, KawaiiStudio.Editor.Link, in the
    /// package's Link folder). Before, it was a second package, com.kawaiistudio.link, that every KS Unity Tools pulled in
    /// (3.6.0 - 3.19.x), so the Creator Companion showed two packages. This puts a project right by itself:
    ///
    /// - The separate package com.kawaiistudio.link leaves the project: out of Packages/vpm-manifest.json (dependencies,
    ///   locked, and the dependency lists of the locked packages) and Packages/manifest.json, then its folder is deleted
    ///   (a junction only loses the link, never what it points at) and Unity resolves the packages again. While that
    ///   package is still there, our Link's assembly stays off by itself (versionDefine KS_SEPARATE_LINK_PACKAGE), so two
    ///   Links never pair at once; the next compile turns ours on.
    /// - A Link left in Assets/Kawaii Studio/Link by the .unitypackage, when KS Unity Tools itself is a VCC package.
    /// - The 3.5.0 copy in KS Unity Tools' own Editor/Link (from before the Link was a package at all).
    ///
    /// Runs once per editor load, only in the interactive editor (a batch build is never changed under its feet; the
    /// command-line switch -ksLinkRepair runs it in batch mode, for tests), never in play mode or while Unity compiles.
    /// </summary>
    [InitializeOnLoad]
    internal static class KawaiiStudioLinkMigration
    {
        private const string SeparatePackage = "com.kawaiistudio.link";
        private static readonly string[] OldScripts = { "KawaiiStudioLink.cs", "KawaiiStudioLinkWindow.cs", "KawaiiStudioTestBuild.cs" };
        private const string Tag = "[KS Unity Tools] ";

        static KawaiiStudioLinkMigration()
        {
            if (Application.isBatchMode && !Environment.GetCommandLineArgs().Any(a => string.Equals(a, "-ksLinkRepair", StringComparison.OrdinalIgnoreCase)))
                return;
#if UNITY_2020_2_OR_NEWER
            // Measured in the lab (2022.3): the Refresh compiles while the Package Manager still lists the removed package,
            // so our Link stays off; the compile has to be asked for again once the package is really gone.
            UnityEditor.PackageManager.Events.registeredPackages += OnRegistered;
#endif
            EditorApplication.delayCall += Run;
        }

#if UNITY_2020_2_OR_NEWER
        private static void OnRegistered(UnityEditor.PackageManager.PackageRegistrationEventArgs args)
        {
            if (args.removed.Any(p => p.name == SeparatePackage)) RecompileForOurLink("the separate package is gone");
        }
#endif

        /// <summary>Our Link's assembly is decided at compile time (KS_SEPARATE_LINK_PACKAGE): once the separate package
        /// is gone, one more compile turns it on. Asked at most twice per editor session, so it can never loop.</summary>
        private static void RecompileForOurLink(string why)
        {
            if (Type.GetType("KawaiiStudio.KawaiiStudioLinkVersion, KawaiiStudio.Editor.Link", false) != null) return;
            int asked = SessionState.GetInt("KawaiiStudio.LinkMerge.Recompile", 0);
            if (asked >= 2) return;
            SessionState.SetInt("KawaiiStudio.LinkMerge.Recompile", asked + 1);
            Debug.Log(Tag + "Turning on the Kawaii Studio Link of KS Unity Tools (" + why + "): recompiling once.");
            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        }

        private static bool SeparatePackageThere()
        {
            string packages = Path.Combine(ProjectRoot, "Packages");
            if (Directory.Exists(Path.Combine(packages, SeparatePackage))) return true;
            foreach (string f in new[] { "vpm-manifest.json", "manifest.json" })
            {
                string p = Path.Combine(packages, f);
                if (File.Exists(p) && File.ReadAllText(p).IndexOf("\"" + SeparatePackage + "\"", StringComparison.Ordinal) >= 0) return true;
            }
            return false;
        }

        private static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged += AfterPlay;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Run;
                return;
            }
            bool changed = false;
            try { changed |= RemoveSeparatePackage(); }
            catch (Exception e) { Debug.LogWarning(Tag + "Could not remove the separate Kawaii Studio Link package: " + e.Message); }
            try { changed |= RemoveUnitypackageLink(); }
            catch (Exception e) { Debug.LogWarning(Tag + "Could not remove the old Kawaii Studio Link in Assets: " + e.Message); }
            try { RemoveCopyFrom350(); }
            catch (Exception e) { Debug.LogWarning(Tag + "Kawaii Studio Link migration: " + e.Message); }
            if (changed) Resolve();
            else if (!SeparatePackageThere()) RecompileForOurLink("no separate package left");
        }

        private static void AfterPlay(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= AfterPlay;
            EditorApplication.delayCall += Run;
        }

        private static string ProjectRoot => Directory.GetParent(Application.dataPath).FullName;

        // ── the separate package (VCC, the Kawaii Studio app, or a UPM git line) ─────────────────────────────────────────

        internal static bool RemoveSeparatePackage()
        {
            string packages = Path.Combine(ProjectRoot, "Packages");
            var removed = new List<string>();

            string vpm = Path.Combine(packages, "vpm-manifest.json");
            if (File.Exists(vpm) && Edit(vpm, root =>
                {
                    bool c = false;
                    if (root.Get("dependencies") is MiniJson.Obj deps) c |= deps.Remove(SeparatePackage);
                    if (root.Get("locked") is MiniJson.Obj locked)
                    {
                        c |= locked.Remove(SeparatePackage);
                        foreach (KeyValuePair<string, object> entry in locked.Items)
                            if (entry.Value is MiniJson.Obj pkg && pkg.Get("dependencies") is MiniJson.Obj pdeps)
                                c |= pdeps.Remove(SeparatePackage);
                    }
                    return c;
                }))
                removed.Add("Packages/vpm-manifest.json");

            string upm = Path.Combine(packages, "manifest.json");
            if (File.Exists(upm) && Edit(upm, root => root.Get("dependencies") is MiniJson.Obj deps && deps.Remove(SeparatePackage)))
                removed.Add("Packages/manifest.json");

            string folder = Path.Combine(packages, SeparatePackage);
            if (Directory.Exists(folder))
            {
                var info = new DirectoryInfo(folder);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) Directory.Delete(folder, false);   // a junction: the link only
                else Directory.Delete(folder, true);
                removed.Add("Packages/" + SeparatePackage);
            }

            if (removed.Count == 0) return false;
            Debug.Log(Tag + "The Kawaii Studio Link is part of KS Unity Tools now: removed the separate package " + SeparatePackage +
                      " (" + string.Join(", ", removed.ToArray()) + "). One package left: KS Unity Tools.");
            return true;
        }

        /// <summary>Rewrites a JSON file when <paramref name="change"/> changed it; the original goes to
        /// Library/KawaiiStudio first. A file that does not parse is left alone (and said so).</summary>
        private static bool Edit(string path, Func<MiniJson.Obj, bool> change)
        {
            string text = File.ReadAllText(path);
            if (text.IndexOf(SeparatePackage, StringComparison.Ordinal) < 0) return false;
            MiniJson.Obj root;
            try { root = MiniJson.Parse(text) as MiniJson.Obj; }
            catch (Exception e) { Debug.LogWarning(Tag + Path.GetFileName(path) + " could not be read (" + e.Message + "); left as it is."); return false; }
            if (root == null || !change(root)) return false;
            string backupDir = Path.Combine(ProjectRoot, "Library", "KawaiiStudio");
            Directory.CreateDirectory(backupDir);
            File.Copy(path, Path.Combine(backupDir, Path.GetFileNameWithoutExtension(path) + ".before-link-merge.json"), true);
            string tmp = path + ".ks-tmp";
            File.WriteAllText(tmp, MiniJson.Write(root) + "\n", new UTF8Encoding(false));
            File.Copy(tmp, path, true);
            File.Delete(tmp);
            return true;
        }

        // ── a .unitypackage Link beside the VCC package ─────────────────────────────────────────────────────────────────

        private static bool RemoveUnitypackageLink()
        {
            const string old = "Assets/Kawaii Studio/Link";
            if (!KawaiiStudioPaths.PackageRoot.StartsWith("Packages/", StringComparison.Ordinal)) return false;   // ours IS that folder
            if (Type.GetType("KawaiiStudio.KawaiiStudioLinkVersion, KawaiiStudio.Editor.Link", false) == null) return false;
            string abs = Path.Combine(ProjectRoot, old.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(Path.Combine(abs, "Editor", "KawaiiStudioLink.cs"))) return false;
            if (!AssetDatabase.DeleteAsset(old)) return false;
            Debug.Log(Tag + "Removed the Kawaii Studio Link the .unitypackage left in " + old + ": it is part of KS Unity Tools now.");
            return false;   // the AssetDatabase saw it already: nothing to resolve
        }

        // ── 3.5.0's copy in KS Unity Tools' own Editor/Link ────────────────────────────────────────────────────────────

        private static void RemoveCopyFrom350()
        {
            string oldFolder = KawaiiStudioPaths.Editor + "/Link";
            if (!KawaiiStudioPaths.FolderExists(oldFolder)) return;
            // only when our Link is loaded, or this would remove the project's only Link
            if (Type.GetType("KawaiiStudio.KawaiiStudioLinkVersion, KawaiiStudio.Editor.Link", false) == null) return;
            int removed = 0;
            foreach (string file in OldScripts)
            {
                string path = oldFolder + "/" + file;
                string abs = KawaiiStudioPaths.ToAbsolute(path);
                if (abs == null || !File.Exists(abs)) continue;
                if (AssetDatabase.DeleteAsset(path)) removed++;
            }
            string folderAbs = KawaiiStudioPaths.ToAbsolute(oldFolder);
            if (folderAbs != null && Directory.Exists(folderAbs) && !Directory.EnumerateFileSystemEntries(folderAbs).Any(e => !e.EndsWith(".meta")))
                AssetDatabase.DeleteAsset(oldFolder);
            if (removed > 0)
                Debug.Log(Tag + "Removed the 3.5.0 copy of the Kawaii Studio Link (" + removed + " script(s) in " + oldFolder + ").");
        }

        private static void Resolve()
        {
            // Client.Resolve (2020.1+) makes the Package Manager drop the deleted package; it imports and compiles after it
            // has (OnRegistered asks the compile that turns our Link on). NO AssetDatabase.Refresh here: measured in the
            // lab, a Refresh compiles while the package is still registered and Unity shows 8 "CS2001 source file could not
            // be found" errors for its deleted scripts until the next compile. Refresh is only for editors without Resolve.
            try
            {
                var m = typeof(UnityEditor.PackageManager.Client).GetMethod("Resolve", Type.EmptyTypes);
                if (m != null)
                {
                    m.Invoke(null, null);
                    return;
                }
            }
            catch (Exception e) { Debug.LogWarning(Tag + "Package resolve: " + e.Message); }
            AssetDatabase.Refresh();
        }
    }

    /// <summary>Just enough JSON for the package manifests: objects keep their key order, numbers keep their text.</summary>
    internal static class MiniJson
    {
        internal sealed class Obj
        {
            public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

            public object Get(string key)
            {
                foreach (KeyValuePair<string, object> kv in Items) if (kv.Key == key) return kv.Value;
                return null;
            }

            public bool Remove(string key) => Items.RemoveAll(kv => kv.Key == key) > 0;
        }

        internal sealed class Num
        {
            public readonly string Text;
            public Num(string text) { Text = text; }
        }

        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Skip(s, ref i);
            if (i != s.Length) throw new FormatException("text after the JSON at " + i);
            return v;
        }

        private static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n' || s[i] == '﻿')) i++;
        }

        private static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("unexpected end");
            char c = s[i];
            if (c == '{')
            {
                var o = new Obj();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Skip(s, ref i);
                    string k = Str(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("':' expected at " + i);
                    i++;
                    o.Items.Add(new KeyValuePair<string, object>(k, Value(s, ref i)));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("',' or '}' expected at " + i);
                }
            }
            if (c == '[')
            {
                var a = new List<object>();
                i++;
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("',' or ']' expected at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("unexpected '" + c + "' at " + i);
            return new Num(s.Substring(start, i - start));
        }

        private static string Str(string s, ref int i)
        {
            if (i >= s.Length || s[i] != '"') throw new FormatException("'\"' expected at " + i);
            i++;
            var b = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return b.ToString();
                if (c != '\\') { b.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': b.Append('\n'); break;
                    case 'r': b.Append('\r'); break;
                    case 't': b.Append('\t'); break;
                    case 'b': b.Append('\b'); break;
                    case 'f': b.Append('\f'); break;
                    case 'u':
                        b.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: b.Append(e); break;
                }
            }
            throw new FormatException("unterminated string");
        }

        public static string Write(object v)
        {
            var b = new StringBuilder();
            Write(b, v, 0);
            return b.ToString();
        }

        private static void Write(StringBuilder b, object v, int depth)
        {
            string pad = new string(' ', (depth + 1) * 2), end = new string(' ', depth * 2);
            if (v is Obj o)
            {
                if (o.Items.Count == 0) { b.Append("{}"); return; }
                b.Append("{\n");
                for (int k = 0; k < o.Items.Count; k++)
                {
                    b.Append(pad);
                    Quote(b, o.Items[k].Key);
                    b.Append(": ");
                    Write(b, o.Items[k].Value, depth + 1);
                    b.Append(k < o.Items.Count - 1 ? ",\n" : "\n");
                }
                b.Append(end).Append('}');
            }
            else if (v is List<object> a)
            {
                if (a.Count == 0) { b.Append("[]"); return; }
                b.Append("[\n");
                for (int k = 0; k < a.Count; k++)
                {
                    b.Append(pad);
                    Write(b, a[k], depth + 1);
                    b.Append(k < a.Count - 1 ? ",\n" : "\n");
                }
                b.Append(end).Append(']');
            }
            else if (v is string s) Quote(b, s);
            else if (v is Num n) b.Append(n.Text);
            else if (v is bool t) b.Append(t ? "true" : "false");
            else b.Append("null");
        }

        private static void Quote(StringBuilder b, string s)
        {
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
        }
    }
}
