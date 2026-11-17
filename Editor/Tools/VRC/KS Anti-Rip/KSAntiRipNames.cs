using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Takes the signposts out of a protected bundle.
    ///
    /// The lock already makes an automated rip useless. What it does not stop is a human opening the bundle in
    /// Unity, reading a hierarchy of helpfully named objects ("DressVeil", "Lingerie", "Shoes") and the clips that
    /// drive them, and working out what to set. This renames the hierarchy, the layers, the states and the
    /// generated assets, and rewrites every animation path to match, so there is nothing left to read. Run at
    /// BUILD time (after VRCFury / Modular Avatar merge) it reaches the toggles those tools add too, which the old
    /// per-prefab pass never saw -- that is why ripped protected avatars still had readable clips.
    ///
    /// WHAT IT DELIBERATELY DOES NOT RENAME, because renaming these breaks real avatars:
    ///   * the SKELETON -- every bone a mesh is skinned to, every humanoid bone, every PhysBone chain, and the
    ///     ancestors that make up their paths. PhysBones, contacts and constraints keep their object references
    ///     across a rename, but MMD worlds and some tools look bones up BY NAME, so the skeleton keeps its names.
    ///     Clothing, props and toggle objects are NOT skeleton, even when they sit under the armature, so they are
    ///     renamed -- that is the whole point;
    ///   * blend shape names -- visemes, eye tracking and MMD all address them by name;
    ///   * animator parameters -- they are named in the expression menu, in OSC tools and in other people's
    ///     assets, and a renamed parameter silently stops working;
    ///   * the "Kawaii Studio Bundle" mark, which VRChat Archive has to be able to find.
    ///
    /// NON-DESTRUCTIVE, and this is the sharp edge: AssetDatabase.CopyAsset on a controller copies the
    /// controller but its states still reference the ORIGINAL clip assets. Remapping those in place would
    /// rewrite the user's own animations. Every clip is therefore copied before it is touched, and the copy is
    /// what the protected controller points at. KSAntiRipNoHarmTest hashes the originals and would catch it.
    /// </summary>
    public static class KSAntiRipNames
    {
        /// <summary>Renames the clone's hierarchy and rewrites every animation that referred to it.</summary>
        /// <returns>how many objects were renamed</returns>
        public static int Obfuscate(GameObject clone, VRCAvatarDescriptor descriptor, string folder,
                                    string tag, System.Random rng, List<string> warnings)
        {
            var skip = new HashSet<Transform>();
            CollectSkeleton(clone, skip);

            // The face/body mesh keeps its name: MMD worlds drive a renderer called "Body" (and the viseme/eye
            // meshes) by name, and renaming it would break every MMD dance. Its blend shape names are kept anyway;
            // the object name reveals nothing about the protection, so this costs nothing.
            if (descriptor != null)
            {
                if (descriptor.VisemeSkinnedMesh != null) skip.Add(descriptor.VisemeSkinnedMesh.transform);
                var eyes = descriptor.customEyeLookSettings.eyelidsSkinnedMesh;
                if (eyes != null) skip.Add(eyes.transform);
            }
            foreach (SkinnedMeshRenderer smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr != null && smr.name.Equals("Body", StringComparison.OrdinalIgnoreCase))
                    skip.Add(smr.transform);

            Transform marker = clone.transform.Find(KSAntiRipProcessor.MarkerName);
            if (marker != null) skip.Add(marker);
            skip.Add(clone.transform);                       // the root keeps its name

            // Animators below the root (VRCFury gives every animated prop/particle its own) animate paths relative
            // to THEIR object, so their controllers are rewritten with their own path map below. A subtree driven by
            // something we cannot rewrite -- an override controller, a legacy Animation -- keeps its names instead.
            Animator[] childAnimators = clone.GetComponentsInChildren<Animator>(true)
                .Where(a => a != null && a.transform != clone.transform).ToArray();
            foreach (Animator a in childAnimators)
                if (a.runtimeAnimatorController != null && !(a.runtimeAnimatorController is AnimatorController))
                    foreach (Transform t in a.GetComponentsInChildren<Transform>(true)) skip.Add(t);
            foreach (Animation legacy in clone.GetComponentsInChildren<Animation>(true))
                foreach (Transform t in legacy.GetComponentsInChildren<Transform>(true)) skip.Add(t);

            // Transform -> new name. Keyed by Transform, never by name string: the 1.x tool used a
            // name->name dictionary, so two objects that happened to share a name collapsed into one entry
            // and every animation that targeted either of them pointed at the wrong object afterwards.
            var rename = new Dictionary<Transform, string>();
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            {
                if (skip.Contains(t)) continue;
                rename[t] = "KS_" + tag + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            }
            if (rename.Count == 0) return 0;

            // Old path -> new path, computed while the hierarchy still has its original names.
            Dictionary<string, string> pathMap = PathMap(clone.transform, rename);
            var childMaps = childAnimators
                .Where(a => a.runtimeAnimatorController is AnimatorController)
                .Select(a => (animator: a, map: PathMap(a.transform, rename)))
                .ToList();

            Dictionary<AnimatorController, AnimatorController> copies =
                RewriteAllAnimators(descriptor, folder, pathMap, tag, rng, warnings);

            // The root Animator's own controller is replaced by the playable layers in game, but it still ships in
            // the bundle: left on the original FX it carried every original clip and path, the whole rename table.
            var rootAnimator = clone.GetComponent<Animator>();
            if (rootAnimator != null && rootAnimator.runtimeAnimatorController is AnimatorController rootSource)
                rootAnimator.runtimeAnimatorController =
                    copies.TryGetValue(rootSource, out AnimatorController rootCopy)
                        ? rootCopy
                        : CopyAndRewrite(rootSource, "root Animator", folder, pathMap,
                                         new Dictionary<AnimationClip, AnimationClip>(), tag, rng, warnings);

            foreach (var (animator, map) in childMaps)
            {
                var source = (AnimatorController)animator.runtimeAnimatorController;
                AnimatorController copy = CopyAndRewrite(source, "'" + animator.name + "' Animator", folder, map,
                                                         new Dictionary<AnimationClip, AnimationClip>(), tag, rng, warnings);
                if (copy != null) animator.runtimeAnimatorController = copy;
            }

            foreach (var pair in rename)
                pair.Key.name = pair.Value;

            return rename.Count;
        }

        /// <summary>
        /// The skeleton, and only the skeleton: bones a mesh is skinned to, humanoid bones, PhysBone chains, and
        /// the ancestors that make up their paths. Everything else -- clothing, props, particle objects, the empty
        /// parents toggles switch -- is left out so it gets renamed, even when it sits under the armature.
        ///
        /// The old pass skipped the ENTIRE subtree under the hips, so a dress or a pair of shoes parented into the
        /// armature kept its name and its animation stayed readable after a rip. This keeps the bones, not the
        /// things hanging off them.
        /// </summary>
        static void CollectSkeleton(GameObject clone, HashSet<Transform> skip)
        {
            Transform root = clone.transform;

            // 1. every bone any skinned mesh is weighted to -- the real skeleton, humanoid or not
            foreach (SkinnedMeshRenderer smr in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr == null) continue;
                if (smr.rootBone != null) skip.Add(smr.rootBone);
                Transform[] bones = smr.bones;
                if (bones != null)
                    foreach (Transform b in bones)
                        if (b != null) skip.Add(b);
            }

            // 2. the humanoid bones by role, so MMD and other tools that look them up by name still find them
            var animator = clone.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;
                    Transform t = null;
                    try { t = animator.GetBoneTransform(bone); } catch { }
                    if (t != null) skip.Add(t);
                }
            }

            // 3. PhysBone chains: their transforms survive a rename by reference, but keeping the names avoids
            //    surprising any tool that addresses a dynamic bone by name, and costs nothing to read.
            foreach (VRCPhysBone pb in clone.GetComponentsInChildren<VRCPhysBone>(true))
            {
                if (pb == null) continue;
                Transform r = pb.rootTransform != null ? pb.rootTransform : pb.transform;
                if (r == null) continue;
                foreach (Transform t in r.GetComponentsInChildren<Transform>(true)) skip.Add(t);
            }

            // 4. the ancestors of everything collected, up to the root, so the skeleton's PATHS stay readable
            //    (a renamed "Armature" would change the path to every bone under it).
            foreach (Transform t in skip.ToList())
                for (Transform p = t.parent; p != null && p != root; p = p.parent)
                    skip.Add(p);
        }

        /// <summary>Old path -> new path for everything under `root`, relative to it.</summary>
        static Dictionary<string, string> PathMap(Transform root, Dictionary<Transform, string> rename)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                string oldPath = AnimationUtility.CalculateTransformPath(t, root);
                string newPath = NewPath(t, root, rename);
                if (oldPath != newPath) map[oldPath] = newPath;
            }
            return map;
        }

        static string NewPath(Transform t, Transform root, Dictionary<Transform, string> rename)
        {
            if (t == root) return "";
            var parts = new List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent)
                parts.Add(rename.TryGetValue(c, out string n) ? n : c.name);
            parts.Reverse();
            return string.Join("/", parts);
        }

        /// <summary>Copies every animator the descriptor uses and rewrites the copies. Returns source -> copy.</summary>
        static Dictionary<AnimatorController, AnimatorController> RewriteAllAnimators(
            VRCAvatarDescriptor descriptor, string folder, Dictionary<string, string> pathMap, string tag,
            System.Random rng, List<string> warnings)
        {
            var copies = new Dictionary<AnimatorController, AnimatorController>();
            if (descriptor == null) return copies;
            var clipCache = new Dictionary<AnimationClip, AnimationClip>();

            void Handle(ref VRCAvatarDescriptor.CustomAnimLayer layer, string what)
            {
                var source = layer.animatorController as AnimatorController;
                if (source == null) return;
                if (!copies.TryGetValue(source, out AnimatorController copy))
                {
                    copy = CopyAndRewrite(source, what, folder, pathMap, clipCache, tag, rng, warnings);
                    if (copy == null) return;
                    copies[source] = copy;
                }
                layer.animatorController = copy;
                layer.isDefault = false;
            }

            var baseLayers = descriptor.baseAnimationLayers;
            for (int i = 0; i < baseLayers.Length; i++) Handle(ref baseLayers[i], baseLayers[i].type.ToString());
            descriptor.baseAnimationLayers = baseLayers;

            var special = descriptor.specialAnimationLayers;
            for (int i = 0; i < special.Length; i++) Handle(ref special[i], special[i].type.ToString());
            descriptor.specialAnimationLayers = special;
            return copies;
        }

        /// <summary>A deep copy of `source` in the folder, every path remapped with `pathMap`. Null on failure.</summary>
        static AnimatorController CopyAndRewrite(AnimatorController source, string what, string folder,
                                                 Dictionary<string, string> pathMap,
                                                 Dictionary<AnimationClip, AnimationClip> clipCache,
                                                 string tag, System.Random rng, List<string> warnings)
        {
            if (source == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(source))) return null;

            string copyPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/KS_" + tag + "_anim.controller");
            AnimatorController copy;
            try
            {
                // A real object-by-object deep copy, NOT AssetDatabase.CopyAsset: at build time this controller
                // comes from NDMF, whose graph is spread across shared "assets N.asset" containers, so a file
                // copy hands back a half-copied graph on the wrong controller (the Gesture-mask and "does not
                // exist in the same StateMachine" failures the owner's upload hit). Clips are kept by reference
                // here and remapped to this run's output copies by the RewriteStateMachine pass below; the avatar
                // mask on layer 0 is kept by reference too, which is what the Gesture layer needs.
                copy = KSAntiRipAnimatorCopy.Copy(source, copyPath, clip => clip);
            }
            catch (Exception e)
            {
                Debug.LogError("[KS ANTI-RIP] " + e);
                copy = null;
            }
            if (copy == null)
            {
                warnings.Add("Could not copy the " + what + " controller, so it was left unobfuscated.");
                return null;
            }

            foreach (var l in copy.layers)
                RewriteStateMachine(l.stateMachine, folder, pathMap, clipCache, tag, rng);

            // the layer names themselves: "KS ANTI-RIP" would otherwise announce where to look
            var layers = copy.layers;
            for (int i = 0; i < layers.Length; i++)
                layers[i].name = "KS_" + tag + "_L" + i;
            copy.layers = layers;
            return copy;
        }

        static void RewriteStateMachine(AnimatorStateMachine sm, string folder,
                                        Dictionary<string, string> pathMap,
                                        Dictionary<AnimationClip, AnimationClip> cache,
                                        string tag, System.Random rng)
        {
            if (sm == null) return;
            sm.name = "KS_" + tag + "_sm" + rng.Next(1000, 9999);

            var states = sm.states;
            for (int i = 0; i < states.Length; i++)
            {
                AnimatorState s = states[i].state;
                if (s == null) continue;
                s.name = "KS_" + tag + "_s" + rng.Next(1000, 9999);
                s.motion = RewriteMotion(s.motion, folder, pathMap, cache, tag, rng);
            }

            foreach (var sub in sm.stateMachines)
                RewriteStateMachine(sub.stateMachine, folder, pathMap, cache, tag, rng);
        }

        static Motion RewriteMotion(Motion motion, string folder, Dictionary<string, string> pathMap,
                                    Dictionary<AnimationClip, AnimationClip> cache, string tag, System.Random rng)
        {
            if (motion is AnimationClip clip) return RewriteClip(clip, folder, pathMap, cache, tag);
            if (motion is BlendTree tree)
            {
                var children = tree.children;
                for (int i = 0; i < children.Length; i++)
                    children[i].motion = RewriteMotion(children[i].motion, folder, pathMap, cache, tag, rng);
                tree.children = children;
                tree.name = "KS_" + tag + "_bt" + rng.Next(1000, 9999);
            }
            return motion;
        }

        /// <summary>A COPY of the clip with every path remapped. The user's own clip is never written.</summary>
        static AnimationClip RewriteClip(AnimationClip source, string folder,
                                         Dictionary<string, string> pathMap,
                                         Dictionary<AnimationClip, AnimationClip> cache, string tag)
        {
            if (source == null) return null;
            if (cache.TryGetValue(source, out AnimationClip done)) return done;

            // Unique across every cache of the run: each Animator has its own, and CreateAsset on a taken path
            // would silently replace a clip another controller already points at.
            string clipPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/KS_" + tag + "_a.anim");
            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = System.IO.Path.GetFileNameWithoutExtension(clipPath);

            // settings (loop time, cycle offset, ...) do not survive ClearCurves, so carry them over
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
            AnimationEvent[] events = source.events;

            var floats = AnimationUtility.GetCurveBindings(source)
                .Select(b => new { b, curve = AnimationUtility.GetEditorCurve(source, b) }).ToArray();
            var objects = AnimationUtility.GetObjectReferenceCurveBindings(source)
                .Select(b => new { b, curve = AnimationUtility.GetObjectReferenceCurve(source, b) }).ToArray();

            copy.ClearCurves();

            foreach (var f in floats)
            {
                EditorCurveBinding b = f.b;
                b.path = Remap(b.path, pathMap);
                AnimationUtility.SetEditorCurve(copy, b, f.curve);
            }
            // Object-reference curves are how material and sprite swaps work. The 1.x tool dropped them
            // entirely, so a protected avatar lost every material swap it had.
            foreach (var o in objects)
            {
                EditorCurveBinding b = o.b;
                b.path = Remap(b.path, pathMap);
                AnimationUtility.SetObjectReferenceCurve(copy, b, o.curve);
            }

            AnimationUtility.SetAnimationClipSettings(copy, settings);
            if (events != null && events.Length > 0) AnimationUtility.SetAnimationEvents(copy, events);

            AssetDatabase.CreateAsset(copy, clipPath);
            cache[source] = copy;
            return copy;
        }

        static string Remap(string path, Dictionary<string, string> pathMap)
        {
            if (string.IsNullOrEmpty(path)) return path;
            return pathMap.TryGetValue(path, out string mapped) ? mapped : path;
        }
    }
}
