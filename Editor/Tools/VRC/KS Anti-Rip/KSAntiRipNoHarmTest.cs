using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Proves KS ANTI-RIP does not touch the avatar it protects — byte for byte.
    ///
    /// Builds a REAL avatar on disk (mesh, material, FX controller, animation clip, expression parameters,
    /// prefab), takes a SHA-256 of every one of those files, runs a full protection pass on an instance of
    /// it, then hashes them all again. Any change at all fails the test. It also checks the scene object
    /// itself still points at the same assets and is still enabled.
    ///
    ///   "…\Unity.exe" -batchmode -nographics -quit -projectPath "<a project with the VRChat SDK>" \
    ///       -executeMethod KawaiiStudio.VRC.AntiRip.KSAntiRipNoHarmTest.RunFromCommandLine
    /// </summary>
    public static class KSAntiRipNoHarmTest
    {
        const string Root = "Assets/KS_AntiRip_NoHarm";

        [MenuItem("Kawaii Studio/VRC/Debug/KS Anti-Rip no-harm test", false, 902)]
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
            KSAntiRipProcessor.Result result = null;
            string keyPath = null;

            try
            {
                string prefabPath = BuildAvatarOnDisk();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // every file that makes up the avatar, plus its .meta
                var watched = Directory.GetFiles(KawaiiStudioPaths.ToAbsolute(Root), "*", SearchOption.AllDirectories)
                                       .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                                       .ToArray();
                Check(watched.Length >= 10, $"the test avatar is made of {watched.Length} files on disk");
                Dictionary<string, string> before = Hashes(watched);
                var beforeTimes = watched.ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p));

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);

                var smr = instance.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Mesh meshBefore = smr.sharedMesh;
                Material matBefore = smr.sharedMaterial;
                int shapesBefore = meshBefore.blendShapeCount;
                int vertsBefore = meshBefore.vertexCount;
                var descriptor = instance.GetComponent<VRCAvatarDescriptor>();
                var paramsBefore = descriptor.expressionParameters;
                int paramCountBefore = paramsBefore.parameters.Length;
                var fxBefore = descriptor.baseAnimationLayers
                    .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
                int fxLayersBefore = ((AnimatorController)fxBefore).layers.Length;

                var planner = new KSAntiRipTestPlan(strength: 5f, groups: 3);
                result = KSAntiRipProcessor.Protect(instance, new KSAntiRipProcessor.Settings
                {
                    strength = 5f,
                    restoreGroups = 3,
                    hideWhileLocked = true,
                }, planner);
                keyPath = result.keyPath;
                Check(result.ok, "the protection pass ran: " + result.message);

                // ── 1. not one byte of the avatar's own files changed
                Dictionary<string, string> after = Hashes(watched);
                var changed = watched.Where(p => !before.TryGetValue(p, out string h) || h != after[p]).ToArray();
                Check(changed.Length == 0,
                      changed.Length == 0
                          ? $"all {watched.Length} source files are byte-for-byte identical"
                          : "THESE SOURCE FILES CHANGED: " + string.Join(", ", changed.Select(Path.GetFileName)));
                var touched = watched.Where(p => File.GetLastWriteTimeUtc(p) != beforeTimes[p]).ToArray();
                Check(touched.Length == 0,
                      touched.Length == 0
                          ? "none of them was even rewritten (timestamps unchanged)"
                          : "rewritten: " + string.Join(", ", touched.Select(Path.GetFileName)));

                // ── 2. nothing was deleted or added inside the avatar's folder
                var now = Directory.GetFiles(KawaiiStudioPaths.ToAbsolute(Root), "*", SearchOption.AllDirectories);
                Check(now.Length == watched.Length, $"no file was added to or removed from the avatar's folder ({now.Length})");

                // ── 3. the scene object still points at its own assets
                Check(smr != null && smr.sharedMesh == meshBefore, "the renderer still uses the ORIGINAL mesh asset");
                Check(smr != null && smr.sharedMaterial == matBefore, "it still uses the original material");
                Check(meshBefore.blendShapeCount == shapesBefore, $"the original mesh still has {shapesBefore} blend shape(s)");
                Check(meshBefore.vertexCount == vertsBefore, "the original mesh still has its own vertex count");
                Check(smr != null && smr.enabled, "the renderer was not disabled");
                Check(instance.activeSelf, "the avatar was not deactivated (the 2.x tool used to switch it off)");
                Check(descriptor.expressionParameters == paramsBefore, "the descriptor still points at its own expression parameters");
                Check(paramsBefore.parameters.Length == paramCountBefore,
                      $"no lock parameter was added to the original ({paramsBefore.parameters.Length})");
                Check(descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX)
                          .animatorController == fxBefore,
                      "the descriptor still points at its own FX controller");
                Check(((AnimatorController)fxBefore).layers.Length == fxLayersBefore,
                      $"no lock layer was added to the original FX controller ({((AnimatorController)fxBefore).layers.Length})");
                // By the names actually issued: since 5.1 they are random words, so a "KS_" search would pass
                // even if the lock had leaked into the original.
                var issuedNames = planner.Issued != null ? planner.Issued.parameters : new System.Collections.Generic.List<string>();
                Check(issuedNames.Count > 0 &&
                      !((AnimatorController)fxBefore).parameters.Any(p => issuedNames.Contains(p.name)),
                      "no key parameter was added to the original FX controller");

                // ── 4. the vertex positions themselves are untouched
                Vector3[] v = meshBefore.vertices;
                bool anyMoved = false;
                for (int i = 0; i < v.Length && !anyMoved; i++)
                    if (v[i].magnitude > 2.0f) anyMoved = true;   // the originals are all inside the unit cube
                Check(!anyMoved, "the original vertices were never displaced");

                // ── 5. no stray clone was left behind in the scene
                var leftovers = UnityEngine.Object.FindObjectsOfType<GameObject>()
                    .Where(g => g.name.StartsWith("KS_PROTECTED", StringComparison.Ordinal)).ToArray();
                Check(leftovers.Length == 0, "no protected clone was left in the scene (the 3.x tool used to leak one)");

                // ── 6. and the protected copy really was produced somewhere else
                Check(result.ok && !string.IsNullOrEmpty(result.prefabPath) &&
                      result.prefabPath.StartsWith(KawaiiStudioPaths.Protected, StringComparison.Ordinal),
                      "the protected avatar was written to the Protected folder instead: " + result.prefabPath);
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL  threw: " + e);
                failures++;
            }
            finally
            {
                if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
                try { if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath)) File.Delete(keyPath); } catch { }
                try
                {
                    if (result != null && !string.IsNullOrEmpty(result.folder) && AssetDatabase.IsValidFolder(result.folder))
                        AssetDatabase.DeleteAsset(result.folder);
                }
                catch { }
                try { if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root); } catch { }
                AssetDatabase.Refresh();
            }

            ok = failures == 0;
            log.AppendLine(ok ? "KS ANTI-RIP NO-HARM TEST: ALL PASSED" : $"KS ANTI-RIP NO-HARM TEST: {failures} FAILED");
            return log.ToString();
        }

        static Dictionary<string, string> Hashes(string[] files)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var sha = SHA256.Create();
            foreach (string f in files)
            {
                using FileStream fs = File.OpenRead(f);
                map[f] = BitConverter.ToString(sha.ComputeHash(fs));
            }
            return map;
        }

        /// <summary>A real avatar saved as assets: mesh, material, clip, FX controller, parameters, prefab.</summary>
        static string BuildAvatarOnDisk()
        {
            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "KS_AntiRip_NoHarm");

            const int n = 400;
            var rng = new System.Random(7);
            var verts = new Vector3[n];
            var delta = new Vector3[n];
            var tris = new int[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                delta[i] = new Vector3(0f, 0.03f, 0f);
                tris[i] = i;
            }
            var mesh = new Mesh { name = "Body" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.AddBlendShapeFrame("Viseme_AA", 100f, delta, null, null);
            AssetDatabase.CreateAsset(mesh, Root + "/Body.asset");

            var mat = new Material(Shader.Find("Standard")) { name = "Skin" };
            AssetDatabase.CreateAsset(mat, Root + "/Skin.mat");

            var clip = new AnimationClip { name = "Wave" };
            clip.SetCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Viseme_AA",
                          AnimationCurve.Linear(0f, 0f, 1f, 100f));
            AssetDatabase.CreateAsset(clip, Root + "/Wave.anim");

            var fx = AnimatorController.CreateAnimatorControllerAtPath(Root + "/FX.controller");
            fx.AddParameter("Talk", AnimatorControllerParameterType.Float);
            fx.AddLayer("Face");
            fx.layers[0].stateMachine.AddState("Idle").motion = clip;

            var ps = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            ps.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "Talk",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                },
            };
            AssetDatabase.CreateAsset(ps, Root + "/Parameters.asset");

            var root = new GameObject("NoHarmAvatar");
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
            pipeline.blueprintId = "avtr_00000000-0000-0000-0000-0000noharm";

            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var smr = body.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.sharedMaterial = mat;

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    animatorController = fx,
                },
            };
            descriptor.expressionParameters = ps;

            string prefabPath = Root + "/NoHarmAvatar.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefabPath;
        }
    }
}
