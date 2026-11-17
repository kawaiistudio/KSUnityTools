using System;
using System.IO;
using System.Linq;
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
    /// Runs a whole protection pass on a synthetic avatar and checks what came out: the prefab, the scrambled
    /// mesh and its restore shapes, the lock layer and its conditions, the synced parameters, and the `.key`.
    /// Compiling proves the APIs exist; this proves the pass actually produces a working avatar.
    ///
    ///   "…\Unity.exe" -batchmode -nographics -quit -projectPath "<a project with the VRChat SDK>" \
    ///       -executeMethod KawaiiStudio.VRC.AntiRip.KSAntiRipIntegrationTest.RunFromCommandLine
    ///
    /// It writes into Assets/Kawaii Studio/Protected like a real run and deletes that output afterwards. No key is
    /// written: the stand-in plan keeps its values in memory, where the real core would write a .key. Exit code
    /// 0 = everything passed.
    /// </summary>
    public static class KSAntiRipIntegrationTest
    {
        [MenuItem("Kawaii Studio/VRC/Debug/KS Anti-Rip integration test", false, 901)]
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

            GameObject avatar = null;
            KSAntiRipProcessor.Result result = null;
            string keyPath = null;

            try
            {
                avatar = BuildSyntheticAvatar(out SkinnedMeshRenderer body, out Mesh sourceMesh);
                var settings = new KSAntiRipProcessor.Settings
                {
                    strength = 4f,
                    restoreGroups = 3,
                    syncKey = true,
                    hideWhileLocked = true,
                };

                var planner = new KSAntiRipTestPlan(strength: 4f, groups: 3);
                result = KSAntiRipProcessor.Protect(avatar, settings, planner);
                Check(result.ok, "the pass reports success: " + result.message);
                if (!result.ok) { ok = false; return log.ToString(); }

                // 1. the source avatar is untouched -- the whole promise of "non-destructive"
                Check(body.sharedMesh == sourceMesh, "the source renderer still points at its original mesh");
                Check(sourceMesh.blendShapeCount == 1, "the source mesh still has only its own blend shape");
                Check(avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.enabled),
                      "the source renderers were not disabled");

                // 2. the protected prefab
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.prefabPath);
                Check(prefab != null, "the protected prefab exists at " + result.prefabPath);
                if (prefab == null) { ok = false; return log.ToString(); }

                // the "do not archive me" mark VRChat Archive reads out of the bundle
                Transform marker = prefab.transform.Find(KSAntiRipProcessor.MarkerName);
                Check(marker != null, "the protected avatar carries the " + KSAntiRipProcessor.MarkerName + " mark");
                Check(marker != null && !marker.gameObject.activeSelf, "the mark is inactive, so it costs nothing at runtime");
                Check(marker != null && marker.GetComponents<Component>().Length == 1,
                      "the mark is an empty GameObject (Transform only)");
                Check(marker != null && marker.GetSiblingIndex() == 0,
                      "the mark is FIRST under the root, exactly where kawaii_inject.py puts it");
                Check(marker != null && marker.name == "Kawaii Studio Bundle",
                      "the mark kept its readable name through the scrambler: " + (marker != null ? marker.name : "<missing>"));
                Check(avatar.transform.Find(KSAntiRipProcessor.MarkerName) == null,
                      "the mark was NOT added to the source avatar");

                var protectedSmr = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault();
                Check(protectedSmr != null && protectedSmr.sharedMesh != null, "the protected prefab has a skinned mesh");
                Mesh scrambled = protectedSmr.sharedMesh;
                Check(scrambled != sourceMesh, "it is a different mesh asset, not the original");
                Check(scrambled.blendShapeCount == 1 + settings.restoreGroups,
                      $"the mesh carries its own shape plus {settings.restoreGroups} restore shapes ({scrambled.blendShapeCount})");

                // 3. the geometry really is scrambled
                Vector3[] a = sourceMesh.vertices, b = scrambled.vertices;
                float maxOff = 0f;
                for (int i = 0; i < Mathf.Min(a.Length, b.Length); i++)
                    maxOff = Mathf.Max(maxOff, (a[i] - b[i]).magnitude);
                Check(maxOff > settings.strength * 0.25f, $"the shipped vertices are displaced (max {maxOff:F2} m)");

                // 4. the lock layer
                var descriptor = prefab.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers
                    .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                Check(fx != null, "the prefab has an FX controller");
                var layer = fx.layers.FirstOrDefault(l => l.name == result.lockLayer);
                Check(layer.stateMachine != null, "the FX controller has the lock layer (" + result.lockLayer + ")");
                Check(!fx.layers.Any(l => l.name == KSAntiRipLock.LayerName),
                      "no layer is called \"" + KSAntiRipLock.LayerName + "\" -- the lock does not announce itself");
                Check(result.objectsRenamed > 0, $"the hierarchy was obfuscated ({result.objectsRenamed} objects renamed)");
                Check(prefab.transform.Find(KSAntiRipProcessor.MarkerName) != null,
                      "the archive mark survived the renaming");
                // the body mesh keeps its name (MMD worlds look up "Body"); the clothing does not
                Check(prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == "Body"),
                      "the body mesh keeps its name");
                Check(prefab.GetComponentsInChildren<Transform>(true).All(t => t.name != "Dress"),
                      "the clothing object was renamed away");

                // The restore layer is one state running a Direct BlendTree: child k puts restore shape k at 100,
                // weighted by key parameter k. So the shape's weight IS the parameter, and nothing else says so.
                var restoreState = layer.stateMachine.defaultState;
                Check(restoreState != null, "the restore layer has its state");
                var tree = restoreState != null ? restoreState.motion as BlendTree : null;
                Check(tree != null && tree.blendType == BlendTreeType.Direct, "it runs a Direct BlendTree");
                Check(restoreState != null && restoreState.writeDefaultValues,
                      "Write Defaults is ON there, which a Direct BlendTree needs");
                var issued = planner.Issued.parameters;
                Check(tree != null && tree.children.Length == settings.restoreGroups,
                      $"one child per restore shape ({(tree != null ? tree.children.Length : -1)})");
                Check(tree != null && tree.children.Select(c => c.directBlendParameter).SequenceEqual(issued),
                      "each child is weighted by the key parameter the app issued for it, in order");

                // the show layer: renderers hidden until the key has arrived
                var show = fx.layers.FirstOrDefault(l => l.stateMachine != null && l.stateMachine != layer.stateMachine &&
                                                         l.stateMachine.states.Length == 2 &&
                                                         l.stateMachine.states.Any(s => s.state.transitions.Any(
                                                             t => t.conditions.Any(c => c.parameter == issued[0]))));
                Check(show.stateMachine != null, "a show layer hides the renderers until key parameter 0 arrives");

                // 5. the key parameters
                var ps = descriptor.expressionParameters;
                Check(ps != null, "the prefab has expression parameters");
                var keys = ps.parameters.Where(p => p != null && issued.Contains(p.name)).ToArray();
                Check(keys.Length == settings.restoreGroups, $"{settings.restoreGroups} key parameters were added ({keys.Length})");
                Check(keys.All(p => p.valueType == VRCExpressionParameters.ValueType.Float), "they are floats");
                Check(keys.All(p => p.networkSynced), "they are network-synced, so other players see the avatar whole");
                Check(keys.All(p => !p.saved && Mathf.Approximately(p.defaultValue, 0f)),
                      "they are not saved and default to 0, so every session starts locked");
                var sourceParams = avatar.GetComponent<VRCAvatarDescriptor>().expressionParameters;
                int sourceCost = sourceParams != null ? sourceParams.CalcTotalCost() : 0;
                Check(ps.CalcTotalCost() - sourceCost == settings.KeyBits,
                      $"the key costs {settings.KeyBits} synced bits, 8 per shape ({ps.CalcTotalCost() - sourceCost})");
                Check(planner.Ended, "the session was closed on the app side");

                // 6. THE POINT OF 5.1: nothing in the bundle says what the weights are. Every curve on a restore
                //    shape, in every clip the protection wrote, reads 0 or 100 -- and none reads a key value.
                var restoreShapes = new System.Collections.Generic.HashSet<string>();
                for (int s = 0; s < scrambled.blendShapeCount; s++)
                {
                    string n = scrambled.GetBlendShapeName(s);
                    if (n != "Viseme_Test") restoreShapes.Add(n);
                }
                int curves = 0, leaks = 0;
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { result.folder }))
                {
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
                    if (clip == null) continue;
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip))
                    {
                        if (!binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal)) continue;
                        if (!restoreShapes.Contains(binding.propertyName.Substring("blendShape.".Length))) continue;
                        curves++;
                        foreach (Keyframe k in AnimationUtility.GetEditorCurve(clip, binding).keys)
                            if (!Mathf.Approximately(k.value, 0f) && !Mathf.Approximately(k.value, 100f)) leaks++;
                    }
                }
                Check(curves == settings.restoreGroups, $"the protection animates every restore shape ({curves} curves)");
                Check(leaks == 0, $"no curve holds a weight: every value is 0 or 100 ({leaks} leaks)");

                // 7. with the key, it comes back -- including after VRChat's 8-bit network rounding
                if (tree != null && tree.children.Length == settings.restoreGroups)
                {
                    var shapeNames = new string[settings.restoreGroups];
                    var weights = new float[settings.restoreGroups];
                    var rounded = new float[settings.restoreGroups];
                    for (int k = 0; k < settings.restoreGroups; k++)
                    {
                        var clip = tree.children[k].motion as AnimationClip;
                        var binding = AnimationUtility.GetCurveBindings(clip)
                            .First(x => x.propertyName.StartsWith("blendShape.", StringComparison.Ordinal));
                        shapeNames[k] = binding.propertyName.Substring("blendShape.".Length);
                        float v = planner.KeyValues[k];
                        weights[k] = v * 100f;
                        rounded[k] = Mathf.Round(v * 127f) / 127f * 100f;   // what a remote player receives
                    }
                    float err = MaxError(KSAntiRipMesh.RestoreForTest(scrambled, weights, shapeNames), a);
                    Check(err < 1e-3f, $"the key restores the original mesh (max error {err:E2} m)");
                    float remote = MaxError(KSAntiRipMesh.RestoreForTest(scrambled, rounded, shapeNames), a);
                    Check(remote < 1e-3f, $"so does the key as VRChat delivers it to others (max error {remote:E2} m)");
                }
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL  threw: " + e);
                failures++;
            }
            finally
            {
                if (avatar != null) UnityEngine.Object.DestroyImmediate(avatar);
                try { if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath)) File.Delete(keyPath); } catch { }
                try
                {
                    if (result != null && !string.IsNullOrEmpty(result.folder) && AssetDatabase.IsValidFolder(result.folder))
                        AssetDatabase.DeleteAsset(result.folder);
                }
                catch { }
                AssetDatabase.Refresh();
            }

            ok = failures == 0;
            log.AppendLine(ok ? "KS ANTI-RIP INTEGRATION TEST: ALL PASSED" : $"KS ANTI-RIP INTEGRATION TEST: {failures} FAILED");
            return log.ToString();
        }

        /// <summary>A minimal but real avatar: descriptor, blueprint id, one skinned mesh with a blend shape.</summary>
        static float MaxError(Vector3[] got, Vector3[] want)
        {
            float err = 0f;
            for (int i = 0; i < Mathf.Min(got.Length, want.Length); i++)
                err = Mathf.Max(err, (got[i] - want[i]).magnitude);
            return err;
        }

        static GameObject BuildSyntheticAvatar(out SkinnedMeshRenderer body, out Mesh mesh)
        {
            var root = new GameObject("KS_AntiRip_TestAvatar");
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
            pipeline.blueprintId = "avtr_00000000-0000-0000-0000-00000000test";

            var child = new GameObject("Body");
            child.transform.SetParent(root.transform, false);
            body = child.AddComponent<SkinnedMeshRenderer>();

            // A clothing object, like a real avatar has: it is NOT the body and NOT a bone, so the obfuscator must
            // rename it (a body mesh named "Body" is kept for MMD; clothing is the thing a ripper reads). Gives the
            // "objects renamed" check something to be true about.
            var dress = new GameObject("Dress");
            dress.transform.SetParent(root.transform, false);
            var dressMesh = new Mesh { name = "KS_AntiRip_DressMesh" };
            dressMesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            dressMesh.triangles = new[] { 0, 1, 2 };
            dress.AddComponent<SkinnedMeshRenderer>().sharedMesh = dressMesh;

            const int n = 600;
            var rng = new System.Random(99);
            var verts = new Vector3[n];
            var delta = new Vector3[n];
            var tris = new int[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                delta[i] = new Vector3(0f, 0.05f, 0f);
                tris[i] = i;
            }
            mesh = new Mesh { name = "KS_AntiRip_TestMesh" };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.AddBlendShapeFrame("Viseme_AA", 100f, delta, null, null);
            body.sharedMesh = mesh;

            // The descriptor needs an FX slot for the lock to land in; leave the controller null so the
            // processor exercises its "this avatar had no FX controller" path too.
            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = true },
            };
            return root;
        }
    }
}
