// Test in Kawaii Studio -- build the selected avatar (or the open world scene) here and open it in the
// Kawaii Studio app's Scene Editor, playing: an avatar in Play mode, a world on the World toolkit's
// Photon sim.
//
// The bundle is prepared the way the VRChat SDK prepares one: the avatar is cloned and the SDK's
// preprocess callbacks run on the clone (Modular Avatar, VRCFury and NDMF hook in there), then Unity's
// BuildPipeline writes it for the active PC target. The SDK's own Build() is not used: it only exists
// while its Control Panel window is open. The SDK is reached by reflection, so this compiles without it.
//
// The bundle lands in Library/KawaiiStudio/TestBuilds (per project, never imported, never committed);
// the app copies it before opening it, so building again never fights the editor over the file.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    public static class KawaiiStudioTestBuild
    {
        public enum TargetKind { None, Avatar, World }

        private const string PrefPlay = "KawaiiStudio.Link.TestPlay";
        private const string TempFolder = "Assets/__KawaiiStudioTestBuild";

        /// <summary>Open the result playing (Play mode, or the Photon sim for a world).</summary>
        public static bool PlayOnOpen
        {
            get { return EditorUserSettings.GetConfigValue(PrefPlay) != "0"; }
            set { EditorUserSettings.SetConfigValue(PrefPlay, value ? "1" : "0"); }
        }

        public static string BuildFolder =>
            Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Library", "KawaiiStudio", "TestBuilds");

        [MenuItem("Kawaii Studio/Test in Kawaii Studio", priority = 2)]
        public static void TestFromMenu() => TestSelection();

        [MenuItem("GameObject/Kawaii Studio/Test in Kawaii Studio", false, 0)]
        private static void TestFromHierarchy() => TestSelection();

        /// <summary>What Test in Kawaii Studio would build now: the selected avatar (its descriptor may sit on a parent),
        /// else the open scene when it is a VRChat world.</summary>
        public static TargetKind Resolve(out GameObject avatar, out string label)
        {
            avatar = null;
            label = "";
            GameObject selected = Selection.activeGameObject;
            for (Transform t = selected != null ? selected.transform : null; t != null; t = t.parent)
            {
                if (HasComponent(t.gameObject, "VRCAvatarDescriptor"))
                {
                    avatar = t.gameObject;
                    label = avatar.name;
                    return TargetKind.Avatar;
                }
            }
            Scene scene = SceneManager.GetActiveScene();
            if (scene.IsValid() && scene.GetRootGameObjects().Any(r => r.GetComponentsInChildren<MonoBehaviour>(true).Any(c => c != null && c.GetType().Name == "VRCSceneDescriptor")))
            {
                label = scene.name;
                return TargetKind.World;
            }
            return TargetKind.None;
        }

        public static void TestSelection()
        {
            GameObject avatar;
            string label;
            TargetKind kind = Resolve(out avatar, out label);
            if (kind == TargetKind.None)
            {
                Tell("Select an avatar (a GameObject with a VRC Avatar Descriptor), or open a VRChat world scene.");
                return;
            }
            if (!KawaiiStudioLink.IsLinked)
            {
                Tell("This project is not linked with Kawaii Studio. Open Kawaii Studio > Kawaii Studio Link and click Connect.");
                KawaiiStudioLinkWindow.ShowWindow();
                return;
            }
            Run(kind, avatar, label, PlayOnOpen, null);
        }

        /// <summary>Build, then hand the bundle to the app. done(ok, message) runs on the main thread.</summary>
        public static void Run(TargetKind kind, GameObject avatar, string label, bool play, Action<bool, string> done)
        {
            string bundle;
            try
            {
                EditorUtility.DisplayProgressBar("Test in Kawaii Studio", "Building " + label, 0.3f);
                bundle = kind == TargetKind.World ? BuildWorld(SceneManager.GetActiveScene()) : BuildAvatar(avatar);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Tell("Test in Kawaii Studio: the build failed. " + e.Message);
                done?.Invoke(false, e.Message);
                return;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            long bytes = new FileInfo(bundle).Length;
            Debug.Log("[Kawaii Studio Link] built " + label + " -> " + bundle + " (" + bytes.ToString("N0") + " bytes)");
            KawaiiStudioLink.OpenInApp(bundle, label, kind == TargetKind.World ? "world" : "avatar", play, (ok, message) =>
            {
                if (!ok) Tell("Kawaii Studio did not open " + label + ": " + message);
                done?.Invoke(ok, message);
            });
        }

        // ------------------------------------------------------------------ builds

        public static string BuildAvatar(GameObject source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            RequirePcTarget();
            string name = KawaiiStudioLinkPaths.SafeName(source.name);
            GameObject clone = null;
            bool preprocessed = false;
            try
            {
                clone = Object.Instantiate(source);
                clone.name = source.name;
                clone.SetActive(true);
                preprocessed = true;
                if (!Preprocess(clone))
                    throw new InvalidOperationException("a VRChat build callback refused the avatar (the Console says which).");
                if (!AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.CreateFolder("Assets", Path.GetFileName(TempFolder));
                string prefab = TempFolder + "/" + name + ".prefab";
                bool saved;
                PrefabUtility.SaveAsPrefabAsset(clone, prefab, out saved);
                if (!saved) throw new InvalidOperationException("the prepared avatar could not be saved for the build.");
                return BuildBundle(prefab, "customavatar.unity3d", name + ".vrca");
            }
            finally
            {
                if (clone != null) Object.DestroyImmediate(clone);
                if (preprocessed) Postprocess();
                if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
            }
        }

        public static string BuildWorld(Scene scene)
        {
            RequirePcTarget();
            if (!scene.IsValid() || string.IsNullOrEmpty(scene.path)) throw new InvalidOperationException("Save the world scene first.");
            if (scene.isDirty && (Application.isBatchMode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() || scene.isDirty))
                throw new InvalidOperationException("Save the scene to test it: the build reads it from disk.");
            return BuildBundle(scene.path, "customscene.vrcw", KawaiiStudioLinkPaths.SafeName(scene.name) + ".vrcw");
        }

        private static string BuildBundle(string assetPath, string bundleName, string fileName)
        {
            string outDir = BuildFolder;
            Directory.CreateDirectory(outDir);
            // built where the VRChat SDK builds its own (Unity refuses to build a scene bundle into Library/), then copied
            string work = Path.Combine(Application.temporaryCachePath, "KawaiiStudioTestBuild");
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);
            var builds = new[] { new AssetBundleBuild { assetBundleName = bundleName, assetNames = new[] { assetPath } } };
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(work, builds,
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle,
                EditorUserBuildSettings.activeBuildTarget);
            string built = Path.Combine(work, bundleName);
            if (manifest == null || !File.Exists(built)) throw new InvalidOperationException("Unity could not build the bundle (the Console says why).");
            string dest = Path.Combine(outDir, fileName);
            File.Copy(built, dest, true);
            try { Directory.Delete(work, true); } catch { }
            return dest;
        }

        private static void RequirePcTarget()
        {
            BuildTarget t = EditorUserBuildSettings.activeBuildTarget;
            if (t != BuildTarget.StandaloneWindows64 && t != BuildTarget.StandaloneWindows)
                throw new InvalidOperationException("Switch the build target to Windows (PC) first: Kawaii Studio plays PC bundles.");
        }

        // VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks: what the SDK runs on the avatar it builds.
        private static bool Preprocess(GameObject clone)
        {
            MethodInfo m = Callbacks()?.GetMethod("OnPreprocessAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            if (m == null)
            {
                Debug.Log("[Kawaii Studio Link] no VRChat SDK build callbacks in this project: the avatar is built as it is");
                return true;
            }
            object result = m.Invoke(null, new object[] { clone });
            return !(result is bool) || (bool)result;
        }

        private static void Postprocess()
        {
            try
            {
                MethodInfo m = Callbacks()?.GetMethod("OnPostprocessAvatar", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (m != null) m.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Kawaii Studio Link] VRChat SDK post-build callbacks: " + e.Message);
            }
        }

        private static Type Callbacks()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = a.GetType("VRC.SDKBase.Editor.BuildPipeline.VRCBuildPipelineCallbacks", false);
                if (t != null) return t;
            }
            return null;
        }

        private static bool HasComponent(GameObject go, string typeName) =>
            go.GetComponents<Component>().Any(c => c != null && c.GetType().Name == typeName);

        private static void Tell(string message)
        {
            if (Application.isBatchMode) Debug.LogError("[Kawaii Studio Link] " + message);
            else EditorUtility.DisplayDialog("Test in Kawaii Studio", message, "OK");
        }

        // ------------------------------------------------------------------ headless

        /// <summary>
        /// Headless entry (tests, CI): -batchmode -executeMethod KawaiiStudio.KawaiiStudioTestBuild.CommandLineTest
        /// without -quit. KS_LINK_TEST = the avatar prefab (.prefab) or world scene (.unity) to test, KS_LINK_TEST_PLAY
        /// (default 1). Waits for the link (KS_LINK_TIMEOUT, default 300 s), builds, asks the app to open it, exits
        /// 0 when the app accepted it.
        /// </summary>
        public static void CommandLineTest()
        {
            string target = Environment.GetEnvironmentVariable("KS_LINK_TEST") ?? "";
            bool play = Environment.GetEnvironmentVariable("KS_LINK_TEST_PLAY") != "0";
            int timeout;
            if (!int.TryParse(Environment.GetEnvironmentVariable("KS_LINK_TIMEOUT"), out timeout)) timeout = 300;
            double until = EditorApplication.timeSinceStartup + timeout;
            Debug.Log("[Kawaii Studio Link] headless test of " + target + " (play " + play + ")");
            KawaiiStudioLink.Connect();
            bool started = false;
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (started) return;
                if (EditorApplication.timeSinceStartup > until)
                {
                    Debug.LogError("[Kawaii Studio Link] headless test: no link within " + timeout + " s (" + KawaiiStudioLink.Detail + ")");
                    EditorApplication.Exit(2);
                    return;
                }
                if (!KawaiiStudioLink.IsLinked || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                started = true;
                EditorApplication.update -= tick;
                TargetKind kind;
                GameObject avatar = null;
                string label;
                if (target.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                {
                    Scene scene = EditorSceneManager.OpenScene(target, OpenSceneMode.Single);
                    kind = TargetKind.World;
                    label = scene.name;
                }
                else
                {
                    avatar = AssetDatabase.LoadAssetAtPath<GameObject>(target);
                    if (avatar == null)
                    {
                        Debug.LogError("[Kawaii Studio Link] headless test: no prefab at " + target);
                        EditorApplication.Exit(1);
                        return;
                    }
                    kind = TargetKind.Avatar;
                    label = avatar.name;
                }
                Run(kind, avatar, label, play, (ok, message) =>
                {
                    Debug.Log("[Kawaii Studio Link] headless test " + (ok ? "accepted" : "refused") + ": " + message);
                    EditorApplication.Exit(ok ? 0 : 1);
                });
            };
            EditorApplication.update += tick;
        }
    }
}
