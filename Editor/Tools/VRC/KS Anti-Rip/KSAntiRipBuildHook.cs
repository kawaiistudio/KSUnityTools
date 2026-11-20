using System;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

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
    ///
    /// THIS CLASS IS A SHELL. The protection itself is delivered by the Kawaii Studio app and reached through
    /// <see cref="KSAntiRipModuleLoader"/>; see that file for why the SDK-facing callback has to live here while the
    /// code it runs does not. What stays here is only what the SDK must see at editor load, the one decision that
    /// says whether this upload is ours at all, and the build folder's plumbing.
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
                KSAntiRipSdkReport.Forget();
                return true;   // not a KS-protected upload: leave it exactly alone
            }

            // From here it IS a protected upload, so the protection has to be here. It is not in this package: the
            // app delivers it to an entitled session (KSAntiRipModuleLoader). A protection that cannot be had must
            // STOP the upload -- never let an avatar its owner marked as protected go out unprotected.
            if (!KSAntiRipModuleLoader.Ensure(out string why))
            {
                KSAntiRipSdkReport.Forget();   // the SDK's "build failed" that follows says nothing more
                Debug.LogError("[KS ANTI-RIP] the protection is unavailable, so the upload was stopped: " + why);
                KSAntiRipClient.Report("the protection is unavailable, so the upload was stopped: " + why);
                if (!Application.isBatchMode)
                    EditorUtility.DisplayDialog("KS Anti-Rip",
                        "This avatar is set to be protected, but the protection could not be loaded, so the upload " +
                        "was stopped:\n\n" + why +
                        "\n\nKS Anti-Rip never uploads an avatar you marked as protected without its protection.", "OK");
                return false;
            }

            return KSAntiRipModuleLoader.Preprocess(avatarGameObject);
        }

        const string BuildPrefix = "_build_";

        /// <summary>
        /// A fresh folder for this build's generated assets (scrambled meshes, rewritten clips, the lock). The SDK
        /// needs them to exist as assets while it packs the bundle, so the protection cannot delete them itself: that
        /// is <see cref="KSAntiRipBuildCleanup"/>'s job, once the bundle is built. What an EARLIER build left behind (an
        /// upload that failed before the cleanup could run) goes first, so the project never holds more than the
        /// build in progress.
        ///
        /// Folder plumbing, not protection: it stays in the package, where the cleanup callback and the bench's
        /// KSCleanupTest can reach it, and the delivered module calls it.
        /// </summary>
        public static string EnsureBuildFolder()
        {
            CleanBuildFolders();
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 4);
            return KawaiiStudioPaths.EnsureFolder(KawaiiStudioPaths.OutputFolder("Protected") + "/" + BuildPrefix + stamp);
        }

        /// <summary>
        /// Deletes the build folders this hook made (Assets/Kawaii Studio/Protected/_build_*), and returns how many.
        ///
        /// Owner, 2026-11-17: "pourquoi ça garde le build temporaire dans le projet Unity?" -- each protected upload
        /// left its scrambled meshes there, 812 MB on his avatar, one more folder per upload, for nothing: once the
        /// bundle is built they are never read again, and the protected copy has no place in the project.
        ///
        /// Only folders whose name starts with _build_, directly under Protected: they are generated and named here,
        /// nothing the user made lives in them. Never creates the workspace on a project that does not use it.
        /// </summary>
        public static int CleanBuildFolders()
        {
            string root = KawaiiStudioPaths.Workspace + "/Protected";
            if (!AssetDatabase.IsValidFolder(root)) return 0;
            int deleted = 0;
            foreach (string sub in AssetDatabase.GetSubFolders(root))
            {
                string name = sub.Substring(sub.LastIndexOf('/') + 1);
                if (!name.StartsWith(BuildPrefix, StringComparison.Ordinal)) continue;
                if (AssetDatabase.DeleteAsset(sub)) deleted++;
                else Debug.LogWarning("[KS ANTI-RIP] could not remove the build folder " + sub);
            }
            return deleted;
        }
    }

    /// <summary>
    /// When the VRChat SDK has finished building, the build's generated assets have done their job and go: the bundle
    /// is packed, nothing reads them again. The same moment NDMF / Modular Avatar delete their own temporary assets.
    /// After <see cref="KSAntiRipAssetNamesRestore"/> (order 0), which still looks at the assets it renamed.
    /// </summary>
    public sealed class KSAntiRipBuildCleanup : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 1000;

        public void OnPostprocessAvatar()
        {
            try
            {
                int n = KSAntiRipBuildHook.CleanBuildFolders();
                if (n > 0) Debug.Log("[KS ANTI-RIP] removed the build's temporary assets from the project (" + n + " folder" + (n == 1 ? "" : "s") + ")");
            }
            catch (Exception e)
            {
                // Never fail an upload over housekeeping: the next protected build clears it.
                Debug.LogWarning("[KS ANTI-RIP] could not remove the build's temporary assets: " + e.Message);
            }
        }
    }
}
