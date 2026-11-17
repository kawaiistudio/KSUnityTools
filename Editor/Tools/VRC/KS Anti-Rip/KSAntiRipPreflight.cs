using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Everything that has to be true before a protection is worth starting, and the repairs for the parts we
    /// can put right ourselves.
    ///
    /// The point is to fail in the Kawaii Studio window, in one list, instead of twenty minutes later in the
    /// VRChat SDK — or worse, after an upload that nobody can wear. The checks are deliberately the SAME ones
    /// the SDK's own avatar builder makes (VRCSdkControlPanelAvatarBuilder: parameter cost, menu/parameters
    /// pairing) plus the ones only KS ANTI-RIP cares about (a blueprint id to name the key after, enough free
    /// synced bits for the lock, no second protection on top of a first).
    ///
    /// Every repair is recorded with Undo, so a single Ctrl+Z in Unity puts it back. Nothing here deletes,
    /// reimports or rewrites an asset: the heaviest thing it does is add a component or create an empty menu.
    /// </summary>
    public static class KSAntiRipPreflight
    {
        /// <summary>VRChat's synced-parameter budget, read from the SDK rather than hard-coded.</summary>
        static int Budget => VRCExpressionParameters.MAX_PARAMETER_COST;

        public const string LevelOk = "ok";
        public const string LevelWarn = "warn";
        public const string LevelError = "error";
        public const string LevelBusy = "busy";   // an online check is still running; ask again in a second

        sealed class Check
        {
            public string id = "", title = "", detail = "", level = LevelOk, fix = "", fixLabel = "";
        }

        // ── the report ──────────────────────────────────────────────────────────────────────────

        /// <param name="bits">How wide a lock the app wants, so the budget check can answer the real question.</param>
        public static string Report(string path, string scene, int bits)
        {
            var checks = new List<Check>();
            GameObject avatar = Environment(checks, path, scene, out string sdk, out string platform);
            VRCAvatarDescriptor descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;

            string blueprint = "";
            int meshes = 0, vertices = 0, freeBits = Budget;
            string owner = "unknown";

            if (avatar != null && descriptor != null)
            {
                blueprint = Blueprint(checks, avatar);
                Meshes(checks, avatar, out meshes, out vertices);
                Rig(checks, avatar);
                Parameters(checks, descriptor, Math.Max(0, bits), out freeBits);
                Already(checks, avatar);
                owner = Ownership(checks, blueprint);
            }

            int errors = checks.Count(c => c.level == LevelError);
            int warnings = checks.Count(c => c.level == LevelWarn);
            bool busy = checks.Any(c => c.level == LevelBusy);

            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(errors == 0 ? "true" : "false")
              .Append(",\"busy\":").Append(busy ? "true" : "false")
              .Append(",\"errors\":").Append(errors)
              .Append(",\"warnings\":").Append(warnings)
              .Append(",\"project\":").Append(Q(Application.productName))
              .Append(",\"unity\":").Append(Q(Application.unityVersion))
              .Append(",\"sdk\":").Append(Q(sdk))
              .Append(",\"platform\":").Append(Q(platform))
              .Append(",\"account\":{\"connected\":").Append(APIUser.IsLoggedIn ? "true" : "false")
              .Append(",\"user\":").Append(Q(APIUser.IsLoggedIn && APIUser.CurrentUser != null ? APIUser.CurrentUser.displayName : ""))
              .Append(",\"canUpload\":").Append(CanUpload() ? "true" : "false")
              .Append(",\"canReconnect\":").Append(CanReconnect() ? "true" : "false")
              .Append("},\"avatar\":{\"name\":").Append(Q(avatar != null ? avatar.name : ""))
              .Append(",\"path\":").Append(Q(path))
              .Append(",\"blueprint\":").Append(Q(blueprint))
              .Append(",\"meshes\":").Append(meshes)
              .Append(",\"vertices\":").Append(vertices)
              .Append(",\"freeBits\":").Append(freeBits)
              .Append(",\"owner\":").Append(Q(owner))
              .Append("},\"checks\":[");
            for (int i = 0; i < checks.Count; i++)
            {
                Check c = checks[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Q(c.id))
                  .Append(",\"title\":").Append(Q(c.title))
                  .Append(",\"detail\":").Append(Q(c.detail))
                  .Append(",\"level\":").Append(Q(c.level))
                  .Append(",\"fix\":").Append(Q(c.fix))
                  .Append(",\"fixLabel\":").Append(Q(c.fixLabel))
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        // ── the repairs ─────────────────────────────────────────────────────────────────────────

        /// <summary>Applies one repair by id, or every repairable item when id is "all", then re-reports.</summary>
        public static string Repair(string id, string path, string scene, int bits)
        {
            var done = new List<string>();
            var failed = new List<string>();
            // Every id is tried once, so the loop ends on its own; the bound is only a backstop. It was 6,
            // and the bridge test's broken avatar had exactly six repairs -- a seventh would have been skipped
            // without a word. `tried` is kept apart from `done` because a repair that FAILS must not be
            // retried either, and must not be reported as repaired (the test caught scene.save in both lists).
            var tried = new List<string>();

            if (id == "all")
            {
                // Re-read the report each round: exiting play mode or adding a PipelineManager changes which
                // other items are still broken, and a stale list would "fix" things that no longer apply.
                for (int round = 0; round < 32; round++)
                {
                    string next = NextRepairable(path, scene, bits, tried);
                    if (next == null) break;
                    tried.Add(next);
                    if (Apply(next, path, scene, out string why)) done.Add(next);
                    else failed.Add(next + ": " + why);
                }
            }
            else if (!string.IsNullOrEmpty(id))
            {
                if (Apply(id, path, scene, out string why)) done.Add(id);
                else failed.Add(id + ": " + why);
            }

            string report = Report(path, scene, bits);
            var sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(failed.Count == 0 ? "true" : "false")
              .Append(",\"repaired\":[");
            for (int i = 0; i < done.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Q(done[i])); }
            sb.Append("],\"failed\":[");
            for (int i = 0; i < failed.Count; i++) { if (i > 0) sb.Append(','); sb.Append(Q(failed[i])); }
            sb.Append("],\"report\":").Append(report).Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Repairs that REPAIR ALL leaves alone, because they change the project rather than the avatar.
        /// Flipping asset serialization rewrites every asset Unity touches afterwards and shows up in every
        /// diff; that is a decision, so it stays a deliberate click on its own line.
        /// </summary>
        static readonly string[] NotInAll = { "project.binary", "platform.windows" };

        static string NextRepairable(string path, string scene, int bits, List<string> alreadyTried)
        {
            string json = Report(path, scene, bits);
            // Cheap scan rather than a parser: the only thing wanted is the next "fix":"…" that is not empty.
            const string needle = "\"fix\":\"";
            int at = 0;
            while ((at = json.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
            {
                at += needle.Length;
                int end = json.IndexOf('"', at);
                if (end < 0) break;
                string fix = json.Substring(at, end - at);
                if (fix.Length > 0 && !alreadyTried.Contains(fix) && Array.IndexOf(NotInAll, fix) < 0) return fix;
            }
            return null;
        }

        static bool Apply(string id, string path, string scene, out string why)
        {
            why = "";
            try
            {
                switch (id)
                {
                    case "account.reconnect":
                        return Reconnect(out why);

                    case "editor.exitplay":
                        EditorApplication.isPlaying = false;
                        why = "";
                        return true;

                    case "scene.save":
                        return SaveScenes(out why);

                    case "project.binary":
                        // A project-wide setting, so it is never applied without the user pressing the button.
                        // Unity re-serializes assets as it touches them; nothing is lost, and Edit > Project
                        // Settings > Editor > Asset Serialization puts it back.
                        EditorSettings.serializationMode = SerializationMode.ForceBinary;
                        AssetDatabase.SaveAssets();
                        return true;

                    case "platform.windows":
                        // KS Anti-Rip is PC only: the key reaches the avatar through KS OSC Key, a PC program.
                        // Switching re-imports the project's textures for Windows, which can take minutes, so this
                        // too is never part of REPAIR ALL.
                        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64) return true;
                        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                        {
                            why = "Unity could not switch to Windows. Is the Windows Build Support module installed?";
                            return false;
                        }
                        return true;

                    case "avatar.pipeline":
                    {
                        GameObject go = KSLinkTools.FindAvatar(path, scene);
                        if (go == null) { why = "the avatar is not in the open scenes any more"; return false; }
                        if (go.GetComponent<PipelineManager>() == null) Undo.AddComponent<PipelineManager>(go);
                        return true;
                    }

                    case "avatar.animator":
                    {
                        GameObject go = KSLinkTools.FindAvatar(path, scene);
                        if (go == null) { why = "the avatar is not in the open scenes any more"; return false; }
                        if (go.GetComponent<Animator>() == null) Undo.AddComponent<Animator>(go);
                        return true;
                    }

                    case "avatar.scale":
                    {
                        GameObject go = KSLinkTools.FindAvatar(path, scene);
                        if (go == null) { why = "the avatar is not in the open scenes any more"; return false; }
                        Undo.RecordObject(go.transform, "KS Anti-Rip: reset avatar scale");
                        go.transform.localScale = Vector3.one;
                        return true;
                    }

                    case "avatar.menu":
                        return CreateMenu(path, scene, out why);

                    default:
                        why = "unknown repair";
                        return false;
                }
            }
            catch (Exception e)
            {
                why = e.Message;
                return false;
            }
        }

        // ── environment: editor, SDK, account, scene ────────────────────────────────────────────

        static GameObject Environment(List<Check> checks, string path, string scene, out string sdk, out string platform)
        {
            sdk = SdkVersion();
            platform = EditorUserBuildSettings.activeBuildTarget.ToString();

            // 1. the account. Nothing below matters if the upload at the end cannot happen.
            if (APIUser.IsLoggedIn && APIUser.CurrentUser != null)
            {
                checks.Add(new Check
                {
                    id = "account",
                    title = "VRChat account",
                    detail = "Connected as " + APIUser.CurrentUser.displayName + ".",
                    level = LevelOk,
                });
                if (!CanUpload())
                    checks.Add(new Check
                    {
                        id = "account.upload",
                        title = "Upload permission",
                        detail = "This account cannot publish avatars yet. Protecting works; the upload is what refuses.",
                        level = LevelError,
                    });
            }
            else
            {
                bool canReconnect = CanReconnect();
                checks.Add(new Check
                {
                    id = "account",
                    title = "VRChat account",
                    detail = canReconnect
                        ? "Not connected in this Unity session. Your saved credentials are here."
                        : "Not connected. Sign in from VRChat SDK > Show Control Panel > Authentication.",
                    level = LevelError,
                    fix = canReconnect ? "account.reconnect" : "",
                    fixLabel = canReconnect ? "RECONNECT" : "",
                });
            }

            // 2. the SDK itself
            checks.Add(new Check
            {
                id = "sdk",
                title = "VRChat SDK",
                detail = string.IsNullOrEmpty(sdk)
                    ? "Avatars SDK found, version unknown."
                    : "Avatars SDK " + sdk + ".",
                level = LevelOk,
            });

            // 3. the editor has to be idle and writable
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                checks.Add(new Check
                {
                    id = "editor.playmode",
                    title = "Play mode",
                    detail = "Assets written in play mode are thrown away when it stops.",
                    level = LevelError,
                    fix = "editor.exitplay",
                    fixLabel = "EXIT PLAY MODE",
                });
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                checks.Add(new Check
                {
                    id = "editor.busy",
                    title = "Unity is busy",
                    detail = "Compiling or importing. Check again in a moment.",
                    level = LevelBusy,
                });

            // 4. unsaved scenes. The protection reads the avatar out of the scene as it is on disk-adjacent
            //    state, and an unsaved scene is also the one thing an Undo cannot bring back if Unity dies.
            var dirty = new List<string>();
            bool untitled = false;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!SavableHere(s)) continue;
                // A scene never saved has an empty name; say so, rather than print " has unsaved changes."
                if (string.IsNullOrEmpty(s.path)) untitled = true;
                dirty.Add(string.IsNullOrEmpty(s.name) ? "An untitled scene" : s.name);
            }
            if (dirty.Count > 0)
                checks.Add(new Check
                {
                    id = "scene.dirty",
                    title = "Unsaved scene",
                    detail = string.Join(", ", dirty) + (dirty.Count == 1 ? " has unsaved changes." : " have unsaved changes.") +
                             (untitled ? " An untitled one is saved to Assets/Kawaii Studio/Scenes." : ""),
                    level = LevelWarn,
                    fix = "scene.save",
                    fixLabel = "SAVE SCENES",
                });

            // 5. the platform. KS Anti-Rip is PC only: the key is sent by KS OSC Key, a program on the wearer's PC,
            // so a Quest build could never be unlocked.
            bool windows = EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows64 ||
                           EditorUserBuildSettings.activeBuildTarget == BuildTarget.StandaloneWindows;
            checks.Add(windows
                ? new Check
                {
                    id = "platform",
                    title = "Build target",
                    detail = platform + ".",
                    level = LevelOk,
                }
                : new Check
                {
                    id = "platform",
                    title = "Build target is " + platform,
                    detail = "KS Anti-Rip is for PC avatars only: the key is sent by KS OSC Key on your PC. " +
                             "Switch this project to Windows to protect it.",
                    level = LevelError,
                    fix = "platform.windows",
                    fixLabel = "SWITCH TO WINDOWS",
                });

            // 6. the avatar itself has to still be there
            GameObject avatar = KSLinkTools.FindAvatar(path, scene);
            if (string.IsNullOrEmpty(path))
            {
                checks.Add(new Check
                {
                    id = "avatar",
                    title = "Avatar",
                    detail = "No avatar picked yet.",
                    level = LevelError,
                });
                return null;
            }
            if (avatar == null)
            {
                checks.Add(new Check
                {
                    id = "avatar",
                    title = "Avatar",
                    detail = "\"" + path + "\" is not in the open scenes any more.",
                    level = LevelError,
                });
                return null;
            }
            if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
            {
                checks.Add(new Check
                {
                    id = "avatar",
                    title = "Avatar",
                    detail = "\"" + avatar.name + "\" has no VRC Avatar Descriptor.",
                    level = LevelError,
                });
                return null;
            }
            checks.Add(new Check
            {
                id = "avatar",
                title = "Avatar",
                detail = avatar.name + " in " + (string.IsNullOrEmpty(scene) ? "the open scene" : scene) + ".",
                level = LevelOk,
            });
            return avatar;
        }

        // ── the avatar ──────────────────────────────────────────────────────────────────────────

        static string Blueprint(List<Check> checks, GameObject avatar)
        {
            var pipeline = avatar.GetComponent<PipelineManager>();
            if (pipeline == null)
            {
                checks.Add(new Check
                {
                    id = "avatar.pipeline",
                    title = "Blueprint ID",
                    detail = "No Pipeline Manager, so no avatar id. VRChat fills the id in on a normal upload.",
                    level = LevelError,
                    fix = "avatar.pipeline",
                    fixLabel = "ADD PIPELINE MANAGER",
                });
                return "";
            }
            string id = pipeline.blueprintId ?? "";
            if (string.IsNullOrEmpty(id))
            {
                checks.Add(new Check
                {
                    id = "avatar.blueprint",
                    title = "Blueprint ID",
                    detail = "Empty. Upload this avatar once normally first — the key is named after the avatar id.",
                    level = LevelError,
                });
                return "";
            }
            checks.Add(new Check
            {
                id = "avatar.blueprint",
                title = "Blueprint ID",
                detail = id,
                level = LevelOk,
            });
            return id;
        }

        static void Meshes(List<Check> checks, GameObject avatar, out int meshes, out int vertices)
        {
            var skinned = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                .Where(r => r.sharedMesh != null).ToArray();
            var unique = new List<Mesh>();
            vertices = 0;
            foreach (var smr in skinned)
                if (!unique.Contains(smr.sharedMesh)) { unique.Add(smr.sharedMesh); vertices += smr.sharedMesh.vertexCount; }
            meshes = unique.Count;

            if (meshes == 0)
            {
                checks.Add(new Check
                {
                    id = "avatar.meshes",
                    title = "Meshes",
                    detail = "No skinned mesh. Only a Skinned Mesh Renderer can carry the restore shapes.",
                    level = LevelError,
                });
                return;
            }
            checks.Add(new Check
            {
                id = "avatar.meshes",
                title = "Meshes",
                detail = meshes + (meshes == 1 ? " mesh, " : " meshes, ") + vertices.ToString("N0") + " vertices to scramble.",
                level = LevelOk,
            });

            // A protected copy carries the mesh plus one full-size blend shape per restore group, so a mesh
            // that is already enormous is written out several times its own size. Unity's serializer took the
            // whole editor down on a multi-million-vertex one during testing, so this is a measured number,
            // not a guess -- and an avatar that heavy is far past what VRChat will show anybody anyway.
            Mesh heaviest = unique.OrderByDescending(m => m.vertexCount).FirstOrDefault();
            if (heaviest != null && heaviest.vertexCount > 200000)
                checks.Add(new Check
                {
                    id = "avatar.heavy",
                    title = "Very heavy mesh",
                    detail = "\"" + heaviest.name + "\" has " + heaviest.vertexCount.ToString("N0") + " vertices. " +
                             "Protecting it writes several copies of that and can run Unity out of memory. " +
                             "Decimate it first.",
                    level = LevelError,
                });

            // THE ONE THAT TAKES UNITY DOWN. A protected mesh is saved as an asset, and in a Force Text
            // project that means YAML -- one text line per blend shape delta. Every shape the avatar already
            // has gets copied too, so an ordinary avatar with 259 visemes and expressions on a 64k mesh is
            // around 600 MB of deltas, and Unity's text writer dies on it with SIGSEGV, before KS ANTI-RIP's
            // own code is reached at all. Measured here on 2022.3.22f1, not guessed. Binary writes the same
            // data as a blob and does not care.
            Mesh worst = null;
            long worstBytes = 0;
            foreach (Mesh m in unique)
            {
                long bytes = (long)m.vertexCount * (m.blendShapeCount + 3) * 36L;
                if (bytes > worstBytes) { worstBytes = bytes; worst = m; }
            }
            if (worst != null && worstBytes > 150L * 1024 * 1024 &&
                EditorSettings.serializationMode == SerializationMode.ForceText)
                checks.Add(new Check
                {
                    id = "project.textassets",
                    title = "Text asset serialization",
                    detail = "\"" + worst.name + "\" carries " + worst.blendShapeCount + " blend shapes on " +
                             worst.vertexCount.ToString("N0") + " vertices, about " + (worstBytes / (1024 * 1024)) +
                             " MB. Saving that as text crashes Unity. Switch this project to binary assets.",
                    level = LevelError,
                    fix = "project.binary",
                    fixLabel = "USE BINARY ASSETS",
                });

            var statics = avatar.GetComponentsInChildren<MeshRenderer>(true)
                                .Where(mr => mr.GetComponent<MeshFilter>() != null &&
                                             mr.GetComponent<MeshFilter>().sharedMesh != null).ToArray();
            if (statics.Length > 0)
                checks.Add(new Check
                {
                    id = "avatar.statics",
                    title = "Static meshes",
                    detail = statics.Length + (statics.Length == 1 ? " static mesh stays" : " static meshes stay") +
                             " unprotected: " + string.Join(", ", statics.Take(4).Select(s => s.name)) +
                             (statics.Length > 4 ? ", …" : "") + ".",
                    level = LevelWarn,
                });

            // Two renderers on one mesh asset is fine (the processor deduplicates), but a mesh shared with
            // ANOTHER avatar in the project is worth saying out loud: the protected copy is a new asset, so
            // the other avatar keeps the original and is not protected by this run.
            var shared = unique.Where(m =>
            {
                string p = AssetDatabase.GetAssetPath(m);
                return !string.IsNullOrEmpty(p) && AssetDatabase.GetDependencies(p, false).Length >= 0 &&
                       UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>()
                           .Where(r => r.sharedMesh == m)
                           .Select(r => Root(r.transform))
                           .Distinct()
                           .Count() > 1;
            }).ToArray();
            if (shared.Length > 0)
                checks.Add(new Check
                {
                    id = "avatar.shared",
                    title = "Shared meshes",
                    detail = shared.Length + (shared.Length == 1 ? " mesh is" : " meshes are") + " also used by another " +
                             "avatar here. The protected copy is a new asset, so that one stays unprotected.",
                    level = LevelWarn,
                });
        }

        static Transform Root(Transform t)
        {
            Transform best = t;
            for (Transform c = t; c != null; c = c.parent)
                if (c.GetComponent<VRCAvatarDescriptor>() != null) best = c;
            return best;
        }

        static void Rig(List<Check> checks, GameObject avatar)
        {
            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                checks.Add(new Check
                {
                    id = "avatar.animator",
                    title = "Animator",
                    detail = "The avatar root has no Animator. The lock is an animator layer, so it needs one.",
                    level = LevelError,
                    fix = "avatar.animator",
                    fixLabel = "ADD ANIMATOR",
                });
            }
            else if (animator.avatar == null)
            {
                checks.Add(new Check
                {
                    id = "avatar.rig",
                    title = "Rig",
                    detail = "The Animator has no Avatar assigned (generic rig). Protection still works.",
                    level = LevelWarn,
                });
            }

            Vector3 s = avatar.transform.localScale;
            if (s.x <= 0.0001f || s.y <= 0.0001f || s.z <= 0.0001f)
                checks.Add(new Check
                {
                    id = "avatar.scale",
                    title = "Avatar scale",
                    detail = "Root scale is " + s + ", which collapses the avatar.",
                    level = LevelError,
                    fix = "avatar.scale",
                    fixLabel = "RESET SCALE",
                });

            var nested = avatar.GetComponentsInChildren<VRCAvatarDescriptor>(true)
                               .Where(d => d.gameObject != avatar).ToArray();
            if (nested.Length > 0)
                checks.Add(new Check
                {
                    id = "avatar.nested",
                    title = "Nested avatar",
                    detail = "\"" + nested[0].gameObject.name + "\" inside this avatar has its own Avatar Descriptor. " +
                             "Unparent it first.",
                    level = LevelError,
                });
        }

        static void Parameters(List<Check> checks, VRCAvatarDescriptor descriptor, int bits, out int freeBits)
        {
            VRCExpressionParameters p = descriptor.expressionParameters;
            int used = 0;
            try { used = p != null ? p.CalcTotalCost() : 0; } catch { used = 0; }
            freeBits = Math.Max(0, Budget - used);

            // The key is one float per restore shape: 8 synced bits each, or none at all when it stays local.
            if (bits == 0)
                checks.Add(new Check
                {
                    id = "avatar.bits",
                    title = "Synced parameters",
                    detail = "The key stays on this PC, so it costs no synced bits. Only you will see the avatar whole.",
                    level = LevelOk,
                });
            else if (bits > freeBits)
                checks.Add(new Check
                {
                    id = "avatar.bits",
                    title = "Synced parameters",
                    detail = "The key needs " + bits + " bits (" + bits / 8 + " shapes), " + freeBits + " of " +
                             Budget + " are free. " +
                             (freeBits >= 8 ? "Use fewer restore shapes." : "Free some parameters up first."),
                    level = LevelError,
                });
            else
                checks.Add(new Check
                {
                    id = "avatar.bits",
                    title = "Synced parameters",
                    detail = bits + " bits for the key (" + bits / 8 + " shapes), " + freeBits + " of " + Budget + " free.",
                    level = LevelOk,
                });

            // VRChat refuses an upload that has parameters but no expressions menu, and the lock always ends up
            // adding parameters -- so an avatar with neither would become unuploadable because of us. The
            // processor creates the menu on its copy; this says so, and offers to do it on the real avatar now.
            if (descriptor.expressionsMenu == null)
                checks.Add(new Check
                {
                    id = "avatar.menu",
                    title = "Expressions menu",
                    detail = "None. VRChat refuses an upload with parameters and no menu, so one is made on the copy.",
                    level = LevelWarn,
                    fix = "avatar.menu",
                    fixLabel = "ADD EMPTY MENU",
                });
        }

        static void Already(List<Check> checks, GameObject avatar)
        {
            Transform mark = avatar.transform.Cast<Transform>()
                                   .FirstOrDefault(t => t.name == KSAntiRipProcessor.MarkerName);
            if (mark != null)
                checks.Add(new Check
                {
                    id = "avatar.already",
                    title = "Already protected",
                    detail = "Already carries the \"" + KSAntiRipProcessor.MarkerName + "\" mark. Protecting it again " +
                             "stacks a second scramble on the first. Pick the original avatar.",
                    level = LevelError,
                });

            // A mixed Write Defaults FX controller is the classic cause of "my avatar works in Unity and not in
            // game". The lock copes with it (it writes in whichever mode dominates) but the avatar's own layers
            // are the user's to fix, so this reports and does not touch anything.
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            AnimatorController fx = null;
            if (descriptor != null && descriptor.baseAnimationLayers != null)
                foreach (var l in descriptor.baseAnimationLayers)
                    if (l.type == VRCAvatarDescriptor.AnimLayerType.FX) fx = l.animatorController as AnimatorController;
            if (fx == null) return;

            int on = 0, off = 0;
            foreach (var layer in fx.layers)
                foreach (var state in States(layer.stateMachine))
                    if (state.writeDefaultValues) on++; else off++;
            if (on > 0 && off > 0)
                checks.Add(new Check
                {
                    id = "avatar.wd",
                    title = "Write Defaults",
                    detail = "Your FX mixes Write Defaults on (" + on + ") and off (" + off + "). KS ANTI-RIP follows " +
                             "the majority, but the mix breaks avatars in game.",
                    level = LevelWarn,
                });
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            if (machine == null) yield break;
            foreach (var s in machine.states) yield return s.state;
            foreach (var sub in machine.stateMachines)
                foreach (var s in States(sub.stateMachine)) yield return s;
        }

        // ── ownership, which needs the network ──────────────────────────────────────────────────

        static readonly Dictionary<string, string> OwnerCache = new Dictionary<string, string>();
        static readonly HashSet<string> OwnerPending = new HashSet<string>();

        /// <summary>
        /// Whether the logged-in account actually owns this blueprint id. Asked over the API, so the first call
        /// starts the fetch and answers "busy"; the next one has it. Done by reflection because the API model
        /// layer is the part of the SDK that changes shape between versions, and a wrong signature here would
        /// stop all of KS Unity Tools compiling.
        /// </summary>
        static string Ownership(List<Check> checks, string blueprint)
        {
            if (string.IsNullOrEmpty(blueprint) || !APIUser.IsLoggedIn || APIUser.CurrentUser == null)
                return "unknown";

            string state;
            lock (OwnerCache)
            {
                if (!OwnerCache.TryGetValue(blueprint, out state))
                {
                    state = OwnerPending.Contains(blueprint) ? "busy" : "start";
                }
            }

            if (state == "start")
            {
                lock (OwnerCache) OwnerPending.Add(blueprint);
                if (!StartOwnerFetch(blueprint))
                {
                    lock (OwnerCache) { OwnerPending.Remove(blueprint); OwnerCache[blueprint] = "unknown"; }
                    return "unknown";
                }
                state = "busy";
            }

            switch (state)
            {
                case "busy":
                    checks.Add(new Check
                    {
                        id = "avatar.owner",
                        title = "Avatar ownership",
                        detail = "Asking VRChat…",
                        level = LevelBusy,
                    });
                    return "busy";
                case "mine":
                    checks.Add(new Check
                    {
                        id = "avatar.owner",
                        title = "Avatar ownership",
                        detail = "Confirmed: it belongs to " + APIUser.CurrentUser.displayName + ".",
                        level = LevelOk,
                    });
                    return "mine";
                case "theirs":
                    checks.Add(new Check
                    {
                        id = "avatar.owner",
                        title = "Avatar ownership",
                        detail = "This blueprint id belongs to another account. The upload would be refused.",
                        level = LevelError,
                    });
                    return "theirs";
                case "missing":
                    checks.Add(new Check
                    {
                        id = "avatar.owner",
                        title = "Avatar ownership",
                        detail = "VRChat does not know this blueprint id. Upload the avatar once normally first.",
                        level = LevelWarn,
                    });
                    return "missing";
                default:
                    return "unknown";
            }
        }

        static bool StartOwnerFetch(string blueprint)
        {
            try
            {
                Type apiAvatar = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => { try { return a.GetType("VRC.Core.ApiAvatar"); } catch { return null; } })
                    .FirstOrDefault(t => t != null);
                if (apiAvatar == null) return false;

                object av = Activator.CreateInstance(apiAvatar);
                PropertyInfo idProp = apiAvatar.GetProperty("id") ?? apiAvatar.BaseType?.GetProperty("id");
                FieldInfo idField = idProp == null ? apiAvatar.GetField("id") : null;
                if (idProp != null) idProp.SetValue(av, blueprint);
                else if (idField != null) idField.SetValue(av, blueprint);
                else return false;

                MethodInfo get = apiAvatar.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Concat(apiAvatar.BaseType != null
                        ? apiAvatar.BaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        : Array.Empty<MethodInfo>())
                    .FirstOrDefault(m => m.Name == "Get" && m.GetParameters().Length == 3);
                if (get == null) return false;

                ParameterInfo[] ps = get.GetParameters();
                Type containerType = ps[1].ParameterType.GetGenericArguments().FirstOrDefault();
                if (containerType == null) return false;

                Delegate onDone = Callback(containerType, model =>
                {
                    string owner = "unknown";
                    if (model != null)
                    {
                        object author = model.GetType().GetProperty("authorId")?.GetValue(model)
                                        ?? model.GetType().GetField("authorId")?.GetValue(model);
                        string authorId = author as string ?? "";
                        owner = string.IsNullOrEmpty(authorId)
                            ? "unknown"
                            : (APIUser.CurrentUser != null && authorId == APIUser.CurrentUser.id ? "mine" : "theirs");
                    }
                    lock (OwnerCache) { OwnerCache[blueprint] = owner; OwnerPending.Remove(blueprint); }
                });
                Delegate onFail = Callback(containerType, _ =>
                {
                    lock (OwnerCache) { OwnerCache[blueprint] = "missing"; OwnerPending.Remove(blueprint); }
                });

                get.Invoke(av, new object[] { false, onDone, onFail });
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KS ANTI-RIP] ownership check unavailable: " + e.Message);
                return false;
            }
        }

        /// <summary>Builds an Action&lt;TContainer&gt; that pulls .Model out and hands it to <paramref name="then"/>.</summary>
        static Delegate Callback(Type containerType, Action<object> then)
        {
            Action<object> body = container =>
            {
                object model = null;
                try
                {
                    model = container?.GetType().GetProperty("Model")?.GetValue(container)
                            ?? container?.GetType().GetField("Model")?.GetValue(container);
                }
                catch { }
                then(model);
            };
            // One tiny closure class so the delegate's parameter type matches what the SDK expects exactly.
            return Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(containerType),
                                           new Trampoline { Body = body },
                                           typeof(Trampoline).GetMethod(nameof(Trampoline.Invoke)));
        }

        sealed class Trampoline
        {
            public Action<object> Body;
            public void Invoke(object container) => Body(container);
        }

        /// <summary>Clears the cached ownership answers, so CHECK AGAIN really asks again.</summary>
        public static void Forget()
        {
            lock (OwnerCache) { OwnerCache.Clear(); OwnerPending.Clear(); }
        }

        // ── account plumbing ────────────────────────────────────────────────────────────────────

        static bool CanUpload()
        {
            try
            {
                return APIUser.IsLoggedIn && APIUser.CurrentUser != null && APIUser.CurrentUser.canPublishAvatars;
            }
            catch { return APIUser.IsLoggedIn; }
        }

        static bool CanReconnect()
        {
            if (APIUser.IsLoggedIn) return false;
            try { return ApiCredentials.Load(); } catch { return false; }
        }

        /// <summary>
        /// The SDK's own reconnect: saved credentials plus InitialFetchCurrentUser, exactly as
        /// VRCSdkControlPanelAccount.InitAccount does it. Reflected for the same reason as the ownership fetch.
        /// </summary>
        static bool Reconnect(out string why)
        {
            why = "";
            try
            {
                if (!ApiCredentials.Load())
                {
                    why = "no saved credentials — sign in once in the VRChat SDK Control Panel";
                    return false;
                }
                MethodInfo fetch = typeof(APIUser).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "InitialFetchCurrentUser" && m.GetParameters().Length == 2);
                if (fetch == null) { why = "this SDK version has no reconnect entry point"; return false; }

                Type containerType = fetch.GetParameters()[0].ParameterType.GetGenericArguments().FirstOrDefault();
                if (containerType == null) { why = "unexpected SDK signature"; return false; }

                Delegate ok = Callback(containerType, model =>
                {
                    if (model == null) Debug.LogWarning("[KS ANTI-RIP] VRChat did not return a user — sign in again.");
                });
                Delegate bad = Callback(containerType, _ =>
                    Debug.LogWarning("[KS ANTI-RIP] VRChat refused the saved credentials — sign in again in the SDK panel."));
                fetch.Invoke(null, new object[] { ok, bad });
                return true;
            }
            catch (Exception e)
            {
                why = e.Message;
                return false;
            }
        }

        /// <summary>
        /// A loaded, modified scene that is ours to save. Scenes inside Packages/ are not: NDMF opens its own
        /// "___NDMF Preview___" there, and a package folder is not the project's to write into.
        /// </summary>
        static bool SavableHere(Scene s) =>
            s.isLoaded && s.isDirty && !(s.path ?? "").StartsWith("Packages/", StringComparison.Ordinal);

        /// <summary>
        /// Saves every modified scene WITHOUT a dialog. EditorSceneManager.SaveOpenScenes asks for a file name when a
        /// scene was never saved -- a Save As window in Unity that nobody is looking at, because this repair is pressed
        /// in the Kawaii Studio app (owner, 2026-11-16: the whole of KS Anti-Rip is driven from the app). An untitled
        /// scene goes to Assets/Kawaii Studio/Scenes under a dated name instead.
        /// </summary>
        static bool SaveScenes(out string why)
        {
            why = "";
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!SavableHere(s)) continue;
                if (string.IsNullOrEmpty(s.path))
                {
                    const string folder = "Assets/Kawaii Studio/Scenes";
                    EnsureFolder(folder);
                    string target = AssetDatabase.GenerateUniqueAssetPath(
                        folder + "/Untitled " + DateTime.Now.ToString("yyyy-MM-dd HHmm") + ".unity");
                    if (!EditorSceneManager.SaveScene(s, target)) { why = "Unity would not save the untitled scene"; return false; }
                }
                else if (!EditorSceneManager.SaveScene(s))
                {
                    why = "Unity would not save " + s.name;
                    return false;
                }
            }
            return true;
        }

        static bool CreateMenu(string path, string scene, out string why)
        {
            why = "";
            GameObject go = KSLinkTools.FindAvatar(path, scene);
            if (go == null) { why = "the avatar is not in the open scenes any more"; return false; }
            var descriptor = go.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) { why = "no Avatar Descriptor"; return false; }
            if (descriptor.expressionsMenu != null) return true;

            string folder = "Assets/Kawaii Studio/Generated";
            if (descriptor.expressionParameters != null)
            {
                string existing = AssetDatabase.GetAssetPath(descriptor.expressionParameters);
                if (!string.IsNullOrEmpty(existing)) folder = System.IO.Path.GetDirectoryName(existing).Replace('\\', '/');
            }
            EnsureFolder(folder);

            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            string file = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + Safe(go.name) + " Menu.asset");
            AssetDatabase.CreateAsset(menu, file);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(descriptor, "KS Anti-Rip: add expressions menu");
            descriptor.expressionsMenu = menu;
            EditorUtility.SetDirty(descriptor);
            return true;
        }

        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string[] parts = folder.Split('/');
            string at = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = at + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]);
                at = next;
            }
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s ?? "avatar")
                sb.Append(System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            string outp = sb.ToString().Trim();
            return outp.Length == 0 ? "avatar" : outp;
        }

        static string SdkVersion()
        {
            try
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(VRCAvatarDescriptor).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.version)) return info.version;
            }
            catch { }
            return "";
        }

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
