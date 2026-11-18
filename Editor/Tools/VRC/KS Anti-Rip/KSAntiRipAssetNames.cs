using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Editor.BuildPipeline;
using Object = UnityEngine.Object;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// The last readable names of a protected bundle: its materials, textures, meshes and sounds.
    ///
    /// KSAntiRipNames renames the hierarchy, the layers, the states and the clips. What it left is the ASSETS: a
    /// ripped protected avatar still listed "Layla_Body_Skin", "Lingerie_Lace", "Horns_Normal", "Scream.wav" -- a
    /// map of what each piece is. This pass, run at build time on the avatar VRChat is uploading, ships every one of
    /// them under a random name.
    ///
    /// NON-DESTRUCTIVE, the same promise as the rest of KS Anti-Rip, kept two different ways:
    ///   * MATERIALS are files of the user's project (.mat). Renaming one, even in memory, can be written back to it by
    ///     any SaveAssets during the build. So each material gets a renamed COPY in this build's folder; the build
    ///     avatar's renderers and its (already copied) clips point at the copies. The user's .mat is never touched.
    ///   * TEXTURES, IMPORTED MESHES and SOUNDS come from an importer: their name is not stored in any file Unity
    ///     writes back, and copying them would duplicate hundreds of MB. They are renamed IN MEMORY for the build and
    ///     given their names back by <see cref="KSAntiRipAssetNamesRestore"/> when the SDK finishes the build.
    ///     A native asset that lives in the user's project (a baked .asset mesh) is left alone for the same reason as
    ///     a material.
    /// Asset names mean nothing to VRChat, the shaders or the animator: materials, textures and meshes are reached by
    /// reference, so renaming them changes what a ripper reads and nothing of what renders.
    /// </summary>
    public static class KSAntiRipAssetNames
    {
        /// <summary>Imported assets renamed in memory for the build in progress, with their real names.</summary>
        static readonly Dictionary<Object, string> Renamed = new Dictionary<Object, string>();

        public class Report
        {
            public int materials, textures, meshes, sounds;
            public override string ToString() =>
                materials + " material(s), " + textures + " texture(s), " + meshes + " mesh(es), " + sounds + " sound(s)";
        }

        /// <summary>
        /// Renames, for this build only, every material, texture, mesh and sound the avatar ships. <paramref name="folder"/>
        /// is this build's KS folder: material copies are created there, and clips found there are KS's own copies,
        /// which may be rewritten.
        /// </summary>
        public static Report Apply(GameObject root, string folder, List<string> warnings)
        {
            Restore();   // a build that never reached its post-process leaves names behind: give them back first
            var report = new Report();
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var copies = new Dictionary<Material, Material>();

            Material CopyOf(Material m)
            {
                if (m == null) return null;
                Material c;
                if (copies.TryGetValue(m, out c)) return c;
                if (IsOurs(m, folder)) { m.name = Neutral(rng); copies[m] = m; return m; }
                c = Object.Instantiate(m);
                c.name = Neutral(rng);
                AssetDatabase.CreateAsset(c, folder + "/MAT_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mat");
                copies[m] = c;
                report.materials++;
                return c;
            }

            // 1. materials on every renderer (particle and trail materials are in sharedMaterials too)
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material c = CopyOf(mats[i]);
                    if (c != mats[i]) { mats[i] = c; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }

            // 2. material swaps in the clips KS already copied for this build (the user's own clips are never edited:
            //    a legacy Animation or an override controller keeps pointing at the original material, which then
            //    ships under its own name, exactly as before this pass)
            foreach (AnimationClip clip in Clips(root))
            {
                if (!IsOurs(clip, folder)) continue;
                foreach (EditorCurveBinding b in AnimationUtility.GetObjectReferenceCurveBindings(clip))
                {
                    ObjectReferenceKeyframe[] keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                    bool changed = false;
                    for (int k = 0; k < keys.Length; k++)
                    {
                        var m = keys[k].value as Material;
                        if (m == null) continue;
                        Material c = CopyOf(m);
                        if (c != m) { keys[k].value = c; changed = true; }
                    }
                    if (changed) AnimationUtility.SetObjectReferenceCurve(clip, b, keys);
                }
            }

            // 3. textures, meshes and sounds the avatar now depends on
            foreach (Object dep in EditorUtility.CollectDependencies(new Object[] { root }))
            {
                bool texture = dep is Texture, mesh = dep is Mesh, sound = dep is AudioClip;
                if (!texture && !mesh && !sound) continue;
                string path = AssetDatabase.GetAssetPath(dep);
                if (string.IsNullOrEmpty(path) || path.StartsWith("Resources/unity_builtin", StringComparison.Ordinal) ||
                    path.StartsWith("Library/", StringComparison.Ordinal)) continue;   // Unity's built-in assets
                if (IsOurs(dep, folder) || IsBuildTemporary(path))
                    dep.name = Neutral(rng);                    // generated for this build: renaming it is final and harmless
                else if (AssetDatabase.IsNativeAsset(dep) || AssetImporter.GetAtPath(path) == null)
                    continue;                                   // a native asset of the user's project: never risk writing it
                else
                {
                    if (!Renamed.ContainsKey(dep)) Renamed[dep] = dep.name;
                    dep.name = Neutral(rng);
                }
                if (texture) report.textures++; else if (mesh) report.meshes++; else report.sounds++;
            }
            return report;
        }

        /// <summary>Gives every imported asset renamed for the last build its real name back. Safe to call any time.</summary>
        public static void Restore()
        {
            foreach (var kv in Renamed)
                if (kv.Key != null) kv.Key.name = kv.Value;
            Renamed.Clear();
        }

        /// <summary>Every clip of every animator the avatar can play: playable layers, the root Animator, props' Animators.</summary>
        static IEnumerable<AnimationClip> Clips(GameObject root)
        {
            var controllers = new HashSet<RuntimeAnimatorController>();
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (desc != null)
            {
                foreach (var l in desc.baseAnimationLayers) if (l.animatorController != null) controllers.Add(l.animatorController);
                if (desc.specialAnimationLayers != null)
                    foreach (var l in desc.specialAnimationLayers) if (l.animatorController != null) controllers.Add(l.animatorController);
            }
            foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
                if (a.runtimeAnimatorController != null) controllers.Add(a.runtimeAnimatorController);
            var seen = new HashSet<AnimationClip>();
            foreach (var c in controllers)
                foreach (AnimationClip clip in c.animationClips)
                    if (clip != null && seen.Add(clip)) yield return clip;
        }

        static bool IsOurs(Object o, string folder)
        {
            string p = AssetDatabase.GetAssetPath(o);
            return !string.IsNullOrEmpty(p) && p.StartsWith(folder + "/", StringComparison.Ordinal);
        }

        /// <summary>Assets NDMF / Modular Avatar generate for one build and delete after it.</summary>
        static bool IsBuildTemporary(string path) =>
            path.IndexOf("/__Generated/", StringComparison.Ordinal) >= 0 ||
            path.IndexOf("ZZZ_GeneratedAssets", StringComparison.Ordinal) >= 0;

        static string Neutral(System.Random rng)
        {
            const string Letters = "abcdefghijklmnopqrstuvwxyz";
            var chars = new char[10];
            for (int i = 0; i < chars.Length; i++) chars[i] = Letters[rng.Next(Letters.Length)];
            return new string(chars);
        }
    }

    /// <summary>When the VRChat SDK has finished building, the imported assets get their real names back.</summary>
    public sealed class KSAntiRipAssetNamesRestore : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;
        public void OnPostprocessAvatar() => KSAntiRipAssetNames.Restore();
    }
}
