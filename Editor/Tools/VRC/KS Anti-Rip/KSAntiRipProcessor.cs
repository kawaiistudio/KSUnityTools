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
    /// in a finally block, and a regression test hashes every source file before and after. It also never
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

            /// <summary>3.17: how much the protection may add UNCOMPRESSED, in MB (VRChat's 500 MB limit); 0 = no cap.
            /// The app sets it from the room the last upload left when its size is on Auto.</summary>
            public int maxAddedUncompressedMegabytes = 0;

            /// <summary>
            /// 3.14: the meshes whose shader allows it are closed by that shader (KSAntiRipShader) instead of by
            /// restore shapes. Off by default. Whoever blocks the wearer's shaders is shown the poster instead.
            /// </summary>
            public bool shaderRestore = true;

            /// <summary>
            /// 3.16.2, opt-in: every blend shape gets a neutral name, and the descriptor and every animation follow
            /// (KSAntiRipNames.RenameBlendShapes). Off by default: MMD worlds drive blend shapes by their standard names.
            /// </summary>
            public bool renameBlendShapes = false;

            /// <summary>
            /// 3.16.2 (owner, 2026-11-19): a SYNCED key is lengthened too, with as many extra values as the avatar's free
            /// synced bits pay for (up to a 128-bit shader key). Stops someone who only has the avatar file; a modified
            /// client in the instance still reads synced values. On by default; a local key is always long.
            /// </summary>
            public bool longKey = true;

            /// <summary>
            /// 3.16.2 (owner, 2026-11-19): static MeshRenderers whose materials the shader method can patch (locked
            /// Poiyomi, lilToon) are closed by their shader too. A static mesh has no restore shapes, so the parts the
            /// shader cannot take stay as they are (the coverage report lists them). On by default.
            /// </summary>
            public bool protectStaticMeshes = true;

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
            public int blendShapesRenamed;   // 3.16.2, only with Settings.renameBlendShapes
            public string lockLayer = "";
            public string freezeLayer = "";      // empty when the avatar is not frozen while locked
            public string badgePath = "";        // the locked badge's path from the root; empty when there is none
            public string fallbackPosterPath = ""; // 3.14: the poster for viewers who block shaders; empty when none
            public List<string> warnings = new List<string>();
            /// <summary>3.16.2: what each part of the avatar got, and why (KSAntiRipCoverage).</summary>
            public KSAntiRipCoverage coverage = new KSAntiRipCoverage();
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
            /// <summary>3.16.1: the key is not synced, so the app lengthens the shader key (AntiRipEngine.LocalKey).</summary>
            public bool LocalKey;
            /// <summary>3.16.2: lengthen a synced key too, with at most <see cref="MaxExtraKeySlots"/> extra values.</summary>
            public bool LongKey;
            public int MaxExtraKeySlots = -1;
            public KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                              IList<KSAntiRipClient.TextureInfo> textures, float strength, int restoreGroups,
                                              int maxAddedMegabytes)
                => KSAntiRipClient.Begin(avatarId, avatarName, meshes, strength, restoreGroups, maxAddedMegabytes, textures, LocalKey,
                                         LongKey, MaxExtraKeySlots);
            public KSAntiRipClient.TexturePlan Texture(string session, string id) => KSAntiRipClient.Texture(session, id);
            public KSAntiRipClient.Plan Begin(string avatarId, string avatarName, IList<KSAntiRipClient.MeshInfo> meshes,
                                              float strength, int restoreGroups, int maxAddedMegabytes = 0)
                => KSAntiRipClient.Begin(avatarId, avatarName, meshes, strength, restoreGroups, maxAddedMegabytes, null, LocalKey,
                                         LongKey, MaxExtraKeySlots);
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
            result.ok = false;
            result.folder = folder;

            var descriptor = root.GetComponent<VRCAvatarDescriptor>();
            // 3.16.2: a synced long key may only spend the synced bits the avatar has left after its own parameters and
            // the restore key (8 bits a value), so the upload never goes over VRChat's limit.
            int maxExtraKeySlots = settings.syncKey && settings.longKey ? FreeKeySlots(descriptor, settings.restoreGroups) : -1;
            planSource = planSource ?? new AppPlanSource
            {
                LocalKey = !settings.syncKey,
                LongKey = settings.syncKey && settings.longKey && maxExtraKeySlots > 0,
                MaxExtraKeySlots = maxExtraKeySlots,
            };
            result.coverage.synced = settings.syncKey;
            result.coverage.longKeyAsked = settings.longKey;
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
            var statics = root.GetComponentsInChildren<MeshRenderer>(true)
                              .Where(r => StaticMeshOf(r) != null).ToArray();
            // 3.16.2, for the coverage report: the parts' own names (before names are obfuscated) and static meshes
            var nameOf = new Dictionary<Renderer, string>();
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                nameOf[r] = AnimationUtility.CalculateTransformPath(r.transform, root.transform);
            var staticOriginal = statics.ToDictionary(r => r, r => StaticMeshOf(r));
            if (skinned.Length == 0 && !(settings.protectStaticMeshes && settings.shaderRestore && statics.Length > 0))
            { result.message = "Nothing to protect: this avatar has no skinned meshes."; return; }

            var uniqueMeshes = new List<Mesh>();
            foreach (var smr in skinned)
                if (!uniqueMeshes.Contains(smr.sharedMesh)) uniqueMeshes.Add(smr.sharedMesh);

            // 3.14: a mesh can be closed by the avatar's OWN shader when every material that draws it, on every
            // renderer that wears it, is a locked-Poiyomi or lilToon shader this can patch (decided by name here; a shader that
            // then fails to patch falls the whole mesh back to restore shapes). Those vertices take no restore shape,
            // so the app leaves them out of the size budget.
            // A renderer whose materials the avatar's animations swap used to stay on restore shapes (the material swapped in
            // is the avatar's own, not a patched copy, and would show the scrambled mesh). 3.16.6: it is closed too when every
            // material that can land on a closed slot can be patched; RewireSwaps points those swaps at the patched copies.
            HashSet<string> swappedPaths = MaterialSwaps(root, descriptor, out HashSet<Material> swapMaterials,
                                                         out var swapSlots, out HashSet<string> swapsNotRewirable);
            // PC only: Quest has no custom shaders, so an Android/iOS upload keeps the usual method whatever the setting.
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool mobile = target == BuildTarget.Android || target == BuildTarget.iOS;
            bool shaderMode = settings.shaderRestore && !mobile;
            if (settings.shaderRestore && mobile)
                result.warnings.Add("Closing meshes with the avatar's shader is PC only: this " + target + " upload keeps the usual method.");
            // PART BY PART (3.14): a sub-mesh is closed by the shader when every material that draws it, on every renderer
            // that wears the mesh, is a locked Poiyomi or a lilToon; the vertices only those sub-meshes use move by the shader, the rest
            // of the mesh keeps the restore shapes. Unity draws the LAST sub-mesh again with every material past the
            // sub-mesh count, so those materials count for it (bench, 2026-11-18: a chain's effect material on its one
            // sub-mesh showed the mesh displaced).
            var shaderChoice = new Dictionary<Mesh, ShaderChoice>();
            if (shaderMode)
                foreach (Mesh um in uniqueMeshes)
                {
                    ShaderChoice c = ChooseSubMeshes(um, skinned.Where(r => r.sharedMesh == um).Cast<Renderer>().ToList(), root, swappedPaths, false,
                                                     swapSlots, swapsNotRewirable);
                    if (c != null) shaderChoice[um] = c;
                }
            var shaderEligible = new HashSet<Mesh>(shaderChoice.Keys);

            // 3.16.2: static MeshRenderers, by the shader only (they have no restore shapes). A mesh a skinned renderer
            // also wears is left to the skinned path.
            var uniqueStatic = new List<Mesh>();
            var staticChoice = new Dictionary<Mesh, ShaderChoice>();
            if (shaderMode && settings.protectStaticMeshes)
                foreach (MeshRenderer mr in statics)
                {
                    Mesh sm = StaticMeshOf(mr);
                    if (uniqueMeshes.Contains(sm) || uniqueStatic.Contains(sm)) continue;
                    ShaderChoice c = ChooseSubMeshes(sm, statics.Where(r => StaticMeshOf(r) == sm).Cast<Renderer>().ToList(), root, swappedPaths, true,
                                                     swapSlots, swapsNotRewirable);
                    if (c == null) continue;
                    uniqueStatic.Add(sm);
                    staticChoice[sm] = c;
                }

            // 3.16.4, THE SIZE BUDGET PAYS FOR EVERYTHING (owner 2026-11-19: the protected avatar came out 203.4 MB, over
            // VRChat's 200, and "Max size added" at 12 or 10 MB changed nothing). The app's engine spent the whole budget
            // on restore shapes, while a mesh closed by its shader -- which takes no restore shape -- was never counted,
            // yet ships with Mesh Compression Off: it has to, its normals and tangents must come back exact (the shader
            // undoes a move of metres along them; Low compression alone left it up to 17 cm off). Measured through real
            // bundles on the bench (KSSizeTest, KSMeshCompressionTest): that costs ~14 bytes per vertex over a compressed
            // source (Wendigo Skull, 620 595 vertices: +8.5 MB) and ~3 over one that was already Off; a restore-shape
            // mesh's base costs ~4 / ~1 more (it now ships Low, see KSAntiRipMesh). Those costs come out of the budget
            // first; while they leave the restore shapes less than a quarter of it, the shader mesh that costs the most
            // goes back to restore shapes (a static one, which has none, stays as it is); the engine gets what is left.
            int engineMegabytes = settings.maxAddedMegabytes;
            var overBudget = new HashSet<Mesh>();
            bool Compressed(Mesh m) => MeshUtility.GetMeshCompression(m) != ModelImporterMeshCompression.Off;
            // 3.16.5: with app v678+, a static mesh moves along public directions and keeps a compression (Low):
            // measured on the 620 595-vertex Wendigo Skull, ~5.4 bytes a vertex over its Medium source, not ~14
            bool axes = KSAntiRipClient.RunningAppVersion() >= KSAntiRipClient.AxesFromAppVersion;
            bool NoTangents(Mesh m) => !m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
            // 3.16.7: a skinned mesh without tangents ships some on its copy (~6 bytes a vertex after LZMA)
            long ShaderCost(Mesh m, bool isStatic) =>
                (long)m.vertexCount * ((!Compressed(m) ? 3 : isStatic && axes ? 6 : 14) + (!isStatic && NoTangents(m) ? 6 : 0));
            long RestoreCost(Mesh m) => (long)m.vertexCount * (Compressed(m) ? 4 : 1);
            // 3.17, the same UNCOMPRESSED (VRChat's 500 MB limit; the SDK reads it from Unity's build report). A copy that
            // ships Off holds every vertex in full -- its own attributes, plus the id (16 bytes in UV7) and any tangents it
            // got -- where a compressed source held them packed; a static mesh on public directions keeps its compression
            // (the id comes packed with it); restore shapes add a frame per group (40 bytes a vertex, counted dense).
            long Raw(Mesh m)
            {
                long stride = 0;
                for (int st = 0; st < m.vertexBufferCount; st++) stride += m.GetVertexBufferStride(st);
                return stride;
            }
            double Packed(Mesh m)
            {
                switch (MeshUtility.GetMeshCompression(m))
                {
                    case ModelImporterMeshCompression.Low: return 0.5;
                    case ModelImporterMeshCompression.Medium: return 0.4;
                    case ModelImporterMeshCompression.High: return 0.33;
                    default: return 1.0;
                }
            }
            long ShaderCostU(Mesh m, bool isStatic)
            {
                long n = m.vertexCount;
                // measured through real builds (bench 2026-11-20, Karin KS, 657 167 vertices, Medium source): +13.45 MB
                // uncompressed for the copy (Low, with the id's 4 floats kept unpacked), i.e. ~21 bytes a vertex
                if (isStatic && axes) return n * (Compressed(m) ? 21 : 16);
                long copy = Raw(m) + 16 + (!isStatic && NoTangents(m) ? 16 : 0);
                return Math.Max(0, n * copy - (long)(n * Raw(m) * Packed(m)));
            }
            long RestoreCostU(Mesh m) =>
                Math.Max(0, (long)m.vertexCount * Raw(m) - (long)(m.vertexCount * Raw(m) * Packed(m))) +
                (long)m.vertexCount * Math.Max(1, settings.restoreGroups) * 40;
            long Fixed() =>
                uniqueMeshes.Sum(m => shaderChoice.ContainsKey(m) ? ShaderCost(m, false) : RestoreCost(m)) +
                staticChoice.Keys.Sum(m => ShaderCost(m, true));
            long FixedU() =>
                uniqueMeshes.Sum(m => shaderChoice.ContainsKey(m) ? ShaderCostU(m, false) : RestoreCostU(m)) +
                staticChoice.Keys.Sum(m => ShaderCostU(m, true));
            if (settings.maxAddedMegabytes > 0 || settings.maxAddedUncompressedMegabytes > 0)
            {
                long cap = settings.maxAddedMegabytes > 0 ? settings.maxAddedMegabytes * 1048576L : long.MaxValue;
                long capU = settings.maxAddedUncompressedMegabytes > 0 ? settings.maxAddedUncompressedMegabytes * 1048576L : long.MaxValue;
                // a quarter of the cap is kept for the restore shapes -- only while some skinned mesh still takes them
                // (3.17: with every mesh closed by its shader, the whole budget closes meshes; owner 2026-11-20)
                bool OverC() => cap != long.MaxValue && cap - Fixed() < (uniqueMeshes.Any(m => !shaderChoice.ContainsKey(m)) ? cap / 4 : 0);
                bool OverU() => capU != long.MaxValue && FixedU() > capU;
                // what leaving a mesh out of the shader frees: a static mesh stays as it is (all of its cost), a skinned
                // one goes to restore shapes (its cost minus theirs, which can be MORE uncompressed: then not that one)
                long SavedC((Mesh mesh, bool isStatic) x) => ShaderCost(x.mesh, x.isStatic) - (x.isStatic ? 0 : RestoreCost(x.mesh));
                long SavedU((Mesh mesh, bool isStatic) x) => ShaderCostU(x.mesh, x.isStatic) - (x.isStatic ? 0 : RestoreCostU(x.mesh));
                while (OverC() || OverU())
                {
                    bool c = OverC(), u = OverU();
                    var pick = shaderChoice.Keys.Select(m => (mesh: m, isStatic: false))
                                   .Concat(staticChoice.Keys.Select(m => (mesh: m, isStatic: true)))
                                   .Where(x => (!c || SavedC(x) > 0) && (!u || SavedU(x) > 0))
                                   .OrderByDescending(x => (c ? SavedC(x) : 0) + (u ? SavedU(x) : 0))
                                   .FirstOrDefault();
                    if (pick.mesh == null) break;   // nothing left that frees what is over: it stays over, and says so
                    if (pick.isStatic) { staticChoice.Remove(pick.mesh); uniqueStatic.Remove(pick.mesh); }
                    else { shaderChoice.Remove(pick.mesh); shaderEligible.Remove(pick.mesh); }
                    overBudget.Add(pick.mesh);
                }
                if (cap != long.MaxValue)
                {
                    long left = cap - Fixed();
                    engineMegabytes = (int)Math.Max(1, left / 1048576L);
                }
                Debug.Log("[KS ANTI-RIP] size budget " + (cap == long.MaxValue ? "none" : settings.maxAddedMegabytes + " MB") +
                          (capU == long.MaxValue ? "" : " / " + settings.maxAddedUncompressedMegabytes + " MB uncompressed") +
                          ": the shader and the mesh copies take ~" + (Fixed() / 1048576.0).ToString("0.0") + " MB (~" +
                          (FixedU() / 1048576.0).ToString("0.0") + " MB uncompressed), the restore shapes get " + engineMegabytes + " MB" +
                          (overBudget.Count > 0 ? "; " + overBudget.Count + " mesh(es) left to restore shapes or as they are, to stay within it" : ""));
                if (overBudget.Count > 0)
                    result.warnings.Add(overBudget.Count + " mesh(es) not closed by their shader, to stay within \"Max size added\" (" +
                                        (cap == long.MaxValue ? "" : settings.maxAddedMegabytes + " MB") +
                                        (capU == long.MaxValue ? "" : (cap == long.MaxValue ? "" : ", ") + settings.maxAddedUncompressedMegabytes + " MB uncompressed") +
                                        "). Raise it if the avatar has room under VRChat's limits (200 MB, 500 MB uncompressed).");
                if (OverC() || OverU())
                    result.warnings.Add("The protection may still go over \"Max size added\": what is left cannot be made smaller.");
            }
            // 3.17: what this build adds, for the size report the app learns the avatar's own size from (KSAntiRipSizes):
            // the fixed costs, plus the restore shapes' share when some mesh keeps them
            bool anyRestore = uniqueMeshes.Any(m => !shaderChoice.ContainsKey(m));
            KSAntiRipSizes.Estimate(Fixed() + (anyRestore && settings.maxAddedMegabytes > 0 ? (long)engineMegabytes * 1048576L : 0), FixedU());

            // the choice for any renderer, skinned or static
            ShaderChoice ChoiceOf(Renderer r)
            {
                if (r is SkinnedMeshRenderer sk) return sk.sharedMesh != null && shaderChoice.TryGetValue(sk.sharedMesh, out ShaderChoice a) ? a : null;
                Mesh m = r is MeshRenderer mr2 ? StaticMeshOf(mr2) : null;
                return m != null && staticChoice.TryGetValue(m, out ShaderChoice b) ? b : null;
            }

            // 3.14, textures: the main texture of a material on a mesh the shader closes, when nothing else that ships
            // uses it (a texture also worn by an unprotected material ships readable anyway, so laying it out gains
            // nothing) and its size can take the layout.
            var texturePlanSource = planSource as ITexturePlanSource;
            var textureRequest = new List<KSAntiRipClient.TextureInfo>();
            var textureIdOf = new Dictionary<Texture2D, string>();
            var textureWhy = new Dictionary<Texture2D, string>();   // 3.16.2: why a main texture was not asked for
            if (shaderMode && (shaderEligible.Count > 0 || staticChoice.Count > 0) && texturePlanSource != null)
            {
                var willPatch = new HashSet<Material>();
                var shipsAsIs = new HashSet<Material>(swapMaterials);
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = r.sharedMaterials;
                    ShaderChoice c = ChoiceOf(r);
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
                    if (t == null || textureIdOf.ContainsKey(t)) continue;
                    if (usedElsewhere.Contains(t)) { textureWhy[t] = "another, unprotected part of the avatar uses it"; continue; }
                    if (liveDuplicate.Contains(t)) { textureWhy[t] = "its material also reads it through a second slot"; continue; }
                    if (!KSAntiRipShaderPatch.CanLayOut(t, 32))
                    { textureWhy[t] = "only a smoothly filtered, power-of-two texture of the Default type, 64 px or more, can be laid out"; continue; }
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
            // 3.16.2: the static meshes after the skinned ones; they take no restore shape, so none of their vertices
            // count in the size budget
            var staticRequest = uniqueStatic
                .Select((m, i) => new KSAntiRipClient.MeshInfo { id = "s" + i, vertexCount = m.vertexCount, shader = true, shaderVertices = m.vertexCount })
                .ToList();
            request.AddRange(staticRequest);

            EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Asking Kawaii Studio…", 0.1f);
            KSAntiRipClient.Plan plan;
            // The SDK's build copy is named "<avatar>(Clone)": the app's log and the key's record show the avatar's name.
            string avatarName = root.name.EndsWith("(Clone)", StringComparison.Ordinal)
                ? root.name.Substring(0, root.name.Length - "(Clone)".Length).TrimEnd()
                : root.name;
            try
            {
                // 3.16.4: what the budget leaves for the restore shapes once the shader and the mesh copies are paid for
                plan = textureRequest.Count > 0 && texturePlanSource != null
                    ? texturePlanSource.Begin(blueprint, avatarName, request, textureRequest, settings.strength, settings.restoreGroups, engineMegabytes)
                    : planSource.Begin(blueprint, avatarName, request, settings.strength, settings.restoreGroups, engineMegabytes);
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
                var protectedRenderers = new List<(Renderer smr, KSAntiRipClient.MeshPlan plan, bool byShader, bool mixed)>();
                var appliedShape = new Dictionary<Mesh, KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>>();
                var appliedShader = new Dictionary<Mesh, ShaderMesh>();
                var plannedMesh = new Dictionary<Mesh, KSAntiRipClient.MeshPlan>();
                // 3.16.6: what each shader-closed renderer wore before, so one whose swaps cannot be rewired goes back
                Func<Renderer, int, IEnumerable<Material>> swapsOf =
                    (r, si) => SwappedInto(swapSlots, AnimationUtility.CalculateTransformPath(r.transform, root.transform), si);
                var closedChoice = new Dictionary<Renderer, ShaderChoice>();
                var closedOriginal = new Dictionary<Renderer, (Mesh mesh, Material[] mats, bool isStatic)>();

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
                            var users = rootSkinned.Where(r => r.sharedMesh == original).Cast<Renderer>().ToList();
                            sm = TryShaderClose(original, users, meshPlan, plan, settings, folder,
                                                patchedShaders, patchedMats, ref shaderIndex, result.warnings, textures, choice, false, swapsOf);
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
                        closedOriginal[smr] = (original, smr.sharedMaterials, false);
                        closedChoice[smr] = shaderChoice[original];
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

                // 3.16.2: the static meshes, by their shader only. One that cannot be closed is left exactly as it was.
                var appliedStatic = new Dictionary<Mesh, ShaderMesh>();
                var refusedStatic = new HashSet<Mesh>();
                int staticClosed = 0;
                if (useShader)
                    foreach (MeshRenderer mr in statics)
                    {
                        if (mr == null) continue;
                        Mesh original = StaticMeshOf(mr);
                        int index = original != null ? uniqueStatic.IndexOf(original) : -1;
                        if (index < 0 || refusedStatic.Contains(original)) continue;
                        if (!appliedStatic.TryGetValue(original, out ShaderMesh sm))
                        {
                            KSAntiRipClient.MeshPlan meshPlan = planSource.Mesh(plan.session, staticRequest[index].id);
                            if (meshPlan == null) { result.message = planSource.LastError; return; }
                            var users = statics.Where(r => r != null && StaticMeshOf(r) == original).Cast<Renderer>().ToList();
                            sm = TryShaderClose(original, users, meshPlan, plan, settings, folder,
                                                patchedShaders, patchedMats, ref shaderIndex, result.warnings, textures,
                                                staticChoice[original], true, swapsOf);
                            if (sm == null) { refusedStatic.Add(original); continue; }
                            AssetDatabase.CreateAsset(sm.Scrambled, folder + "/MSH_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset");
                            appliedStatic[original] = sm;
                            plannedMesh[original] = meshPlan;
                            result.meshesProtected++;
                            result.verticesProtected += sm.Scrambled.vertexCount;
                            shaderMeshes++;
                            shaderVerts += sm.ShaderVertices;
                        }
                        closedOriginal[mr] = (original, mr.sharedMaterials, true);
                        closedChoice[mr] = staticChoice[original];
                        mr.GetComponent<MeshFilter>().sharedMesh = sm.Scrambled;
                        mr.sharedMaterials = sm.Materials[mr];
                        protectedRenderers.Add((mr, plannedMesh[original], true, false));
                        staticClosed++;
                    }

                // 3.16.6: the material swaps on closed renderers now put in the patched copies (copied controllers and
                // clips, never the user's). Where that cannot be done, the renderer goes back -- a skinned one to restore
                // shapes, a static one to what it was -- BEFORE the report and the lock read the list.
                var reverted = new HashSet<Renderer>();
                if (closedChoice.Count > 0)
                    foreach (Renderer back in RewireSwaps(root, descriptor, folder, closedChoice, patchedMats, result.warnings))
                    {
                        if (!closedOriginal.TryGetValue(back, out var o)) continue;
                        reverted.Add(back);
                        int at = protectedRenderers.FindIndex(e => e.smr == back);
                        if (o.isStatic)
                        {
                            back.GetComponent<MeshFilter>().sharedMesh = o.mesh;
                            back.sharedMaterials = o.mats;
                            if (at >= 0) protectedRenderers.RemoveAt(at);
                            staticClosed--;
                            continue;
                        }
                        KSAntiRipClient.MeshPlan mp = plannedMesh[o.mesh];
                        if (!appliedShape.TryGetValue(o.mesh, out var shaped))
                        {
                            Mesh scrambledBack = KSAntiRipMesh.Apply(o.mesh, mp.offsets, mp.groups, mp.shapeNames);
                            AssetDatabase.CreateAsset(scrambledBack, folder + "/MSH_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".asset");
                            appliedShape[o.mesh] = shaped = new KeyValuePair<Mesh, KSAntiRipClient.MeshPlan>(scrambledBack, mp);
                        }
                        ((SkinnedMeshRenderer)back).sharedMesh = shaped.Key;
                        back.sharedMaterials = o.mats;
                        if (at >= 0) protectedRenderers[at] = (back, mp, false, false);
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

                // Static MeshRenderers cannot carry blend shapes; since 3.16.2 the shader closes those whose materials it
                // can patch. Say out loud how many are left as they are.
                int staticsLeft = statics.Length - staticClosed;
                if (staticsLeft > 0)
                    result.warnings.Add(staticsLeft + " static mesh(es) were left unprotected" +
                                        (staticClosed > 0 ? " (" + staticClosed + " closed by their shader)" : "") +
                                        ": their shader has no source in the project, or could not be patched (the report says which).");

                // 3.16.2: the coverage report, while the renderers still carry what they were given (names come from nameOf)
                string WhyNotShader(Renderer r, Mesh m, bool isStatic)
                {
                    if (!settings.shaderRestore) return "closing meshes with their shader is off";
                    if (m != null && overBudget.Contains(m)) return "left out to stay within \"Max size added\" (" + settings.maxAddedMegabytes + " MB)";
                    if (mobile) return "a Quest/mobile build has no custom shaders";
                    if (isStatic && !settings.protectStaticMeshes) return "closing static meshes with their shader is off";
                    if (nameOf.TryGetValue(r, out string p) && swappedPaths.Contains(p))
                    {
                        // 3.16.6: a swap no longer blocks by itself; say which one did
                        if (reverted.Contains(r)) return "an animation swaps its materials, and the swap could not be pointed at the protected material";
                        if (swapsNotRewirable.Contains(p)) return "an override controller swaps its materials";
                        var badSwap = swapSlots.TryGetValue(p, out var ps)
                            ? ps.Values.SelectMany(s => s).Where(x => x != null && x.shader != null &&
                                                                      !(KSAntiRipShaderPatch.IsLockedPoiyomi(x.shader) || KSAntiRipLilToon.CanPatch(x) ||
                                                                        KSAntiRipShaderUniversal.CanTry(x.shader)))
                                .Select(x => "'" + x.name + "' (" + x.shader.name + ")").Distinct().ToList()
                            : new List<string>();
                        if (badSwap.Count > 0)
                            return "an animation swaps in material " + string.Join(", ", badSwap.Take(3)) +
                                   (badSwap.Count > 3 ? " and " + (badSwap.Count - 3) + " more" : "") + ", whose shader has no source in the project";
                    }
                    if (m == null || !m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) return "its mesh has no normals";
                    var bad = r.sharedMaterials.Where(x => x != null && x.shader != null &&
                                                           !(KSAntiRipShaderPatch.IsLockedPoiyomi(x.shader) || KSAntiRipLilToon.CanPatch(x) ||
                                                             KSAntiRipShaderUniversal.CanTry(x.shader) ||
                                                             x.shader.name.StartsWith("Hidden/Locked/KS/", StringComparison.Ordinal)))
                                               .Select(x => "'" + x.name + "' (" + x.shader.name + ")").Distinct().ToList();
                    if (bad.Count > 0)
                        return "material " + string.Join(", ", bad.Take(3)) + (bad.Count > 3 ? " and " + (bad.Count - 3) + " more" : "") +
                               " has no shader source in the project (built in, or an unlocked Poiyomi: lock it first)";
                    foreach (Material x in r.sharedMaterials)
                    {
                        if (x == null || x.shader == null) continue;
                        if (patchedShaders.TryGetValue(AssetDatabase.GetAssetPath(x.shader), out KSAntiRipShaderPatch.PatchResult pr) && pr.Shader == null)
                            return "its shader could not be patched (" + pr.Why + ")";
                    }
                    if (!m.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) && !(plan != null && plan.shaderTangentless))
                        return "its mesh has no tangents (Kawaii Studio v" + KSAntiRipClient.TangentlessFromAppVersion + " or newer closes it anyway)";
                    return "its vertices are shared with a part on another material";
                }
                var entryOf = new Dictionary<Renderer, (Renderer smr, KSAntiRipClient.MeshPlan plan, bool byShader, bool mixed)>();
                foreach (var e in protectedRenderers) if (!entryOf.ContainsKey(e.smr)) entryOf[e.smr] = e;
                foreach (SkinnedMeshRenderer r in skinned)
                {
                    if (r == null) continue;
                    string name = nameOf.TryGetValue(r, out string n) ? n : r.name;
                    int vc = r.sharedMesh != null ? r.sharedMesh.vertexCount : 0;
                    if (!entryOf.TryGetValue(r, out var e)) { result.coverage.Add(name, "left", false, vc, "no plan matched this mesh"); continue; }
                    if (e.byShader && !e.mixed) result.coverage.Add(name, "shader", false, vc);
                    else if (e.byShader) result.coverage.Add(name, "shader+shapes", false, vc, "the rest: " + WhyNotShader(r, r.sharedMesh, false));
                    else result.coverage.Add(name, "shapes", false, vc, WhyNotShader(r, r.sharedMesh, false));
                }
                foreach (MeshRenderer r in statics)
                {
                    if (r == null) continue;
                    string name = nameOf.TryGetValue(r, out string n) ? n : r.name;
                    Mesh orig = staticOriginal.TryGetValue(r, out Mesh om) ? om : StaticMeshOf(r);
                    int vc = orig != null ? orig.vertexCount : 0;
                    if (entryOf.ContainsKey(r))
                    {
                        bool whole = orig != null && staticChoice.TryGetValue(orig, out ShaderChoice c) && c.Whole;
                        if (whole) result.coverage.Add(name, "shader", true, vc);
                        else result.coverage.Add(name, "shader+left", true, vc, "the parts on other materials stay as they are: " + WhyNotShader(r, orig, true));
                    }
                    else result.coverage.Add(name, "left", true, vc, WhyNotShader(r, orig, true));
                }
                foreach (Texture2D t in textureIdOf.Keys.Concat(textureWhy.Keys).Distinct())
                {
                    bool laid = textures != null && textures.IsLaidOut(t);
                    string why = laid ? "" :
                                 textureWhy.TryGetValue(t, out string w1) ? w1 :
                                 textures != null && textures.WhyNot.TryGetValue(t, out string w2) ? w2 :
                                 "the shader that draws it could not read it back, or its mesh kept the usual method";
                    result.coverage.AddTexture(t.name, laid, why);
                }
                result.coverage.keyValues = plan.parameters.Count;
                result.coverage.shaderKeyBits = plan.shaderKeyBits;

                EditorUtility.DisplayProgressBar("KS ANTI-RIP", "Hiding the signposts…", 0.6f);

                // Renaming copies and rewrites every animator the descriptor uses, so it runs BEFORE the lock
                // is built -- otherwise the lock's own layer would be copied again and renamed away.
                if (settings.obfuscateNames)
                    result.objectsRenamed = KSAntiRipNames.Obfuscate(root, descriptor, folder, tag, rng, result.warnings);

                // 3.16.2, opt-in: blend shape names too. After the object rename (paths are resolved on the final
                // names) and before the lock, which addresses the restore shapes by name -- those are kept.
                if (settings.renameBlendShapes)
                {
                    var restoreShapes = new HashSet<string>(protectedRenderers.SelectMany(e => e.plan.shapeNames), StringComparer.Ordinal);
                    result.blendShapesRenamed = KSAntiRipNames.RenameBlendShapes(root, descriptor, folder, restoreShapes, rng, result.warnings);
                }

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

                // 3.16.2: the coverage report, in the console and on the app's KS Anti-Rip page (best effort)
                Debug.Log("[KS ANTI-RIP] coverage: " + result.coverage.Summary() + "\n" + string.Join("\n", result.coverage.Lines()));
                if (planSource is AppPlanSource) KSAntiRipClient.Coverage(result.coverage.ToJson(blueprint, avatarName));
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
            public readonly Dictionary<Renderer, Material[]> Materials = new Dictionary<Renderer, Material[]>();
        }

        /// <summary>3.16.2: the mesh a static MeshRenderer draws (its MeshFilter's), or null.</summary>
        static Mesh StaticMeshOf(MeshRenderer r)
        {
            if (r == null) return null;
            MeshFilter f = r.GetComponent<MeshFilter>();
            return f != null ? f.sharedMesh : null;
        }

        /// <summary>
        /// 3.16.2: how many extra key values a SYNCED long key may add: VRChat's synced-parameter budget, minus what the
        /// avatar's own parameters already cost, minus the restore key (one float, 8 bits, per restore shape).
        /// </summary>
        static int FreeKeySlots(VRCAvatarDescriptor descriptor, int restoreGroups)
        {
            int used = 0;
            try { used = descriptor != null && descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0; }
            catch { used = 0; }
            int free = global::VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.MAX_PARAMETER_COST - used - Math.Max(1, restoreGroups) * 8;
            return Math.Max(0, free / 8);
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
        /// that wears the mesh, is a locked Poiyomi or a lilToon (KSAntiRipLilToon.CanPatch); and the vertices only those sub-meshes use. Null when there are none,
        /// when an animation swaps the materials of one of its renderers, or when the mesh has no normals and tangents.
        /// </summary>
        static ShaderChoice ChooseSubMeshes(Mesh mesh, List<Renderer> users, GameObject root, HashSet<string> swappedPaths, bool staticMesh,
                                            Dictionary<string, Dictionary<int, HashSet<Material>>> swapSlots = null,
                                            HashSet<string> notRewirable = null)
        {
            if (mesh == null || users.Count == 0) return null;
            // A static mesh without tangents gets them on its copy (TryShaderClose), which needs UVs to compute them.
            // 3.16.7: so does a SKINNED mesh when the app's shader code hands the creator's code back the tangent Unity
            // feeds a mesh without any (app v679+); its copy's tangents need no UVs (any direction across the normal).
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal)) return null;
            if (!mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) &&
                !(staticMesh && mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)) &&
                !(!staticMesh && KSAntiRipClient.RunningAppVersion() >= KSAntiRipClient.TangentlessFromAppVersion)) return null;
            // 3.16.6: a renderer whose materials an animation swaps may be closed too, when every material that can land on
            // a closed slot is one the shader can patch (RewireSwaps then points the swaps at the patched copies). Without
            // the per-slot map (an older caller), or when an override controller does the swap, it stays out as before.
            foreach (var u in users)
            {
                string up = AnimationUtility.CalculateTransformPath(u.transform, root.transform);
                if (!swappedPaths.Contains(up)) continue;
                if (swapSlots == null || (notRewirable != null && notRewirable.Contains(up))) return null;
            }
            // 3.16.6: any shader whose source is in the project too (KSAntiRipShaderUniversal); one that then does not
            // patch drops the mesh back to its current method in TryShaderClose, with nothing changed
            bool Patchable(Material m) => m != null && m.shader != null &&
                                          (KSAntiRipShaderPatch.IsLockedPoiyomi(m.shader) || KSAntiRipLilToon.CanPatch(m) ||
                                           KSAntiRipShaderUniversal.CanTry(m.shader));

            int subCount = mesh.subMeshCount;
            var choice = new ChoiceBuilder();
            for (int sub = 0; sub < subCount; sub++)
            {
                bool ok = true, drawn = false;
                foreach (var u in users)
                {
                    Material[] mats = u.sharedMaterials;
                    string up = swapSlots != null ? AnimationUtility.CalculateTransformPath(u.transform, root.transform) : null;
                    for (int i = 0; i < mats.Length && ok; i++)
                    {
                        if (Math.Min(i, subCount - 1) != sub) continue;
                        drawn = true;
                        if (!Patchable(mats[i])) ok = false;
                        // what an animation can put in this slot must be patchable too
                        if (ok && up != null && SwappedInto(swapSlots, up, i).Any(x => !Patchable(x))) ok = false;
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
        /// <summary>
        /// 3.16.7: tangents for a skinned mesh that has none -- any unit direction across each normal. Unity skins them
        /// like the normals, which is all the undo needs; the creator's code never sees them (the shader hands it back
        /// (1,0,0,1)). A missing or zero normal gets (1,0,0).
        /// </summary>
        static Vector4[] TangentsAcross(Vector3[] normals)
        {
            var t = new Vector4[normals.Length];
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3 n = normals[i];
                Vector3 axis = Mathf.Abs(n.x) < 0.9f ? Vector3.right : Vector3.up;
                Vector3 d = Vector3.Cross(n, axis);
                d = d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.right;
                t[i] = new Vector4(d.x, d.y, d.z, 1f);
            }
            return t;
        }

        static ShaderMesh TryShaderClose(Mesh original, List<Renderer> users, KSAntiRipClient.MeshPlan meshPlan,
                                         KSAntiRipClient.Plan plan, Settings settings, string folder,
                                         Dictionary<string, KSAntiRipShaderPatch.PatchResult> patchedShaders,
                                         Dictionary<Material, Material> patchedMats, ref int shaderIndex, List<string> warnings,
                                         TextureWork textures = null, ShaderChoice choice = null, bool staticMesh = false,
                                         Func<Renderer, int, IEnumerable<Material>> swapsOf = null)
        {
            if (choice == null) return null;
            if (meshPlan.shaderIds.Length != original.vertexCount || meshPlan.shaderAmounts.Length != original.vertexCount)
                return null;   // the app did not send usable shader numbers for this mesh
            // 3.16.7: a skinned mesh without tangents needs the app's w = 2 undo (v679+); an older app keeps restore shapes
            bool tangentless = !staticMesh && !original.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent);
            if (tangentless && !plan.shaderTangentless) return null;

            // what can be on slot si of renderer r: its own material, and (3.16.6) whatever an animation swaps in there
            IEnumerable<Material> OnSlot(Renderer r, Material[] slotMats, int si)
            {
                yield return slotMats[si];
                if (swapsOf != null) foreach (Material m in swapsOf(r, si)) yield return m;
            }

            // Patch every distinct shader first; one failure drops the whole mesh to the shape path, with no change made.
            foreach (var smr in users)
            {
                Material[] slots = smr.sharedMaterials;
                for (int si = 0; si < slots.Length; si++)
                {
                    if (!choice.PatchesSlot(si)) continue;
                    foreach (Material mm in OnSlot(smr, slots, si).Distinct().ToList())
                    {
                    if (mm == null || mm.shader == null) return null;
                    string path = AssetDatabase.GetAssetPath(mm.shader);
                    if (string.IsNullOrEmpty(path)) return null;
                    if (!patchedShaders.TryGetValue(path, out KSAntiRipShaderPatch.PatchResult pr))
                    {
                        if (KSAntiRipLilToon.IsLilToon(mm.shader))
                        {
                            // 3.16, lilToon: its code lives in shared package includes, so KSAntiRipLilToon flattens
                            // the shader onto an edited copy of them; the result is used exactly like a Poiyomi copy.
                            KSAntiRipLilToon.FlattenResult fr = KSAntiRipLilToon.PatchFlatten(
                                path, shaderIndex++, folder,
                                plan.shaderCode, plan.shaderCall, plan.shaderIdField, plan.shaderProperties,
                                textures != null ? plan.textureCode : null,
                                textures != null ? plan.textureFn : null,
                                textures != null ? plan.textureTidProperty : null);
                            pr = new KSAntiRipShaderPatch.PatchResult
                            {
                                Shader = fr.ShaderPath != null ? AssetDatabase.LoadAssetAtPath<Shader>(fr.ShaderPath) : null,
                                Why = fr.Why,
                                Entries = fr.Entries,
                                Files = 1,
                                TextureOk = fr.TextureOk,
                                TextureSites = fr.TextureSites,
                                TextureWhy = fr.TextureWhy,
                            };
                        }
                        else if (KSAntiRipShaderPatch.IsLockedPoiyomi(mm.shader))
                        {
                            pr = textures != null
                                ? KSAntiRipShaderPatch.PatchShader(path, plan.shaderCode, plan.shaderCall, plan.shaderIdField,
                                                                   plan.shaderProperties, shaderIndex++, folder,
                                                                   plan.textureCode, plan.textureFn, plan.textureTidProperty)
                                : KSAntiRipShaderPatch.PatchShader(path, plan.shaderCode, plan.shaderCall, plan.shaderIdField,
                                                                   plan.shaderProperties, shaderIndex++, folder);
                        }
                        else
                        {
                            // 3.16.6: any other shader whose source is in the project; its textures stay as they are
                            pr = KSAntiRipShaderUniversal.Patch(path, plan.shaderCode, plan.shaderCall, plan.shaderIdField,
                                                                plan.shaderProperties, shaderIndex++, folder);
                        }
                        patchedShaders[path] = pr;
                        if (pr.Shader == null && warnings != null)
                            warnings.Add("A mesh kept the blend-shape method: its shader could not be patched (" + pr.Why + ").");
                        else if (textures != null && !pr.TextureOk && warnings != null && !string.IsNullOrEmpty(pr.TextureWhy))
                            warnings.Add("A shader kept its texture readable: " + pr.TextureWhy + ".");
                    }
                    if (pr.Shader == null) return null;
                    }
                }
            }

            // The rest of the mesh, when there is one, keeps restore shapes: what the shader takes is zeroed in the
            // offsets and in every shape, so no vertex is moved by both, and the shapes close the others exactly as before.
            // 3.16.2: a static mesh has no restore shapes -- its rest stays where it is; and a static mesh without
            // tangents gets them computed on the copy (the restore moves vertices along normal and tangent).
            // 3.16.5: a static mesh moves along its id's public directions when the app's shader code can undo that
            // (plan.shaderAxes, app v678+), and can then ship compressed; it then needs no tangents, and gets none (the
            // creator's shader keeps reading what it read). 3.16.7: a skinned mesh without tangents gets some across its
            // normals; the shader undoes along them and hands the creator's code back Unity's (1,0,0,1) (UV7.w = 2).
            bool axes = staticMesh && plan.shaderAxes;
            Mesh source = original;
            if (!original.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent) && !axes)
            {
                source = UnityEngine.Object.Instantiate(original);
                if (staticMesh) source.RecalculateTangents();
                else source.tangents = TangentsAcross(source.normals);
            }
            if (!choice.Whole && !staticMesh)
            {
                var offsets = (Vector3[])meshPlan.offsets.Clone();
                var groups = meshPlan.groups.Select(g => (Vector3[])g.Clone()).ToList();
                for (int i = 0; i < offsets.Length; i++)
                {
                    if (!choice.Move[i]) continue;
                    offsets[i] = Vector3.zero;
                    foreach (Vector3[] g in groups) g[i] = Vector3.zero;
                }
                Mesh basis = source;
                source = KSAntiRipMesh.Apply(basis, offsets, groups, meshPlan.shapeNames);
                if (basis != original) UnityEngine.Object.DestroyImmediate(basis);
            }
            Mesh scrambled = KSAntiRipShaderPatch.BuildScrambledMesh(source, choice.Move, meshPlan.shaderIds, meshPlan.shaderAmounts,
                                                                     settings.strength, axes, tangentless, out int moved);
            if (source != original) UnityEngine.Object.DestroyImmediate(source);

            var result = new ShaderMesh { Scrambled = scrambled, Mixed = !choice.Whole && !staticMesh, ShaderVertices = moved };
            Material Patched(Material om)
            {
                if (patchedMats.TryGetValue(om, out Material pm)) return pm;
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
                return pm;
            }
            foreach (var smr in users)
            {
                Material[] mats = smr.sharedMaterials;
                var copy = new Material[mats.Length];
                for (int j = 0; j < mats.Length; j++)
                {
                    Material om = mats[j];
                    // a slot on a sub-mesh the shader does not take keeps the avatar's own material
                    if (om == null || !choice.PatchesSlot(j)) { copy[j] = om; continue; }
                    copy[j] = Patched(om);
                    // 3.16.6: what an animation swaps into this slot gets its patched copy too (RewireSwaps uses it)
                    if (swapsOf != null) foreach (Material sw in swapsOf(smr, j)) if (sw != null) Patched(sw);
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
            /// <summary>3.16.2, for the coverage report: why a texture asked for was finally left readable.</summary>
            public readonly Dictionary<Texture2D, string> WhyNot = new Dictionary<Texture2D, string>();
            public bool IsLaidOut(Texture2D t) => t != null && _built.ContainsKey(t);

            /// <summary>Gives <paramref name="patched"/> the laid-out copy of <paramref name="original"/>'s main texture
            /// and its id, or id 0 (read plainly) when that texture is not laid out.</summary>
            public void Apply(Material patched, Material original)
            {
                float tid = 0f;
                var t = original.HasProperty("_MainTex") ? original.GetTexture("_MainTex") as Texture2D : null;
                bool lil = KSAntiRipLilToon.IsLilToon(original.shader);
                string otid = lil ? KSAntiRipLilToon.OutlineTidProperty(Plan.textureTidProperty) : null;
                float outlineTid = 0f;
                if (t != null && Build(t, out Texture2D laid, out uint id))
                {
                    patched.SetTexture("_MainTex", laid);
                    // lilToon: an outline that wears the main texture reads the laid-out copy through the texture function
                    if (lil && patched.HasProperty(KSAntiRipLilToon.OutlineSlot) && original.GetTexture(KSAntiRipLilToon.OutlineSlot) == t)
                    {
                        patched.SetTexture(KSAntiRipLilToon.OutlineSlot, laid);
                        outlineTid = id;
                    }
                    // The copy inherits every saved slot of the original, stale ones included. A stale slot
                    // (e.g. _BaseColorMap / _BaseMap from an earlier shader) still holds the ORIGINAL main texture
                    // and would ship it readable, defeating the layout. The current shader does not read these
                    // slots, so clearing them changes nothing on screen.
                    StripSavedRefs(patched, t, lil ? KSAntiRipLilToon.UnreadSlots : null, "_MainTex");
                    tid = id;
                    if (!_users.TryGetValue(t, out List<Material> list)) _users[t] = list = new List<Material>();
                    list.Add(patched);
                }
                if (patched.HasProperty(Plan.textureTidProperty)) patched.SetFloat(Plan.textureTidProperty, tid);
                if (otid != null && patched.HasProperty(otid)) patched.SetFloat(otid, outlineTid);
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
                    WhyNot[original] = "Kawaii Studio did not send its layout";
                    Warnings?.Add("A texture was left readable: Kawaii Studio did not send its layout.");
                    return false;
                }
                laid = KSAntiRipShaderPatch.BuildScrambledTexture(original, layout.dest, layout.state, Plan.textureBlock, Folder, out string why);
                if (laid == null)
                {
                    _failed.Add(original);
                    WhyNot[original] = why;
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
                    Texture2D laidCopy = _built[t].Key;
                    string otid = KSAntiRipLilToon.OutlineTidProperty(Plan.textureTidProperty);
                    if (_users.TryGetValue(t, out List<Material> list))
                        foreach (Material m in list)
                        {
                            m.SetTexture("_MainTex", t);
                            if (m.HasProperty(Plan.textureTidProperty)) m.SetFloat(Plan.textureTidProperty, 0f);
                            // lilToon's outline followed the laid-out copy: it goes back with it
                            if (m.HasProperty(KSAntiRipLilToon.OutlineSlot) && m.GetTexture(KSAntiRipLilToon.OutlineSlot) == laidCopy)
                            {
                                m.SetTexture(KSAntiRipLilToon.OutlineSlot, t);
                                if (m.HasProperty(otid)) m.SetFloat(otid, 0f);
                            }
                            EditorUtility.SetDirty(m);
                        }
                    _built.Remove(t);
                    WhyNot[t] = "another part of the avatar, or another slot of the same material, ships it as it is";
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
            // lilToon: _BaseMap/_BaseColorMap are declared but never read here (cleared on the copy), and the outline's
            // read goes through the texture function too (KSAntiRipLilToon), so neither is a second plain read.
            bool lil = KSAntiRipLilToon.IsLilToon(s);
            for (int i = 0; i < s.GetPropertyCount(); i++)
            {
                if (s.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string p = s.GetPropertyName(i);
                if (p == "_MainTex") continue;
                if (lil && (p == KSAntiRipLilToon.OutlineSlot || Array.IndexOf(KSAntiRipLilToon.UnreadSlots, p) >= 0)) continue;
                if (m.GetTexture(p) == mainTex) return true;
            }
            return false;
        }

        /// <summary>Nulls every STALE saved texture slot of <paramref name="m"/> (one its shader does not declare,
        /// so never read on screen) that still points at <paramref name="tex"/>, except the names in
        /// <paramref name="keep"/>. Called after the main texture has been laid out: the copy inherits the
        /// original's saved slots, and a stale one (Layla's face/body: _BaseColorMap, _BaseMap) would otherwise ship
        /// the original readable. A declared slot is left alone (LiveDuplicateSlot keeps such a texture readable,
        /// and Finish checks what remains). Returns how many slots were cleared.</summary>
        static int StripSavedRefs(Material m, Texture tex, string[] alsoClear, params string[] keep)
        {
            var keepSet = new HashSet<string>(keep);
            if (m.shader != null)
                for (int i = 0; i < m.shader.GetPropertyCount(); i++) keepSet.Add(m.shader.GetPropertyName(i));
            // declared but never read by this shader (lilToon's _BaseMap/_BaseColorMap): cleared like a stale slot
            if (alsoClear != null) foreach (string a in alsoClear) keepSet.Remove(a);
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
            => MaterialSwaps(root, descriptor, out swapped, out _, out _);

        /// <summary>
        /// 3.16.6: also, per renderer path, which slot gets which materials (bySlot), and the paths some controller that is
        /// not a plain AnimatorController swaps (an override controller, which cannot be copied and rewired: those stay
        /// on restore shapes). A swap whose slot cannot be read counts for every slot.
        /// </summary>
        static HashSet<string> MaterialSwaps(GameObject root, VRCAvatarDescriptor descriptor, out HashSet<Material> swapped,
                                             out Dictionary<string, Dictionary<int, HashSet<Material>>> bySlot,
                                             out HashSet<string> notRewirable)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            swapped = new HashSet<Material>();
            var found = swapped;
            var slots = new Dictionary<string, Dictionary<int, HashSet<Material>>>(StringComparer.Ordinal);
            var fixedPaths = new HashSet<string>(StringComparer.Ordinal);
            bySlot = slots;
            notRewirable = fixedPaths;
            void Scan(RuntimeAnimatorController c, string prefix)
            {
                if (c == null) return;
                bool rewirable = c is AnimatorController;
                foreach (AnimationClip clip in c.animationClips)
                {
                    if (clip == null) continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                    {
                        if (!b.propertyName.StartsWith("m_Materials", StringComparison.Ordinal)) continue;
                        if (b.type == null || !typeof(Renderer).IsAssignableFrom(b.type)) continue;
                        string p = string.IsNullOrEmpty(prefix) ? b.path : string.IsNullOrEmpty(b.path) ? prefix : prefix + "/" + b.path;
                        paths.Add(p);
                        if (!rewirable) fixedPaths.Add(p);
                        int slot = SlotOf(b.propertyName);
                        if (!slots.TryGetValue(p, out var perSlot)) slots[p] = perSlot = new Dictionary<int, HashSet<Material>>();
                        if (!perSlot.TryGetValue(slot, out var set)) perSlot[slot] = set = new HashSet<Material>();
                        foreach (ObjectReferenceKeyframe k in AnimationUtility.GetObjectReferenceCurve(clip, b))
                            if (k.value is Material m) { found.Add(m); set.Add(m); }
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

        /// <summary>"m_Materials.Array.data[2]" -> 2; -1 when the property names no slot.</summary>
        static int SlotOf(string propertyName)
        {
            int open = propertyName.LastIndexOf('['), close = propertyName.LastIndexOf(']');
            return open >= 0 && close > open && int.TryParse(propertyName.Substring(open + 1, close - open - 1), out int s) ? s : -1;
        }

        /// <summary>3.16.6: the materials an animation may put in slot <paramref name="slot"/> of the renderer at
        /// <paramref name="path"/> (a swap whose slot is unknown counts for every slot).</summary>
        static IEnumerable<Material> SwappedInto(Dictionary<string, Dictionary<int, HashSet<Material>>> bySlot, string path, int slot)
        {
            if (bySlot == null || !bySlot.TryGetValue(path, out var perSlot)) yield break;
            if (perSlot.TryGetValue(slot, out var exact)) foreach (Material m in exact) yield return m;
            if (perSlot.TryGetValue(-1, out var any)) foreach (Material m in any) yield return m;
        }

        /// <summary>
        /// 3.16.6 (owner, 2026-11-19, after an attacker's test of his own upload: the face "Body" stayed on restore shapes,
        /// which a least-squares solve undoes in milliseconds, only because "an animation swaps its materials"). A renderer
        /// the shader closed may have materials swapped in by the avatar's animations; those swaps must put in the PATCHED
        /// copy, or the scrambled mesh would show on the swapped-in (unpatched) shader -- to everyone, key or not. The user's
        /// own clips and controllers are never written: each controller with such a swap is COPIED into the build folder
        /// with copies of those clips whose swaps point at the patched materials, and put in its place on this build copy.
        /// Returns the closed renderers this could not do for (an override controller, a copy that failed): the caller
        /// puts those back on restore shapes before anything else uses them.
        /// </summary>
        static HashSet<Renderer> RewireSwaps(GameObject root, VRCAvatarDescriptor descriptor, string folder,
                                             Dictionary<Renderer, ShaderChoice> closed, Dictionary<Material, Material> patchedMats,
                                             List<string> warnings)
        {
            var failed = new HashSet<Renderer>();
            if (closed.Count == 0) return failed;

            Renderer TargetOf(Transform animRoot, EditorCurveBinding b)
            {
                Transform t = string.IsNullOrEmpty(b.path) ? animRoot : animRoot.Find(b.path);
                return t != null && b.type != null ? t.GetComponent(b.type) as Renderer : null;
            }
            bool NeedsPatch(Transform animRoot, EditorCurveBinding b, out Renderer r, out int slot)
            {
                r = null; slot = -1;
                if (!b.propertyName.StartsWith("m_Materials", StringComparison.Ordinal) || b.type == null ||
                    !typeof(Renderer).IsAssignableFrom(b.type)) return false;
                r = TargetOf(animRoot, b);
                if (r == null || !closed.TryGetValue(r, out ShaderChoice c)) return false;
                slot = SlotOf(b.propertyName);
                return slot < 0 || c.PatchesSlot(slot);
            }

            // returns the controller to use instead (null = keep), and adds to failed what it could not rewire
            RuntimeAnimatorController Rewire(RuntimeAnimatorController source, Transform animRoot)
            {
                if (source == null) return null;
                var touched = new Dictionary<AnimationClip, List<(EditorCurveBinding b, Renderer r)>>();
                foreach (AnimationClip clip in source.animationClips.Distinct())
                {
                    if (clip == null) continue;
                    foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                        if (NeedsPatch(animRoot, b, out Renderer r, out _))
                        {
                            if (!touched.TryGetValue(clip, out var list)) touched[clip] = list = new List<(EditorCurveBinding, Renderer)>();
                            list.Add((b, r));
                        }
                }
                if (touched.Count == 0) return null;
                if (!(source is AnimatorController ac))
                {
                    foreach (var l in touched.Values) foreach (var (_, r) in l) failed.Add(r);
                    return null;
                }
                var clipMap = new Dictionary<AnimationClip, AnimationClip>();
                foreach (var kv in touched)
                {
                    AnimationClip copy = UnityEngine.Object.Instantiate(kv.Key);
                    copy.name = kv.Key.name;
                    foreach (var (b, r) in kv.Value)
                    {
                        ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(kv.Key, b);
                        for (int k = 0; k < keys.Length; k++)
                            if (keys[k].value is Material m && patchedMats.TryGetValue(m, out Material pm)) keys[k].value = pm;
                            else if (keys[k].value is Material m2 && m2.shader != null &&
                                     !m2.shader.name.StartsWith("Hidden/Locked/KS/", StringComparison.Ordinal)) failed.Add(r);
                        AnimationUtility.SetObjectReferenceCurve(copy, b, keys);
                    }
                    AssetDatabase.CreateAsset(copy, folder + "/KS_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".anim");
                    clipMap[kv.Key] = copy;
                }
                AnimatorController rewired = null;
                try
                {
                    rewired = KSAntiRipAnimatorCopy.Copy(ac, folder + "/KS_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".controller",
                                                         clip => clip != null && clipMap.TryGetValue(clip, out AnimationClip cc) ? cc : clip);
                }
                catch (Exception e) { Debug.LogError("[KS ANTI-RIP] " + e); }
                if (rewired == null)
                {
                    foreach (var l in touched.Values) foreach (var (_, r) in l) failed.Add(r);
                    return null;
                }
                return rewired;
            }

            if (descriptor != null)
            {
                for (int i = 0; descriptor.baseAnimationLayers != null && i < descriptor.baseAnimationLayers.Length; i++)
                {
                    RuntimeAnimatorController c = Rewire(descriptor.baseAnimationLayers[i].animatorController, root.transform);
                    if (c != null) descriptor.baseAnimationLayers[i].animatorController = c;
                }
                for (int i = 0; descriptor.specialAnimationLayers != null && i < descriptor.specialAnimationLayers.Length; i++)
                {
                    RuntimeAnimatorController c = Rewire(descriptor.specialAnimationLayers[i].animatorController, root.transform);
                    if (c != null) descriptor.specialAnimationLayers[i].animatorController = c;
                }
            }
            foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
            {
                RuntimeAnimatorController c = Rewire(a.runtimeAnimatorController, a.transform);
                if (c != null) a.runtimeAnimatorController = c;
            }
            if (failed.Count > 0 && warnings != null)
                warnings.Add(failed.Count + " part(s) whose materials an animation swaps kept restore shapes: the swap could not be pointed at the protected material.");
            return failed;
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
