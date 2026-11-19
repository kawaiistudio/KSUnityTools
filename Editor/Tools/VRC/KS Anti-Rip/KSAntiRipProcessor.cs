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

            /// <summary>
            /// 3.14: the meshes whose shader allows it are closed by that shader (KSAntiRipShader) instead of by
            /// restore shapes. Off by default. Whoever blocks the wearer's shaders is shown the poster instead.
            /// </summary>
            public bool shaderRestore = false;

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
            public string fallbackPosterPath = ""; // 3.14: the poster for viewers who block shaders; empty when none
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

        /// <summary>
        /// 3.14, optional: a plan source that can also lay out textures. Kept apart from <see cref="IPlanSource"/> so a
        /// source that does not know about textures (an older test) still works -- it simply gets no textures.
        /// </summary>
        public interface ITexturePlanSource
        {
            KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                       IList<KSAntiRipClient.TextureInfo> textures, float strength, int restoreGroups,
                                       int maxAddedMegabytes);
            KSAntiRipClient.TexturePlan Texture(string session, string id);
        }

        sealed class AppPlanSource : IPlanSource, ITexturePlanSource
        {
            public KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                              IList<KSAntiRipClient.TextureInfo> textures, float strength, int restoreGroups,
                                              int maxAddedMegabytes)
                => KSAntiRipClient.Begin(avatarId, avatarName, meshes, strength, restoreGroups, maxAddedMegabytes, textures);
            public KSAntiRipClient.TexturePlan Texture(string session, string id) => KSAntiRipClient.Texture(session, id);
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

            // 3.14: a mesh can be closed by the avatar's OWN shader when every material that draws it, on every
            // renderer that wears it, is a locked-Poiyomi shader this can patch (decided by name here; a shader that
            // then fails to patch falls the whole mesh back to restore shapes). Those vertices take no restore shape,
            // so the app leaves them out of the size budget.
            // A renderer whose materials the avatar's animations swap stays on restore shapes: the material swapped in is
            // the avatar's own, not a patched copy, and would show the scrambled mesh.
            HashSet<string> swappedPaths = MaterialSwaps(root, descriptor, out HashSet<Material> swapMaterials);
            // PC only: Quest has no custom shaders, so an Android/iOS upload keeps the usual method whatever the setting.
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == BuildTarget.Android || target == BuildTarget.iOS;
            bool shaderMode = settings.shaderRestore && !mobile;
            if (settings.shaderRestore && mobile)
                result.warnings.Add("Closing meshes with the avatar's shader is PC only: this " + target + " upload keeps the usual method.");
            // PART BY PART (3.14): a sub-mesh is closed by the shader when every material that draws it, on every renderer
            // that wears the mesh, is a locked Poiyomi; the vertices only those sub-meshes use move by the shader, the rest
            // of the mesh keeps the restore shapes. Unity draws the LAST sub-mesh again with every material past the
            // sub-mesh count, so those materials count for it (bench, 2026-11-18: a chain's effect material on its one
            // sub-mesh showed the mesh displaced).
            var shaderChoice = new Dictionary<Mesh, ShaderChoice>();
            if (shaderMode)
                foreach (Mesh um in uniqueMeshes)
                {
                    ShaderChoice c = ChooseSubMeshes(um, skinned.Where(r => r.sharedMesh == um).ToList(), root, swappedPaths);
                    if (c != null) shaderChoice[um] = c;
                }
            var shaderEligible = new HashSet<Mesh>(shaderChoice.Keys);

            // 3.14, textures: the main texture of a material on a mesh the shader closes, when nothing else that ships
            // uses it (a texture also worn by an unprotected material ships readable anyway, so laying it out gains
            // nothing) and its size can take the layout.
            var texturePlanSource = (planSource ?? new AppPlanSource()) as ITexturePlanSource;
            var textureRequest = new List<KSAntiRipClient.TextureInfo>();
            var textureIdOf = new Dictionary<Texture2D, string>();
            if (shaderMode && shaderEligible.Count > 0 && texturePlanSource != null)
            {
                var willPatch = new HashSet<Material>();
                var shipsAsIs = new HashSet<Material>(swapMaterials);
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    ShaderChoice c = r is SkinnedMeshRenderer sk && sk.sharedMesh != null && shaderChoice.TryGetValue(sk.sharedMesh, out ShaderChoice cc) ? cc : null;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] != null) (c != null && c.PatchesSlot(i) ? willPatch : shipsAsIs).Add(mats[i]);
                }
                HashSet<Texture> usedElsewhere = TexturesOf(shipsAsIs);
                // 3.14.1: a main texture that ANY patched material also samples through a second slot its shader
                // declares (Layla's claws: _MatcapMask = the base colour) cannot be laid out: the restore code only
                // un-scrambles the main-texture read, so that second read would show the mosaic to honest viewers,
                // and the original would still ship in that slot. Gathered over every material first: two patched
                // materials can share one texture and only one of them read it twice.
                var liveDuplicate = new HashSet<Texture>();
                foreach (Material mm in willPatch)
                {
                    var t = mm.HasProperty("_MainTex") ? mm.GetTexture("_MainTex") : null;
                    if (t != null && LiveDuplicateSlot(mm, t)) liveDuplicate.Add(t);
                }
                if (liveDuplicate.Count > 0)
                    result.warnings.Add(liveDuplicate.Count + " texture(s) left readable: a material also samples them through a second slot.");
                foreach (Material mm in willPatch)
                {
                    if (!mm.HasProperty("_MainTex")) continue;
                    var t = mm.GetTexture("_MainTex") as Texture2D;
                    if (t == null || textureIdOf.ContainsKey(t) || usedElsewhere.Contains(t) || liveDuplicate.Contains(t)) continue;
                    if (!KSAntiRipShaderPatch.CanLayOut(t, 32)) continue;
                    string tid = "t" + textureIdOf.Count;
                    textureIdOf[t] = tid;
                    textureRequest.Add(new KSAntiRipClient.TextureInfo { id = tid, width = t.width, height = t.height });
                }
            }

            var request = uniqueMeshes
                .Select((m, i) => new KSAntiRipClient.MeshInfo
                {
                    id = "m" + i,
                    vertexCount = m.vertexCount,
                    shader = shaderEligible.Contains(m),
                    shaderVertices = shaderChoice.TryGetValue(m, out ShaderChoice sc) ? sc.Count : 0,
                })
                .ToList();

            EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Asking Kawaii Studio…", 0.1f);
            KSAntiRipClient.Plan plan;
            // The SDK's build copy is named "<avatar>(Clone)": the app's log and the key's record show the avatar's name.
            string avatarName = root.name.EndsWith("(Clone)", StringComparison.Ordinal)
                ? root.name.Substring(0, root.name.Length - "(Clone)".Length).TrimEnd()
                : root.name;
            try
            {
                plan = textureRequest.Count > 0 && texturePlanSource != null
                    ? texturePlanSource.Begin(blueprint, avatarName, request, textureRequest, settings.strength, settings.restoreGroups, settings.maxAddedMegabytes)
                    : planSource.Begin(blueprint, avatarName, request, settings.strength, settings.restoreGroups, settings.maxAddedMegabytes);
            }
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

                // 3.14, the shader method's workspace (empty unless the app answered with a restore function).
                bool useShader = shaderMode && plan.HasShader;
                var patchedShaders = new Dictionary<string, KSAntiRipShaderPatch.PatchResult>(StringComparer.Ordinal);
                var patchedMats = new Dictionary<Material, Material>();
                int shaderIndex = 0;
                int shaderMeshes = 0, shaderVerts = 0;
                TextureWork textures = useShader && plan.HasTextures && texturePlanSource != null
                    ? new TextureWork { Source = texturePlanSource, Plan = plan, Folder = folder, Warnings = result.warnings, IdOf = textureIdOf }
                    : null;

                // Renderers, not paths: the hierarchy is renamed below, so paths are resolved afterwards.
                // byShader: some of its mesh is closed by the shader; mixed: and the rest by restore shapes
                var protectedRenderers = new List<(SkinnedMeshRenderer smr, KSAntiRipClient.MeshPlan plan, bool byShader, bool mixed)>();
                var appliedShape = new Dictionary<Mesh, KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>>();
                var appliedShader = new Dictionary<Mesh, ShaderMesh>();
                var plannedMesh = new Dictionary<Mesh, KSAntiRipClient.MeshPlan>();

                for (int i = 0; i < rootSkinned.Length; i++)
                {
                    var smr = rootSkinned[i];
                    Mesh original = smr.sharedMesh;

                    // one plan per distinct mesh
                    if (!plannedMesh.TryGetValue(original, out KSAntiRipClient.MeshPlan meshPlan))
                    {
                        int index = uniqueMeshes.FindIndex(m => m.vertexCount == original.vertexCount && m.name == original.name);
                        if (index < 0) index = uniqueMeshes.Count > 0 ? 0 : -1;
                        if (index < 0) continue;

                        EditorUtility.DisplayProgressBar("KS ANTI-RIP",
                            $"Protecting mesh {plannedMesh.Count + 1}/{uniqueMeshes.Count}…",
                            0.2f + 0.3f * plannedMesh.Count / Math.Max(1, uniqueMeshes.Count));

                        meshPlan = planSource.Mesh(plan.session, request[index].id);
                        if (meshPlan == null) { result.message = planSource.LastError; return; }
                        plannedMesh[original] = meshPlan;

                        // Shader first, for the sub-meshes it may take and when all their shaders patch; otherwise (and
                        // for the rest of the mesh) restore shapes.
                        ShaderMesh sm = null;
                        if (useShader && shaderChoice.TryGetValue(original, out ShaderChoice choice))
                        {
                            var users = rootSkinned.Where(r => r.sharedMesh == original).ToList();
                            sm = TryShaderClose(original, users, meshPlan, plan, settings, folder,
                                                patchedShaders, patchedMats, ref shaderIndex, result.warnings, textures, choice);
                        }
                        if (sm != null)
                        {
                            AssetDatabase.CreateAsset(sm.Scrambled, folder + "/MSH_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset");
                            appliedShader[original] = sm;
                            result.meshesProtected++;
                            result.verticesProtected += sm.Scrambled.vertexCount;
                            shaderMeshes++;
                            shaderVerts += sm.ShaderVertices;
                        }
                        else
                        {
                            Mesh scrambled = KSAntiRipMesh.Apply(original, meshPlan.offsets, meshPlan.groups, meshPlan.shapeNames);
                            AssetDatabase.CreateAsset(scrambled, folder + "/MSH_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset");
                            appliedShape[original] = new KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>(scrambled, meshPlan);
                            result.meshesProtected++;
                            result.verticesProtected += scrambled.vertexCount;
                        }
                    }

                    if (appliedShader.TryGetValue(original, out ShaderMesh smk))
                    {
                        smr.sharedMesh = smk.Scrambled;
                        smr.sharedMaterials = smk.Materials[smr];
                        protectedRenderers.Add((smr, meshPlan, true, smk.Mixed));
                    }
                    else if (appliedShape.TryGetValue(original, out var done))
                    {
                        smr.sharedMesh = done.Key;
                        protectedRenderers.Add((smr, done.Value, false, false));
                    }
                }
                if (shaderMeshes > 0)
                    result.warnings.Add(shaderMeshes + " mesh(es) are closed by the avatar's shader (" + shaderVerts.ToString("N0") +
                                        " vertices); whoever blocks the shaders sees the Kawaii Studio poster instead.");
                if (textures != null) textures.Finish(root, patchedMats, swapMaterials);

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
                var materialTargets = new List<KSAntiRipLock.MaterialTarget>();
                var hidePaths = new List<string>();
                foreach (var entry in protectedRenderers)
                {
                    string path = AnimationUtility.CalculateTransformPath(entry.smr.transform, root.transform);
                    if (entry.byShader)
                    {
                        // The shader reads key property k; the lock drives it from key parameter k, the same way it
                        // drives restore shape k -- so the mesh closes only at the key's values.
                        for (int k = 0; k < plan.shaderProperties.Count; k++)
                            materialTargets.Add(new KSAntiRipLock.MaterialTarget { path = path, property = plan.shaderProperties[k], slot = k });
                    }
                    if (!entry.byShader || entry.mixed)
                    {
                        // Shape k of every mesh follows key parameter k: one key for the whole avatar.
                        for (int k = 0; k < entry.plan.shapeNames.Count; k++)
                            targets.Add(new KSAntiRipLock.ShapeTarget { path = path, shapeName = entry.plan.shapeNames[k], slot = k });
                    }
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

                // 3.14: whoever blocks the wearer's shaders does not see the meshes the shader closes; they get the
                // poster instead (owner, 2026-11-18). Added before the lock, so its show layer handles it like the rest.
                if (shaderMeshes > 0)
                {
                    string posterName = KSAntiRipBadge.FallbackObjectName, posterAssets = "KS_Fallback";
                    if (settings.obfuscateNames)
                    {
                        const string letters = "abcdefghijklmnopqrstuvwxyz";
                        var chars = new char[10];
                        for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
                        posterName = new string(chars);
                        for (int i = 0; i < chars.Length; i++) chars[i] = letters[rng.Next(letters.Length)];
                        posterAssets = new string(chars);
                    }
                    result.fallbackPosterPath = KSAntiRipBadge.AddFallbackPoster(root, descriptor, folder, posterName, result.warnings, posterAssets) ?? "";
                }

                AnimatorController fx = PrepareFxController(descriptor, folder, result);
                int layersBefore = fx.layers.Length;
                KSAntiRipLock.Build(root, descriptor, fx, targets, hidePaths, plan.parameters,
                                    settings.syncKey, folder, rng, settings.obfuscateNames, settings.freezeWhileLocked,
                                    badgePath, hideEverything: settings.hideWhileLocked, notes: result.warnings,
                                    materialTargets: materialTargets);
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

        /// <summary>
        /// One mesh closed by the shader: the scrambled copy, the materials for each renderer (patched copies on the
        /// slots the shader takes, the avatar's own on the others), and whether the rest of the mesh carries restore
        /// shapes too.
        /// </summary>
        sealed class ShaderMesh
        {
            public Mesh Scrambled;
            public bool Mixed;
            public int ShaderVertices;
            public readonly Dictionary<SkinnedMeshRenderer, Material[]> Materials = new Dictionary<SkinnedMeshRenderer, Material[]>();
        }

        /// <summary>3.14: which sub-meshes of a mesh the shader takes, and which of its vertices it moves.</summary>
        sealed class ShaderChoice
        {
            public HashSet<int> Subs = new HashSet<int>();
            public bool[] Move;          // per vertex: moved by the shader (used only by sub-meshes the shader takes)
            public int Count;            // how many
            public bool Whole;           // no drawn vertex is left to restore shapes
            public int SubMeshCount;
            /// <summary>Whether material slot <paramref name="slot"/> draws a sub-mesh the shader takes (the slots past the
            /// sub-mesh count draw the last sub-mesh again).</summary>
            public bool PatchesSlot(int slot) => SubMeshCount > 0 && Subs.Contains(Math.Min(slot, SubMeshCount - 1));
        }

        /// <summary>
        /// The sub-meshes of <paramref name="mesh"/> the shader may close: every material that draws one, on every renderer
        /// that wears the mesh, is a locked Poiyomi; and the vertices only those sub-meshes use. Null when there are none,
        /// when an animation swaps the materials of one of its renderers, or when the mesh has no normals and tangents.
        /// </summary>
        static ShaderChoice ChooseSubMeshes(Mesh mesh, List<SkinnedMeshRenderer> users, GameObject root, HashSet<string> swappedPaths)
        {
            if (mesh == null || users.Count == 0) return null;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal) ||
                !mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) return null;
            foreach (var u in users)
                if (swappedPaths.Contains(AnimationUtility.CalculateTransformPath(u.transform, root.transform))) return null;

            int subCount = mesh.subMeshCount;
            var choice = new ChoiceBuilder();
            for (int sub = 0; sub < subCount; sub++)
            {
                bool ok = true, drawn = false;
                foreach (var u in users)
                {
                    Material[] mats = u.sharedMaterials;
                    for (int i = 0; i < mats.Length && ok; i++)
                    {
                        if (Math.Min(i, subCount - 1) != sub) continue;
                        drawn = true;
                        Material m = mats[i];
                        if (m == null || m.shader == null || !KSAntiRipShaderPatch.IsLockedPoiyomi(m.shader)) ok = false;
                    }
                    if (!ok) break;
                }
                if (ok && drawn) choice.Subs.Add(sub);
            }
            if (choice.Subs.Count == 0) return null;

            int n = mesh.vertexCount;
            var inTaken = new bool[n];
            var inOther = new bool[n];
            for (int s = 0; s < subCount; s++)
            {
                bool[] mark = choice.Subs.Contains(s) ? inTaken : inOther;
                foreach (int i in mesh.GetIndices(s)) mark[i] = true;
            }
            var move = new bool[n];
            int count = 0;
            bool other = false;
            for (int i = 0; i < n; i++)
            {
                if (inOther[i]) other = true;
                // a vertex a sub-mesh on another shader also draws stays put: that shader would show it displaced
                if (inTaken[i] && !inOther[i]) { move[i] = true; count++; }
            }
            if (count == 0) return null;
            return new ShaderChoice { Subs = choice.Subs, Move = move, Count = count, Whole = !other, SubMeshCount = subCount };
        }

        sealed class ChoiceBuilder { public readonly HashSet<int> Subs = new HashSet<int>(); }

        /// <summary>
        /// Tries to close <paramref name="original"/> through the shaders of the renderers that wear it, for the
        /// sub-meshes <paramref name="choice"/> names: patch every distinct shader those slots use, build the scrambled
        /// mesh (the rest of the mesh, if any, with its restore shapes), and make a patched material per original on those
        /// slots. Returns null to fall back to restore shapes for the whole mesh -- if any shader will not patch, nothing
        /// is changed.
        /// </summary>
        static ShaderMesh TryShaderClose(Mesh original, List<SkinnedMeshRenderer> users, KSAntiRipClient.MeshPlan meshPlan,
                                         KSAntiRipClient.Plan plan, Settings settings, string folder,
                                         Dictionary<string, KSAntiRipShaderPatch.PatchResult> patchedShaders,
                                         Dictionary<Material, Material> patchedMats, ref int shaderIndex, List<string> warnings,
                                         TextureWork textures = null, ShaderChoice choice = null)
        {
            if (choice == null) return null;
            if (meshPlan.shaderIds.Length != original.vertexCount || meshPlan.shaderAmounts.Length != original.vertexCount)
                return null;   // the app did not send usable shader numbers for this mesh

            // Patch every distinct shader first; one failure drops the whole mesh to the shape path, with no change made.
            foreach (var smr in users)
            {
                Material[] slots = smr.sharedMaterials;
                for (int si = 0; si < slots.Length; si++)
                {
                    if (!choice.PatchesSlot(si)) continue;
                    Material mm = slots[si];
                    if (mm == null || mm.shader == null) return null;
                    string path = AssetDatabase.GetAssetPath(mm.shader);
                    if (string.IsNullOrEmpty(path)) return null;
                    if (!patchedShaders.TryGetValue(path, out KSAntiRipShaderPatch.PatchResult pr))
                    {
                        pr = textures != null
                            ? KSAntiRipShaderPatch.PatchShader(path, plan.shaderCode, plan.shaderCall, plan.shaderIdField,
                                                               plan.shaderProperties, shaderIndex++, folder,
                                                               plan.textureCode, plan.textureFn, plan.textureTidProperty)
                            : KSAntiRipShaderPatch.PatchShader(path, plan.shaderCode, plan.shaderCall, plan.shaderIdField,
                                                               plan.shaderProperties, shaderIndex++, folder);
                        patchedShaders[path] = pr;
                        if (pr.Shader == null && warnings != null)
                            warnings.Add("A mesh kept the blend-shape method: its shader could not be patched (" + pr.Why + ").");
                        else if (textures != null && !pr.TextureOk && warnings != null && !string.IsNullOrEmpty(pr.TextureWhy))
                            warnings.Add("A shader kept its texture readable: " + pr.TextureWhy + ".");
                    }
                    if (pr.Shader == null) return null;
                }
            }

            // The rest of the mesh, when there is one, keeps restore shapes: what the shader takes is zeroed in the
            // offsets and in every shape, so no vertex is moved by both, and the shapes close the others exactly as before.
            Mesh source = original;
            if (!choice.Whole)
            {
                var offsets = (Vector3[])meshPlan.offsets.Clone();
                var groups = meshPlan.groups.Select(g => (Vector3[])g.Clone()).ToList();
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (!choice.Move[i]) continue;
                    offsets[i] = Vector3.zero;
                    foreach (Vector3[] g in groups) g[i] = Vector3.zero;
                }
                source = KSAntiRipMesh.Apply(original, offsets, groups, meshPlan.shapeNames);
            }
            Mesh scrambled = KSAntiRipShaderPatch.BuildScrambledMesh(source, choice.Move, meshPlan.shaderIds, meshPlan.shaderAmounts,
                                                                     settings.strength, out int moved);
            if (source != original) UnityEngine.Object.DestroyImmediate(source);

            var result = new ShaderMesh { Scrambled = scrambled, Mixed = !choice.Whole, ShaderVertices = moved };
            foreach (var smr in users)
            {
                Material[] mats = smr.sharedMaterials;
                var copy = new Material[mats.Length];
                for (int j = 0; j < mats.Length; j++)
                {
                    Material om = mats[j];
                    // a slot on a sub-mesh the shader does not take keeps the avatar's own material
                    if (om == null || !choice.PatchesSlot(j)) { copy[j] = om; continue; }
                    if (!patchedMats.TryGetValue(om, out Material pm))
                    {
                        pm = new Material(om)
                        {
                            shader = patchedShaders[AssetDatabase.GetAssetPath(om.shader)].Shader,
                            name = om.name + "_ks",
                        };
                        pm.renderQueue = om.renderQueue;   // a shader swap resets the queue; the draw order must not change
                        // Poiyomi also writes VRCFallback as a MATERIAL override tag, which the copy inherits and which
                        // wins over the shader's own tag: say Hidden here too, or a viewer who blocks shaders gets the
                        // scrambled mesh on a Standard fallback (bench, 2026-11-18: 2 of 6 materials kept "Standard").
                        pm.SetOverrideTag("VRCFallback", "Hidden");
                        // 3.14: the laid-out main texture, and the id the texture function needs; 0 = read plainly
                        KSAntiRipShaderPatch.PatchResult spr = patchedShaders[AssetDatabase.GetAssetPath(om.shader)];
                        if (textures != null && spr.TextureOk) textures.Apply(pm, om);
                        AssetDatabase.CreateAsset(pm, folder + "/MAT_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mat");
                        patchedMats[om] = pm;
                    }
                    copy[j] = pm;
                }
                result.Materials[smr] = copy;
            }
            return result;
        }

        /// <summary>
        /// 3.14: the textures of one protection run. Laid out lazily, the first time a patched material wears one, from
        /// the layout the app sends for it; checked once more at the end (Finish), because a mesh that fell back to
        /// restore shapes keeps its original material, and with it the original texture, which then ships anyway.
        /// </summary>
        sealed class TextureWork
        {
            public ITexturePlanSource Source;
            public KSAntiRipClient.Plan Plan;
            public string Folder;
            public List<string> Warnings;
            public Dictionary<Texture2D, string> IdOf = new Dictionary<Texture2D, string>();
            readonly Dictionary<Texture2D, KeyValuePair<Texture2D, uint>> _built = new Dictionary<Texture2D, KeyValuePair<Texture2D, uint>>();
            readonly HashSet<Texture2D> _failed = new HashSet<Texture2D>();
            readonly Dictionary<Texture2D, List<Material>> _users = new Dictionary<Texture2D, List<Material>>();

            /// <summary>Gives <paramref name="patched"/> the laid-out copy of <paramref name="original"/>'s main texture
            /// and its id, or id 0 (read plainly) when that texture is not laid out.</summary>
            public void Apply(Material patched, Material original)
            {
                float tid = 0f;
                var t = original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") as Texture2D : null;
                if (t != null && Build(t, out Texture2D laid, out uint id))
                {
                    patched.SetTexture("_MainTex", laid);
                    // The copy inherits every saved slot of the original, stale ones included. A stale slot
                    // (e.g. _BaseColorMap / _BaseMap from an earlier shader) still holds the ORIGINAL main texture
                    // and would ship it readable, defeating the layout. The current shader does not read these
                    // slots, so clearing them changes nothing on screen.
                    StripSavedRefs(patched, t, "_MainTex");
                    tid = id;
                    if (!_users.TryGetValue(t, out List<Material> list)) _users[t] = list = new List<Material>();
                    list.Add(patched);
                }
                if (patched.HasProperty(Plan.textureTidProperty)) patched.SetFloat(Plan.textureTidProperty, tid);
                EditorUtility.SetDirty(patched);
            }

            bool Build(Texture2D original, out Texture2D laid, out uint tid)
            {
                laid = null; tid = 0;
                if (_built.TryGetValue(original, out var done)) { laid = done.Key; tid = done.Value; return true; }
                if (_failed.Contains(original)) return false;
                if (!IdOf.TryGetValue(original, out string id) || !Plan.textureTids.TryGetValue(id, out uint expected))
                { _failed.Add(original); return false; }
                KSAntiRipClient.TexturePlan layout = Source.Texture(Plan.session, id);
                if (layout == null || layout.tid != expected)
                {
                    _failed.Add(original);
                    Warnings?.Add("A texture was left readable: Kawaii Studio did not send its layout.");
                    return false;
                }
                laid = KSAntiRipShaderPatch.BuildScrambledTexture(original, layout.dest, layout.state, Plan.textureBlock, Folder, out string why);
                if (laid == null)
                {
                    _failed.Add(original);
                    Warnings?.Add("A texture was left readable: " + why + ".");
                    return false;
                }
                tid = layout.tid;
                _built[original] = new KeyValuePair<Texture2D, uint>(laid, tid);
                return true;
            }

            /// <summary>Puts back the original texture (id 0) wherever it would ship readable anyway, and says how many
            /// textures stay laid out.</summary>
            public void Finish(GameObject root, Dictionary<Material, Material> patchedMats, HashSet<Material> swapMaterials)
            {
                if (_built.Count == 0) return;
                var patched = new HashSet<Material>(patchedMats.Values);
                var shipped = new HashSet<Material>(swapMaterials);
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                    foreach (Material m in r.sharedMaterials)
                        if (m != null && !patched.Contains(m)) shipped.Add(m);
                HashSet<Texture> exposed = TexturesOf(shipped);
                // Safety net (3.14.1): a patched copy that still holds the original in another saved slot ships it
                // too, so the texture is not "laid out" in any useful sense -- put it back and say so.
                foreach (var kv in _users)
                    foreach (Material m in kv.Value)
                        foreach (var slot in SavedTextures(m))
                            if (slot.Key != "_MainTex" && slot.Value == kv.Key) exposed.Add(kv.Key);
                int reverted = 0;
                foreach (Texture2D t in _built.Keys.ToList())
                {
                    if (!exposed.Contains(t)) continue;
                    if (_users.TryGetValue(t, out List<Material> list))
                        foreach (Material m in list)
                        {
                            m.SetTexture("_MainTex", t);
                            if (m.HasProperty(Plan.textureTidProperty)) m.SetFloat(Plan.textureTidProperty, 0f);
                            EditorUtility.SetDirty(m);
                        }
                    _built.Remove(t);
                    reverted++;
                }
                if (reverted > 0)
                    Warnings?.Add(reverted + " texture(s) left readable: another part of the avatar, or another slot of the same material, ships them as they are.");
                if (_built.Count > 0)
                    Warnings?.Add(_built.Count + " texture(s) laid out from the key: a ripped copy shows them as a mosaic.");
            }
        }

        /// <summary>Every texture any of <paramref name="materials"/> carries, through ANY saved texture slot --
        /// including ones the current shader no longer declares (stale slots left behind by an earlier shader, e.g.
        /// _BaseColorMap / _BaseMap from a URP or lilToon past). Unity serialises every saved slot into the bundle
        /// whether the shader reads it or not, so a stale slot ships its texture readable all the same. Read straight
        /// from m_SavedProperties, so the answer does not depend on what the Material API chooses to list.</summary>
        static HashSet<Texture> TexturesOf(IEnumerable<Material> materials)
        {
            var set = new HashSet<Texture>();
            foreach (Material m in materials)
            {
                if (m == null) continue;
                foreach (var kv in SavedTextures(m))
                    if (kv.Value != null) set.Add(kv.Value);
            }
            return set;
        }

        /// <summary>Reads m_SavedProperties.m_TexEnvs: (slot name, texture) for every saved texture slot of a
        /// material, whether or not its current shader still declares the slot.</summary>
        static List<KeyValuePair<string, Texture>> SavedTextures(Material m)
        {
            var list = new List<KeyValuePair<string, Texture>>();
            var so = new SerializedObject(m);
            SerializedProperty envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            if (envs != null && envs.isArray)
                for (int i = 0; i < envs.arraySize; i++)
                {
                    SerializedProperty e = envs.GetArrayElementAtIndex(i);
                    string name = e.FindPropertyRelative("first")?.stringValue ?? "";
                    var tex = e.FindPropertyRelative("second.m_Texture")?.objectReferenceValue as Texture;
                    list.Add(new KeyValuePair<string, Texture>(name, tex));
                }
            so.Dispose();
            return list;
        }

        /// <summary>True if a texture property DECLARED BY THE SHADER of <paramref name="m"/>, other than _MainTex,
        /// carries the same texture as _MainTex -- a second read the restore code would not un-scramble. Walks the
        /// shader's own property list: Material.GetTexturePropertyNames() also returns stale saved slots (bench,
        /// 2026-11-18: an undeclared _BaseColorMap made it fire), and a stale slot is never read, so it is cleared
        /// instead (StripSavedRefs).</summary>
        static bool LiveDuplicateSlot(Material m, Texture mainTex)
        {
            Shader s = m.shader;
            if (s == null || mainTex == null) return false;
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                if (s.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string p = s.GetPropertyName(i);
                if (p != "_MainTex" && m.GetTexture(p) == mainTex) return true;
            }
            return false;
        }

        /// <summary>Nulls every STALE saved texture slot of <paramref name="m"/> (one its shader does not declare,
        /// so never read on screen) that still points at <paramref name="tex"/>, except the names in
        /// <paramref name="keep"/>. Called after the main texture has been laid out: the copy inherits the
        /// original's saved slots, and a stale one (Layla's face/body: _BaseColorMap, _BaseMap) would otherwise ship
        /// the original readable. A declared slot is left alone (LiveDuplicateSlot keeps such a texture readable,
        /// and Finish checks what remains). Returns how many slots were cleared.</summary>
        static int StripSavedRefs(Material m, Texture tex, params string[] keep)
        {
            var keepSet = new HashSet<string>(keep);
            if (m.shader != null)
                for (int i = 0; i < m.shader.GetPropertyCount(); i++) keepSet.Add(m.shader.GetPropertyName(i));
            var so = new SerializedObject(m);
            SerializedProperty envs = so.FindProperty("m_SavedProperties.m_TexEnvs");
            int cleared = 0;
            if (envs != null && envs.isArray)
                for (int i = 0; i < envs.arraySize; i++)
                {
                    SerializedProperty e = envs.GetArrayElementAtIndex(i);
                    string name = e.FindPropertyRelative("first")?.stringValue ?? "";
                    if (keepSet.Contains(name)) continue;
                    SerializedProperty texProp = e.FindPropertyRelative("second.m_Texture");
                    if (texProp != null && texProp.objectReferenceValue == (UnityEngine.Object)tex)
                    {
                        texProp.objectReferenceValue = null;
                        cleared++;
                    }
                }
            if (cleared > 0) so.ApplyModifiedPropertiesWithoutUndo();
            so.Dispose();
            return cleared;
        }

        /// <summary>
        /// The renderers (paths from the root) whose materials the avatar's own animations swap, and the materials
        /// swapped in: every object-reference curve on "m_Materials" in the playable layers and in the Animators below
        /// the root. At build time VRCFury and Modular Avatar have merged theirs into the playable layers already.
        /// </summary>
        static HashSet<string> MaterialSwaps(GameObject root, VRCAvatarDescriptor descriptor, out HashSet<Material> swapped)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            swapped = new HashSet<Material>();
            var found = swapped;
            void Scan(RuntimeAnimatorController c, string prefix)
            {
                if (c == null) return;
                foreach (AnimationClip clip in c.animationClips)
                {
                    if (clip == null) continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        if (!b.propertyName.StartsWith("m_Materials", StringComparison.Ordinal)) continue;
                        if (b.type == null || !typeof(Renderer).IsAssignableFrom(b.type)) continue;
                        string p = string.IsNullOrEmpty(prefix) ? b.path : string.IsNullOrEmpty(b.path) ? prefix : prefix + "/" + b.path;
                        paths.Add(p);
                        foreach (ObjectReferenceKeyframe k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                            if (k.value is Material m) found.Add(m);
                    }
                }
            }
            if (descriptor != null)
            {
                if (descriptor.baseAnimationLayers != null)
                    foreach (var l in descriptor.baseAnimationLayers) Scan(l.animatorController, "");
                if (descriptor.specialAnimationLayers != null)
                    foreach (var l in descriptor.specialAnimationLayers) Scan(l.animatorController, "");
            }
            foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
            {
                if (a.gameObject == root) { Scan(a.runtimeAnimatorController, ""); continue; }
                Scan(a.runtimeAnimatorController, AnimationUtility.CalculateTransformPath(a.transform, root.transform));
            }
            return paths;
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
