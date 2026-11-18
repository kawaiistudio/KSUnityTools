using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// What the Kawaii Studio app may ask this project about KS ANTI-RIP.
    ///
    /// Found by name through KawaiiStudioLinkPlugins, so the Link package does not need to know KS Unity Tools
    /// exists. Five questions only:
    ///   antirip.list     -- the avatars in the open scenes, so the app's KS ANTI RIP section can show them
    ///   antirip.check    -- is everything in order? account, project, editor, avatar (KSAntiRipPreflight)
    ///   antirip.fix      -- repair one of the things the check found, or all of them
    ///   antirip.protect  -- protect one of them, which calls back into the app for the plan
    ///   antirip.select   -- select and highlight the protected prefab, ready for the VRChat SDK's upload
    ///
    /// Nothing here decides anything about the protection: the app does. This only looks, applies and reports. Since
    /// 3.9.1 it is also the ONLY way in: the Unity window has no controls left (owner, 2026-11-16).
    ///
    /// Since 3.10.0 the app does not ask for a protected COPY any more. It turns protection on or off for an avatar
    /// (antirip.mark / antirip.unmark), which adds or removes a KSAntiRipMark component; the real protection then
    /// happens at UPLOAD, in KSAntiRipBuildHook, on the avatar VRChat builds — non-destructive
    /// (owner, 2026-11-17: "pas recréer un prefab, au moment de l'upload"). antirip.protect stays for
    /// the old make-a-copy path, but the app no longer calls it.
    /// </summary>
    public static class KSLinkTools
    {
        public static bool Knows(string tool) =>
            tool == "antirip.list" || tool == "antirip.protect" ||
            tool == "antirip.check" || tool == "antirip.fix" || tool == "antirip.select" ||
            tool == "antirip.mark" || tool == "antirip.unmark";

        public static string Run(string tool, string argsJson)
        {
            switch (tool)
            {
                case "antirip.list": return List();
                case "antirip.check": return Check(argsJson);
                case "antirip.fix": return Fix(argsJson);
                case "antirip.protect": return Protect(argsJson);
                case "antirip.select": return Select(argsJson);
                case "antirip.mark": return Mark(argsJson);
                case "antirip.unmark": return Unmark(argsJson);
                default: throw new ArgumentException("unknown tool " + tool);
            }
        }

        [Serializable]
        class SelectArgs
        {
            public string asset = "";
        }

        /// <summary>
        /// Selects the protected prefab in the Project window and highlights it, so the upload in the VRChat SDK starts
        /// from the right object. Only protected output is accepted: this is a door the app opens, not a general one.
        /// </summary>
        static string Select(string argsJson)
        {
            SelectArgs a;
            try { a = JsonUtility.FromJson<SelectArgs>(argsJson) ?? new SelectArgs(); }
            catch { a = new SelectArgs(); }
            string protectedRoot = KawaiiStudioPaths.OutputFolder("Protected");
            if (string.IsNullOrEmpty(a.asset) || !a.asset.StartsWith(protectedRoot + "/", StringComparison.Ordinal))
                return Fail("only a protected avatar can be shown from here");
            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(a.asset);
            if (obj == null) return Fail("\"" + a.asset + "\" is not in this project any more");
            Selection.activeObject = obj;
            EditorUtility.FocusProjectWindow();
            EditorGUIUtility.PingObject(obj);
            return "{\"ok\":true,\"message\":" + Q("Selected " + a.asset) + "}";
        }

        [Serializable]
        class ProtectArgs
        {
            public string path = "";       // hierarchy path of the avatar root in its scene
            public string scene = "";      // which open scene it is in
            public float strength = 5f;
            public int restoreGroups = 3;
            public bool syncKey = true;    // others see the avatar whole; false keeps the key on this PC only
            public bool hideWhileLocked = true;
            public bool freezeWhileLocked = true;   // an app that does not send these two (before v660) gets the defaults
            public bool lockedBadge = true;
            public bool obfuscateNames = true;
            public bool force = false;     // protect even though the check found blocking problems
        }

        [Serializable]
        class MarkArgs
        {
            public string path = "";
            public string scene = "";
            public float strength = 5f;
            public int restoreGroups = 3;
            public bool syncKey = true;
            public bool hideWhileLocked = true;
            public bool freezeWhileLocked = true;   // an app that does not send these two (before v660) gets the defaults
            public bool lockedBadge = true;
            public bool obfuscateNames = true;
            public int maxAddedMegabytes = 12;
        }

        [Serializable]
        class CheckArgs
        {
            public string path = "";
            public string scene = "";
            public int bits = 24;          // what the key will cost in synced bits: 8 per shape, 0 when local
            public string fix = "";        // antirip.fix only: one repair id, or "all"
            public bool again = false;     // forget cached online answers and ask VRChat afresh
        }

        // ── antirip.list ────────────────────────────────────────────────────────────────────────

        static string List()
        {
            var sb = new StringBuilder();
            // canMark tells the app this package understands antirip.mark/unmark (the "Protect on upload" switch).
            // KS Unity Tools 3.9.1 and earlier do not send it, so the app can show "update KS Unity Tools" instead
            // of calling a question the project cannot answer -- this matters while v653 is out but 3.10.0 is not.
            sb.Append("{\"ok\":true,\"canMark\":true,\"project\":").Append(Q(Application.productName))
              .Append(",\"avatars\":[");

            bool first = true;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (var descriptor in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                    {
                        GameObject avatar = descriptor.gameObject;
                        var pipeline = avatar.GetComponent<PipelineManager>();
                        string blueprint = pipeline != null ? pipeline.blueprintId : "";

                        var skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                            .Where(r => r.sharedMesh != null).ToArray();
                        int vertices = 0;
                        var seen = new List<Mesh>();
                        foreach (var smr in skinned)
                            if (!seen.Contains(smr.sharedMesh)) { seen.Add(smr.sharedMesh); vertices += smr.sharedMesh.vertexCount; }

                        int statics = avatar.GetComponentsInChildren<MeshRenderer>(true)
                                            .Count(mr => mr.GetComponent<MeshFilter>() != null &&
                                                         mr.GetComponent<MeshFilter>().sharedMesh != null);

                        // whether protection is already turned on for this avatar, so the app's toggle is right
                        var mark = avatar.GetComponent<KSAntiRipMark>();
                        bool marked = mark != null && mark.enabled_;

                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append("{\"name\":").Append(Q(avatar.name))
                          .Append(",\"path\":").Append(Q(PathOf(avatar.transform)))
                          .Append(",\"scene\":").Append(Q(scene.name))
                          .Append(",\"blueprint\":").Append(Q(blueprint))
                          .Append(",\"meshes\":").Append(seen.Count)
                          .Append(",\"vertices\":").Append(vertices)
                          .Append(",\"staticMeshes\":").Append(statics)
                          .Append(",\"marked\":").Append(marked ? "true" : "false")
                          // the one thing that stops a protection before it starts, so the app can grey the row
                          .Append(",\"ready\":").Append(!string.IsNullOrEmpty(blueprint) && seen.Count > 0 ? "true" : "false")
                          .Append(",\"why\":").Append(Q(
                              string.IsNullOrEmpty(blueprint)
                                  ? "upload this avatar once normally first — the key is named after its avatar id"
                                  : seen.Count == 0 ? "no skinned mesh to protect" : ""))
                          .Append('}');
                    }
                }
            }

            sb.Append("]}");
            return sb.ToString();
        }

        // ── antirip.mark / antirip.unmark ───────────────────────────────────────────────────────
        //
        // Turn protection on or off for an avatar, the app's on/off switch. "On" adds (or updates) a KSAntiRipMark
        // carrying the settings; the avatar is untouched until it is uploaded, when KSAntiRipBuildHook reads the
        // mark and protects the build copy. Both are Undo-able and mark the scene dirty, so the choice is saved
        // with the scene and the user can Ctrl+Z it like anything else.

        static string Mark(string argsJson)
        {
            MarkArgs a;
            try { a = JsonUtility.FromJson<MarkArgs>(argsJson) ?? new MarkArgs(); }
            catch (Exception e) { return Fail("could not read the request: " + e.Message); }
            if (string.IsNullOrEmpty(a.path)) return Fail("no avatar was named");

            GameObject avatar = FindAvatar(a.path, a.scene);
            if (avatar == null)
                return Fail("\"" + a.path + "\" is not in the open scenes any more — reopen it and try again");
            if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
                return Fail("\"" + a.path + "\" has no VRC Avatar Descriptor");

            KSAntiRipMark mark = avatar.GetComponent<KSAntiRipMark>();
            if (mark == null) mark = Undo.AddComponent<KSAntiRipMark>(avatar);
            Undo.RecordObject(mark, "KS Anti-Rip: protect on upload");
            mark.enabled_ = true;
            mark.strength = a.strength <= 0f ? 5f : a.strength;
            mark.restoreGroups = a.restoreGroups <= 0 ? 3 : a.restoreGroups;
            mark.syncKey = a.syncKey;
            mark.hideWhileLocked = a.hideWhileLocked;
            mark.freezeWhileLocked = a.freezeWhileLocked;
            mark.lockedBadge = a.lockedBadge;
            mark.obfuscateNames = a.obfuscateNames;
            mark.maxAddedMegabytes = a.maxAddedMegabytes < 0 ? 0 : a.maxAddedMegabytes;
            EditorUtility.SetDirty(avatar);
            EditorSceneManager.MarkSceneDirty(avatar.scene);
            return "{\"ok\":true,\"marked\":true,\"message\":" + Q("This avatar will be protected when you upload it.") + "}";
        }

        static string Unmark(string argsJson)
        {
            MarkArgs a;
            try { a = JsonUtility.FromJson<MarkArgs>(argsJson) ?? new MarkArgs(); }
            catch (Exception e) { return Fail("could not read the request: " + e.Message); }
            if (string.IsNullOrEmpty(a.path)) return Fail("no avatar was named");

            GameObject avatar = FindAvatar(a.path, a.scene);
            if (avatar == null)
                return Fail("\"" + a.path + "\" is not in the open scenes any more");

            KSAntiRipMark mark = avatar.GetComponent<KSAntiRipMark>();
            if (mark != null)
            {
                Undo.DestroyObjectImmediate(mark);
                EditorUtility.SetDirty(avatar);
                EditorSceneManager.MarkSceneDirty(avatar.scene);
            }
            return "{\"ok\":true,\"marked\":false,\"message\":" + Q("Protection off for this avatar.") + "}";
        }

        // ── antirip.check / antirip.fix ─────────────────────────────────────────────────────────

        static string Check(string argsJson)
        {
            CheckArgs args = ReadCheckArgs(argsJson);
            if (args.again) KSAntiRipPreflight.Forget();
            return KSAntiRipPreflight.Report(args.path, args.scene, args.bits);
        }

        static string Fix(string argsJson)
        {
            CheckArgs args = ReadCheckArgs(argsJson);
            return KSAntiRipPreflight.Repair(args.fix, args.path, args.scene, args.bits);
        }

        static CheckArgs ReadCheckArgs(string argsJson)
        {
            CheckArgs args;
            try { args = JsonUtility.FromJson<CheckArgs>(argsJson) ?? new CheckArgs(); }
            catch { args = new CheckArgs(); }
            if (args.bits < 0) args.bits = 0;   // 0 is real: a local key costs no synced bits
            return args;
        }

        // ── antirip.protect ─────────────────────────────────────────────────────────────────────

        static string Protect(string argsJson)
        {
            ProtectArgs args;
            try { args = JsonUtility.FromJson<ProtectArgs>(argsJson) ?? new ProtectArgs(); }
            catch (Exception e) { return Fail("could not read the request: " + e.Message); }

            if (string.IsNullOrEmpty(args.path)) return Fail("no avatar was named");

            GameObject avatar = FindAvatar(args.path, args.scene);
            if (avatar == null)
                return Fail("\"" + args.path + "\" is not in the open scenes any more — reopen it and try again");
            if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
                return Fail("\"" + args.path + "\" has no VRC Avatar Descriptor");

            var settings = new KSAntiRipProcessor.Settings
            {
                strength = args.strength <= 0f ? 5f : args.strength,
                restoreGroups = args.restoreGroups <= 0 ? 3 : args.restoreGroups,
                syncKey = args.syncKey,
                hideWhileLocked = args.hideWhileLocked,
                freezeWhileLocked = args.freezeWhileLocked,
                lockedBadge = args.lockedBadge,
                obfuscateNames = args.obfuscateNames,
            };

            // The gate, not a courtesy: a protection that ends in an upload VRChat refuses has still rewritten
            // meshes and burned a key. The app greys its button on the same report, but the app is not the only
            // thing that can call this, so the refusal lives here as well.
            if (!args.force)
            {
                string report = KSAntiRipPreflight.Report(args.path, args.scene, settings.KeyBits);
                if (report.IndexOf("\"ok\":false", StringComparison.Ordinal) >= 0)
                    return "{\"ok\":false,\"message\":" + Q("Not ready yet — see the checks.") +
                           ",\"blocked\":true,\"report\":" + report + "}";
            }

            KSAntiRipProcessor.Result result = KSAntiRipProcessor.Protect(avatar, settings);

            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(result.ok ? "true" : "false")
              .Append(",\"message\":").Append(Q(result.message))
              .Append(",\"needsPremium\":").Append(result.needsPremium ? "true" : "false")
              .Append(",\"prefab\":").Append(Q(result.prefabPath))
              .Append(",\"folder\":").Append(Q(result.folder))
              .Append(",\"keyPath\":").Append(Q(result.keyPath))
              .Append(",\"meshes\":").Append(result.meshesProtected)
              .Append(",\"vertices\":").Append(result.verticesProtected)
              .Append(",\"renamed\":").Append(result.objectsRenamed)
              .Append(",\"warnings\":[");
            for (int i = 0; i < result.warnings.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Q(result.warnings[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ── plumbing ────────────────────────────────────────────────────────────────────────────

        /// <summary>The avatar the app named, by hierarchy path, in the open scenes. Public: the preflight's
        /// repairs act on the same object, and resolving it twice two different ways is how they drift apart.</summary>
        public static GameObject FindAvatar(string path, string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                if (!string.IsNullOrEmpty(sceneName) && scene.name != sceneName) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    if (PathOf(root.transform) == path) return root;
                    foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                        if (PathOf(t) == path) return t.gameObject;
                }
            }
            return null;
        }

        static string PathOf(Transform t)
        {
            var parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        static string Fail(string message) => "{\"ok\":false,\"message\":" + Q(message) + "}";

        static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
