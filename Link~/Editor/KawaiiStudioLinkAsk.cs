// QUESTIONS FROM THE APP, FOR AN AI (Link 1.1.0). The Kawaii Studio app has an MCP door (UnityMcp.cs in the app) through
// which an AI -- Capri, when Claude Code is her AI -- asks about the project open here: its scenes, an object's
// components, the selection, the Console, assets, a text file. The app queues each question like an import ("query"
// instead of "import"), the long poll hands it over, and it is answered here, on Unity's main thread, then posted back on
// /unity/answer.
//
// THIS FILE ONLY READS: nothing in it changes a scene, an object, an asset, a setting or a file. The editing questions
// (unity_write_text, unity_set_field...) are KawaiiStudioLinkEdit.cs's, and the app only passes them on while its
// "edit" switch is on. Each answer is plain text (an AI reads it best that way) and is capped, so a huge scene cannot
// stall the editor or the wire.
//
// Unity 2019.4 to Unity 6: C# 7.3 only (no switch expressions, no ??=), and the one internal API (the Console's entries)
// is reached by reflection with a way out when it is not there.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    public static partial class KawaiiStudioLink
    {
        // Characters of one answer at most. Claude Code warns past 10 000 tokens of a tool's answer and cuts at 25 000
        // (MAX_MCP_OUTPUT_TOKENS): 80 000 characters stay under that; each tool also stops itself at LinkAsk.Budget.
        private const int MaxAnswer = 80000;

        // A question taken from the poll (OnPoll, main thread): answered at once, here.
        private static void AnswerQuestion(string id, string tool, string args)
        {
            string result = null, error = null;
            try
            {
                result = LinkAsk.Run(tool ?? "", string.IsNullOrEmpty(args) ? "{}" : args);
                if (result.Length > MaxAnswer)
                {
                    result = result.Substring(0, MaxAnswer) + "\n... (cut: the answer was longer than " + MaxAnswer + " characters; ask for less)";
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                LinkEdit.After = null;
            }
            // An edit may leave work for after its answer (an import that recompiles scripts, Play mode): that reloads the
            // scripts, and a send still on the thread pool would die with them. So the answer goes first, at once, and then
            // the work, on the next editor frame.
            Action after = LinkEdit.After;
            LinkEdit.After = null;
            Send("/unity/answer", error == null
                ? "{\"id\":" + Quote(id) + ",\"ok\":true,\"result\":" + Quote(result) + "}"
                : "{\"id\":" + Quote(id) + ",\"ok\":false,\"error\":" + Quote(error) + "}", after != null);
            if (after != null)
            {
                EditorApplication.delayCall += () =>
                {
                    try { after(); }
                    catch (Exception e) { Debug.LogException(e); }
                };
            }
        }
    }

    /// <summary>The read-only questions an AI may ask about this project, by the names the app's MCP door gives them.</summary>
    public static class LinkAsk
    {
#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        private class Args
        {
            public string root;
            public int depth;
            public int max;
            public string path;
            public int maxFields;
            public string kinds;
            public string filter;
            public string folder;
            public int from;
            public int lines;
        }
#pragma warning restore 0649

        /// <summary>What each tool writes at most before it stops and says how to ask for the rest.</summary>
        internal const int Budget = 60000;

        public static string Run(string tool, string argsJson)
        {
            Args a;
            try { a = JsonUtility.FromJson<Args>(argsJson) ?? new Args(); }
            catch { a = new Args(); }
            switch (tool)
            {
                case "unity_project": return Project();
                case "unity_hierarchy": return Hierarchy(a.root, Clamp(a.depth, 4, 1, 10), Clamp(a.max, 400, 1, 2000));
                case "unity_inspect": return Inspect(a.path, Clamp(a.maxFields, 60, 1, 300));
                case "unity_selection": return SelectionNow();
                case "unity_console": return Console(string.IsNullOrEmpty(a.kinds) ? "error,warning" : a.kinds, Clamp(a.max, 40, 1, 300));
                case "unity_find_assets": return FindAssets(a.filter, a.folder, Clamp(a.max, 100, 1, 1000));
                case "unity_read_text": return ReadText(a.path, Math.Max(1, a.from), Math.Max(0, a.lines));
                default:
                    if (LinkEdit.Knows(tool)) return LinkEdit.Run(tool, argsJson);
                    throw new ArgumentException("this KS Unity Tools does not know the question \"" + tool + "\": update it");
            }
        }

        private static int Clamp(int v, int fallback, int min, int max) => v <= 0 ? fallback : Math.Max(min, Math.Min(max, v));

        internal static string ProjectFolder => Directory.GetParent(Application.dataPath).FullName;

        // ------------------------------------------------------------------ unity_project

        private static string Project()
        {
            var b = new StringBuilder();
            string root = ProjectFolder;
            b.Append("Project: ").Append(Path.GetFileName(root)).Append('\n');
            b.Append("Folder: ").Append(root).Append('\n');
            b.Append("Unity: ").Append(Application.unityVersion).Append('\n');
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            b.Append("Render pipeline: ").Append(rp == null ? "Built-in" : rp.GetType().Name + " (" + rp.name + ")").Append('\n');
            b.Append("Color space: ").Append(PlayerSettings.colorSpace).Append('\n');
            var packages = new List<string>();
            foreach (string id in new[] { "com.vrchat.base", "com.vrchat.avatars", "com.vrchat.worlds", "com.vrcfury.vrcfury", "com.poiyomi.toon", "com.kawaiistudio.ksunitytools", "com.kawaiistudio.link" })
            {
                string v = PackageVersion(root, id);
                if (v != null) packages.Add(id + " " + v);
            }
            b.Append("Packages of note: ").Append(packages.Count == 0 ? "none of the VRChat SDK" : string.Join(", ", packages)).Append('\n');
            b.Append("State: ").Append(EditorApplication.isPlaying ? "in Play mode" : "edit mode")
             .Append(EditorApplication.isCompiling ? ", compiling scripts" : "").Append('\n');
            int errors, warnings, logs;
            if (ConsoleCounts(out errors, out warnings, out logs))
            {
                b.Append("Console: ").Append(errors).Append(" error(s), ").Append(warnings).Append(" warning(s), ").Append(logs).Append(" log(s)\n");
            }
            b.Append("Open scenes:\n");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                b.Append("  - ").Append(string.IsNullOrEmpty(s.name) ? "(untitled)" : s.name)
                 .Append(string.IsNullOrEmpty(s.path) ? "" : " (" + s.path + ")")
                 .Append(s.isLoaded ? ", " + s.rootCount + " root object(s)" : ", not loaded")
                 .Append(s.isDirty ? ", unsaved changes" : "")
                 .Append(s == SceneManager.GetActiveScene() ? ", active" : "").Append('\n');
            }
            b.Append("Selected: ").Append(Selection.objects.Length).Append(" object(s)\n");
            b.Append("Kawaii Studio Link ").Append(KawaiiStudioLinkVersion.Current);
            return b.ToString();
        }

        private static readonly Regex VersionInJson = new Regex("\"version\"\\s*:\\s*\"([^\"]+)\"");

        // A package's version: its folder under Packages (VCC and embedded packages), else packages-lock.json.
        private static string PackageVersion(string root, string id)
        {
            try
            {
                string pj = Path.Combine(root, "Packages", id, "package.json");
                if (File.Exists(pj))
                {
                    Match m = VersionInJson.Match(File.ReadAllText(pj));
                    if (m.Success) return m.Groups[1].Value;
                }
                string lockFile = Path.Combine(root, "Packages", "packages-lock.json");
                if (File.Exists(lockFile))
                {
                    string text = File.ReadAllText(lockFile);
                    int at = text.IndexOf("\"" + id + "\"", StringComparison.Ordinal);
                    if (at >= 0)
                    {
                        Match m = VersionInJson.Match(text, at);
                        if (m.Success) return m.Groups[1].Value;
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        // ------------------------------------------------------------------ unity_hierarchy

        private static string Hierarchy(string root, int depth, int max)
        {
            var b = new StringBuilder();
            int shown = 0;
            bool cut = false;
            if (!string.IsNullOrEmpty(root))
            {
                GameObject go = FindByPath(root);
                if (go == null) throw new ArgumentException("no object at \"" + root + "\" in the open scenes (paths go from a root object: 'Avatar/Armature/Hips')");
                b.Append("Branch of ").Append(PathOf(go)).Append(" (scene ").Append(go.scene.name).Append("):\n");
                Walk(go.transform, 0, depth, max, b, ref shown, ref cut);
            }
            else
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene s = SceneManager.GetSceneAt(i);
                    if (!s.isLoaded) continue;
                    b.Append("Scene ").Append(s.name).Append(':').Append('\n');
                    foreach (GameObject go in s.GetRootGameObjects())
                    {
                        Walk(go.transform, 1, depth, max, b, ref shown, ref cut);
                        if (cut) break;
                    }
                    if (cut) break;
                }
            }
            b.Append(shown).Append(" object(s) shown");
            if (cut && shown >= max) b.Append(" -- CUT at ").Append(max).Append(": ask for a branch (root) or a bigger max");
            else if (cut) b.Append(" -- CUT (the answer is as long as it may be): ask for a branch (root) or less depth");
            return b.ToString();
        }

        private static void Walk(Transform t, int level, int depth, int max, StringBuilder b, ref int shown, ref bool cut)
        {
            if (cut) return;
            if (shown >= max || b.Length > Budget)
            {
                cut = true;
                return;
            }
            shown++;
            b.Append(' ', level * 2).Append(t.name);
            var types = new List<string>();
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null) types.Add("MISSING SCRIPT");
                else if (!(c is Transform)) types.Add(c.GetType().Name);
            }
            if (types.Count > 0) b.Append(" [").Append(string.Join(", ", types)).Append(']');
            if (!t.gameObject.activeSelf) b.Append(" (inactive)");
            if (t.childCount > 0 && level + 1 > depth) b.Append(" (+").Append(t.childCount).Append(" child(ren) below)");
            b.Append('\n');
            if (level + 1 > depth) return;
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), level + 1, depth, max, b, ref shown, ref cut);
        }

        // 'Root/Child/Grandchild' in the loaded scenes, the first match at each step (inactive ones too).
        internal static GameObject FindByPath(string path)
        {
            string[] parts = path.Trim('/').Split('/');
            if (parts.Length == 0) return null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isLoaded) continue;
                foreach (GameObject r in s.GetRootGameObjects())
                {
                    if (r.name != parts[0]) continue;
                    Transform t = r.transform;
                    for (int k = 1; k < parts.Length && t != null; k++)
                    {
                        Transform next = null;
                        for (int c = 0; c < t.childCount; c++)
                        {
                            if (t.GetChild(c).name == parts[k])
                            {
                                next = t.GetChild(c);
                                break;
                            }
                        }
                        t = next;
                    }
                    if (t != null) return t.gameObject;
                }
            }
            return null;
        }

        internal static string PathOf(GameObject go)
        {
            var names = new List<string>();
            for (Transform t = go.transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        // ------------------------------------------------------------------ unity_inspect

        private static string Inspect(string path, int maxFields)
        {
            GameObject go;
            if (string.IsNullOrEmpty(path))
            {
                go = Selection.activeGameObject;
                if (go == null) throw new ArgumentException("nothing is selected in Unity: give the object's path");
            }
            else
            {
                go = FindByPath(path);
                if (go == null) throw new ArgumentException("no object at \"" + path + "\" in the open scenes");
            }
            var b = new StringBuilder();
            b.Append(PathOf(go)).Append(go.activeSelf ? "" : " (inactive)").Append(go.activeInHierarchy || !go.activeSelf ? "" : " (a parent is inactive)").Append('\n');
            b.Append("Tag: ").Append(go.tag).Append(", layer: ").Append(LayerMask.LayerToName(go.layer)).Append(", static: ").Append(go.isStatic ? "yes" : "no").Append('\n');
            if (PrefabUtility.IsPartOfAnyPrefab(go))
            {
                Object src = PrefabUtility.GetCorrespondingObjectFromSource(go);
                b.Append("Prefab: ").Append(src != null ? AssetDatabase.GetAssetPath(src) : "yes").Append('\n');
            }
            foreach (Component c in go.GetComponents<Component>())
            {
                if (c == null)
                {
                    b.Append("\n== MISSING SCRIPT (its script is gone from the project)\n");
                    continue;
                }
                Behaviour be = c as Behaviour;
                b.Append("\n== ").Append(c.GetType().Name).Append(be != null && !be.enabled ? " (disabled)" : "").Append('\n');
                var so = new SerializedObject(c);
                SerializedProperty p = so.GetIterator();
                int n = 0;
                bool enter = true;
                while (p.NextVisible(enter))
                {
                    enter = false;
                    if (n++ >= maxFields)
                    {
                        b.Append("  ... (more fields: ask with a bigger maxFields)\n");
                        break;
                    }
                    b.Append("  ").Append(p.displayName).Append(": ").Append(Value(p, 1)).Append('\n');
                }
            }
            return b.ToString();
        }

        internal static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);

        internal static string Value(SerializedProperty p, int level)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
                case SerializedPropertyType.Float: return F(p.floatValue);
                case SerializedPropertyType.String: return "\"" + Clip(p.stringValue, 300) + "\"";
                case SerializedPropertyType.Color:
                {
                    Color c = p.colorValue;
                    return "RGBA(" + F(c.r) + ", " + F(c.g) + ", " + F(c.b) + ", " + F(c.a) + ")";
                }
                case SerializedPropertyType.ObjectReference: return Ref(p.objectReferenceValue);
                case SerializedPropertyType.LayerMask: return "layer mask " + p.intValue;
                case SerializedPropertyType.Enum:
                {
                    int i = p.enumValueIndex;
                    return i >= 0 && i < p.enumDisplayNames.Length ? p.enumDisplayNames[i] : p.intValue.ToString(CultureInfo.InvariantCulture);
                }
                case SerializedPropertyType.Vector2: return "(" + F(p.vector2Value.x) + ", " + F(p.vector2Value.y) + ")";
                case SerializedPropertyType.Vector3: return "(" + F(p.vector3Value.x) + ", " + F(p.vector3Value.y) + ", " + F(p.vector3Value.z) + ")";
                case SerializedPropertyType.Vector4: return "(" + F(p.vector4Value.x) + ", " + F(p.vector4Value.y) + ", " + F(p.vector4Value.z) + ", " + F(p.vector4Value.w) + ")";
                case SerializedPropertyType.Vector2Int: return "(" + p.vector2IntValue.x + ", " + p.vector2IntValue.y + ")";
                case SerializedPropertyType.Vector3Int: return "(" + p.vector3IntValue.x + ", " + p.vector3IntValue.y + ", " + p.vector3IntValue.z + ")";
                case SerializedPropertyType.Quaternion:
                {
                    Vector3 e = p.quaternionValue.eulerAngles;
                    return "euler (" + F(e.x) + ", " + F(e.y) + ", " + F(e.z) + ")";
                }
                case SerializedPropertyType.Rect: return "rect " + p.rectValue;
                case SerializedPropertyType.Bounds: return "bounds center " + p.boundsValue.center + " size " + p.boundsValue.size;
                case SerializedPropertyType.ArraySize: return p.intValue.ToString(CultureInfo.InvariantCulture);
                case SerializedPropertyType.Character: return "'" + (char)p.intValue + "'";
                case SerializedPropertyType.AnimationCurve: return "curve (" + (p.animationCurveValue != null ? p.animationCurveValue.length : 0) + " keys)";
                case SerializedPropertyType.Gradient: return "gradient";
            }
            if (p.isArray)
            {
                int count = p.arraySize;
                if (level > 2 || count == 0) return "[" + count + " item(s)]";
                var items = new List<string>();
                for (int i = 0; i < Math.Min(count, 8); i++) items.Add(Value(p.GetArrayElementAtIndex(i), level + 1));
                return "[" + count + " item(s): " + string.Join("; ", items.ToArray()) + (count > 8 ? "; ..." : "") + "]";
            }
            if (p.hasVisibleChildren && level <= 2)
            {
                var parts = new List<string>();
                SerializedProperty child = p.Copy();
                SerializedProperty end = p.GetEndProperty();
                bool enter = true;
                while (child.NextVisible(enter) && !SerializedProperty.EqualContents(child, end) && parts.Count < 8)
                {
                    enter = false;
                    parts.Add(child.displayName + " " + Value(child, level + 1));
                }
                return "{" + string.Join(", ", parts.ToArray()) + "}";
            }
            return "(" + p.propertyType + ")";
        }

        internal static string Ref(Object o)
        {
            if (o == null) return "None";
            string asset = AssetDatabase.GetAssetPath(o);
            if (!string.IsNullOrEmpty(asset)) return o.name + " (" + o.GetType().Name + ", " + asset + ")";
            GameObject go = o as GameObject;
            Component c = o as Component;
            if (go == null && c != null) go = c.gameObject;
            return o.name + " (" + o.GetType().Name + (go != null ? ", " + PathOf(go) : "") + ")";
        }

        private static string Clip(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "...";

        // ------------------------------------------------------------------ unity_selection

        private static string SelectionNow()
        {
            Object[] all = Selection.objects;
            if (all.Length == 0) return "Nothing is selected in Unity.";
            var b = new StringBuilder();
            b.Append(all.Length).Append(" selected:\n");
            foreach (Object o in all.Take(100))
            {
                GameObject go = o as GameObject;
                string asset = AssetDatabase.GetAssetPath(o);
                if (go != null && string.IsNullOrEmpty(asset)) b.Append("  - GameObject ").Append(PathOf(go)).Append(" (scene ").Append(go.scene.name).Append(")\n");
                else b.Append("  - ").Append(o.GetType().Name).Append(' ').Append(string.IsNullOrEmpty(asset) ? o.name : asset).Append('\n');
            }
            if (all.Length > 100) b.Append("  ... and ").Append(all.Length - 100).Append(" more\n");
            return b.ToString();
        }

        // ------------------------------------------------------------------ unity_console

        // Unity's Console flags (UnityEditor.LogMessageFlags, the same since 2017): which ones are errors, which warnings.
        private const int ErrorMask = (1 << 0) | (1 << 1) | (1 << 4) | (1 << 6) | (1 << 8) | (1 << 11) | (1 << 17) | (1 << 20) | (1 << 21) | (1 << 22);
        private const int WarningMask = (1 << 7) | (1 << 9) | (1 << 12);

        private static Type _entries, _entry;

        private static bool ConsoleTypes()
        {
            if (_entries != null && _entry != null) return true;
            Assembly editor = typeof(Editor).Assembly;
            _entries = editor.GetType("UnityEditor.LogEntries") ?? editor.GetType("UnityEditorInternal.LogEntries");
            _entry = editor.GetType("UnityEditor.LogEntry") ?? editor.GetType("UnityEditorInternal.LogEntry");
            return _entries != null && _entry != null;
        }

        private static bool ConsoleCounts(out int errors, out int warnings, out int logs)
        {
            errors = warnings = logs = 0;
            try
            {
                if (!ConsoleTypes()) return false;
                MethodInfo m = _entries.GetMethod("GetCountsByType", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null) return false;
                object[] args = { 0, 0, 0 };
                m.Invoke(null, args);
                errors = (int)args[0];
                warnings = (int)args[1];
                logs = (int)args[2];
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string Console(string kinds, int max)
        {
            bool wantErrors = kinds.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantWarnings = kinds.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantLogs = kinds.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0;
            if (!ConsoleTypes()) throw new InvalidOperationException("this version of Unity does not let the Console be read");
            const BindingFlags any = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo start = _entries.GetMethod("StartGettingEntries", any);
            MethodInfo get = _entries.GetMethod("GetEntryInternal", any);
            MethodInfo stop = _entries.GetMethod("EndGettingEntries", any);
            FieldInfo text = _entry.GetField("message", any) ?? _entry.GetField("condition", any);
            FieldInfo mode = _entry.GetField("mode", any);
            if (start == null || get == null || stop == null || text == null || mode == null)
            {
                throw new InvalidOperationException("this version of Unity does not let the Console be read");
            }
            // Unity hands out only what its Console window shows: the window's Log / Warning / Error buttons filter the entries
            // (measured: a lab whose Console hid warnings gave none). Shown all three while reading, the user's own buttons back
            // right after (ConsoleWindow.ConsoleFlags: LogLevelLog 1<<7, LogLevelWarning 1<<8, LogLevelError 1<<9).
            const int allLevels = (1 << 7) | (1 << 8) | (1 << 9);
            PropertyInfo flagsProp = _entries.GetProperty("consoleFlags", any);
            int userFlags = 0;
            bool reflag = false;
            try
            {
                if (flagsProp != null && flagsProp.CanRead && flagsProp.CanWrite)
                {
                    userFlags = (int)flagsProp.GetValue(null, null);
                    if ((userFlags & allLevels) != allLevels)
                    {
                        flagsProp.SetValue(null, userFlags | allLevels, null);
                        reflag = true;
                    }
                }
            }
            catch
            {
                reflag = false;
            }
            var found = new List<string>();
            int count;
            try
            {
                count = (int)start.Invoke(null, null);
            }
            catch
            {
                if (reflag) flagsProp.SetValue(null, userFlags, null);
                throw;
            }
            try
            {
                object entry = Activator.CreateInstance(_entry);
                for (int i = 0; i < count; i++)
                {
                    if (!(bool)get.Invoke(null, new[] { i, entry })) continue;
                    int flags = (int)mode.GetValue(entry);
                    string kind = (flags & ErrorMask) != 0 ? "ERROR" : (flags & WarningMask) != 0 ? "WARNING" : "LOG";
                    if (kind == "ERROR" ? !wantErrors : kind == "WARNING" ? !wantWarnings : !wantLogs) continue;
                    string[] lines = ((string)text.GetValue(entry) ?? "").Replace("\r", "").Split('\n');
                    var one = new StringBuilder();
                    one.Append(kind).Append(": ").Append(lines[0]);
                    for (int k = 1; k < Math.Min(lines.Length, 7); k++)
                    {
                        if (lines[k].Trim().Length > 0) one.Append("\n    ").Append(lines[k].Trim());
                    }
                    found.Add(one.ToString());
                }
            }
            finally
            {
                stop.Invoke(null, null);
                if (reflag)
                {
                    try { flagsProp.SetValue(null, userFlags, null); } catch { }
                }
            }
            if (found.Count == 0) return "The Console shows no " + kinds.Replace(",", " or ") + ".";
            // the newest are kept: from the end, as many as max and the budget allow
            int from = found.Count, size = 0;
            while (from > 0 && found.Count - from < max && size + found[from - 1].Length + 1 <= Budget)
            {
                from--;
                size += found[from].Length + 1;
            }
            var b = new StringBuilder();
            if (from > 0) b.Append("(").Append(from).Append(" older one(s) left out)\n");
            for (int i = from; i < found.Count; i++) b.Append(found[i]).Append('\n');
            return b.ToString();
        }

        // ------------------------------------------------------------------ unity_find_assets

        private static string FindAssets(string filter, string folder, int max)
        {
            if (string.IsNullOrEmpty(filter)) throw new ArgumentException("a filter is needed, like 't:Material Skin' or a name");
            string[] guids;
            if (!string.IsNullOrEmpty(folder))
            {
                folder = folder.Replace('\\', '/').TrimEnd('/');
                if (!AssetDatabase.IsValidFolder(folder)) throw new ArgumentException("no folder \"" + folder + "\" in the project (it starts with Assets/ or Packages/)");
                guids = AssetDatabase.FindAssets(filter, new[] { folder });
            }
            else
            {
                guids = AssetDatabase.FindAssets(filter);
            }
            if (guids.Length == 0) return "No asset matches \"" + filter + "\"" + (string.IsNullOrEmpty(folder) ? "." : " under " + folder + ".");
            var b = new StringBuilder();
            b.Append(guids.Length).Append(" found").Append(guids.Length > max ? " (the first " + max + ")" : "").Append(":\n");
            foreach (string g in guids.Take(max))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                Type t = AssetDatabase.GetMainAssetTypeAtPath(p);
                b.Append("  ").Append(p).Append(t != null ? " (" + t.Name + ")" : "").Append('\n');
            }
            return b.ToString();
        }

        // ------------------------------------------------------------------ unity_read_text

        internal static readonly HashSet<string> TextTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".shader", ".cginc", ".hlsl", ".hlslinc", ".compute", ".glsl", ".asmdef", ".asmref", ".json", ".txt", ".md",
            ".xml", ".yaml", ".yml", ".uss", ".uxml", ".csv", ".unity", ".prefab", ".mat", ".anim", ".controller",
            ".overrideController", ".asset", ".mask", ".playable", ".physicMaterial", ".lighting", ".sln", ".csproj", ".props",
            ".targets", ".editorconfig", ".gitignore", ".gitattributes", ".vpm", ".rsp", ".meta",
        };

        internal static readonly string[] NeverIn = { "Library", "Temp", "Logs", "obj", ".git", ".vs", ".idea", "Build", "Builds" };

        // Numbered lines ("12: ..."), the way a compile error names them; a big file comes part by part (from=, lines=).
        private static string ReadText(string path, int from, int count)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("a path is needed, relative to the project folder, like 'Assets/Scripts/Spin.cs'");
            string root = Path.GetFullPath(ProjectFolder).TrimEnd('\\', '/');
            string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("only files of the project are read (\"" + path + "\" is outside it)");
            }
            string first = full.Substring(root.Length + 1).Split(Path.DirectorySeparatorChar)[0];
            if (NeverIn.Any(n => string.Equals(n, first, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("the " + first + " folder is Unity's own, not read");
            }
            if (!TextTypes.Contains(Path.GetExtension(full)))
            {
                throw new ArgumentException("not a text file this tool reads (" + Path.GetExtension(full) + ")");
            }
            if (!File.Exists(full)) throw new ArgumentException("no file \"" + path + "\" in the project");
            var info = new FileInfo(full);
            if (info.Length > 64L * 1024 * 1024) throw new ArgumentException("\"" + path + "\" is " + info.Length + " bytes: too big to read");
            string text = File.ReadAllText(full);
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("\"" + path + "\" is saved as binary, not text");
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int total = lines.Length;
            if (total > 1 && lines[total - 1].Length == 0) total--; // the last line's end is not a line
            string shown = full.Substring(root.Length + 1).Replace('\\', '/');
            if (total == 0) return "File " + shown + " (0 bytes): empty";
            if (from > total) throw new ArgumentException("\"" + shown + "\" has only " + total + " line(s)");
            int last = count > 0 ? Math.Min(total, from + count - 1) : total;
            var body = new StringBuilder();
            int end = from - 1;
            for (int i = from; i <= last; i++)
            {
                string line = lines[i - 1];
                if (line.Length > 2000) line = line.Substring(0, 2000) + " ...(this line goes on: " + line.Length + " characters)";
                if (body.Length + line.Length > Budget && i > from) break;
                body.Append(i).Append(": ").Append(line).Append('\n');
                end = i;
            }
            var b = new StringBuilder();
            b.Append("File ").Append(shown).Append(" (").Append(info.Length).Append(" bytes, ").Append(total).Append(" lines), lines ")
             .Append(from).Append('-').Append(end).Append(":\n").Append(body);
            if (end < total) b.Append("... (lines ").Append(end + 1).Append('-').Append(total).Append(" remain: ask again with from=").Append(end + 1).Append(")\n");
            return b.ToString();
        }
    }
}
