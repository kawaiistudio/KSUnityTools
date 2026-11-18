using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Runs one protection pass: ask the Kawaii Studio app for a plan, apply it to a COPY of the avatar, save.
    ///
    /// The thinking happens in the app's core (obfuscated, hash-sealed, Premium-checked). This side only
    /// applies what it is given — which is why KS Unity Tools can keep shipping as readable source without
    /// publishing how the protection works.
    ///
    /// NON-DESTRUCTIVE, and tested as such: every change happens on an Instantiate() clone that is destroyed
    /// in a finally block, and KSAntiRipNoHarmTest hashes every source file before and after. It also never
    /// reads or rewrites shader source, so Poiyomi, lilToon and the Quest mobile shaders come through intact.
    /// </summary>
    public static class KSAntiRipProcessor
    {
        public const string Version = "5.1.0";

        /// <summary>
        /// The mark a protected avatar carries, so VRChat Archive REFUSES to archive it -- auto-archiver and
        /// manual upload alike. A KS-protected avatar belongs to its creator, and our own archive is the first
        /// place that has to respect that.
        ///
        /// It is deliberately the SAME GameObject a Kawaii Studio hot swap already injects
        /// (dependencies\VAABS\uploader\kawaii_inject.py, BUNDLE_GO_NAME), owner's call 2026-11-16: one mark and
        /// one detection for everything we put into a bundle, rather than a different rule per feature.
        ///
        /// The exact string matters -- two spaces, matched as a WHOLE name, never translated, never suffixed with
        /// a version, and never renamed by the name obfuscator. Like kawaii_inject, it goes first under the root.
        /// </summary>
        public const string MarkerName = "Kawaii Studio Bundle";

        public class Settings
        {
            public float strength = 5f;           // metres of displacement, requested of the core
            public int restoreGroups = 3;         // how many shapes the restoration is split across; 8 synced bits each
            /// <summary>
            /// True: other players receive the key and see the avatar whole (and a modified client in the same
            /// instance could read it). False: the key never leaves the wearer's PC, and only the wearer sees the
            /// avatar whole. See KSAntiRipLock.
            /// </summary>
            public bool syncKey = true;
            public bool hideWhileLocked = true;
            public bool obfuscateNames = true;

            /// <summary>
            /// Worn without its key for longer than KSAntiRipLock.FreezeGraceSeconds, the avatar stops its wearer
            /// walking and its own FX layers weigh nothing, so its menu does nothing (owner, 2026-11-17). An allowed
            /// wearer's key arrives well inside the grace period and nothing is ever applied to them.
            /// </summary>
            public bool freezeWhileLocked = true;

            /// <summary>
            /// While the avatar is locked, show the Kawaii Studio badge in its place ("ANTI-RIP PROTECTION -- ask me
            /// if you want to use this avatar"), gone the moment the key arrives. See KSAntiRipBadge.
            /// </summary>
            public bool lockedBadge = true;

            /// <summary>
            /// How much bigger the protected avatar may get, in MB (owner, 2026-11-17). The core turns this into
            /// how many vertices it scrambles, so a near-200-MB avatar still fits under VRChat's limit. 0 = no cap.
            /// </summary>
            public int maxAddedMegabytes = 12;

            /// <summary>What the key costs out of VRChat's 256 synced bits: one float (8 bits) per restore shape.</summary>
            public int KeyBits => syncKey ? Math.Max(1, restoreGroups) * 8 : 0;
        }

        public class Result
        {
            public bool ok;
            public string message = "";
            public bool needsPremium;
            public string folder = "";
            public string keyPath = "";
            public string prefabPath = "";
            public int meshesProtected;
            public int verticesProtected;
            public int objectsRenamed;
            public string lockLayer = "";
            public string freezeLayer = "";      // empty when the avatar is not frozen while locked
            public string badgePath = "";        // the locked badge's path from the root; empty when there is none
            public List<string> warnings = new List<string>();
        }

        /// <summary>
        /// Where the plan comes from. The app by default; a test supplies its own so the applicator can be
        /// checked with no app running.
        /// </summary>
        public interface IPlanSource
        {
            KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                       float strength, int restoreGroups, int maxAddedMegabytes = 0);
            KSAntiRipClient.MeshPlan Mesh(string session, string id);
            void End(string session);
            string LastError { get; }
        }

        sealed class AppPlanSource : IPlanSource
        {
            public KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                              float strength, int restoreGroups, int maxAddedMegabytes = 0)
                => KSAntiRipClient.Begin(avatarId, avatarName, meshes, strength, restoreGroups, maxAddedMegabytes);
            public KSAntiRipClient.MeshPlan Mesh(string session, string id) => KSAntiRipClient.Mesh(session, id);
            public void End(string session) => KSAntiRipClient.End(session);
            public string LastError => KSAntiRipClient.LastError;
        }

        /// <summary>
        /// The manual path: protect a COPY of <paramref name="source"/> and save it as a prefab. The original in
        /// the scene is never touched. The build hook (<see cref="KSAntiRipBuildHook"/>) does not go through here —
        /// it protects the avatar VRChat is already building, in place, with no copy and no prefab.
        /// </summary>
        public static Result Protect(GameObject source, Settings settings, IPlanSource planSource = null)
        {
            var result = new Result();
            if (source == null) { result.message = "No avatar selected."; return result; }
            if (source.GetComponent<VRCAvatarDescriptor>() == null)
            {
                result.message = "That GameObject has no VRC Avatar Descriptor.";
                return result;
            }

            GameObject clone = UnityEngine.Object.Instantiate(source);
            clone.name = "KS_PROTECTED_" + source.name;
            try
            {
                string folder = KawaiiStudioPaths.EnsureFolder(
                    KawaiiStudioPaths.OutputFolder("Protected") + "/" + KawaiiStudioPaths.SafeName(source.name) + "_" +
                    Guid.NewGuid().ToString("N").Substring(0, 6));

                ProtectInPlace(clone, settings, planSource, folder, result);
                if (!result.ok) return result;

                EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Saving…", 0.9f);
                PrefabUtility.SaveAsPrefabAsset(clone, folder + "/KS_PROTECTED.prefab", out bool saved);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                if (!saved)
                {
                    result.ok = false;
                    result.message = "Could not write the protected prefab to:\n" + folder;
                    return result;
                }
                result.prefabPath = folder + "/KS_PROTECTED.prefab";
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError("[KS ANTI-RIP] " + e);
                result.ok = false;
                result.message = "Protection failed: " + e.Message + "\n\nSee the Console for the full error.";
                return result;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        /// <summary>
        /// Protects <paramref name="root"/> IN PLACE: scrambles its skinned meshes, hides the signposts, builds the
        /// lock, writes the key. No copy is made and no prefab is saved — the caller owns the GameObject. The
        /// manual path hands in a throwaway clone; the build hook hands in the avatar VRChat is uploading, which is
        /// already a disposable build copy. Generated assets (scrambled meshes, rewritten clips, the lock
        /// controller) go into <paramref name="folder"/>.
        ///
        /// On return <see cref="Result.ok"/> says whether it worked; on failure <see cref="Result.message"/> says
        /// why and nothing partial is left wired that would ship a mesh that cannot be opened.
        /// </summary>
        public static void ProtectInPlace(GameObject root, Settings settings, IPlanSource planSource, string folder,
                                          Result result)
        {
            planSource = planSource ?? new AppPlanSource();
            result.ok = false;
            result.folder = folder;

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) { result.message = "That GameObject has no VRC Avatar Descriptor."; return; }

            var pipeline = root.GetComponent<PipelineManager>();
            string blueprint = pipeline != null ? pipeline.blueprintId : null;
            if (string.IsNullOrEmpty(blueprint))
            {
                // The key is named after the avatar id, and KS OSC KEY matches it against /avatar/change.
                // Without an id there is nothing to match, so say so instead of writing a dead key.
                result.message = "This avatar has no Blueprint ID yet.\n\n" +
                                 "Upload it once normally first — KS Anti-Rip needs the avatar id to name its key, " +
                                 "and the key is what unlocks it in game.";
                return;
            }

            // What we will ask the core to plan for. Meshes are deduplicated: two renderers sharing one mesh
            // must share one scrambled copy AND one set of weights, or the second never opens.
            var skinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                              .Where(r => r.sharedMesh != null).ToArray();
            if (skinned.Length == 0) { result.message = "Nothing to protect: this avatar has no skinned meshes."; return; }

            var uniqueMeshes = new List<Mesh>();
            foreach (var smr in skinned)
                if (!uniqueMeshes.Contains(smr.sharedMesh)) uniqueMeshes.Add(smr.sharedMesh);

            var request = uniqueMeshes
                .Select((m, i) => new KSAntiRipClient.MeshInfo { id = "m" + i, vertexCount = m.vertexCount })
                .ToList();

            EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Asking Kawaii Studio…", 0.1f);
            KSAntiRipClient.Plan plan;
            // The SDK's build copy is named "<avatar>(Clone)": the app's log and the key's record show the avatar's name.
            string avatarName = root.name.EndsWith("(Clone)", StringComparison.Ordinal)
                ? root.name.Substring(0, root.name.Length - "(Clone)".Length).TrimEnd()
                : root.name;
            try { plan = planSource.Begin(blueprint, avatarName, request, settings.strength, settings.restoreGroups, settings.maxAddedMegabytes); }
            finally { EditorUtility.ClearProgressBar(); }

            if (plan == null || !plan.ok)
            {
                result.message = plan != null && !string.IsNullOrEmpty(plan.error) ? plan.error : planSource.LastError;
                result.needsPremium = plan != null && plan.needsPremium;
                if (string.IsNullOrEmpty(result.message)) result.message = "Kawaii Studio did not answer.";
                return;
            }

            try
            {
                string tag = string.IsNullOrEmpty(plan.tag) ? Guid.NewGuid().ToString("N").Substring(0, 6) : plan.tag;
                string marker = string.IsNullOrEmpty(plan.marker) ? MarkerName : plan.marker;
                var rng = new System.Random();
                result.keyPath = plan.keyPath;

                // The "do not archive me" mark, first under the root exactly like kawaii_inject.py, and the one
                // thing the name obfuscator never touches so the archive can still read it.
                var markerObject = new GameObject(marker);
                markerObject.transform.SetParent(root.transform, false);
                markerObject.transform.SetSiblingIndex(0);
                markerObject.SetActive(false);

                var rootSkinned = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                                      .Where(r => r.sharedMesh != null).ToArray();

                // Renderers, not paths: the hierarchy is renamed below, so paths are resolved afterwards.
                var protectedRenderers = new List<KeyValuePair<SkinnedMeshRenderer, KSAntiRipClient.MeshPlan>>();
                var applied = new Dictionary<Mesh, KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>>();

                for (int i = 0; i < rootSkinned.Length; i++)
                {
                    var smr = rootSkinned[i];
                    Mesh original = smr.sharedMesh;

                    if (!applied.TryGetValue(original, out var done))
                    {
                        int index = uniqueMeshes.FindIndex(m => m.vertexCount == original.vertexCount &&
                                                                m.name == original.name);
                        if (index < 0) index = uniqueMeshes.Count > 0 ? 0 : -1;
                        if (index < 0) continue;

                        EditorUtility.DisplayProgressBar("KS ANTI-RIP",
                            $"Protecting mesh {applied.Count + 1}/{uniqueMeshes.Count}…",
                            0.2f + 0.3f * applied.Count / Math.Max(1, uniqueMeshes.Count));

                        KSAntiRipClient.MeshPlan meshPlan = planSource.Mesh(plan.session, request[index].id);
                        if (meshPlan == null) { result.message = planSource.LastError; return; }

                        Mesh scrambled = KSAntiRipMesh.Apply(original, meshPlan.offsets, meshPlan.groups, meshPlan.shapeNames);
                        AssetDatabase.CreateAsset(scrambled, folder + "/MSH_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset");

                        done = new KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>(scrambled, meshPlan);
                        applied[original] = done;
                        result.meshesProtected++;
                        result.verticesProtected += scrambled.vertexCount;
                    }

                    smr.sharedMesh = done.Key;
                    protectedRenderers.Add(new KeyValuePair<SkinnedMeshRenderer, KSAntiRipClient.MeshPlan>(smr, done.Value));
                }

                if (result.meshesProtected == 0)
                {
                    result.message = "Nothing to protect: no mesh could be matched to the plan.";
                    return;
                }

                // Static MeshRenderers cannot carry blend shapes, so they cannot be restored this way. Say it
                // out loud rather than shipping an avatar with a permanently exploded prop.
                int statics = root.GetComponentsInChildren<MeshRenderer>(true)
                                  .Count(mr => mr.GetComponent<MeshFilter>() != null &&
                                               mr.GetComponent<MeshFilter>().sharedMesh != null);
                if (statics > 0)
                    result.warnings.Add(statics + " static mesh(es) were left unprotected — blend shapes only exist on skinned meshes.");

                EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Hiding the signposts…", 0.6f);

                // Renaming copies and rewrites every animator the descriptor uses, so it runs BEFORE the lock
                // is built -- otherwise the lock's own layer would be copied again and renamed away.
                if (settings.obfuscateNames)
                    result.objectsRenamed = KSAntiRipNames.Obfuscate(root, descriptor, folder, tag, rng, result.warnings);

                EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Building the lock…", 0.75f);

                var targets = new List<KSAntiRipLock.ShapeTarget>();
                var hidePaths = new List<string>();
                foreach (var entry in protectedRenderers)
                {
                    string path = AnimationUtility.CalculateTransformPath(entry.Key.transform, root.transform);
                    // Shape k of every mesh follows key parameter k: one key for the whole avatar.
                    for (int k = 0; k < entry.Value.shapeNames.Count; k++)
                        targets.Add(new KSAntiRipLock.ShapeTarget { path = path, shapeName = entry.Value.shapeNames[k], slot = k });
                    if (settings.hideWhileLocked) hidePaths.Add(path);
                }

                // The locked badge: the one thing shown while the avatar is locked. Added last, so the archive mark
                // stays the first child of the root, and under a name that says nothing when names are obfuscated.
                string badgePath = null;
                if (settings.lockedBadge)
                {
                    string badgeName = KSAntiRipBadge.ObjectName, badgeAssets = "KS_Badge";
                    if (settings.obfuscateNames)
                    {
                        const string letters = "abcdefghijklmnopqrstuvwxyz";
                        var chars = new char[10];
                        for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
                        badgeName = new string(chars);
                        for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
                        badgeAssets = new string(chars);
                    }
                    badgePath = KSAntiRipBadge.Add(root, descriptor, folder, badgeName, result.warnings, badgeAssets);
                    result.badgePath = badgePath ?? "";
                }

                AnimatorController fx = PrepareFxController(descriptor, folder, result);
                int layersBefore = fx.layers.Length;
                KSAntiRipLock.Build(root, descriptor, fx, targets, hidePaths, plan.parameters,
                                    settings.syncKey, folder, rng, settings.obfuscateNames, settings.freezeWhileLocked,
                                    badgePath, hideEverything: settings.hideWhileLocked, notes: result.warnings);
                result.lockLayer = fx.layers.Length > layersBefore ? fx.layers[layersBefore].name : "";
                // the freeze layer is always the last one the lock adds
                result.freezeLayer = settings.freezeWhileLocked && fx.layers.Length > layersBefore + 1
                    ? fx.layers[fx.layers.Length - 1].name : "";

                // A scrambled avatar whose lock is not wired can never be opened, key or no key. Refuse rather
                // than hand the creator an upload they cannot wear.
                if (!LockIsWired(descriptor, fx) || string.IsNullOrEmpty(result.lockLayer))
                {
                    result.message = "The lock could not be attached to this avatar's FX layer. " +
                                     "Open the avatar's VRC Avatar Descriptor once in the Inspector and try again.";
                    return;
                }

                result.ok = true;
                result.message = "Protected.";
            }
            catch (Exception e)
            {
                Debug.LogError("[KS ANTI-RIP] " + e);
                result.ok = false;
                result.message = "Protection failed: " + e.Message + "\n\nSee the Console for the full error.";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                try { planSource.End(plan.session); } catch { }
            }
        }

        /// <summary>Copies the avatar's FX controller into the output folder, or creates one if it has none.</summary>
        static AnimatorController PrepareFxController(VRCAvatarDescriptor descriptor, string folder, Result result)
        {
            string path = folder + "/KS_FX.controller";
            EnsureFxSlot(descriptor);
            int index = -1;
            for (int i = 0; i < descriptor.baseAnimationLayers.Length; i++)
                if (descriptor.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX) index = i;

            AnimatorController existing = index >= 0
                ? descriptor.baseAnimationLayers[index].animatorController as AnimatorController
                : null;
            string existingPath = existing != null ? AssetDatabase.GetAssetPath(existing) : null;

            AnimatorController fx;
            if (existing != null && !string.IsNullOrEmpty(existingPath) &&
                existingPath.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                // The name obfuscator already copied it into this run's folder. Copying again would strand its
                // rewritten clips and give the lock a controller nobody is pointing at.
                fx = existing;
            }
            else if (existing != null && !string.IsNullOrEmpty(existingPath))
            {
                // A real object-by-object deep copy, NOT AssetDatabase.CopyAsset: at build time an NDMF-merged FX
                // controller is spread across shared "assets N.asset" containers, and a file copy would hand back a
                // half-copied graph on the wrong controller. This branch runs only when name obfuscation is off (with
                // it on, the obfuscator already deep-copied this controller into the folder and the branch above
                // reuses it), so clips stay by reference -- no paths were renamed, so none need rewriting.
                try
                {
                    fx = KSAntiRipAnimatorCopy.Copy(existing, path, clip => clip);
                }
                catch (Exception e)
                {
                    Debug.LogError("[KS ANTI-RIP] " + e);
                    fx = null;
                }
                if (fx == null)
                {
                    AssetDatabase.CopyAsset(existingPath, path);
                    fx = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                }
            }
            else
            {
                fx = AnimatorController.CreateAnimatorControllerAtPath(path);
                result.warnings.Add("This avatar had no FX controller, so KS Anti-Rip made one.");
            }

            var layers = descriptor.baseAnimationLayers;
            layers[index].animatorController = fx;
            layers[index].isDefault = false;
            descriptor.baseAnimationLayers = layers;
            descriptor.customizeAnimationLayers = true;
            return fx;
        }

        /// <summary>
        /// Gives the descriptor an FX slot when it has none, the way the SDK's own inspector does
        /// (VRCAvatarDescriptorEditor3AnimLayerInit: Base, Additive, Gesture, Action, FX).
        ///
        /// Found by the 2026-11-16 proof run: an avatar whose baseAnimationLayers was empty -- built by script, or
        /// never opened in the descriptor inspector -- got its lock built into a brand-new FX controller that was
        /// then attached to NOTHING. Its mesh shipped scrambled with no lock wired, so it could not be opened even
        /// with the key. The other avatars only escaped because they happened to have the slot already.
        /// </summary>
        static void EnsureFxSlot(VRCAvatarDescriptor descriptor)
        {
            var layers = descriptor.baseAnimationLayers;
            if (layers != null && layers.Any(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX)) return;

            if (layers == null || layers.Length == 0)
            {
                layers = new[]
                {
                    VRCAvatarDescriptor.AnimLayerType.Base,
                    VRCAvatarDescriptor.AnimLayerType.Additive,
                    VRCAvatarDescriptor.AnimLayerType.Gesture,
                    VRCAvatarDescriptor.AnimLayerType.Action,
                    VRCAvatarDescriptor.AnimLayerType.FX,
                }.Select(t => new VRCAvatarDescriptor.CustomAnimLayer { type = t, isDefault = true }).ToArray();
            }
            else
            {
                layers = layers.Concat(new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = true },
                }).ToArray();
            }
            descriptor.baseAnimationLayers = layers;
        }

        /// <summary>True when the descriptor's FX slot really points at <paramref name="fx"/>.</summary>
        static bool LockIsWired(VRCAvatarDescriptor descriptor, AnimatorController fx)
        {
            if (descriptor == null || descriptor.baseAnimationLayers == null || !descriptor.customizeAnimationLayers) return false;
            return descriptor.baseAnimationLayers.Any(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX &&
                                                           !l.isDefault && l.animatorController == fx);
        }
    }
}
