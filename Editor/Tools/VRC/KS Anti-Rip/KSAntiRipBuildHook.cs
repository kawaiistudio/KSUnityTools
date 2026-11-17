using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Protects an avatar AT UPLOAD instead of making a protected copy of it.
    ///
    /// WHY (owner, 2026-11-17: "ça doit pas recréer un prefab mais être non destructif, directement
    /// au moment de l'upload"). The old path built a protected prefab in the project. On an avatar that uses
    /// VRCFury or Modular Avatar that is wrong twice over: the dress and toggle objects those tools add only exist
    /// after they merge at build, so the per-prefab pass never saw them and their animations shipped readable; and
    /// renaming the prefab's hierarchy would break the VRCFury / MA components still pointing at it.
    ///
    /// Running here fixes both. The VRChat SDK calls every <see cref="IVRCSDKPreprocessAvatarCallback"/> on the
    /// throwaway avatar it is about to upload, in <see cref="callbackOrder"/> order. VRCFury and Modular Avatar
    /// (through NDMF) run around -11000..-1025, so by a high order everything is merged into one hierarchy and one
    /// set of controllers, there are no tool components left to break, and the scramble + the full path
    /// randomization reach the merged toggles too. Nothing in the user's scene is touched.
    ///
    /// The avatar is protected only when the Kawaii Studio app has turned it on — a <see cref="KSAntiRipMark"/> the
    /// app adds through Link. No mark, nothing happens, so an ordinary upload is untouched.
    /// </summary>
    public sealed class KSAntiRipBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        /// <summary>
        /// After VRCFury / Modular Avatar / NDMF (which run well below zero) and after mesh optimizers, but below
        /// Modular Avatar's IEditorOnly strip at int.MaxValue, so the mark is still here to read. We protect the
        /// final, merged, optimized avatar — which also keeps the restore shapes as small as possible.
        /// </summary>
        public int callbackOrder => int.MaxValue - 1024;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return true;

            var mark = avatarGameObject.GetComponent<KSAntiRipMark>();
            // The app can pause protection without removing the component.
            if (mark == null || !mark.enabled_)
            {
                if (mark != null) UnityEngine.Object.DestroyImmediate(mark);
                return true;   // not a KS-protected upload: leave it exactly alone
            }

            try
            {
                // Strip the mark first: it must never ship, and if protection throws we still want it gone.
                var settings = new KSAntiRipProcessor.Settings
                {
                    strength = mark.strength,
                    restoreGroups = mark.restoreGroups,
                    syncKey = mark.syncKey,
                    hideWhileLocked = mark.hideWhileLocked,
                    obfuscateNames = mark.obfuscateNames,
                    maxAddedMegabytes = mark.maxAddedMegabytes,
                };
                UnityEngine.Object.DestroyImmediate(mark);

                string folder = EnsureBuildFolder();
                var result = new KSAntiRipProcessor.Result();
                KSAntiRipProcessor.ProtectInPlace(avatarGameObject, settings, null, folder, result);

                if (!result.ok)
                {
                    // A protection that could not run must STOP the upload, never let a half-scrambled or
                    // unprotected avatar go out silently under the owner's name.
                    Debug.LogError("[KS ANTI-RIP] " + result.message);
                    EditorUtility.DisplayDialog("KS Anti-Rip",
                        "This avatar is set to be protected, but the protection could not run, so the upload was " +
                        "stopped:\n\n" + result.message, "OK");
                    return false;
                }

                Debug.Log($"[KS ANTI-RIP] protected at upload: {result.meshesProtected} mesh(es), " +
                          $"{result.verticesProtected:N0} vertices, key -> {result.keyPath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[KS ANTI-RIP] build hook failed: " + e);
                EditorUtility.DisplayDialog("KS Anti-Rip",
                    "This avatar is set to be protected, but the protection hit an error, so the upload was " +
                    "stopped:\n\n" + e.Message + "\n\nSee the Console for details.", "OK");
                return false;
            }
        }

        /// <summary>
        /// A fresh folder for this build's generated assets (scrambled meshes, rewritten clips, the lock). The SDK
        /// needs them to exist as assets while it packs the bundle; they are harmless to keep and the app's
        /// cleanup can clear old ones, so the hook does not race the bundler by deleting them here.
        /// </summary>
        static string EnsureBuildFolder()
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            return KawaiiStudioPaths.EnsureFolder(KawaiiStudioPaths.OutputFolder("Protected") + "/_build_" + stamp);
        }
    }
}
