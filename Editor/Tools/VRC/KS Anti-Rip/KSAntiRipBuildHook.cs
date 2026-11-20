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

        /// <summary>For the hook test only: a plan source and a report sink in place of the Kawaii Studio app, so the
        /// hook runs whole -- the exact code an upload runs -- without the app. Null in every real build. Public so the
        /// test suite can live outside this assembly (it is kept out of the shipped package); it does nothing on its
        /// own and is never read unless a test sets it.</summary>
        public static KSAntiRipProcessor.IPlanSource PlanSourceForTests;
        public static Action<string, System.Collections.Generic.IList<string>> ReportForTests;

        static void Report(string message, System.Collections.Generic.IList<string> details = null)
        {
            if (ReportForTests != null) ReportForTests(message, details);
            else KSAntiRipClient.Report(message, details);
        }

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

            var pipeline = avatarGameObject.GetComponent<global::VRC.Core.PipelineManager>();
            string blueprintId = pipeline != null ? pipeline.blueprintId : "";
            // 3.17.1, FULL LOCAL: the Build & Test this hook asked for when the avatar was too big to be protected. It never
            // leaves this PC (a test build is not uploaded), so it is built as it is, without the protection.
            bool testBuild = KSAntiRipFullLocal.ActiveBuildType() == "Test";
            if (testBuild && KSAntiRipFullLocal.TakePending(blueprintId))
            {
                UnityEngine.Object.DestroyImmediate(mark);
                KSAntiRipSdkReport.Forget();
                Report("FULL LOCAL: Build & Test of the avatar as it is, without the protection: it stays on this PC and is never uploaded");
                return true;
            }

            // From here the app's KS ANTI-RIP log hears how this upload ends (3.12.2), and (3.17) the build's real sizes,
            // under the avatar's blueprint id (the same on the SDK's build copy as on the avatar the app lists)
            KSAntiRipSdkReport.Watch();
            KSAntiRipSizes.Begin(blueprintId);
            try
            {
                // Strip the mark first: it must never ship, and if protection throws we still want it gone.
                var settings = new KSAntiRipProcessor.Settings
                {
                    strength = mark.strength,
                    restoreGroups = mark.restoreGroups,
                    syncKey = mark.syncKey,
                    hideWhileLocked = mark.hideWhileLocked,
                    freezeWhileLocked = mark.freezeWhileLocked,
                    lockedBadge = mark.lockedBadge,
                    obfuscateNames = mark.obfuscateNames,
                    maxAddedMegabytes = mark.maxAddedMegabytes,
                    maxAddedUncompressedMegabytes = mark.maxAddedUncompressedMegabytes,
                    shaderRestore = mark.shaderRestore,
                    renameBlendShapes = mark.renameBlendShapes,
                    longKey = mark.longKey,
                    protectStaticMeshes = mark.protectStaticMeshes,
                };
                bool autoSize = mark.autoSize;
                UnityEngine.Object.DestroyImmediate(mark);

                string folder = EnsureBuildFolder();

                // 3.17.1, AUTO: measure the avatar as it stands now -- after VRCFury, Modular Avatar and NDMF, as VRChat will
                // build it -- and give the protection exactly the room left under VRChat's limits (KSAntiRipSizes.AutoCaps).
                // A measure that fails keeps the caps the app sent (the room its last upload left), and says so.
                // A Build & Test is not uploaded and the SDK checks no size for it: the caps the app sent stand. Nor is a
                // play-mode build (VRCFury / NDMF run these callbacks when you press Play), and Unity cannot build a bundle
                // in play mode anyway (owner's editor, 2026-11-20: "Building AssetBundles while in play mode is not allowed").
                string autoNote = "";
                if (autoSize && !testBuild && EditorApplication.isPlayingOrWillChangePlaymode)
                    Debug.Log("[KS ANTI-RIP] Auto size: play mode, nothing is uploaded -- the size the app sent stands (" +
                              settings.maxAddedMegabytes + " MB)");
                else if (autoSize && !testBuild)
                {
                    var t0 = DateTime.Now;
                    if (KSAntiRipSizes.AutoCaps(avatarGameObject, folder, out int capC, out int capU, out autoNote))
                    {
                        settings.maxAddedMegabytes = capC;
                        settings.maxAddedUncompressedMegabytes = capU;
                    }
                    autoNote = "Auto size: " + autoNote + " (" + (int)(DateTime.Now - t0).TotalSeconds + " s)";
                    Debug.Log("[KS ANTI-RIP] " + autoNote);
                    Report(autoNote);

                    // 3.17.1: BUNDLE TOO BIG. The avatar alone is at VRChat's limit: there is no room for the protection and
                    // the SDK would refuse the upload anyway. Stop here (before minutes of protection for nothing) and offer
                    // FULL LOCAL: VRChat's Build & Test, which is never uploaded (KSAntiRipFullLocal).
                    if (KSAntiRipSizes.NoRoom)
                    {
                        KSAntiRipSdkReport.Forget();
                        CleanBuildFolders();
                        string why = "Bundle too big: this avatar cannot be protected. Before protection it already weighs " +
                                     Mb(KSAntiRipSizes.PreCompressed) + " MB of VRChat's " + Mb(KSAntiRipSizes.LimitCompressedSeen) + " MB" +
                                     (KSAntiRipSizes.PreUncompressed > 0 ? " (" + Mb(KSAntiRipSizes.PreUncompressed) + " of " + Mb(KSAntiRipSizes.LimitUncompressedSeen) + " MB uncompressed)" : "") +
                                     ", which leaves no room for the protection.";
                        Report("the upload was stopped: " + why);
                        bool local = !Application.isBatchMode && EditorUtility.DisplayDialog("KS Anti-Rip: bundle too big",
                            why + "\n\nUse the FULL LOCAL option instead?\n\nThe VRChat SDK builds the avatar with Build & Test: it stays " +
                            "on this PC and is never uploaded, so nobody can download or rip it from VRChat. You wear it in game " +
                            "from your SDK test avatars (Avatars > Other).",
                            "Use Full Local (Build & Test)", "Cancel the upload");
                        if (local)
                        {
                            if (KSAntiRipFullLocal.Request(blueprintId))
                                Report("FULL LOCAL chosen: the Build & Test starts as soon as the SDK is ready");
                            else
                                EditorUtility.DisplayDialog("KS Anti-Rip", "The VRChat SDK panel is not open, so the Build & Test could not " +
                                                            "start. Open it and choose \"Build & Test\".", "OK");
                        }
                        return false;
                    }
                }

                var result = new KSAntiRipProcessor.Result();
                KSAntiRipProcessor.ProtectInPlace(avatarGameObject, settings, PlanSourceForTests, folder, result);

                if (!result.ok)
                {
                    // A protection that could not run must STOP the upload, never let a half-scrambled or
                    // unprotected avatar go out silently under the owner's name.
                    Debug.LogError("[KS ANTI-RIP] " + result.message);
                    Report("the protection could not run, so the upload was stopped: " + result.message);
                    KSAntiRipSdkReport.Forget();   // the SDK's "build failed" that follows says nothing more
                    EditorUtility.DisplayDialog("KS Anti-Rip",
                        "This avatar is set to be protected, but the protection could not run, so the upload was " +
                        "stopped:\n\n" + result.message, "OK");
                    return false;
                }

                // The asset names (materials, textures, meshes, sounds) go with the rest of the signposts. Only after
                // the protection succeeded, and only with name obfuscation on, like the hierarchy renaming.
                string names = "";
                if (settings.obfuscateNames)
                    names = ", renamed " + KSAntiRipAssetNames.Apply(avatarGameObject, folder, result.warnings);

                Debug.Log($"[KS ANTI-RIP] protected at upload: {result.meshesProtected} mesh(es), " +
                          $"{result.verticesProtected:N0} vertices{names}, key -> {result.keyPath}");
                Report($"protected {result.meshesProtected} mesh(es), {result.verticesProtected:N0} vertices{names}; " +
                                       "the VRChat SDK builds it now", result.warnings);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("[KS ANTI-RIP] build hook failed: " + e);
                Report("the protection hit an error, so the upload was stopped: " + e.Message);
                KSAntiRipSdkReport.Forget();
                EditorUtility.DisplayDialog("KS Anti-Rip",
                    "This avatar is set to be protected, but the protection hit an error, so the upload was " +
                    "stopped:\n\n" + e.Message + "\n\nSee the Console for details.", "OK");
                return false;
            }
        }

        static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        const string BuildPrefix = "_build_";

        /// <summary>
        /// A fresh folder for this build's generated assets (scrambled meshes, rewritten clips, the lock). The SDK
        /// needs them to exist as assets while it packs the bundle, so the hook cannot delete them itself: that is
        /// <see cref="KSAntiRipBuildCleanup"/>'s job, once the bundle is built. What an EARLIER build left behind (an
        /// upload that failed before the cleanup could run) goes first, so the project never holds more than the
        /// build in progress.
        /// </summary>
        static string EnsureBuildFolder()
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
