using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Takes the signposts out of a protected bundle.
    ///
    /// The lock already makes an automated rip useless. What it does not stop is a human opening the bundle in
    /// Unity, reading a hierarchy of helpfully named objects and a layer called "KS ANTI-RIP", and working out
    /// what to set. This renames the hierarchy, the layers, the states and the generated assets so there is
    /// nothing to read.
    ///
    /// WHAT IT DELIBERATELY DOES NOT RENAME, because renaming these breaks real avatars:
    ///   * the armature -- every bone under the humanoid root. PhysBones, contacts, constraints and other
    ///     creators' tools resolve bones by name, and MMD worlds look for them;
    ///   * blend shape names -- visemes, eye tracking and MMD all address them by name;
    ///   * animator parameters -- they are named in the expression menu, in OSC tools and in other people's
    ///     assets, and a renamed parameter silently stops working;
    ///   * the KS_ANTIRIP_PROTECTED mark, which VRChat Archive has to be able to find.
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
            CollectArmature(clone, skip);

            Transform marker = clone.transform.Find(KSAntiRipProcessor.MarkerName);
            if (marker != null) skip.Add(marker);
            skip.Add(clone.transform);                       // the root keeps its name

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
            var pathMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
            {
                string oldPath = AnimationUtility.CalculateTransformPath(t, clone.transform);
                string newPath = NewPath(t, clone.transform, rename);
                if (oldPath != newPath) pathMap[oldPath] = newPath;
            }

            RewriteAllAnimators(descriptor, folder, pathMap, tag, rng, warnings);

            foreach (var pair in rename)
                pair.Key.name = pair.Value;

            return rename.Count;
        }

        /// <summary>Every bone of the humanoid rig, plus everything under the hips, left alone.</summary>
        static void CollectArmature(GameObject clone, HashSet<Transform> skip)
        {
            var animator = clone.GetComponent<Animator>();
            if (animator == null) return;

            if (animator.isHuman)
            {
                foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
                {
                    if (bone == HumanBodyBones.LastBone) continue;
                    Transform t = null;
                    try { t = animator.GetBoneTransform(bone); } catch { }
                    if (t != null) skip.Add(t);
                }
                Transform hips = null;
                try { hips = animator.GetBoneTransform(HumanBodyBones.Hips); } catch { }
                if (hips != null)
                {
                    // the whole skeleton under the hips, including non-humanoid extras (tails, ears)
                    foreach (Transform t in hips.GetComponentsInChildren<Transform>(true)) skip.Add(t);
                    // and the chain up to the root, usually "Armature"
                    for (Transform p = hips.parent; p != null; p = p.parent) skip.Add(p);
                }
            }
            else
            {
                // Not humanoid: an "Armature" by name is the best guess available.
                foreach (Transform t in clone.GetComponentsInChildren<Transform>(true))
                    if (t.name.Equals("Armature", StringComparison.OrdinalIgnoreCase))
                        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) skip.Add(c);
            }
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

        /// <summary>Copies every animator the descriptor uses and rewrites the copies.</summary>
        static void RewriteAllAnimators(VRCAvatarDescriptor descriptor, string folder,
                                        Dictionary<string, string> pathMap, string tag,
                                        System.Random rng, List<string> warnings)
        {
            if (descriptor == null) return;
            var clipCache = new Dictionary<AnimationClip, AnimationClip>();
            int index = 0;

            void Handle(ref VRCAvatarDescriptor.CustomAnimLayer layer, string what)
            {
                var source = layer.animatorController as AnimatorController;
                if (source == null) return;
                string sourcePath = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(sourcePath)) return;

                string copyPath = folder + "/KS_" + tag + "_anim" + (index++) + ".controller";
                if (!AssetDatabase.CopyAsset(sourcePath, copyPath))
                {
                    warnings.Add("Could not copy the " + what + " controller, so it was left unobfuscated.");
                    return;
                }
                var copy = AssetDatabase.LoadAssetAtPath<AnimatorController>(copyPath);
                if (copy == null) return;

                foreach (var l in copy.layers)
                    RewriteStateMachine(l.stateMachine, folder, pathMap, clipCache, tag, rng);

                // the layer names themselves: "KS ANTI-RIP" would otherwise announce where to look
                var layers = copy.layers;
                for (int i = 0; i < layers.Length; i++)
                    layers[i].name = "KS_" + tag + "_L" + i;
                copy.layers = layers;

                layer.animatorController = copy;
                layer.isDefault = false;
            }

            var baseLayers = descriptor.baseAnimationLayers;
            for (int i = 0; i < baseLayers.Length; i++) Handle(ref baseLayers[i], baseLayers[i].type.ToString());
            descriptor.baseAnimationLayers = baseLayers;

            var special = descriptor.specialAnimationLayers;
            for (int i = 0; i < special.Length; i++) Handle(ref special[i], special[i].type.ToString());
            descriptor.specialAnimationLayers = special;
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

            var copy = UnityEngine.Object.Instantiate(source);
            copy.name = "KS_" + tag + "_a" + cache.Count;

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

            AssetDatabase.CreateAsset(copy, folder + "/" + copy.name + ".anim");
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
