using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// 3.17, THE REAL SIZE OF A PROTECTED BUILD (owner, 2026-11-20: "monte à 20 MB avec encore plus de sécu, mais ça doit
    /// analyser si ça va passer la limite VRChat : 200 compressé, 500 décompressé -- l'app devrait déterminer ce qui est
    /// disponible pour la protection, comme les bits restants pour la clé").
    ///
    /// Right after the VRChat SDK has built a protected avatar -- before it checks the size and uploads -- this asks the
    /// SDK's OWN checks what the bundle weighs (compressed: the file; uncompressed: Unity's build report) and what its limits
    /// are, and sends the Kawaii Studio app those numbers with what this build's protection added (its own estimate,
    /// <see cref="Estimate"/>). From them the app knows the avatar's size without the protection, so the next upload's
    /// "Max size added" can be the room that is really left under both limits.
    ///
    /// The SDK's checks are reached by reflection: an SDK without them still gets a report (the file's size, the limits
    /// VRChat has used since 2023), and nothing here can change or stop a build.
    /// </summary>
    public static class KSAntiRipSizes
    {
        // VRChat's PC avatar limits (VRC.ValidationHelpers): 200 MB compressed, 500 MB uncompressed
        const long DefaultLimitCompressed = 200L * 1048576L, DefaultLimitUncompressed = 500L * 1048576L;

        static string _blueprint = "";
        static long _addedCompressed, _addedUncompressed;

        /// <summary>The build hook, for a protected avatar: its blueprint id (the app's key for its sizes).</summary>
        public static void Begin(string blueprint)
        {
            _blueprint = blueprint ?? "";
            _addedCompressed = _addedUncompressed = 0;
        }

        /// <summary>The processor: what its protection adds, compressed and uncompressed, in bytes (estimates).</summary>
        public static void Estimate(long compressed, long uncompressed)
        {
            _addedCompressed = Math.Max(0, compressed);
            _addedUncompressed = Math.Max(0, uncompressed);
        }

        /// <summary>The last protection's own estimates (bench: compared with what real builds measure).</summary>
        public static long AddedCompressed => _addedCompressed;
        public static long AddedUncompressed => _addedUncompressed;

        /// <summary>
        /// The bundle's sizes and limits as the VRChat SDK counts them. False only when not even the file can be read.
        /// <paramref name="uncompressed"/> is 0 when the SDK cannot say.
        /// </summary>
        public static bool Measure(string bundlePath, out long compressed, out long uncompressed, out long limitCompressed, out long limitUncompressed)
        {
            compressed = uncompressed = 0;
            limitCompressed = DefaultLimitCompressed;
            limitUncompressed = DefaultLimitUncompressed;
            if (string.IsNullOrEmpty(bundlePath) || !File.Exists(bundlePath)) return false;
            compressed = new FileInfo(bundlePath).Length;
            try
            {
                Type helpers = Find("VRC.SDKBase.Editor.Validation.ValidationEditorHelpers");
                Type limits = Find("VRC.ValidationHelpers");
                if (helpers == null) return true;
                MethodInfo fileCheck = helpers.GetMethod("CheckIfAssetBundleFileTooLarge", BindingFlags.Public | BindingFlags.Static);
                MethodInfo rawCheck = helpers.GetMethod("CheckIfUncompressedAssetBundleFileTooLarge", BindingFlags.Public | BindingFlags.Static);
                MethodInfo isMobile = helpers.GetMethod("IsMobilePlatform", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (fileCheck == null || isMobile == null) return true;
                Type contentType = fileCheck.GetParameters()[0].ParameterType;
                object avatar = Enum.Parse(contentType, "Avatar");
                object mobile = isMobile.Invoke(null, null);

                var a = new object[] { avatar, bundlePath, 0, mobile };
                fileCheck.Invoke(null, a);
                if (a[2] is int c && c > 0) compressed = c;
                if (rawCheck != null)
                {
                    var b = new object[] { avatar, 0, mobile };
                    rawCheck.Invoke(null, b);
                    if (b[1] is int u && u > 0) uncompressed = u;
                }
                MethodInfo limit = limits?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                         .FirstOrDefault(m => m.Name == "GetAssetBundleSizeLimit" && m.GetParameters().Length == 3);
                if (limit != null)
                {
                    if (limit.Invoke(null, new[] { avatar, mobile, (object)true }) is int lc && lc > 0) limitCompressed = lc;
                    if (limit.Invoke(null, new[] { avatar, mobile, (object)false }) is int lu && lu > 0) limitUncompressed = lu;
                }
            }
            catch
            {
                // another SDK: the file's size and the known limits stand
            }
            return true;
        }

        /// <summary>
        /// The SDK built the protected avatar (KSAntiRipSdkReport, on its build-success event, before its size checks):
        /// the sizes go to the app. Best effort: an app before v679 does not know the route and nothing changes.
        /// </summary>
        public static void AfterBuild(string bundlePath)
        {
            try
            {
                if (!Measure(bundlePath, out long compressed, out long uncompressed, out long limitC, out long limitU)) return;
                var sb = new StringBuilder();
                sb.Append("{\"blueprint\":\"").Append(_blueprint.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"')
                  .Append(",\"compressed\":").Append(compressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"uncompressed\":").Append(uncompressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"limitCompressed\":").Append(limitC.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"limitUncompressed\":").Append(limitU.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"addedCompressed\":").Append(_addedCompressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"addedUncompressed\":").Append(_addedUncompressed.ToString(CultureInfo.InvariantCulture))
                  .Append('}');
                KSAntiRipClient.Sizes(sb.ToString());
            }
            catch
            {
                // a report must never hold up or fail a build
            }
        }

        static Type Find(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }
    }
}
