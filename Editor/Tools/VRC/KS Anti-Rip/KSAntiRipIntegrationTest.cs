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

                // the freeze layer: no key after the grace period, and the wearer cannot walk and the menu does nothing
                var freeze = fx.layers.FirstOrDefault(l => l.name == result.freezeLayer);
                Check(!string.IsNullOrEmpty(result.freezeLayer) && freeze.stateMachine != null,
                      "a freeze layer was added (" + result.freezeLayer + ")");
                if (freeze.stateMachine != null)
                {
                    Check(fx.layers[fx.layers.Length - 1].name == result.freezeLayer, "it is the last layer of the controller");
                    Check(result.freezeLayer != KSAntiRipLock.FreezeLayerName, "it does not announce itself by name");
                    AnimatorState waitState = freeze.stateMachine.defaultState;
                    var all = freeze.stateMachine.states.Select(s => s.state).ToArray();
                    Check(all.Length == 4, "it has its four states: wait, open, frozen, thaw (" + all.Length + ")");
                    Check(waitState != null && waitState.behaviours.Length == 0, "the state an avatar starts in does nothing");
                    Check(waitState != null && waitState.transitions.Length == 2 &&
                          !waitState.transitions[0].hasExitTime &&
                          waitState.transitions[0].conditions.Length == 1 &&
                          waitState.transitions[0].conditions[0].parameter == issued[0] &&
                          waitState.transitions[0].conditions[0].mode == AnimatorConditionMode.Greater,
                          "a key that is already here leaves it at once, before the clock is looked at");
                    AnimatorState openState = waitState != null && waitState.transitions.Length > 0 ? waitState.transitions[0].destinationState : null;
                    Check(openState != null && openState.behaviours.Length == 0,
                          "and lands in a state with no behaviour: an allowed wearer's avatar is never touched");
                    AnimatorStateTransition late = waitState != null && waitState.transitions.Length > 1 ? waitState.transitions[1] : null;
                    Check(late != null && late.hasExitTime && Mathf.Approximately(late.exitTime, KSAntiRipLock.FreezeGraceSeconds) &&
                          late.conditions.Length == 0,
                          "without a key it freezes after the grace period (" + KSAntiRipLock.FreezeGraceSeconds + " s)");
                    var waitClip = waitState != null ? waitState.motion as AnimationClip : null;
                    Check(waitClip != null && Mathf.Approximately(waitClip.length, 1f), "the grace period counts against a one-second clip");
                    AnimatorState frozenState = late != null ? late.destinationState : null;
                    var stop = frozenState != null ? frozenState.behaviours.OfType<VRCAnimatorLocomotionControl>().ToArray() : new VRCAnimatorLocomotionControl[0];
                    Check(stop.Length == 1 && stop[0].disableLocomotion, "frozen: the wearer cannot walk");
                    AnimatorState thawState = frozenState != null && frozenState.transitions.Length == 1 ? frozenState.transitions[0].destinationState : null;
                    Check(thawState != null && frozenState.transitions[0].conditions.Length == 1 &&
                          frozenState.transitions[0].conditions[0].parameter == issued[0] &&
                          frozenState.transitions[0].conditions[0].mode == AnimatorConditionMode.Greater,
                          "the key arriving later thaws it");
                    var go2 = thawState != null ? thawState.behaviours.OfType<VRCAnimatorLocomotionControl>().ToArray() : new VRCAnimatorLocomotionControl[0];
                    Check(go2.Length == 1 && !go2[0].disableLocomotion, "thaw: the wearer walks again");
                    Check(thawState != null && thawState.transitions.Length == 1 && thawState.transitions[0].destinationState == openState,
                          "and the avatar ends in the same open state");
                    Check(openState != null && openState.transitions.Length == 1 && openState.transitions[0].destinationState == waitState &&
                          openState.transitions[0].conditions[0].mode == AnimatorConditionMode.Less,
                          "a key that goes away starts the clock again");
                    Check(all.SelectMany(s => s.transitions).SelectMany(t => t.conditions).All(c => Mathf.Approximately(c.threshold, 0.001f)),
                          "no condition of the freeze layer holds a key value: they all test for \"not zero\"");
                }
                FreezeBlocksLayers(Check, result.folder);

                // the locked badge: the one thing shown while the avatar is locked
                Check(!string.IsNullOrEmpty(result.badgePath), "a locked badge was added (" + result.badgePath + ")");
                Transform badge = string.IsNullOrEmpty(result.badgePath) ? null : prefab.transform.Find(result.badgePath);
                Check(badge != null && badge.parent == prefab.transform, "it is one object, directly under the avatar root");
                if (badge != null)
                {
                    Check(badge.name != KSAntiRipBadge.ObjectName, "it does not announce itself by name");
                    Check(badge.gameObject.activeSelf, "it is ON by default: an avatar starts locked");
                    Check(badge.GetSiblingIndex() == prefab.transform.childCount - 1 && marker != null && marker.GetSiblingIndex() == 0,
                          "it is the LAST child, so the archive mark is still the first");
                    var badgeRenderer = badge.GetComponent<MeshRenderer>();
                    var badgeMesh = badge.GetComponent<MeshFilter>() != null ? badge.GetComponent<MeshFilter>().sharedMesh : null;
                    Material[] badgeMaterials = badgeRenderer != null ? badgeRenderer.sharedMaterials : new Material[0];
                    // 3.13.0: the poster alone -- the 3D heart filled the wearer's own view
                    Check(badgeMaterials.Length == 1 && badgeMaterials[0] != null && badgeMaterials[0].shader != null &&
                          badgeMaterials[0].shader.name == KSAntiRipBadge.ShaderName,
                          "it is the poster alone: one material, the badge shader (" + KSAntiRipBadge.ShaderName + ")");
                    Check(badgeMaterials.Length == 1 && badgeMaterials[0] != null && badgeMaterials[0].HasProperty("_Push") &&
                          Mathf.Approximately(badgeMaterials[0].GetFloat("_Push"), 0f),
                          "it is not pushed towards the viewer, so it never comes closer to the wearer's own eyes");
                    string picture = badgeMaterials.Length == 1 && badgeMaterials[0] != null && badgeMaterials[0].mainTexture != null
                        ? AssetDatabase.GetAssetPath(badgeMaterials[0].mainTexture) : "";
                    Check(picture.Length > 0 && picture != KSAntiRipBadge.TexturePath && picture.StartsWith(result.folder + "/"),
                          "with its own copy of the badge picture, in the build folder (" + picture + ")");
                    var pictureImport = AssetImporter.GetAtPath(picture) as TextureImporter;
                    // VRChat SDK, VerifyAvatarMipMapStreaming: a mipmapped texture without streaming stops the upload
                    Check(pictureImport != null && !(pictureImport.mipmapEnabled && !pictureImport.streamingMipmaps),
                          "imported the way the VRChat SDK accepts it (mip maps " + (pictureImport != null && pictureImport.mipmapEnabled ? "on" : "off") +
                          ", streaming " + (pictureImport != null && pictureImport.streamingMipmaps ? "on" : "off") + ")");
                    Check(badgeMesh != null && badgeMesh.subMeshCount == 1 && badgeMesh.triangles.Length == 6 && badgeMesh.vertexCount == 4,
                          "one quad: four corners, two triangles");
                    Check(badgeMesh != null && Mathf.Abs(badgeMesh.bounds.center.y) < 0.001f,
                          "the poster is centred on its object, where the heart used to sit above it");
                    Check(badgeMesh != null && badgeMesh.bounds.size.z > 1f, "its bounds are a box, so turning to the viewer never culls it");
                    // 3.13.1: in front of the eyes, where the wearer reads it straight on (no head bone here: on the root)
                    Vector3 view = descriptor.ViewPosition.y > 0.2f ? descriptor.ViewPosition : new Vector3(0f, 1.5f, 0.1f);
                    float width = Mathf.Clamp(view.y * 0.2f, 0.12f, 0.6f), ahead = Mathf.Clamp(view.y * 0.21f, 0.12f, 0.6f);
                    Check(Mathf.Abs(badge.position.y - view.y) < 0.01f && Mathf.Abs(badge.position.z - (view.z + ahead)) < 0.01f &&
                          Mathf.Abs(badge.lossyScale.x - width) < 0.01f,
                          $"it sits in front of the eyes, sized to the avatar ({badge.lossyScale.x:F2} m wide, {ahead:F2} m ahead, at {badge.position.y:F2} m)");
                    // the show layer switches it: there while locked, gone once the key has arrived
                    AnimatorState lockedState = show.stateMachine != null ? show.stateMachine.defaultState : null;
                    AnimatorState keyState = lockedState != null && lockedState.transitions.Length > 0 ? lockedState.transitions[0].destinationState : null;
                    float lockedValue = BadgeActive(lockedState, result.badgePath), keyValue = BadgeActive(keyState, result.badgePath);
                    Check(Mathf.Approximately(lockedValue, 1f), "locked: the show layer keeps the badge on (" + lockedValue + ")");
                    Check(Mathf.Approximately(keyValue, 0f), "key arrived: the show layer switches the badge off (" + keyValue + ")");
                }
                Check(avatar.GetComponentsInChildren<MeshRenderer>(true).SelectMany(r => r.sharedMaterials)
                          .All(x => x == null || x.shader == null || x.shader.name != KSAntiRipBadge.ShaderName),
                      "the badge was NOT added to the source avatar");

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

        /// <summary>What a state's clip sets the badge's m_IsActive to; -1 when it does not drive it.</summary>
        static float BadgeActive(AnimatorState state, string badgePath)
        {
            var clip = state != null ? state.motion as AnimationClip : null;
            if (clip == null) return -1f;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                if (b.path == badgePath && b.type == typeof(GameObject) && b.propertyName == "m_IsActive")
                    return AnimationUtility.GetEditorCurve(clip, b).Evaluate(0f);
            return -1f;
        }

        /// <summary>
        /// The animator blocker, on a controller with layers of its own: while frozen every one of them weighs
        /// nothing, the thaw gives each its own weight back, and neither touches layer 0 or the lock's own layers.
        /// </summary>
        static void FreezeBlocksLayers(Action<bool, string> Check, string folder)
        {
            var controller = AnimatorController.CreateAnimatorControllerAtPath(folder + "/_freeze_test.controller");
            controller.AddLayer("Toggles");
            controller.AddLayer("Half");
            controller.AddLayer("Off");
            AnimatorControllerLayer[] own = controller.layers;
            own[1].defaultWeight = 1f;
            own[2].defaultWeight = 0.5f;
            own[3].defaultWeight = 0f;
            controller.layers = own;
            var dummy = new GameObject("freeze_test");
            try
            {
                KSAntiRipLock.Build(dummy, null, controller, new System.Collections.Generic.List<KSAntiRipLock.ShapeTarget>(),
                                    new System.Collections.Generic.List<string>(), new[] { "k0", "k1" }, true, folder,
                                    new System.Random(7), false, true);
                AnimatorControllerLayer[] layers = controller.layers;
                Check(layers.Length == 6 && layers[4].name == KSAntiRipLock.LayerName && layers[5].name == KSAntiRipLock.FreezeLayerName,
                      "animator blocker: the lock added its restore layer and its freeze layer after the avatar's four (" + layers.Length + ")");
                AnimatorState[] states = layers[layers.Length - 1].stateMachine.states.Select(s => s.state).ToArray();
                AnimatorState frozen = states.FirstOrDefault(s => s.behaviours.OfType<VRCAnimatorLocomotionControl>().Any(b => b.disableLocomotion));
                AnimatorState thaw = states.FirstOrDefault(s => s.behaviours.OfType<VRCAnimatorLocomotionControl>().Any(b => !b.disableLocomotion));
                Check(frozen != null && thaw != null && frozen != thaw, "animator blocker: it has a frozen state and a thaw state");
                if (frozen == null || thaw == null) return;
                var off = frozen.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
                var on = thaw.behaviours.OfType<VRCAnimatorLayerControl>().ToArray();
                Check(off.Select(b => b.layer).SequenceEqual(new[] { 1, 2, 3 }) && off.All(b => Mathf.Approximately(b.goalWeight, 0f)),
                      "frozen: the avatar's layers 1, 2 and 3 weigh nothing, so its menu does nothing");
                Check(on.Select(b => b.layer).SequenceEqual(new[] { 1, 2, 3 }) &&
                      Mathf.Approximately(on[0].goalWeight, 1f) && Mathf.Approximately(on[1].goalWeight, 0.5f) && Mathf.Approximately(on[2].goalWeight, 0f),
                      "thaw: each layer gets its own weight back (1, 0.5, 0)");
                Check(off.Concat(on).All(b => b.playable == global::VRC.SDKBase.VRC_AnimatorLayerControl.BlendableLayer.FX && Mathf.Approximately(b.blendDuration, 0f)),
                      "both act on the FX controller, at once");
                Check(states.Where(s => s != frozen && s != thaw).All(s => s.behaviours.Length == 0),
                      "the two other states carry no behaviour");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dummy);
            }
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
