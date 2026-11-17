using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Measures how broken a protected avatar actually is, on a real mesh, with a real plan from the app.
    ///
    /// The other three suites answer "does it work" and "does it harm". This one answers the question an
    /// owner actually asks: WITHOUT THE KEY, HOW WRECKED IS IT? So it reports numbers rather than PASS lines
    /// alone -- how far every vertex moved, how big the mesh became, and what the four obvious ways of
    /// attacking it without the key get you:
    ///
    ///   shipped      every restore shape at 0, which is what sits in the bundle and what a ripper downloads
    ///   all at 100   the first thing anybody tries
    ///   best single  one weight swept 0..100 and applied to every shape at once, the second thing
    ///   per shape    each shape swept on its own against a smoothness score -- the attack that breaks the
    ///                naive design, and the reason the shapes are dense (AntiRipEngine.PlanMesh)
    ///
    /// and then, with the real weights, that the mesh comes back to within float noise.
    ///
    /// It also writes a PNG of the vertex cloud before and after, because a number is not a picture.
    ///
    ///   "…\Unity.exe" -batchmode -nographics -quit -projectPath "<project>" \
    ///       -executeMethod KawaiiStudio.VRC.AntiRip.KSAntiRipProofTest.RunFromCommandLine
    ///
    /// Kawaii Studio must be running and signed in: the plan comes from its core over the loopback bridge,
    /// exactly as a real protection does.
    /// </summary>
    public static class KSAntiRipProofTest
    {
        const string Root = "Assets/KS_AntiRip_Proof";

        [MenuItem("Kawaii Studio/VRC/Debug/KS Anti-Rip proof", false, 903)]
        public static void RunFromMenu() => Debug.Log(Run(out _));

        public static void RunFromCommandLine()
        {
            string report = Run(out bool ok);
            Console.WriteLine(report);
            Debug.Log(report);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string Run(out bool ok)
        {
            var log = new StringBuilder();
            int failures = 0;
            void Check(bool condition, string what)
            {
                log.AppendLine((condition ? "PASS  " : "FAIL  ") + what);
                if (!condition) failures++;
            }

            GameObject instance = null;
            EnginePlanSource engine = null;
            try
            {
                // ── the subject: the biggest skinned mesh in the project ────────────────────────
                Mesh original = BiggestSkinnedMesh(out string meshPath);
                if (original == null)
                {
                    log.AppendLine("FAIL  no skinned mesh in this project to protect");
                    ok = false;
                    return log.ToString();
                }
                log.AppendLine("mesh      " + original.name + "  (" + meshPath + ")");
                log.AppendLine("vertices  " + original.vertexCount.ToString("N0"));
                // Echoed as they happen, so a run that dies half way still says where it was.
                Debug.Log("[KS PROOF] subject " + original.name + " " + original.vertexCount + " verts (" + meshPath + ")");
                Vector3[] before = original.vertices;
                Bounds ob = Bounds(before);
                log.AppendLine("size      " + Fmt(ob.size) + " m");
                log.AppendLine();

                // ── protect it, with the real core ──────────────────────────────────────────────
                instance = BuildAvatar(original);
                var settings = new KSAntiRipProcessor.Settings
                {
                    strength = 5f,
                    restoreGroups = 3,
                    syncKey = true,
                    hideWhileLocked = true,
                    obfuscateNames = true,    // as shipped: nothing below relies on a readable name
                };
                engine = EnginePlanSource.Available ? new EnginePlanSource() : null;
                KSAntiRipProcessor.IPlanSource source = engine;
                log.AppendLine("plan from " + (engine != null
                    ? "the real engine behind the real bridge (KS_PLANDUMP), read by KSAntiRipClient's own parser"
                    : "the running app"));
                Debug.Log("[KS PROOF] protecting…");
                KSAntiRipProcessor.Result result = KSAntiRipProcessor.Protect(instance, settings, source);
                Debug.Log("[KS PROOF] protect returned ok=" + result.ok + " " + result.message);
                if (!result.ok)
                {
                    log.AppendLine("FAIL  the protection did not run: " + result.message);
                    ok = false;
                    return log.ToString();
                }
                log.AppendLine("protected into " + result.folder);

                Mesh scrambled = LoadScrambled(result.folder, original, out string[] shapeNames);
                Check(scrambled != null, "the protected mesh was saved and can be read back");
                if (scrambled == null) { ok = false; return log.ToString(); }
                Check(shapeNames.Length == settings.restoreGroups,
                      shapeNames.Length + " restore shapes, one per group");

                Vector3[] shipped = scrambled.vertices;
                Check(shipped.Length == before.Length, "same vertex count, so nothing was added or dropped");

                // ── 1. what ships ───────────────────────────────────────────────────────────────
                log.AppendLine();
                log.AppendLine("── WHAT A RIPPER DOWNLOADS ──────────────────────────────");
                Spread(before, shipped, out float dmin, out float dmean, out float dmax);
                Bounds sb = Bounds(shipped);
                log.AppendLine("displacement   min " + dmin.ToString("F3") + " m   mean " + dmean.ToString("F3") +
                               " m   max " + dmax.ToString("F3") + " m");
                log.AppendLine("mesh size      " + Fmt(ob.size) + "  ->  " + Fmt(sb.size) + " m");
                log.AppendLine("volume         x" + (Volume(sb) / Math.Max(1e-6f, Volume(ob))).ToString("F0"));
                Check(dmean > 0.5f, "every vertex is displaced, " + dmean.ToString("F2") + " m on average");
                Check(dmin > 0.001f, "not one vertex was left where it was (closest moved " +
                                     (dmin * 100f).ToString("F1") + " cm)");
                Check(Volume(sb) > Volume(ob) * 8f, "the mesh no longer fits in anything like its own bounds");

                // ── 2..4. the attacks, scored by how far off they land ──────────────────────────
                //
                // The shape deltas are read ONCE here. Sweeping a weight means re-evaluating the mesh
                // thousands of times, and GetBlendShapeFrameVertices inside that loop would be a billion
                // vector copies on a real avatar -- the attack has to be cheap to measure, or it does not
                // get measured.
                var deltas = new Vector3[shapeNames.Length][];
                for (int k = 0; k < shapeNames.Length; k++)
                {
                    deltas[k] = new Vector3[shipped.Length];
                    scrambled.GetBlendShapeFrameVertices(scrambled.GetBlendShapeIndex(shapeNames[k]), 0,
                                                         deltas[k], null, null);
                }
                int[] sample = Sample(shipped.Length, 4000);
                int[] edges = SampleEdges(scrambled.triangles, 4000);

                // Scored against the avatar's own height, not a number picked out of the air: 5 % of it is
                // already past any resemblance to the original, and it means the same thing on a chibi and
                // on a 2 m model.
                float scale = Math.Max(0.1f, ob.size.y);
                float ruined = scale * 0.05f;
                log.AppendLine();
                log.AppendLine("── WITHOUT THE KEY ──────────────────────────────────────");
                log.AppendLine("(unusable above " + (ruined * 100f).ToString("F1") + " cm, 5% of this avatar's height)");
                float shippedErr = Rms(before, shipped);
                log.AppendLine("shipped as-is            RMS " + shippedErr.ToString("F3") + " m");
                Check(shippedErr > ruined * 4f, "what ships is " + (shippedErr / scale * 100f).ToString("F0") +
                                                "% of the avatar's own height out of place");

                float[] hundreds = Enumerable.Repeat(100f, shapeNames.Length).ToArray();
                float allHundred = RmsAt(before, shipped, deltas, hundreds, null);
                log.AppendLine("every shape at 100       RMS " + allHundred.ToString("F3") + " m");
                Check(allHundred > ruined,
                      "dragging every slider to 100 does not get it back (" + allHundred.ToString("F2") + " m out)");

                float bestSingle = float.MaxValue, bestSingleAt = 0f;
                var uniform = new float[shapeNames.Length];
                for (int w = 0; w <= 1000; w++)
                {
                    for (int k = 0; k < uniform.Length; k++) uniform[k] = w / 10f;
                    float e = RmsAt(before, shipped, deltas, uniform, sample);
                    if (e < bestSingle) { bestSingle = e; bestSingleAt = w / 10f; }
                }
                log.AppendLine("best one weight for all  RMS " + bestSingle.ToString("F3") + " m  (at " +
                               bestSingleAt.ToString("F1") + ")");
                Check(bestSingle > ruined,
                      "the BEST single weight for every shape is still " + bestSingle.ToString("F2") +
                      " m out, " + (bestSingle / scale * 100f).ToString("F0") + "% of the avatar's height");

                // The attack the dense decomposition exists to stop: solve one shape at a time, scoring each
                // guess by how smooth the mesh looks -- a rip script does not have the original to compare
                // against, so roughness is what it can actually optimise. If the shapes owned disjoint
                // vertices this converges in a few hundred steps per shape.
                var greedy = new float[shapeNames.Length];
                for (int pass = 0; pass < 2; pass++)
                {
                    for (int k = 0; k < greedy.Length; k++)
                    {
                        float bestW = greedy[k], bestScore = float.MaxValue;
                        for (int w = 0; w <= 200; w++)
                        {
                            greedy[k] = w / 2f;
                            float s = RoughnessAt(shipped, deltas, greedy, edges);
                            if (s < bestScore) { bestScore = s; bestW = greedy[k]; }
                        }
                        greedy[k] = bestW;
                    }
                }
                float greedyErr = RmsAt(before, shipped, deltas, greedy, null);
                log.AppendLine("one shape at a time      RMS " + greedyErr.ToString("F3") + " m  (landed on " +
                               string.Join(", ", greedy.Select(g => g.ToString("F1"))) + ")");
                Check(greedyErr > ruined,
                      "sweeping the shapes one at a time does NOT recover the mesh (" +
                      greedyErr.ToString("F2") + " m out) -- the shapes are not separable");

                // ── 5. what the bundle says about the answer: nothing ───────────────────────────
                //
                // Up to 5.0 the exact weights sat in an "open" clip in the FX controller, so this test could
                // read them straight out of the protected folder -- and so could anybody with the bundle.
                // Now every curve on a restore shape must read 0 or 100.
                log.AppendLine();
                log.AppendLine("── WHAT THE BUNDLE GIVES AWAY ───────────────────────────");
                ScanCurves(result.folder, shapeNames, out int curves, out int leaks);
                log.AppendLine("curves on restore shapes  " + curves + "   holding a weight  " + leaks);
                Check(curves > 0, "the protection animates the restore shapes (" + curves + " curves)");
                Check(leaks == 0, "no curve in the bundle holds a weight -- every value is 0 or 100");

                // ── 6. with the key ─────────────────────────────────────────────────────────────
                log.AppendLine();
                log.AppendLine("── WITH THE KEY ─────────────────────────────────────────");
                float[] real = WeightsFromKey(result, shapeNames, out string keyWhy);
                Check(real != null, "the .key, through the animator's own wiring, gives every restore shape a weight" +
                                    (real == null ? " -- " + keyWhy : ""));
                if (real != null)
                {
                    log.AppendLine("weights        " + string.Join(", ", real.Select(r => r.ToString("F2"))));
                    float restored = Rms(before, KSAntiRipMesh.RestoreForTest(scrambled, real, shapeNames));
                    log.AppendLine("restored       RMS " + (restored * 1000f).ToString("F4") + " mm");
                    Check(restored < 1e-3f, "the key puts the mesh back to within " +
                                            (restored * 1000f).ToString("F3") + " mm -- non-destructive");

                    // What the OTHER players get: VRChat sends a synced float in 8 bits, round(v*127)/127.
                    float[] remote = real.Select(w => Mathf.Round(w / 100f * 127f) / 127f * 100f).ToArray();
                    float remoteErr = Rms(before, KSAntiRipMesh.RestoreForTest(scrambled, remote, shapeNames));
                    log.AppendLine("as received by others   RMS " + (remoteErr * 1000f).ToString("F4") + " mm");
                    Check(remoteErr < 1e-3f, "after VRChat's 8-bit network rounding it is still exact (" +
                                             (remoteErr * 1000f).ToString("F3") + " mm) -- the key is on the grid");
                }

                // ── the picture ─────────────────────────────────────────────────────────────────
                string png = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "ks_antirip_proof.png");
                WriteComparison(png, before, shipped);
                log.AppendLine();
                log.AppendLine("picture   " + png);
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL  " + e);
                failures++;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                engine?.Dispose();
                AssetDatabase.DeleteAsset(Root);
                AssetDatabase.Refresh();
            }

            log.AppendLine();
            log.AppendLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
            ok = failures == 0;
            return log.ToString();
        }

        // ── where the plan comes from ───────────────────────────────────────────────────────────

        /// <summary>
        /// The REAL engine behind the REAL bridge, without starting the app.
        ///
        /// KS_PLANDUMP points at a harness that runs AntiRipEngine through AntiRipBridge.Handle and writes the
        /// bridge's replies to disk byte for byte. They are read back here through KSAntiRipClient.ParseBegin and
        /// ParseMesh -- the same parser the live link uses -- so the only thing skipped is the TCP socket. The
        /// 2026-11-16 base64 bug lived in that parser; a test with a parser of its own could not have seen it.
        ///
        /// Why not start the app: a dev build started as first instance with writes on the owner's live data ran
        /// another session's unfinished boot code against it that same night. The harness needs no app, no data
        /// root and no login, and writes its .key into its own folder.
        ///
        /// With KS_PLANDUMP unset the test falls back to the running app, which is the shipping path.
        /// </summary>
        sealed class EnginePlanSource : KSAntiRipProcessor.IPlanSource, IDisposable
        {
            public static bool Available => !string.IsNullOrEmpty(Exe) && File.Exists(Exe);
            static string Exe => Environment.GetEnvironmentVariable("KS_PLANDUMP") ?? "";

            readonly string _dir = Path.Combine(Path.GetTempPath(), "ks_plan_" + Guid.NewGuid().ToString("N"));
            readonly Dictionary<string, KSAntiRipClient.MeshPlan> _meshes =
                new Dictionary<string, KSAntiRipClient.MeshPlan>();

            public string LastError { get; private set; } = "";
            public bool Ended { get; private set; }

            public KSAntiRipClient.Plan Begin(string avatarId, string avatarName,
                                              IList<KSAntiRipClient.MeshInfo> meshes,
                                              float strength, int restoreGroups)
            {
                // One harness run for the whole avatar, as one bridge session would be.
                var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                psi.ArgumentList.Add(Exe);
                psi.ArgumentList.Add(string.Join(",", meshes.Select(m => m.id + ":" + m.vertexCount)));
                psi.ArgumentList.Add(strength.ToString(System.Globalization.CultureInfo.InvariantCulture));
                psi.ArgumentList.Add(restoreGroups.ToString());
                psi.ArgumentList.Add(_dir);
                using (System.Diagnostics.Process p = System.Diagnostics.Process.Start(psi))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.StandardOutput.ReadToEnd();
                    p.WaitForExit(120000);
                    if (p.ExitCode != 0)
                        return new KSAntiRipClient.Plan { error = LastError = "the engine refused: " + err.Trim() };
                }

                KSAntiRipClient.Plan plan = KSAntiRipClient.ParseBegin(File.ReadAllText(Path.Combine(_dir, "begin.json")));
                if (!plan.ok) { LastError = plan.error; return plan; }

                foreach (KSAntiRipClient.MeshInfo info in meshes)
                {
                    string reply = File.ReadAllText(Path.Combine(_dir, "mesh_" + info.id + ".json"));
                    KSAntiRipClient.MeshPlan mesh = KSAntiRipClient.ParseMesh(info.id, reply);
                    if (mesh == null)
                    {
                        plan.ok = false;
                        plan.error = LastError = KSAntiRipClient.LastError;
                        return plan;
                    }
                    _meshes[info.id] = mesh;
                    plan.meshes.Add(new KSAntiRipClient.MeshInfo { id = info.id, vertexCount = info.vertexCount });
                }
                return plan;
            }

            public KSAntiRipClient.MeshPlan Mesh(string session, string id)
                => _meshes.TryGetValue(id, out KSAntiRipClient.MeshPlan m) ? m : null;

            public void End(string session) => Ended = true;

            /// <summary>Removes the harness's folder, the lab .key with it.</summary>
            public void Dispose()
            {
                try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
            }
        }

        // ── the subject ─────────────────────────────────────────────────────────────────────────

        static Mesh BiggestSkinnedMesh(out string path)
        {
            path = "";
            Mesh best = null;
            // Assets/KS_Subject first when it exists, so a run can be pointed at one model instead of
            // loading every mesh asset in a real project to compare them.
            string[] search = AssetDatabase.IsValidFolder("Assets/KS_Subject")
                ? new[] { "Assets/KS_Subject" }
                : new[] { "Assets" };
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", search))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (p.StartsWith(Root, StringComparison.Ordinal)) continue;
                foreach (Mesh m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Mesh>())
                {
                    // A real body mesh: enough vertices to be worth measuring, and skinned, because that is
                    // what the protection works on.
                    // Between 2k and 120k: big enough to be a real body, small enough to be a real avatar.
                    // An unbounded "biggest" picked a multi-million-vertex prop out of a model pack and took
                    // Unity's serializer down with it when the protected copy was written.
                    if (m == null || m.vertexCount < 2000 || m.vertexCount > 120000) continue;
                    if (m.boneWeights.Length == 0) continue;
                    if (best == null || m.vertexCount > best.vertexCount) { best = m; path = p; }
                }
            }
            return best;
        }

        static GameObject BuildAvatar(Mesh mesh)
        {
            Directory.CreateDirectory(KawaiiStudioPaths.ToAbsolute(Root));
            AssetDatabase.Refresh();

            var root = new GameObject("KS Proof Avatar");
            root.AddComponent<Animator>();
            root.AddComponent<VRCAvatarDescriptor>();
            var pipeline = root.AddComponent<PipelineManager>();
            // A local, obviously-fake id: the key is named after it and is deleted with the rest of the run.
            pipeline.blueprintId = "avtr_ks000000-0000-4000-8000-000000000000";

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterials = Enumerable.Range(0, Math.Max(1, mesh.subMeshCount))
                                            .Select(_ => new Material(Shader.Find("Standard")))
                                            .ToArray();
            var bone = new GameObject("Root").transform;
            bone.SetParent(root.transform, false);
            smr.bones = new[] { bone };
            smr.rootBone = bone;
            return root;
        }

        /// <summary>
        /// The protected copy of <paramref name="original"/>, and its restore shapes: the shapes it has that the
        /// original does not. Found by difference, not by name -- since 5.1 the names are random words.
        /// </summary>
        static Mesh LoadScrambled(string folder, Mesh original, out string[] shapeNames)
        {
            shapeNames = Array.Empty<string>();
            var own = new HashSet<string>();
            for (int i = 0; i < original.blendShapeCount; i++) own.Add(original.GetBlendShapeName(i));

            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { folder }))
            {
                Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(AssetDatabase.GUIDToAssetPath(guid));
                if (m == null || m.vertexCount != original.vertexCount) continue;
                var names = new List<string>();
                for (int i = 0; i < m.blendShapeCount; i++)
                {
                    string n = m.GetBlendShapeName(i);
                    if (!own.Contains(n)) names.Add(n);
                }
                if (names.Count == 0) continue;
                shapeNames = names.ToArray();
                return m;
            }
            return null;
        }

        /// <summary>Every curve on a restore shape in the protected folder, and how many hold anything but 0 or 100.</summary>
        static void ScanCurves(string folder, string[] shapeNames, out int curves, out int leaks)
        {
            curves = 0;
            leaks = 0;
            var restore = new HashSet<string>(shapeNames);
            foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                {
                    const string prefix = "blendShape.";
                    if (!b.propertyName.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    if (!restore.Contains(b.propertyName.Substring(prefix.Length))) continue;
                    curves++;
                    foreach (Keyframe k in AnimationUtility.GetEditorCurve(clip, b).keys)
                        if (!Mathf.Approximately(k.value, 0f) && !Mathf.Approximately(k.value, 100f)) leaks++;
                }
            }
        }

        [Serializable] class KeyParamMirror { public string name = ""; public string type = ""; public float value; }
        [Serializable] class KeyFileMirror { public List<KeyParamMirror> parameters = new List<KeyParamMirror>(); }

        /// <summary>
        /// The weight each restore shape reaches with the key on, worked out the way VRChat would: the .key gives
        /// each parameter a value, the protected FX controller's Direct BlendTree says which parameter drives which
        /// shape. Neither half alone gives the answer -- which is the point.
        /// </summary>
        static float[] WeightsFromKey(KSAntiRipProcessor.Result result, string[] shapeNames, out string why)
        {
            why = "";
            if (string.IsNullOrEmpty(result.keyPath) || !File.Exists(result.keyPath)) { why = "no .key at " + result.keyPath; return null; }
            KeyFileMirror key;
            try { key = JsonUtility.FromJson<KeyFileMirror>(File.ReadAllText(result.keyPath)); }
            catch (Exception e) { why = "unreadable .key: " + e.Message; return null; }
            var values = key.parameters.ToDictionary(p => p.name, p => p.value);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.prefabPath);
            var fx = prefab?.GetComponent<VRCAvatarDescriptor>()?.baseAnimationLayers
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            if (fx == null) { why = "no FX controller on the protected prefab"; return null; }
            var tree = fx.layers.FirstOrDefault(l => l.name == result.lockLayer).stateMachine?.defaultState?.motion as BlendTree;
            if (tree == null || tree.blendType != BlendTreeType.Direct) { why = "no Direct BlendTree in layer " + result.lockLayer; return null; }

            var weightOf = new Dictionary<string, float>();
            foreach (ChildMotion child in tree.children)
            {
                if (!values.TryGetValue(child.directBlendParameter, out float v)) { why = "the key has no " + child.directBlendParameter; return null; }
                if (!(child.motion is AnimationClip clip)) continue;
                foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                    if (b.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
                        weightOf[b.propertyName.Substring("blendShape.".Length)] = v * 100f;
            }
            if (shapeNames.Any(n => !weightOf.ContainsKey(n))) { why = "a restore shape is driven by no key parameter"; return null; }
            return shapeNames.Select(n => weightOf[n]).ToArray();
        }

        // ── measurements ────────────────────────────────────────────────────────────────────────

        static Bounds Bounds(Vector3[] v)
        {
            var b = new Bounds(v[0], Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(v[i]);
            return b;
        }

        static float Volume(Bounds b) => Math.Max(1e-6f, b.size.x * b.size.y * b.size.z);

        static void Spread(Vector3[] a, Vector3[] b, out float min, out float mean, out float max)
        {
            min = float.MaxValue;
            max = 0f;
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++)
            {
                float d = Vector3.Distance(a[i], b[i]);
                if (d < min) min = d;
                if (d > max) max = d;
                sum += d;
            }
            mean = (float)(sum / a.Length);
        }

        static float Rms(Vector3[] a, Vector3[] b)
        {
            double sum = 0.0;
            for (int i = 0; i < a.Length; i++) sum += (a[i] - b[i]).sqrMagnitude;
            return (float)Math.Sqrt(sum / a.Length);
        }

        /// <summary>Every k-th index, up to <paramref name="want"/> of them.</summary>
        static int[] Sample(int count, int want)
        {
            int step = Math.Max(1, count / Math.Max(1, want));
            var list = new List<int>(want + 1);
            for (int i = 0; i < count; i += step) list.Add(i);
            return list.ToArray();
        }

        /// <summary>Pairs of vertex indices that share a triangle edge, sampled.</summary>
        static int[] SampleEdges(int[] tris, int want)
        {
            int faces = tris.Length / 3;
            int step = Math.Max(1, faces / Math.Max(1, want));
            var list = new List<int>(want * 2 + 2);
            for (int f = 0; f < faces; f += step)
            {
                list.Add(tris[f * 3]);
                list.Add(tris[f * 3 + 1]);
            }
            return list.ToArray();
        }

        /// <summary>One vertex with the restore shapes applied at <paramref name="w"/>, from cached deltas.</summary>
        static Vector3 At(Vector3[] shipped, Vector3[][] deltas, float[] w, int i)
        {
            Vector3 p = shipped[i];
            for (int k = 0; k < deltas.Length; k++) p += deltas[k][i] * (w[k] / 100f);
            return p;
        }

        static float RmsAt(Vector3[] target, Vector3[] shipped, Vector3[][] deltas, float[] w, int[] sample)
        {
            double sum = 0.0;
            int n = sample?.Length ?? shipped.Length;
            for (int s = 0; s < n; s++)
            {
                int i = sample != null ? sample[s] : s;
                sum += (At(shipped, deltas, w, i) - target[i]).sqrMagnitude;
            }
            return (float)Math.Sqrt(sum / Math.Max(1, n));
        }

        /// <summary>
        /// How jagged the mesh is at these weights: the spread of its edge lengths. A real mesh scores low, a
        /// scrambled one scores high. This is what a rip script can actually optimise, because it does not
        /// have the original to compare against -- so it is what the attacks above are scored with.
        /// </summary>
        static float RoughnessAt(Vector3[] shipped, Vector3[][] deltas, float[] w, int[] edges)
        {
            double sum = 0.0, sumSq = 0.0;
            int n = edges.Length / 2;
            for (int e = 0; e < n; e++)
            {
                float d = (At(shipped, deltas, w, edges[e * 2]) - At(shipped, deltas, w, edges[e * 2 + 1])).magnitude;
                sum += d;
                sumSq += (double)d * d;
            }
            if (n == 0) return 0f;
            double mean = sum / n;
            return (float)Math.Sqrt(Math.Max(0.0, sumSq / n - mean * mean));
        }

        // ── the picture ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Both vertex clouds side by side, front view, drawn by hand into a texture. Done pixel by pixel on
        /// purpose: it needs no camera, no shader and no GPU, so it works under -nographics.
        /// </summary>
        static void WriteComparison(string path, Vector3[] before, Vector3[] after)
        {
            const int W = 1200, H = 700, Pad = 20;
            var pixels = new Color32[W * H];
            var bg = new Color32(16, 10, 32, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = bg;

            // Which way up, decided from the mesh and not assumed: an FBX that was authored Z-up comes in
            // with its own axis baked the other way, and an upside-down avatar reads as a bug rather than as
            // the point. A head is narrower than a pelvis with the arms out, so the slimmer end goes on top.
            bool flip = SlimEndIsLow(before);
            Draw(pixels, W, H, before, 0, W / 2, new Color32(127, 230, 160, 255), Pad, flip);
            Draw(pixels, W, H, after, W / 2, W, new Color32(255, 107, 129, 255), Pad, flip);
            for (int y = 0; y < H; y++) pixels[y * W + W / 2] = new Color32(90, 70, 140, 255);

            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        /// <summary>True when the narrow end of the mesh sits at the LOW end of Y, so the picture needs flipping.</summary>
        static bool SlimEndIsLow(Vector3[] v)
        {
            Bounds b = Bounds(v);
            float lo = b.min.y, hi = b.max.y, span = Math.Max(1e-4f, hi - lo);
            float lowCut = lo + span * 0.15f, highCut = hi - span * 0.15f;
            float lowWide = 0f, highWide = 0f;
            float lowMin = float.MaxValue, lowMax = float.MinValue, highMin = float.MaxValue, highMax = float.MinValue;
            foreach (Vector3 p in v)
            {
                if (p.y <= lowCut) { lowMin = Math.Min(lowMin, p.x); lowMax = Math.Max(lowMax, p.x); }
                else if (p.y >= highCut) { highMin = Math.Min(highMin, p.x); highMax = Math.Max(highMax, p.x); }
            }
            if (lowMax > lowMin) lowWide = lowMax - lowMin;
            if (highMax > highMin) highWide = highMax - highMin;
            return lowWide > 0f && highWide > 0f && lowWide < highWide;
        }

        static void Draw(Color32[] px, int w, int h, Vector3[] v, int x0, int x1, Color32 colour, int pad, bool flip)
        {
            Bounds b = Bounds(v);
            float spanX = Math.Max(1e-4f, b.size.x), spanY = Math.Max(1e-4f, b.size.y);
            float scale = Math.Min((x1 - x0 - 2 * pad) / spanX, (h - 2 * pad) / spanY);
            float cx = (x0 + x1) * 0.5f, cy = h * 0.5f;
            float dir = flip ? -1f : 1f;
            foreach (Vector3 p in v)
            {
                int px0 = (int)(cx + (p.x - b.center.x) * scale);
                int py0 = (int)(cy - dir * (p.y - b.center.y) * scale);
                if (px0 < x0 || px0 >= x1 || py0 < 0 || py0 >= h) continue;
                px[py0 * w + px0] = colour;
            }
        }

        static string Fmt(Vector3 v) =>
            v.x.ToString("F2") + " x " + v.y.ToString("F2") + " x " + v.z.ToString("F2");
    }
}
