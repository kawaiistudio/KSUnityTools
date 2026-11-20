using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Where the protection comes from: the Kawaii Studio app, per session, in memory.
    ///
    /// WHY (owner, 2026-11-20). Up to 3.17.1 the whole protection shipped as readable C# in this package, which is
    /// public on GitHub: anyone could clone it and read exactly how an avatar is closed and reopened. The apply code
    /// now lives in the app instead. The app hands it to an entitled session over the loopback bridge, this loads it
    /// in memory for that session only, and nothing of it is ever written to disk.
    ///
    /// SAID PLAINLY, because it matters: this does NOT make the protection unbreakable, and we do not claim it does.
    /// The restore code ships compiled inside every protected bundle, so its shape can always be read back out of a
    /// .vrca, and the module runs inside the user's own Unity, so an entitled user can dump it from memory. What this
    /// buys is real but bounded: the zero-effort read (clone the repo) is gone, the protection is gated on the
    /// account, and it can be fixed or improved without a VCC release. The only hard endpoint is the key, and the
    /// server entitlement that hands it out.
    ///
    /// WHY THE SDK-FACING CALLBACKS STAY IN THE PACKAGE. VRChat's SDK finds its build callbacks once, at editor load:
    /// VRCBuildPipelineCallbacks.Initialize is an [InitializeOnLoadMethod] that walks the assemblies loaded AT THAT
    /// MOMENT looking for IVRCSDKPreprocessAvatarCallback. An assembly brought in later with a byte load is in the
    /// AppDomain but was never scanned, so a callback inside the module would simply never run -- measured on the
    /// bench, not assumed. That is why <see cref="KSAntiRipBuildHook"/> and the other SDK-facing types are compiled
    /// into this package and only their BODIES are delegated here.
    /// </summary>
    public static class KSAntiRipModuleLoader
    {
        /// <summary>
        /// KS Anti-Rip's version as this package reports it (the pairing string the app reads, and the Unity window's
        /// banner). It lived on KSAntiRipProcessor while that class was in the package; the delivered module carries
        /// its own version, which <see cref="ModuleVersion"/> shows once one is loaded.
        /// </summary>
        public const string Version = "5.1.0";

        /// <summary>The one type the whole shell talks to. Its methods are the entire reflection surface.</summary>
        const string EntryTypeName = "KawaiiStudio.VRC.AntiRip.KSAntiRipModuleEntry";

        static Type _entry;
        static readonly Dictionary<string, MethodInfo> Methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        /// <summary>A protection module is loaded in this session. The postprocess shells ask before delegating:
        /// with no module nothing was protected, so there is nothing for them to undo either.</summary>
        public static bool Loaded => _entry != null;

        /// <summary>The delivered module's own version, once one is loaded; empty before that.</summary>
        public static string ModuleVersion { get; private set; } = "";

        /// <summary>
        /// Makes sure the protection is here, fetching it from the app if it is not. False with
        /// <paramref name="error"/> set to a sentence worth showing the user -- the caller then STOPS the upload.
        /// </summary>
        public static bool Ensure(out string error)
        {
            error = "";
            if (_entry != null) return true;

            // Already in this editor: the module loaded for an earlier upload in this session, or -- on the bench --
            // the apply code is compiled into the project instead of being delivered. In a shipped package the apply
            // types do not exist until this method loads them, so this can never stand in for the fetch.
            _entry = FindLoaded();
            if (_entry != null) return true;

            KSAntiRipClient.ModuleReply reply = KSAntiRipClient.Module();
            if (!reply.ok)
            {
                error = reply.error;
                return false;
            }
            if (reply.bytes == null || reply.bytes.Length == 0)
            {
                error = "Kawaii Studio sent an empty protection module. Update the app, then try again.";
                return false;
            }
            if (!SdkAllows(reply.minSdk, reply.maxSdk, out error)) return false;

            try
            {
                // The DEFAULT, non-collectible load context, so the module shares the one set of Unity and VRChat SDK
                // types this editor already has -- load it anywhere else and its GameObject is not our GameObject.
                // Unity's editor is Mono and has no System.Runtime.Loader, so this is Assembly.Load(byte[]), which in
                // Mono is that same default context. (The warning we carry about Assembly.Load is about COLLECTIBLE
                // contexts on .NET Core, which this is not.)
                Assembly module = Assembly.Load(reply.bytes);
                _entry = module.GetType(EntryTypeName, false);
                if (_entry == null)
                {
                    error = "The protection module Kawaii Studio sent is not the one this version of KS Unity Tools " +
                            "expects. Update both, then try again.";
                    return false;
                }
                ModuleVersion = reply.version ?? "";
                Methods.Clear();
                Debug.Log("[KS ANTI-RIP] protection loaded from Kawaii Studio" +
                          (ModuleVersion.Length > 0 ? " (" + ModuleVersion + ")" : "") + ", in memory for this session");
                return true;
            }
            catch (Exception e)
            {
                _entry = null;
                error = "The protection module could not be loaded: " + e.Message;
                return false;
            }
        }

        static Type FindLoaded()
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = a.GetType(EntryTypeName, false);
                    if (t != null) return t;
                }
                catch
                {
                    // an assembly that cannot be reflected over is not ours
                }
            }
            return null;
        }

        // ── the reflection surface: five entry points, nothing else ─────────────────────────────────

        /// <summary>The whole of a protected upload. Returns what the SDK callback returns: false stops the upload.</summary>
        public static bool Preprocess(GameObject avatar) => (bool)Invoke("Preprocess", avatar);

        /// <summary>The SDK finished the build: the assets renamed in memory for it get their real names back.</summary>
        public static void RestoreAssetNames() => Invoke("RestoreAssetNames");

        /// <summary>The SDK built the protected bundle: its real sizes go to the app (KSAntiRipSizes).</summary>
        public static void AfterBuild(string bundlePath) => Invoke("AfterBuild", bundlePath);

        /// <summary>VRChat accepted the upload: the recovery-blacklist lines go into the avatar's description.</summary>
        public static void ApplyDescription(string id) => Invoke("ApplyDescription", id);

        /// <summary>The app's questions that need the protection itself: antirip.check / fix / protect.</summary>
        public static string Link(string tool, string argsJson) => (string)Invoke("Link", tool, argsJson);

        static object Invoke(string name, params object[] args)
        {
            MethodInfo m;
            if (!Methods.TryGetValue(name, out m))
            {
                if (_entry == null) throw new InvalidOperationException("the KS Anti-Rip protection module is not loaded");
                m = _entry.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                if (m == null)
                    throw new MissingMethodException("the KS Anti-Rip protection module has no " + name +
                                                     " -- the app and KS Unity Tools are not a matching pair");
                Methods[name] = m;
            }
            try
            {
                return m.Invoke(null, args);
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // The module's own failure, not a reflection failure: let the caller see the real exception, with the
                // message it wrote for the user.
                throw e.InnerException;
            }
        }

        // ── the SDK the module was built for ────────────────────────────────────────────────────────

        /// <summary>
        /// The module is compiled against one VRChat SDK. Running apply code built for a different SDK against this
        /// project's is how a protected upload breaks in ways nobody can read, so the app says which SDKs its module
        /// is good for and anything outside that stops the upload instead.
        ///
        /// An SDK version we cannot read is NOT treated as a mismatch: refusing every upload because a package layout
        /// changed would be worse than running, and the shell itself only touches SDK interfaces that have been
        /// constant across 3.5-3.10.
        /// </summary>
        static bool SdkAllows(string minSdk, string maxSdk, out string error)
        {
            error = "";
            string have = SdkVersion();
            if (have.Length == 0) return true;
            if (minSdk != null && minSdk.Length > 0 && Compare(have, minSdk) < 0)
            {
                error = "This project's VRChat SDK (" + have + ") is older than the " + minSdk +
                        " the protection needs. Update the VRChat SDK, then try again.";
                return false;
            }
            if (maxSdk != null && maxSdk.Length > 0 && Compare(have, maxSdk) > 0)
            {
                error = "This project's VRChat SDK (" + have + ") is newer than the " + maxSdk +
                        " this protection was built for. Update Kawaii Studio, then try again.";
                return false;
            }
            return true;
        }

        static string _sdk;

        /// <summary>com.vrchat.base's version, from the package that owns the SDK assembly; "" when it cannot be read.</summary>
        static string SdkVersion()
        {
            if (_sdk != null) return _sdk;
            _sdk = "";
            try
            {
                Assembly sdk = typeof(global::VRC.SDKBase.IEditorOnly).Assembly;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(sdk);
                if (info != null && !string.IsNullOrEmpty(info.version)) _sdk = info.version;
            }
            catch
            {
                // an SDK installed some other way: no gate rather than a wrong one
            }
            return _sdk;
        }

        /// <summary>Dot-separated numbers, compared part by part; anything unparsable counts as 0.</summary>
        static int Compare(string a, string b)
        {
            string[] x = Core(a).Split('.'), y = Core(b).Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length && int.TryParse(x[i], out int xv) ? xv : 0;
                int q = i < y.Length && int.TryParse(y[i], out int yv) ? yv : 0;
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        /// <summary>"3.10.6-beta.1" -> "3.10.6": a pre-release tag is not a version number.</summary>
        static string Core(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            int cut = v.IndexOfAny(new[] { '-', '+' });
            return (cut >= 0 ? v.Substring(0, cut) : v).Trim();
        }
    }
}
