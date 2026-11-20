using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

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
        const long MiB = 1048576L;

        /// <summary>3.17.1: what Auto keeps free under each limit (the build is never measured to the byte twice the same).</summary>
        public const long MarginCompressed = 1 * MiB, MarginUncompressed = 2 * MiB;

        /// <summary>
        /// 3.17.1: what a protection costs beyond what it counts (its animator, its lock, the restored fallback shaders, the
        /// textures it lays out...), on an avatar never protected here before. Measured on the bench (Layla Wendigo: ~3 MB,
        /// ~20 MB uncompressed) and kept above it; from the second upload on, the avatar's own measure replaces it.
        /// </summary>
        public static long DefaultOverheadCompressed = 4 * MiB, DefaultOverheadUncompressed = 24 * MiB;
        /// <summary>3.17.1: the share of its own count the protection may be off by (3 %, 5 % uncompressed).</summary>
        const double CountError = 1.03, CountErrorUncompressed = 1.05;

        /// <summary>Bench only: limits in place of VRChat's (0 = VRChat's), so a test can make room tight. Never set in a real build.</summary>
        public static long LimitCompressedForTests, LimitUncompressedForTests;
        /// <summary>Bench only: where the size report goes in place of the app. Null in every real build.</summary>
        public static Action<string> SendForTests;

        static string _blueprint = "";
        static long _addedCompressed, _addedUncompressed;
        static long _preCompressed, _preUncompressed;
        static bool _auto;

        /// <summary>The build hook, for a protected avatar: its blueprint id (the app's key for its sizes).</summary>
        public static void Begin(string blueprint)
        {
            _blueprint = blueprint ?? "";
            _addedCompressed = _addedUncompressed = 0;
            _preCompressed = _preUncompressed = 0;
            _auto = false;
        }

        /// <summary>The avatar's own size, measured at this upload before it was protected (0 when not measured).</summary>
        public static long PreCompressed => _preCompressed;
        public static long PreUncompressed => _preUncompressed;

        // ── 3.17.1: AUTO, measured at the upload itself ──────────────────────────────────────────────────────────────
        //
        // Owner, 2026-11-20: "le système doit regarder ce qu'il lui reste APRÈS compilation, les bits libres pour la clé, les
        // MB disponibles, pour tout le système, et s'adapter". The build hook runs after VRCFury, Modular Avatar and NDMF have
        // merged everything, so the avatar it holds is the one VRChat will build. Before protecting it, Auto builds it once
        // the way the SDK does (a prefab, one LZMA bundle for the active platform), asks the SDK's own checks what that
        // weighs, and gives the protection what is left under both limits, minus a margin and minus what the protection
        // costs beyond its own count -- learned from this avatar's last upload (the SDK's measure of the protected bundle,
        // AfterBuild), or a default the first time. So a dress added since the last upload, or a VRCFury toggle that brings
        // a mesh, is in the measure: nothing is assumed from the previous upload but the protection's own overhead.

        /// <summary>
        /// The room Auto gives this upload's protection, in whole MB (at least 1; 0 for "no uncompressed cap" when the SDK
        /// cannot read the uncompressed size). False when the avatar could not be measured: the caller keeps its own caps.
        /// </summary>
        /// <summary>The last <see cref="AutoCaps"/> found no room at all for the protection (the avatar alone is at the limit).</summary>
        public static bool NoRoom { get; private set; }
        /// <summary>The limits the last <see cref="AutoCaps"/> measured against (VRChat's, from its SDK).</summary>
        public static long LimitCompressedSeen { get; private set; } = DefaultLimitCompressed;
        public static long LimitUncompressedSeen { get; private set; } = DefaultLimitUncompressed;

        public static bool AutoCaps(GameObject avatar, string folder, out int capMegabytes, out int capUncompressedMegabytes, out string note)
        {
            capMegabytes = capUncompressedMegabytes = 0;
            note = "";
            NoRoom = false;
            if (!PreMeasure(avatar, folder, out long c, out long u, out long limitC, out long limitU, out string error))
            {
                note = "could not measure the avatar before protecting it (" + error + ")";
                return false;
            }
            _preCompressed = c;
            _preUncompressed = u;
            LimitCompressedSeen = limitC;
            LimitUncompressedSeen = limitU;
            _auto = true;

            Learned last = Load(_blueprint);
            bool learnedC = last != null && last.pre > 0 && last.final > 0;
            bool learnedU = last != null && last.preU > 0 && last.finalU > 0;
            // what the protection cost beyond its own count last time; never less than nothing (a count that was too high
            // last time is kept as a margin, not spent)
            long overC = learnedC ? Clamp(last.final - last.pre - last.est, 0, 64 * MiB) : DefaultOverheadCompressed;
            long overU = learnedU ? Clamp(last.finalU - last.preU - last.estU, 0, 256 * MiB) : DefaultOverheadUncompressed;

            // what the protection counts is itself off by a few percent, and by a different amount when a tight limit leaves
            // other meshes out than last time (bench 2026-11-20, Wendigo at 505 MB uncompressed: 1.7 MB more than learned):
            // the cap is the room divided by 1 + that share, so the count's own error has room too
            long roomC = limitC - MarginCompressed - c - overC;
            capMegabytes = (int)Math.Max(1, (long)(roomC / CountError) / MiB);
            long roomU = u > 0 ? limitU - MarginUncompressed - u - overU : 0;
            capUncompressedMegabytes = u > 0 ? (int)Math.Max(1, (long)(roomU / CountErrorUncompressed) / MiB) : 0;

            note = "measured before protection: " + Mb(c) + " of " + Mb(limitC) + (u > 0 ? ", " + Mb(u) + " of " + Mb(limitU) + " uncompressed" : "") +
                   "; protection gets " + capMegabytes + " MB" + (u > 0 ? " (" + capUncompressedMegabytes + " MB uncompressed)" : "") +
                   " -- beyond its own count it is expected to cost " + Mb(overC) + (u > 0 ? " (" + Mb(overU) + " uncompressed)" : "") +
                   (learnedC ? ", as this avatar's last upload measured" : ", a first-upload allowance");
            // 3.17.1: no room at all -- the build hook stops the upload and offers FULL LOCAL (KSAntiRipFullLocal)
            NoRoom = roomC < MiB || (u > 0 && roomU < MiB);
            if (NoRoom)
                note += ". The bundle is too big to be protected: not even 1 MB is left under VRChat's limits before protection";
            return true;
        }

        /// <summary>
        /// Builds <paramref name="avatar"/> as the SDK would and measures it with the SDK's own checks. The prefab and the
        /// bundle are temporary and deleted before this returns; nothing in the scene or the avatar changes.
        /// </summary>
        public static bool PreMeasure(GameObject avatar, string folder, out long compressed, out long uncompressed,
                                      out long limitCompressed, out long limitUncompressed, out string error)
        {
            compressed = uncompressed = 0;
            limitCompressed = DefaultLimitCompressed;
            limitUncompressed = DefaultLimitUncompressed;
            error = "";
            string stamp = Guid.NewGuid().ToString("N").Substring(0, 8);
            string prefab = folder + "/ks_measure_" + stamp + ".prefab";
            string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Temp", "KSAntiRipMeasure_" + stamp);
            string bundle = "ks_measure_" + stamp + ".vrca";
            try
            {
                Directory.CreateDirectory(outDir);
                PrefabUtility.SaveAsPrefabAsset(avatar, prefab, out bool saved);
                if (!saved) { error = "the avatar could not be saved as a prefab"; return false; }
                var manifest = BuildPipeline.BuildAssetBundles(outDir,
                    new[] { new AssetBundleBuild { assetBundleName = bundle, assetNames = new[] { prefab } } },
                    BuildAssetBundleOptions.ForceRebuildAssetBundle, EditorUserBuildSettings.activeBuildTarget);
                if (manifest == null) { error = "Unity could not build it"; return false; }
                if (!Measure(Path.Combine(outDir, bundle), out compressed, out uncompressed, out limitCompressed, out limitUncompressed))
                { error = "the bundle was not written"; return false; }
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
            finally
            {
                try { if (File.Exists(prefab) || AssetDatabase.LoadMainAssetAtPath(prefab) != null) AssetDatabase.DeleteAsset(prefab); } catch { }
                try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
            }
        }

        // what each avatar's last protected upload measured, kept in the project (Library: never in the scene, never shipped)
        [Serializable] class Learned { public string blueprint = ""; public long pre, preU, final, finalU, est, estU; public string when = ""; }
        [Serializable] class LearnedFile { public List<Learned> items = new List<Learned>(); }

        static string StorePath => Path.Combine(Directory.GetCurrentDirectory(), "Library", "KSAntiRip", "sizes.json");

        static LearnedFile LoadAll()
        {
            try
            {
                if (File.Exists(StorePath)) return JsonUtility.FromJson<LearnedFile>(File.ReadAllText(StorePath)) ?? new LearnedFile();
            }
            catch { }
            return new LearnedFile();
        }

        static Learned Load(string blueprint) =>
            string.IsNullOrEmpty(blueprint) ? null : LoadAll().items.FirstOrDefault(x => x.blueprint == blueprint);

        static void Remember(long final, long finalU)
        {
            if (string.IsNullOrEmpty(_blueprint) || !_auto || _preCompressed <= 0 || final <= 0) return;
            try
            {
                LearnedFile all = LoadAll();
                all.items.RemoveAll(x => x.blueprint == _blueprint);
                all.items.Add(new Learned
                {
                    blueprint = _blueprint, pre = _preCompressed, preU = _preUncompressed, final = final, finalU = finalU,
                    est = _addedCompressed, estU = _addedUncompressed, when = DateTime.UtcNow.ToString("o"),
                });
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath));
                File.WriteAllText(StorePath, JsonUtility.ToJson(all, true));
            }
            catch { }
        }

        /// <summary>Bench only: forget what an avatar's last upload measured (a first upload again).</summary>
        public static void ForgetForTests(string blueprint)
        {
            LearnedFile all = LoadAll();
            if (all.items.RemoveAll(x => x.blueprint == blueprint) > 0) File.WriteAllText(StorePath, JsonUtility.ToJson(all, true));
        }

        static long Clamp(long v, long lo, long hi) => Math.Max(lo, Math.Min(hi, v));
        static string Mb(long b) => (b / (double)MiB).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

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
            finally
            {
                if (LimitCompressedForTests > 0) limitCompressed = LimitCompressedForTests;
                if (LimitUncompressedForTests > 0) limitUncompressed = LimitUncompressedForTests;
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
                // 3.17.1: what the protection really cost beyond its count, for this avatar's next upload on Auto
                Remember(compressed, uncompressed);
                var sb = new StringBuilder();
                sb.Append("{\"blueprint\":\"").Append(_blueprint.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"')
                  .Append(",\"compressed\":").Append(compressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"uncompressed\":").Append(uncompressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"limitCompressed\":").Append(limitC.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"limitUncompressed\":").Append(limitU.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"addedCompressed\":").Append(_addedCompressed.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"addedUncompressed\":").Append(_addedUncompressed.ToString(CultureInfo.InvariantCulture));
                // 3.17.1: the avatar's own size, measured at this upload before it was protected (Auto only)
                if (_auto)
                    sb.Append(",\"preCompressed\":").Append(_preCompressed.ToString(CultureInfo.InvariantCulture))
                      .Append(",\"preUncompressed\":").Append(_preUncompressed.ToString(CultureInfo.InvariantCulture));
                sb.Append('}');
                if (SendForTests != null) SendForTests(sb.ToString());
                else KSAntiRipClient.Sizes(sb.ToString());
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
