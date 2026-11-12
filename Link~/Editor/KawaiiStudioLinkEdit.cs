// EDITING FROM THE APP, FOR AN AI (Link 1.1.0; the owner: "edition full access, sinon c'est pas un assistant Unity").
// The Kawaii Studio app passes these on only while the "edit" switch of its Capricorn section is on (UnityMcp.Edit);
// they reach this project the way the read-only questions do (KawaiiStudioLinkAsk.cs), on Unity's main thread.
//
// WHAT CAN BE TAKEN BACK. Every change to a scene or an object is ONE Undo step named "Capri: ...": Ctrl+Z in Unity
// takes it back. A text file that is overwritten or edited is copied first into Assets/Kawaii Studio/Backups~/AI (Unity
// never imports a folder whose name ends in ~) -- only text files, never the same content twice in a row, and the
// folder keeps the 30 newest copies within 10 MB, the oldest go (the owner: "faut pas que ca abuse des sauvegardes").
// A deleted asset goes to the Windows Recycle Bin. Nothing is saved by itself: scene changes stay unsaved until
// unity_editor save (or the user's Ctrl+S).
//
// Work that reloads the scripts (an import that recompiles, Play mode) is left in After and done once the answer is sent.
//
// Unity 2019.4 to Unity 6: C# 7.3 only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    /// <summary>The editing questions an AI may ask through the app, by the names the app's MCP door gives them.</summary>
    public static class LinkEdit
    {
        /// <summary>Work for after the answer is sent (an import, a refresh, Play mode): taken by AnswerQuestion.</summary>
        internal static Action After;

        private const string BackupRoot = "Assets/Kawaii Studio/Backups~/AI";
        private const int BackupsKept = 30;
        private const long BackupBytes = 10L * 1024 * 1024;

        private static readonly HashSet<string> Tools = new HashSet<string>
        {
            "unity_write_text", "unity_edit_text", "unity_create_object", "unity_modify_object", "unity_component",
            "unity_set_field", "unity_asset", "unity_editor",
        };

        public static bool Knows(string tool) => Tools.Contains(tool);

#pragma warning disable 0649 // filled by JsonUtility
        [Serializable]
        private class Args
        {
            public string path, content, old, @new, name, parent, primitive, components, position, rotation, scale, tag, layer;
            public string action, type, component, field, value, to, shader, menu;
            public bool all, active, delete, duplicate;
            public int index;
        }
#pragma warning restore 0649

        public static string Run(string tool, string json)
        {
            Args a;
            try { a = JsonUtility.FromJson<Args>(json) ?? new Args(); }
            catch { a = new Args(); }
            if (tool == "unity_editor") return EditorAction(a);   // undo / redo must not open a group of their own
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Capri: " + tool.Replace("unity_", "").Replace('_', ' '));
            try
            {
                string done;
                switch (tool)
                {
                    case "unity_write_text": done = WriteText(a.path, a.content); break;
                    case "unity_edit_text": done = EditText(a.path, a.old, a.@new, a.all); break;
                    case "unity_create_object": done = CreateObject(a); break;
                    case "unity_modify_object": done = ModifyObject(a, Has(json, "active")); break;
                    case "unity_component": done = Component(a); break;
                    case "unity_set_field": done = SetField(a.path, a.component, a.field, a.value); break;
                    case "unity_asset": done = AssetAction(a); break;
                    default: throw new ArgumentException("unknown editing question " + tool);
                }
                if (EditorApplication.isPlaying && (tool == "unity_create_object" || tool == "unity_modify_object" || tool == "unity_component" ||
                                                    (tool == "unity_set_field" && !IsAssetPath(a.path))))
                {
                    done += "\n(Unity is in Play mode: this change to the scene is lost when Play stops.)";
                }
                return done;
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        private static bool Has(string json, string key) => Regex.IsMatch(json ?? "", "\"" + Regex.Escape(key) + "\"\\s*:");

        private static bool IsAssetPath(string p) =>
            !string.IsNullOrEmpty(p) && (p.StartsWith("Assets/", StringComparison.Ordinal) || p.StartsWith("Packages/", StringComparison.Ordinal) || p == "Assets");

        // ------------------------------------------------------------------ text files

        // A file of the project that may be written: under Assets/ or Packages/, a text type, never a .meta, never Unity's own.
        private static string Writable(string path, out string shown)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("a path is needed, relative to the project folder, like 'Assets/Scripts/Spin.cs'");
            string root = Path.GetFullPath(LinkAsk.ProjectFolder).TrimEnd('\\', '/');
            string full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("only files of the project are written (\"" + path + "\" is outside it)");
            }
            shown = full.Substring(root.Length + 1).Replace('\\', '/');
            string first = shown.Split('/')[0];
            if (!string.Equals(first, "Assets", StringComparison.Ordinal) && !string.Equals(first, "Packages", StringComparison.Ordinal))
            {
                throw new ArgumentException("files are written under Assets/ or Packages/ only (not \"" + shown + "\")");
            }
            if (shown.StartsWith(BackupRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("the AI's backups are not written by the AI");
            }
            string ext = Path.GetExtension(full);
            if (string.Equals(ext, ".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("a .meta file is Unity's: not written");
            if (!LinkAsk.TextTypes.Contains(ext)) throw new ArgumentException("not a text file this tool writes (" + ext + ")");
            return full;
        }

        private static int Lines(string text) => text.Length == 0 ? 0 : text.TrimEnd('\n').Split('\n').Length;

        private static string WriteText(string path, string content)
        {
            if (content == null) throw new ArgumentException("content is needed (the whole file)");
            string shown;
            string full = Writable(path, out shown);
            bool existed = File.Exists(full);
            string backup = null;
            if (existed)
            {
                string old = File.ReadAllText(full);
                if (old.IndexOf("\r\n", StringComparison.Ordinal) >= 0 && content.IndexOf("\r\n", StringComparison.Ordinal) < 0)
                {
                    content = content.Replace("\n", "\r\n");   // the file's own line ends
                }
                if (old == content) return "No change: " + shown + " already has exactly this content.";
                backup = Backup(shown, full);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content, new UTF8Encoding(false));
            After = () => AssetDatabase.Refresh();
            return (existed ? "Replaced " : "Created ") + shown + " (" + Encoding.UTF8.GetByteCount(content) + " bytes, " + Lines(content.Replace("\r\n", "\n")) + " lines)."
                   + (backup != null ? " The previous version is kept in " + backup + "." : "")
                   + " Unity imports it now" + (shown.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || shown.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                       ? ": scripts recompile; wait a few seconds, then read unity_console for compile errors." : ".");
        }

        private static string EditText(string path, string oldText, string newText, bool all)
        {
            if (string.IsNullOrEmpty(oldText)) throw new ArgumentException("old is needed: the exact text to replace");
            if (newText == null) newText = "";
            string shown;
            string full = Writable(path, out shown);
            if (!File.Exists(full)) throw new ArgumentException("no file \"" + shown + "\" in the project (unity_write_text creates one)");
            string raw = File.ReadAllText(full);
            bool crlf = raw.IndexOf("\r\n", StringComparison.Ordinal) >= 0;
            string text = raw.Replace("\r\n", "\n");
            string find = oldText.Replace("\r\n", "\n"), put = newText.Replace("\r\n", "\n");
            int count = 0;
            for (int at = text.IndexOf(find, StringComparison.Ordinal); at >= 0; at = text.IndexOf(find, at + find.Length, StringComparison.Ordinal)) count++;
            if (count == 0) throw new ArgumentException("old was not found in " + shown + " (spaces, tabs and line breaks count; read it again with unity_read_text)");
            if (count > 1 && !all) throw new ArgumentException("old is found " + count + " times in " + shown + ": give more of the lines around it, or all=true");
            int first = text.IndexOf(find, StringComparison.Ordinal);
            string changed = all ? text.Replace(find, put) : text.Substring(0, first) + put + text.Substring(first + find.Length);
            string backup = Backup(shown, full);
            File.WriteAllText(full, crlf ? changed.Replace("\n", "\r\n") : changed, new UTF8Encoding(false));
            After = () => AssetDatabase.Refresh();
            // the lines around the (first) change, numbered as unity_read_text numbers them
            string[] lines = changed.Split('\n');
            int line = changed.Substring(0, first).Count(c => c == '\n');
            int from = Math.Max(0, line - 3), to = Math.Min(lines.Length - 1, line + Lines(put) + 3);
            var b = new StringBuilder();
            b.Append("Edited ").Append(shown).Append(": ").Append(all ? count + " replacement(s)" : "1 replacement").Append(". Previous version kept in ").Append(backup).Append(".\n");
            for (int i = from; i <= to; i++) b.Append(i + 1).Append(": ").Append(lines[i]).Append('\n');
            if (shown.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) b.Append("Scripts recompile: wait a few seconds, then read unity_console for compile errors.");
            return b.ToString();
        }

        // A copy of the file before it changes, in the AI's backup folder; the folder is kept small.
        private static string Backup(string shown, string full)
        {
            string root = LinkAsk.ProjectFolder;
            string dir = Path.Combine(root, BackupRoot.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = File.ReadAllBytes(full);
            string flat = shown.Replace('/', '_');
            Directory.CreateDirectory(dir);
            // the same content already kept as the newest copy of this file: not again
            FileInfo last = new DirectoryInfo(dir).GetFiles("*" + flat).OrderByDescending(f => f.Name, StringComparer.Ordinal).FirstOrDefault();
            if (last != null && last.Length == bytes.Length && File.ReadAllBytes(last.FullName).SequenceEqual(bytes))
            {
                return BackupRoot + "/" + last.Name;
            }
            string name = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + "_" + flat;
            File.WriteAllBytes(Path.Combine(dir, name), bytes);
            // keep the newest 30 within 10 MB
            FileInfo[] all = new DirectoryInfo(dir).GetFiles().OrderByDescending(f => f.Name, StringComparer.Ordinal).ToArray();
            long size = 0;
            for (int i = 0; i < all.Length; i++)
            {
                size += all[i].Length;
                if (i >= BackupsKept || (size > BackupBytes && i > 0))
                {
                    try { all[i].Delete(); } catch { }
                }
            }
            return BackupRoot + "/" + name;
        }

        // ------------------------------------------------------------------ GameObjects

        private static GameObject Find(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("the object's hierarchy path is needed, like 'Avatar/Armature/Hips'");
            GameObject go = LinkAsk.FindByPath(path);
            if (go == null) throw new ArgumentException("no object at \"" + path + "\" in the open scenes");
            return go;
        }

        private static bool Vector(string s, out Vector3 v)
        {
            v = Vector3.zero;
            float[] f;
            if (!Floats(s, out f) || f.Length != 3) return false;
            v = new Vector3(f[0], f[1], f[2]);
            return true;
        }

        private static bool Floats(string s, out float[] f)
        {
            f = null;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Trim().Trim('(', ')', '[', ']').Split(new[] { ',', ';' }, StringSplitOptions.None);
            if (parts.Length == 1) parts = s.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<float>();
            foreach (string p in parts)
            {
                float x;
                if (!float.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                list.Add(x);
            }
            f = list.ToArray();
            return true;
        }

        private static Vector3 NeedVector(string s, string what)
        {
            Vector3 v;
            if (!Vector(s, out v)) throw new ArgumentException(what + " is 'x,y,z' (three numbers), not \"" + s + "\"");
            return v;
        }

        private static string CreateObject(Args a)
        {
            string name = string.IsNullOrEmpty(a.name) ? (string.IsNullOrEmpty(a.primitive) ? "GameObject" : Cap(a.primitive)) : a.name;
            GameObject go;
            if (!string.IsNullOrEmpty(a.primitive))
            {
                PrimitiveType type;
                if (!Enum.TryParse(a.primitive.Trim(), true, out type)) throw new ArgumentException("primitive is cube, sphere, capsule, cylinder, plane or quad, not " + a.primitive);
                go = GameObject.CreatePrimitive(type);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }
            try
            {
                if (!string.IsNullOrEmpty(a.parent) && a.parent != "/") go.transform.SetParent(Find(a.parent).transform, false);
                if (!string.IsNullOrEmpty(a.position)) go.transform.localPosition = NeedVector(a.position, "position");
            }
            catch
            {
                Object.DestroyImmediate(go);
                throw;
            }
            Undo.RegisterCreatedObjectUndo(go, "Capri: create " + go.name);
            var added = new List<string>();
            foreach (string c in (a.components ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0))
            {
                Component made = Undo.AddComponent(go, ComponentType(c));
                if (made != null) added.Add(made.GetType().Name);
            }
            Selection.activeGameObject = go;
            return "Created " + LinkAsk.PathOf(go) + " in scene " + go.scene.name
                   + Describe(go) + (added.Count > 0 ? " (added: " + string.Join(", ", added.ToArray()) + ")" : "") + ". It is selected in Unity.";
        }

        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1).ToLowerInvariant();

        private static string Describe(GameObject go)
        {
            var types = go.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name).ToArray();
            return types.Length > 0 ? " [" + string.Join(", ", types) + "]" : "";
        }

        private static string ModifyObject(Args a, bool setActive)
        {
            GameObject go = Find(a.path);
            if (a.delete)
            {
                string was = LinkAsk.PathOf(go);
                int children = go.GetComponentsInChildren<Transform>(true).Length - 1;
                Undo.DestroyObjectImmediate(go);
                return "Deleted " + was + (children > 0 ? " and its " + children + " child object(s)" : "") + " (Ctrl+Z in Unity brings it back).";
            }
            var done = new List<string>();
            if (a.duplicate)
            {
                GameObject copy = Object.Instantiate(go, go.transform.parent);
                copy.name = string.IsNullOrEmpty(a.name) ? go.name : a.name;
                if (go.transform.parent == null) SceneManager.MoveGameObjectToScene(copy, go.scene);
                copy.transform.SetSiblingIndex(go.transform.GetSiblingIndex() + 1);
                Undo.RegisterCreatedObjectUndo(copy, "Capri: duplicate " + go.name);
                Selection.activeGameObject = copy;
                return "Duplicated " + LinkAsk.PathOf(go) + " as " + LinkAsk.PathOf(copy) + Describe(copy)
                       + (PrefabUtility.IsPartOfPrefabInstance(go) ? " (the copy is not linked to the prefab)" : "") + ". It is selected in Unity.";
            }
            if (!string.IsNullOrEmpty(a.name) && a.name != go.name)
            {
                Undo.RecordObject(go, "Capri: rename");
                go.name = a.name;
                done.Add("renamed");
            }
            if (setActive && go.activeSelf != a.active)
            {
                Undo.RecordObject(go, "Capri: active");
                go.SetActive(a.active);
                done.Add(a.active ? "activated" : "deactivated");
            }
            if (!string.IsNullOrEmpty(a.tag))
            {
                Undo.RecordObject(go, "Capri: tag");
                try { go.tag = a.tag; }
                catch (UnityException) { throw new ArgumentException("there is no tag \"" + a.tag + "\" in this project (Project Settings > Tags and Layers)"); }
                done.Add("tag " + a.tag);
            }
            if (!string.IsNullOrEmpty(a.layer))
            {
                int layer;
                if (!int.TryParse(a.layer, out layer)) layer = LayerMask.NameToLayer(a.layer);
                if (layer < 0 || layer > 31) throw new ArgumentException("there is no layer \"" + a.layer + "\"");
                Undo.RecordObject(go, "Capri: layer");
                go.layer = layer;
                done.Add("layer " + LayerMask.LayerToName(layer));
            }
            if (!string.IsNullOrEmpty(a.parent))
            {
                Transform parent = a.parent == "/" ? null : Find(a.parent).transform;
                for (Transform t = parent; t != null; t = t.parent)
                {
                    if (t == go.transform) throw new ArgumentException("an object cannot go under itself or its own children");
                }
                Undo.SetTransformParent(go.transform, parent, "Capri: parent");
                done.Add("moved under " + (parent == null ? "the scene root" : LinkAsk.PathOf(parent.gameObject)));
            }
            if (!string.IsNullOrEmpty(a.position) || !string.IsNullOrEmpty(a.rotation) || !string.IsNullOrEmpty(a.scale))
            {
                Vector3 p = string.IsNullOrEmpty(a.position) ? go.transform.localPosition : NeedVector(a.position, "position");
                Vector3 r = string.IsNullOrEmpty(a.rotation) ? go.transform.localEulerAngles : NeedVector(a.rotation, "rotation");
                Vector3 s = string.IsNullOrEmpty(a.scale) ? go.transform.localScale : NeedVector(a.scale, "scale");
                Undo.RecordObject(go.transform, "Capri: transform");
                go.transform.localPosition = p;
                if (!string.IsNullOrEmpty(a.rotation)) go.transform.localEulerAngles = r;
                go.transform.localScale = s;
                done.Add("local position (" + V(go.transform.localPosition) + "), rotation (" + V(go.transform.localEulerAngles) + "), scale (" + V(go.transform.localScale) + ")");
            }
            if (done.Count == 0) return "Nothing to change on " + LinkAsk.PathOf(go) + ": give name, active, parent, position, rotation, scale, tag, layer, duplicate or delete.";
            return LinkAsk.PathOf(go) + ": " + string.Join("; ", done.ToArray()) + ".";
        }

        private static string V(Vector3 v) => LinkAsk.F(v.x) + ", " + LinkAsk.F(v.y) + ", " + LinkAsk.F(v.z);

        // ------------------------------------------------------------------ components

        private static Type ComponentType(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a component type is needed, like 'BoxCollider'");
            string n = name.Trim();
            var all = TypeCache.GetTypesDerivedFrom<Component>().Where(t => !t.IsAbstract && !t.IsGenericType).ToList();
            List<Type> hits = all.Where(t => string.Equals(t.FullName, n, StringComparison.Ordinal)).ToList();
            if (hits.Count == 0) hits = all.Where(t => string.Equals(t.Name, n, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hits.Count > 1)
            {
                List<Type> unity = hits.Where(t => t.Namespace == "UnityEngine").ToList();
                if (unity.Count == 1) return unity[0];
                throw new ArgumentException("\"" + n + "\" is more than one type: " + string.Join(", ", hits.Take(6).Select(t => t.FullName).ToArray()) + " -- give the full name");
            }
            if (hits.Count == 0)
            {
                string[] near = all.Where(t => t.Name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0).Take(8).Select(t => t.FullName).ToArray();
                throw new ArgumentException("no component type \"" + n + "\" in this project" + (near.Length > 0 ? " (close: " + string.Join(", ", near) + ")" : ""));
            }
            return hits[0];
        }

        private static string Component(Args a)
        {
            GameObject go = Find(a.path);
            string action = (a.action ?? "").Trim().ToLowerInvariant();
            Type type = ComponentType(a.type);
            if (action == "add")
            {
                Component made = Undo.AddComponent(go, type);
                if (made == null) throw new InvalidOperationException("Unity refused to add " + type.Name + " to " + LinkAsk.PathOf(go) + " (only one may be there, or it needs another component)");
                return "Added " + made.GetType().Name + " to " + LinkAsk.PathOf(go) + Describe(go) + ". unity_inspect shows its fields.";
            }
            if (action == "remove")
            {
                if (type == typeof(Transform) || type == typeof(RectTransform)) throw new ArgumentException("the Transform is the object itself: delete the object instead");
                Component[] found = go.GetComponents(type);
                if (found.Length == 0) throw new ArgumentException(LinkAsk.PathOf(go) + " has no " + type.Name);
                int i = Math.Max(0, a.index);
                if (i >= found.Length) throw new ArgumentException(LinkAsk.PathOf(go) + " has " + found.Length + " " + type.Name + "(s): index goes from 0 to " + (found.Length - 1));
                Undo.DestroyObjectImmediate(found[i]);
                if (found[i] != null) throw new InvalidOperationException("Unity kept " + type.Name + ": another component needs it");
                return "Removed " + type.Name + (found.Length > 1 ? " #" + i : "") + " from " + LinkAsk.PathOf(go) + Describe(go) + ".";
            }
            throw new ArgumentException("action is add or remove");
        }

        // ------------------------------------------------------------------ fields

        private static string SetField(string path, string component, string field, string value)
        {
            if (string.IsNullOrEmpty(field)) throw new ArgumentException("field is needed: the name the Inspector shows, or its property path");
            if (value == null) value = "";
            Object target;
            if (IsAssetPath(path))
            {
                Object main = AssetDatabase.LoadMainAssetAtPath(path);
                if (main == null) throw new ArgumentException("no asset at \"" + path + "\"");
                target = main;
                if (!string.IsNullOrEmpty(component))
                {
                    GameObject prefab = main as GameObject;
                    if (prefab == null) throw new ArgumentException(path + " is a " + main.GetType().Name + ", it has no components");
                    target = Pick(prefab, component);
                }
            }
            else
            {
                GameObject go = Find(path);
                target = string.IsNullOrEmpty(component) ? (Object)go : Pick(go, component);
            }

            // a material's shader property, by its name
            Material mat = target as Material;
            if (mat != null && field.StartsWith("_", StringComparison.Ordinal))
            {
                return SetMaterial(mat, field, value);
            }

            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field) ?? ByName(so, field);
            if (p == null)
            {
                var names = new List<string>();
                SerializedProperty it = so.GetIterator();
                bool enter = true;
                while (it.NextVisible(enter) && names.Count < 40)
                {
                    enter = false;
                    names.Add(it.displayName + " (" + it.propertyPath + ")");
                }
                throw new ArgumentException("no field \"" + field + "\" on " + target.GetType().Name + ". Its fields: " + string.Join(", ", names.ToArray()));
            }
            Assign(p, value);
            so.ApplyModifiedProperties();
            if (EditorUtility.IsPersistent(target)) EditorUtility.SetDirty(target);
            so.Update();
            SerializedProperty back = so.FindProperty(p.propertyPath);
            return target.GetType().Name + (target is Component ? " of " + LinkAsk.PathOf(((Component)target).gameObject) : IsAssetPath(path) ? " " + path : "")
                   + ": " + back.displayName + " = " + LinkAsk.Value(back, 1) + (EditorUtility.IsPersistent(target) ? " (an asset: saved with unity_editor save)" : "");
        }

        private static Component Pick(GameObject go, string component)
        {
            string n = component.Trim();
            Component c = go.GetComponents<Component>().FirstOrDefault(x => x != null && (string.Equals(x.GetType().Name, n, StringComparison.OrdinalIgnoreCase) || x.GetType().FullName == n));
            if (c == null) throw new ArgumentException(go.name + " has no " + n + Describe(go));
            return c;
        }

        private static string Squash(string s) => new string((s ?? "").Where(ch => !char.IsWhiteSpace(ch) && ch != '_').ToArray()).ToLowerInvariant();

        // by the Inspector's name ("Use Gravity"), or its field name ("m_UseGravity", "useGravity")
        private static SerializedProperty ByName(SerializedObject so, string field)
        {
            string want = Squash(field);
            SerializedProperty it = so.GetIterator();
            bool enter = true;
            while (it.Next(enter))
            {
                enter = true;
                string pathSquash = Squash(it.propertyPath);
                if (Squash(it.displayName) == want || Squash(it.name) == want || Squash(it.name) == "m" + want || pathSquash == want || pathSquash == "m" + want)
                {
                    return it.Copy();
                }
            }
            return null;
        }

        private static void Assign(SerializedProperty p, string value)
        {
            string v = value.Trim();
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.ArraySize:
                {
                    long x;
                    if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out x)) throw new ArgumentException(p.displayName + " is a whole number, not \"" + value + "\"");
                    if (p.propertyType == SerializedPropertyType.ArraySize) p.intValue = (int)x;
                    else p.longValue = x;
                    return;
                }
                case SerializedPropertyType.Boolean:
                {
                    string b = v.ToLowerInvariant();
                    if (b == "true" || b == "1" || b == "on" || b == "yes") p.boolValue = true;
                    else if (b == "false" || b == "0" || b == "off" || b == "no") p.boolValue = false;
                    else throw new ArgumentException(p.displayName + " is true or false, not \"" + value + "\"");
                    return;
                }
                case SerializedPropertyType.Float:
                {
                    float x;
                    if (!float.TryParse(v.IndexOf('.') < 0 ? v.Replace(',', '.') : v, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) throw new ArgumentException(p.displayName + " is a number, not \"" + value + "\"");
                    p.floatValue = x;
                    return;
                }
                case SerializedPropertyType.String:
                    p.stringValue = value;
                    return;
                case SerializedPropertyType.Character:
                    if (value.Length != 1) throw new ArgumentException(p.displayName + " is one character");
                    p.intValue = value[0];
                    return;
                case SerializedPropertyType.Color:
                    p.colorValue = NeedColor(v, p.displayName);
                    return;
                case SerializedPropertyType.Enum:
                {
                    for (int i = 0; i < p.enumNames.Length; i++)
                    {
                        if (Squash(p.enumNames[i]) == Squash(v) || Squash(p.enumDisplayNames[i]) == Squash(v))
                        {
                            p.enumValueIndex = i;
                            return;
                        }
                    }
                    int n;
                    if (int.TryParse(v, out n) && n >= 0 && n < p.enumNames.Length)
                    {
                        p.enumValueIndex = n;
                        return;
                    }
                    throw new ArgumentException(p.displayName + " is one of: " + string.Join(", ", p.enumDisplayNames));
                }
                case SerializedPropertyType.LayerMask:
                {
                    int mask;
                    if (!int.TryParse(v, out mask))
                    {
                        mask = 0;
                        foreach (string part in v.Split('|', ','))
                        {
                            string name = part.Trim();
                            if (name.Length == 0 || string.Equals(name, "Nothing", StringComparison.OrdinalIgnoreCase)) continue;
                            if (string.Equals(name, "Everything", StringComparison.OrdinalIgnoreCase)) { mask = ~0; continue; }
                            int layer = LayerMask.NameToLayer(name);
                            if (layer < 0) throw new ArgumentException("there is no layer \"" + name + "\"");
                            mask |= 1 << layer;
                        }
                    }
                    p.intValue = mask;
                    return;
                }
                case SerializedPropertyType.Vector2:
                {
                    float[] f = NeedFloats(v, 2, p.displayName);
                    p.vector2Value = new Vector2(f[0], f[1]);
                    return;
                }
                case SerializedPropertyType.Vector3:
                {
                    float[] f = NeedFloats(v, 3, p.displayName);
                    p.vector3Value = new Vector3(f[0], f[1], f[2]);
                    return;
                }
                case SerializedPropertyType.Vector4:
                {
                    float[] f = NeedFloats(v, 4, p.displayName);
                    p.vector4Value = new Vector4(f[0], f[1], f[2], f[3]);
                    return;
                }
                case SerializedPropertyType.Vector2Int:
                {
                    float[] f = NeedFloats(v, 2, p.displayName);
                    p.vector2IntValue = new Vector2Int((int)f[0], (int)f[1]);
                    return;
                }
                case SerializedPropertyType.Vector3Int:
                {
                    float[] f = NeedFloats(v, 3, p.displayName);
                    p.vector3IntValue = new Vector3Int((int)f[0], (int)f[1], (int)f[2]);
                    return;
                }
                case SerializedPropertyType.Quaternion:
                {
                    float[] f = NeedFloats(v, 3, p.displayName + " (euler degrees)");
                    p.quaternionValue = Quaternion.Euler(f[0], f[1], f[2]);
                    return;
                }
                case SerializedPropertyType.Rect:
                {
                    float[] f = NeedFloats(v, 4, p.displayName + " (x, y, width, height)");
                    p.rectValue = new Rect(f[0], f[1], f[2], f[3]);
                    return;
                }
                case SerializedPropertyType.ObjectReference:
                    AssignReference(p, v);
                    return;
            }
            throw new ArgumentException(p.displayName + " is a " + p.propertyType + ": this tool does not set that kind of field (set its parts, or edit the file)");
        }

        private static float[] NeedFloats(string v, int n, string what)
        {
            float[] f;
            if (!Floats(v, out f) || f.Length != n) throw new ArgumentException(what + " takes " + n + " numbers separated by commas, not \"" + v + "\"");
            return f;
        }

        private static Color NeedColor(string v, string what)
        {
            Color c;
            if (v.StartsWith("#", StringComparison.Ordinal) && ColorUtility.TryParseHtmlString(v, out c)) return c;
            float[] f;
            if (Floats(v, out f) && (f.Length == 3 || f.Length == 4))
            {
                bool bytes = f.Any(x => x > 1f);
                float k = bytes ? 1f / 255f : 1f;
                return new Color(f[0] * k, f[1] * k, f[2] * k, f.Length == 4 ? f[3] * (bytes && f[3] > 1f ? 1f / 255f : 1f) : 1f);
            }
            throw new ArgumentException(what + " is a colour: '#FF8800', '#FF880080' or 'r,g,b,a' (0 to 1, or 0 to 255)");
        }

        // A reference: None, an asset path (optionally 'path:SubAssetName'), or a hierarchy path (the object, or the first of
        // its components the field takes).
        private static void AssignReference(SerializedProperty p, string v)
        {
            if (v.Length == 0 || string.Equals(v, "none", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "null", StringComparison.OrdinalIgnoreCase))
            {
                p.objectReferenceValue = null;
                return;
            }
            var candidates = new List<Object>();
            if (IsAssetPath(v))
            {
                string assetPath = v, sub = null;
                int colon = v.IndexOf(':');
                if (colon > 0)
                {
                    assetPath = v.Substring(0, colon);
                    sub = v.Substring(colon + 1);
                }
                Object[] all = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                if (all.Length == 0) throw new ArgumentException("no asset at \"" + assetPath + "\"");
                Object main = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if (sub != null) candidates.AddRange(all.Where(o => o != null && o.name == sub));
                else
                {
                    candidates.Add(main);
                    candidates.AddRange(all.Where(o => o != null && o != main));
                }
            }
            else
            {
                GameObject go = Find(v);
                candidates.Add(go);
                candidates.AddRange(go.GetComponents<Component>().Where(c => c != null));
            }
            foreach (Object o in candidates)
            {
                p.objectReferenceValue = o;
                if (p.objectReferenceValue == o) return;
            }
            throw new ArgumentException(p.displayName + " does not take \"" + v + "\" (wrong kind of object; the field shows " + LinkAsk.Ref(p.objectReferenceValue) + ")");
        }

        private static string SetMaterial(Material mat, string prop, string value)
        {
            if (!mat.HasProperty(prop))
            {
                Shader sh = mat.shader;
                var names = new List<string>();
                for (int i = 0; i < sh.GetPropertyCount() && names.Count < 60; i++) names.Add(sh.GetPropertyName(i));
                throw new ArgumentException(mat.name + " (" + sh.name + ") has no " + prop + ". Its properties: " + string.Join(", ", names.ToArray()));
            }
            Shader shader = mat.shader;
            ShaderPropertyType type = shader.GetPropertyType(shader.FindPropertyIndex(prop));
            Undo.RecordObject(mat, "Capri: " + prop);
            string v = value.Trim();
            string shown;
            switch (type)
            {
                case ShaderPropertyType.Color:
                    mat.SetColor(prop, NeedColor(v, prop));
                    Color c = mat.GetColor(prop);
                    shown = "RGBA(" + LinkAsk.F(c.r) + ", " + LinkAsk.F(c.g) + ", " + LinkAsk.F(c.b) + ", " + LinkAsk.F(c.a) + ")";
                    break;
                case ShaderPropertyType.Vector:
                {
                    float[] f = NeedFloats(v, 4, prop);
                    mat.SetVector(prop, new Vector4(f[0], f[1], f[2], f[3]));
                    shown = mat.GetVector(prop).ToString();
                    break;
                }
                case ShaderPropertyType.Texture:
                {
                    Texture t = null;
                    if (v.Length > 0 && !string.Equals(v, "none", StringComparison.OrdinalIgnoreCase))
                    {
                        t = AssetDatabase.LoadAssetAtPath<Texture>(v);
                        if (t == null) throw new ArgumentException("no texture at \"" + v + "\"");
                    }
                    mat.SetTexture(prop, t);
                    shown = t == null ? "None" : AssetDatabase.GetAssetPath(t);
                    break;
                }
                default:
                {
                    float x;
                    if (!float.TryParse(v.IndexOf('.') < 0 ? v.Replace(',', '.') : v, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) throw new ArgumentException(prop + " is a number, not \"" + value + "\"");
                    mat.SetFloat(prop, x);
                    shown = LinkAsk.F(mat.GetFloat(prop));
                    break;
                }
            }
            EditorUtility.SetDirty(mat);
            return "Material " + AssetDatabase.GetAssetPath(mat) + ": " + prop + " = " + shown + " (saved with unity_editor save)";
        }

        // ------------------------------------------------------------------ assets

        private static string AssetAction(Args a)
        {
            string action = (a.action ?? "").Trim().ToLowerInvariant();
            if (!IsAssetPath(a.path)) throw new ArgumentException("path is an asset path starting with Assets/, like 'Assets/Avatar/Body.mat'");
            string path = a.path.Replace('\\', '/').TrimEnd('/');
            switch (action)
            {
                case "create_folder":
                    Folder(path);
                    return "Folder " + path + " is there.";
                case "create_material":
                {
                    if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)) path += ".mat";
                    if (AssetDatabase.LoadMainAssetAtPath(path) != null) throw new ArgumentException(path + " already exists");
                    string wanted = string.IsNullOrEmpty(a.shader) ? DefaultShader() : a.shader.Trim();
                    Shader sh = Shader.Find(wanted);
                    if (sh == null) throw new ArgumentException("no shader \"" + wanted + "\" in this project");
                    Folder(Path.GetDirectoryName(path).Replace('\\', '/'));
                    AssetDatabase.CreateAsset(new Material(sh), path);
                    return "Created material " + path + " (" + sh.name + "). unity_set_field sets its properties ('_Color', '_MainTex'...).";
                }
                case "move":
                case "rename":
                {
                    string to = Target(a.to, path);
                    Folder(Path.GetDirectoryName(to).Replace('\\', '/'));
                    string error = AssetDatabase.MoveAsset(path, to);
                    if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
                    return "Moved " + path + " to " + to + " (every reference to it is kept).";
                }
                case "copy":
                {
                    string to = Target(a.to, path);
                    Folder(Path.GetDirectoryName(to).Replace('\\', '/'));
                    if (!AssetDatabase.CopyAsset(path, to)) throw new InvalidOperationException("Unity could not copy " + path + " to " + to);
                    return "Copied " + path + " to " + to + ".";
                }
                case "delete":
                    if (path == "Assets") throw new ArgumentException("not the whole Assets folder");
                    if (AssetDatabase.LoadMainAssetAtPath(path) == null && !AssetDatabase.IsValidFolder(path)) throw new ArgumentException("no asset at \"" + path + "\"");
                    if (!AssetDatabase.MoveAssetToTrash(path)) throw new InvalidOperationException("Unity could not delete " + path);
                    return "Deleted " + path + ": it is in the Windows Recycle Bin.";
            }
            throw new ArgumentException("action is create_folder, create_material, move, copy or delete");
        }

        private static string Target(string to, string from)
        {
            if (string.IsNullOrEmpty(to)) throw new ArgumentException("`to` is needed: the new asset path");
            to = to.Replace('\\', '/').TrimEnd('/');
            if (!IsAssetPath(to)) throw new ArgumentException("`to` starts with Assets/");
            if (AssetDatabase.IsValidFolder(to) && !AssetDatabase.IsValidFolder(from)) to += "/" + Path.GetFileName(from);
            if (AssetDatabase.LoadMainAssetAtPath(to) != null) throw new ArgumentException(to + " already exists");
            return to;
        }

        private static void Folder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            if (parts[0] != "Assets") throw new ArgumentException("folders are made under Assets/");
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static string DefaultShader()
        {
            RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
            if (rp != null && rp.defaultShader != null) return rp.defaultShader.name;
            return "Standard";
        }

        // ------------------------------------------------------------------ the editor

        private static string EditorAction(Args a)
        {
            string action = (a.action ?? "").Trim().ToLowerInvariant();
            switch (action)
            {
                case "save":
                {
                    if (EditorApplication.isPlaying) throw new InvalidOperationException("Unity is in Play mode: stop it first (scenes are not saved while playing)");
                    var saved = new List<string>();
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene s = SceneManager.GetSceneAt(i);
                        if (!s.isLoaded || !s.isDirty) continue;
                        if (string.IsNullOrEmpty(s.path))
                        {
                            if (string.IsNullOrEmpty(a.path) || s != SceneManager.GetActiveScene()) throw new ArgumentException("the scene \"" + s.name + "\" has no file yet: give `path`, like 'Assets/Scenes/MyScene.unity'");
                            string to = a.path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? a.path : a.path + ".unity";
                            Folder(Path.GetDirectoryName(to).Replace('\\', '/'));
                            if (!EditorSceneManager.SaveScene(s, to)) throw new InvalidOperationException("Unity could not save the scene to " + to);
                            saved.Add(to);
                            continue;
                        }
                        if (!EditorSceneManager.SaveScene(s)) throw new InvalidOperationException("Unity could not save " + s.path);
                        saved.Add(s.path);
                    }
                    AssetDatabase.SaveAssets();
                    return saved.Count == 0 ? "Saved the changed assets; no open scene had unsaved changes." : "Saved " + string.Join(", ", saved.ToArray()) + " and the changed assets.";
                }
                case "refresh":
                    After = () => AssetDatabase.Refresh();
                    return "Unity imports what changed on disk now (scripts recompile if needed; then read unity_console).";
                case "play":
                    if (EditorApplication.isPlaying) return "Unity is already in Play mode.";
                    After = () => EditorApplication.isPlaying = true;
                    return "Unity enters Play mode now. Changes made while playing are lost when Play stops.";
                case "stop":
                    if (!EditorApplication.isPlaying) return "Unity is not in Play mode.";
                    After = () => EditorApplication.isPlaying = false;
                    return "Unity leaves Play mode now.";
                case "pause":
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return EditorApplication.isPaused ? "Paused." : "Running again.";
                case "select":
                {
                    if (string.IsNullOrEmpty(a.path)) throw new ArgumentException("path is needed: a hierarchy path or an asset path");
                    Object o = IsAssetPath(a.path) ? AssetDatabase.LoadMainAssetAtPath(a.path) : Find(a.path);
                    if (o == null) throw new ArgumentException("no asset at \"" + a.path + "\"");
                    Selection.activeObject = o;
                    EditorGUIUtility.PingObject(o);
                    return "Selected " + a.path + " in Unity.";
                }
                case "undo":
                    Undo.PerformUndo();
                    return "Undone (the last step of Unity's Undo).";
                case "redo":
                    Undo.PerformRedo();
                    return "Redone.";
                case "menu":
                    if (string.IsNullOrEmpty(a.menu)) throw new ArgumentException("menu is needed: the menu item's path, like 'GameObject/Align With View'");
                    if (!EditorApplication.ExecuteMenuItem(a.menu)) throw new ArgumentException("no menu item \"" + a.menu + "\" (or it is greyed out right now)");
                    return "Ran the menu item " + a.menu + ". unity_console shows what it said.";
            }
            throw new ArgumentException("action is save, refresh, play, stop, pause, select, undo, redo or menu");
        }
    }
}
