using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3A.Editor;
using VRC.SDKBase.Editor;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// 3.17.1, FULL LOCAL (owner, 2026-11-20: "si le bundle est trop gros pour être protégé, ça demande : voulez-vous utiliser
    /// l'option FULL LOCAL ? -- le système SDK TEST de Unity, l'avatar en full local only, impossible de le ripper").
    ///
    /// An avatar that already weighs VRChat's limit before protection has no room for it: the upload would be refused
    /// anyway. In its place KS Anti-Rip offers VRChat's own Build &amp; Test: the SDK builds the avatar into this PC's local
    /// test avatars, it is never uploaded, so nobody can download it, and its owner wears it from the SDK test avatars in
    /// game. The SDK checks no size for a Build &amp; Test (VRCSdkControlPanelAvatarBuilder: its size checks run in Upload only).
    ///
    /// The build hook cannot turn the upload it is running into a test build, so it stops the upload and asks for a Build &amp;
    /// Test of the same avatar once the SDK is idle again; that one build skips the protection (it never leaves the PC).
    /// Everything goes through the SDK's public builder API; nothing here can upload anything.
    /// </summary>
    public static class KSAntiRipFullLocal
    {
        static string _pending = "";
        static GameObject _target;
        static double _notBefore, _deadline;

        /// <summary>The SDK's own kind of the build in progress (VRC_SdkBuilder.ActiveBuildType): "Publish", "Test" or "None";
        /// "" when this SDK does not say.</summary>
        public static string ActiveBuildType()
        {
            try
            {
                if (!_looked)
                {
                    _looked = true;
                    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                    foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        if (!asm.GetName().Name.StartsWith("VRC", StringComparison.OrdinalIgnoreCase)) continue;
                        Type t;
                        try { t = asm.GetTypes().FirstOrDefault(x => x.Name == "VRC_SdkBuilder"); }
                        catch { continue; }
                        if (t == null) continue;
                        _property = t.GetProperty("ActiveBuildType", F);
                        _field = t.GetField("ActiveBuildType", F);
                        break;
                    }
                }
                object v = _property != null ? _property.GetValue(null) : _field?.GetValue(null);
                return v != null ? v.ToString() : "";
            }
            catch { }
            return "";
        }

        static bool _looked;
        static PropertyInfo _property;
        static FieldInfo _field;

        /// <summary>Bench only: as if FULL LOCAL had been chosen for this avatar (the SDK panel is not there in batch mode).</summary>
        public static void PendingForTests(string blueprint) { _pending = blueprint ?? ""; _target = null; }

        /// <summary>The build hook, on a Build &amp; Test: true (once) when it is the full-local build asked for this avatar.</summary>
        public static bool TakePending(string blueprint)
        {
            if (_pending.Length == 0 || _pending != (blueprint ?? "")) return false;
            _pending = "";
            _target = null;
            return true;
        }

        /// <summary>
        /// Asks the SDK for a Build &amp; Test of the avatar the panel was uploading, as soon as the stopped upload has wound
        /// down. False when the SDK panel's builder is not there (the owner then clicks Build &amp; Test himself).
        /// </summary>
        public static bool Request(string blueprint)
        {
            try
            {
                if (VRCSdkControlPanel.window == null ||
                    !VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder) || builder == null)
                    return false;
                _target = builder.SelectedAvatar;
                _pending = blueprint ?? "";
                _notBefore = EditorApplication.timeSinceStartup + 2.0;
                _deadline = EditorApplication.timeSinceStartup + 180.0;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < _notBefore) return;
            if (now > _deadline)
            {
                EditorApplication.update -= Tick;
                _pending = "";
                KSAntiRipClient.Report("FULL LOCAL: the VRChat SDK stayed busy, so the Build & Test was not started. Choose \"Build & Test\" in the SDK panel.");
                return;
            }
            if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder) || builder == null) return;
            if (builder.BuildState == SdkBuildState.Building) return;   // the stopped upload is still winding down
            EditorApplication.update -= Tick;

            GameObject target = _target != null ? _target : FindByBlueprint(_pending);
            if (target == null)
            {
                _pending = "";
                KSAntiRipClient.Report("FULL LOCAL: the avatar is no longer in the open scene, so the Build & Test was not started.");
                return;
            }
            Start(builder, target);
        }

        static async void Start(IVRCSdkAvatarBuilderApi builder, GameObject target)
        {
            try
            {
                KSAntiRipClient.Report("FULL LOCAL: the VRChat SDK builds \"" + target.name + "\" with Build & Test now.");
                await builder.BuildAndTest(target);
                KSAntiRipClient.Report("FULL LOCAL: done. The avatar is in this PC's SDK test avatars (in game: Avatars, Other, SDK test). " +
                                       "It was never uploaded: nobody can download it.");
            }
            catch (Exception e)
            {
                _pending = "";
                KSAntiRipClient.Report("FULL LOCAL: the Build & Test failed: " + e.Message);
            }
        }

        static GameObject FindByBlueprint(string blueprint)
        {
            if (string.IsNullOrEmpty(blueprint)) return null;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (VRCAvatarDescriptor d in root.GetComponentsInChildren<VRCAvatarDescriptor>(true))
                    {
                        var pm = d.GetComponent<global::VRC.Core.PipelineManager>();
                        if (pm != null && pm.blueprintId == blueprint && d.gameObject.activeInHierarchy) return d.gameObject;
                    }
            }
            return null;
        }
    }
}
