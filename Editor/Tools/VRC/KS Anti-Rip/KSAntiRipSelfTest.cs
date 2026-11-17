using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Checks the applicator: given a plan, the mesh really ships scrambled and really comes back.
    ///
    ///   "G:\…\Unity.exe" -batchmode -nographics -quit -projectPath "<project>" \
    ///       -executeMethod KawaiiStudio.VRC.AntiRip.KSAntiRipSelfTest.RunFromCommandLine
    ///
    /// Since 2026-11-16 the scramble is computed by the Kawaii Studio app, not here, so this uses a stand-in
    /// plan (KSAntiRipTestPlan) and verifies what Unity DOES with one. Exit code 0 = everything passed.
    /// </summary>
    public static class KSAntiRipSelfTest
    {
        [MenuItem("Kawaii Studio/VRC/Debug/KS Anti-Rip self-test", false, 900)]
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

            const int vertexCount = 3000;
            const int groups = 3;

            var rng = new System.Random(1234);
            var verts = new Vector3[vertexCount];
            var existing = new Vector3[vertexCount];
            var tris = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                verts[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                existing[i] = new Vector3(0f, (float)rng.NextDouble() * 0.1f, 0f);
                tris[i] = i;
            }

            var source = new Mesh { name = "KS_SelfTest" };
            source.vertices = verts;
            source.triangles = tris;
            source.RecalculateNormals();
            source.AddBlendShapeFrame("Viseme_Test", 100f, existing, null, null);
            Bounds sourceBounds = source.bounds;

            // a plan exactly as the app would hand one over
            var planner = new KSAntiRipTestPlan(strength: 5f, groups: groups);
            planner.Begin("avtr_test", "Self Test",
                          new[] { new KSAntiRipClient.MeshInfo { id = "m0", vertexCount = vertexCount } },
                          5f, groups);
            KSAntiRipClient.MeshPlan meshPlan = planner.Mesh("test-session", "m0");
            Check(meshPlan != null, "the planner issued a mesh plan");
            if (meshPlan == null) { ok = false; return log.ToString(); }
            // The weights are the key's values times 100. Unity never receives them; only a test has them.
            float[] keyWeights = planner.KeyValues.Select(v => v * 100f).ToArray();

            Mesh scrambled = KSAntiRipMesh.Apply(source, meshPlan.offsets, meshPlan.groups, meshPlan.shapeNames);

            // 1. the shipped geometry really is displaced
            Vector3[] sv = scrambled.vertices;
            float maxDisplacement = 0f;
            for (int i = 0; i < vertexCount; i++)
                maxDisplacement = Mathf.Max(maxDisplacement, (sv[i] - verts[i]).magnitude);
            Check(maxDisplacement > 1f, $"the shipped mesh is displaced (max {maxDisplacement:F2} m)");

            // 2. the key's weights restore it exactly
            Vector3[] restored = KSAntiRipMesh.RestoreForTest(scrambled, keyWeights, meshPlan.shapeNames.ToArray());
            float maxError = 0f;
            for (int i = 0; i < vertexCount; i++)
                maxError = Mathf.Max(maxError, (restored[i] - verts[i]).magnitude);
            Check(maxError < 1e-3f, $"the plan's weights restore the original (max error {maxError:E2} m)");

            // 3. the obvious attack -- shove every slider to 100 -- does not
            var allHundred = Enumerable.Repeat(100f, groups).ToArray();
            Vector3[] wrong = KSAntiRipMesh.RestoreForTest(scrambled, allHundred, meshPlan.shapeNames.ToArray());
            float wrongError = 0f;
            for (int i = 0; i < vertexCount; i++)
                wrongError = Mathf.Max(wrongError, (wrong[i] - verts[i]).magnitude);
            Check(wrongError > 0.5f, $"all-100 does NOT restore it (still off by {wrongError:F2} m)");

            // 4. one wrong weight out of three is still a broken mesh
            var oneOff = keyWeights.ToArray();
            oneOff[1] += 7f;
            Vector3[] partial = KSAntiRipMesh.RestoreForTest(scrambled, oneOff, meshPlan.shapeNames.ToArray());
            float partialError = 0f;
            for (int i = 0; i < vertexCount; i++)
                partialError = Mathf.Max(partialError, (partial[i] - verts[i]).magnitude);
            Check(partialError > 0.05f, $"a single wrong weight leaves it broken (off by {partialError:F2} m)");

            // 5. the avatar's own blend shapes came through untouched
            int visemeIndex = scrambled.GetBlendShapeIndex("Viseme_Test");
            Check(visemeIndex >= 0, "existing blend shapes are preserved");
            if (visemeIndex >= 0)
            {
                var got = new Vector3[vertexCount];
                scrambled.GetBlendShapeFrameVertices(visemeIndex, 0, got, null, null);
                float visemeError = 0f;
                for (int i = 0; i < vertexCount; i++)
                    visemeError = Mathf.Max(visemeError, (got[i] - existing[i]).magnitude);
                Check(visemeError < 1e-4f, $"existing blend shape deltas are unchanged (max {visemeError:E2})");
            }
            Check(scrambled.blendShapeCount == groups + 1,
                  $"shape count is the original plus the restore shapes ({scrambled.blendShapeCount})");

            // 6. bounds stay the ORIGINAL ones, so culling is right once unlocked
            Check((scrambled.bounds.center - sourceBounds.center).magnitude < 1e-3f &&
                  (scrambled.bounds.extents - sourceBounds.extents).magnitude < 1e-3f,
                  "bounds are the original mesh's");

            // 7. the source mesh was not touched
            Check(source.blendShapeCount == 1, "the source mesh still has only its own shape");
            Vector3[] sourceNow = source.vertices;
            float moved = 0f;
            for (int i = 0; i < vertexCount; i++) moved = Mathf.Max(moved, (sourceNow[i] - verts[i]).magnitude);
            Check(moved < 1e-6f, "the source vertices were never displaced");

            // 8. a plan sized for another mesh is refused rather than silently misapplied
            bool refused = false;
            try { KSAntiRipMesh.Apply(source, new Vector3[7], meshPlan.groups, meshPlan.shapeNames); }
            catch (ArgumentException) { refused = true; }
            Check(refused, "a plan with the wrong vertex count is refused");

            UnityEngine.Object.DestroyImmediate(scrambled);
            UnityEngine.Object.DestroyImmediate(source);

            ok = failures == 0;
            log.AppendLine(ok ? "KS ANTI-RIP SELF-TEST: ALL PASSED" : $"KS ANTI-RIP SELF-TEST: {failures} FAILED");
            return log.ToString();
        }
    }
}
