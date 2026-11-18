using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// THE UPLOAD HOOK, WHOLE, ON AND OFF (3.13.0). Owner, 2026-11-18: "faudra que tu fasses des tests aussi : quand la
    /// protection est off ça doit upload normalement, quand c'est on ça upload en protection".
    ///
    /// Runs <see cref="KSAntiRipBuildHook.OnPreprocessAvatar"/> -- the exact code the VRChat SDK runs on the copy it is
    /// about to upload -- on a test avatar with the parts a real one has: a scrambled body, a static prop, particles,
    /// a light, a sound, an Animator of its own, and a jacket its FX toggles.
    ///   OFF (no mark)          the copy comes out exactly as it went in, nothing is written to the project, nothing
    ///                          is said to the app.
    ///   OFF (mark switched off) the same, and the mark itself is gone (it must never ship).
    ///   ON                     protected: the body scrambled, the lock and the freeze in the FX, the key parameters,
    ///                          the poster; while locked everything is hidden and stilled except the jacket the
    ///                          avatar switches itself; then the cleanup leaves no build files behind.
    /// The plan comes from <see cref="KSAntiRipTestPlan"/> in place of the app, so it runs anywhere, app or not.
    /// </summary>
    public static class KSAntiRipHookTest
    {
        [MenuItem("Kawaii Studio/VRC/Debug/KS Anti-Rip upload hook test", false, 902)]
        public static void RunFromMenu() => Debug.Log(Run(out _));

        const string Work = "Assets/Kawaii Studio/_hooktest";

        public static string Run(out bool ok)
        {
            var log = new StringBuilder();
            int failures = 0;
            void Check(bool condition, string what)
            {
                log.AppendLine((condition ? "PASS  " : "FAIL  ") + what);
                if (!condition) failures++;
            }

            var made = new List<GameObject>();
            var reports = new List<string>();
            KSAntiRipBuildHook.ReportForTests = (m, d) =>
            {
                reports.Add(m);
                if (d != null) reports.AddRange(d.Select(x => "   " + x));
            };
            try
            {
                KawaiiStudioPaths.EnsureFolder(Work);
                var hook = new KSAntiRipBuildHook();

                // ── OFF: no mark ───────────────────────────────────────────────────────────────────────
                GameObject plain = Avatar("off", made);
                string before = Snapshot(plain);
                int folders = BuildFolders();
                reports.Clear();
                bool go = hook.OnPreprocessAvatar(plain);
                Check(go, "off, no mark: the hook lets the upload go on");
                Check(Snapshot(plain) == before, "off, no mark: the avatar comes out exactly as it went in");
                Check(BuildFolders() == folders, "off, no mark: nothing is written to the project");
                Check(reports.Count == 0, "off, no mark: nothing is said to the app");

                // ── OFF: the mark is there but switched off in the app ─────────────────────────────────
                GameObject paused = Avatar("paused", made);
                string pausedBefore = Snapshot(paused);
                var pausedMark = paused.AddComponent<KSAntiRipMark>();
                pausedMark.enabled_ = false;
                reports.Clear();
                go = hook.OnPreprocessAvatar(paused);
                Check(go, "off, mark switched off: the hook lets the upload go on");
                Check(paused.GetComponent<KSAntiRipMark>() == null, "off, mark switched off: the mark itself does not ship");
                Check(Snapshot(paused) == pausedBefore, "off, mark switched off: everything else comes out exactly as it went in");
                Check(BuildFolders() == folders && reports.Count == 0, "off, mark switched off: nothing written, nothing said");

                // ── ON ─────────────────────────────────────────────────────────────────────────────────
                GameObject source = Avatar("on", made);
                source.AddComponent<KSAntiRipMark>();   // the defaults the app sets: hide, freeze, badge, names
                string sourceBefore = Snapshot(source);
                // what the SDK does: it protects a COPY, named "<avatar>(Clone)"
                GameObject copy = UnityEngine.Object.Instantiate(source);
                made.Add(copy);
                Mesh bodyMesh = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0].sharedMesh;
                var planner = new KSAntiRipTestPlan(strength: 4f, groups: 3);
                KSAntiRipBuildHook.PlanSourceForTests = planner;
                reports.Clear();
                go = hook.OnPreprocessAvatar(copy);
                Check(go, "on: the hook lets the upload go on, protected");
                Check(Snapshot(source) == sourceBefore, "on: the avatar in the scene is untouched (only the SDK's copy changes)");
                Check(copy.GetComponent<KSAntiRipMark>() == null, "on: the mark does not ship");
                Check(planner.Issued != null && planner.Ended, "on: a plan was asked for and its session closed");
                Check(reports.Count > 0 && reports[0].StartsWith("protected ", StringComparison.Ordinal),
                      "on: the app is told it was protected (" + (reports.Count > 0 ? reports[0] : "nothing") + ")");
                var body = copy.GetComponentsInChildren<SkinnedMeshRenderer>(true)[0];
                Check(body.sharedMesh != bodyMesh && body.sharedMesh.blendShapeCount > bodyMesh.blendShapeCount,
                      "on: the body is the scrambled mesh, with its restore shapes");
                // one folder: the hook first clears what an earlier, failed build left (the bench had one)
                Check(BuildFolders() == 1, "on: the build's files are in one build folder (" + folders + " left from before, cleared)");

                var descriptor = copy.GetComponent<VRCAvatarDescriptor>();
                var fx = descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
                var keys = descriptor.expressionParameters != null
                    ? descriptor.expressionParameters.parameters.Where(p => p != null && planner.Issued.parameters.Contains(p.name)).ToArray()
                    : new VRCExpressionParameters.Parameter[0];
                Check(keys.Length == 3, "on: the three key parameters are on the avatar (" + keys.Length + ")");

                // the poster: one quad, one material
                MeshRenderer poster = copy.GetComponentsInChildren<MeshRenderer>(true)
                    .FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == KSAntiRipBadge.ShaderName);
                Check(poster != null && poster.sharedMaterials.Length == 1 && poster.GetComponent<MeshFilter>().sharedMesh.vertexCount == 4,
                      "on: the locked avatar shows the poster alone");
                string posterPath = poster != null ? AnimationUtility.CalculateTransformPath(poster.transform, copy.transform) : "";

                // WHILE LOCKED: the show layer's first state
                AnimatorControllerLayer show = fx.layers.FirstOrDefault(l => l.stateMachine != null && l.stateMachine.states.Any(s =>
                    s.state.motion is AnimationClip c && AnimationUtility.GetCurveBindings(c).Any(b => b.path == posterPath && b.propertyName == "m_IsActive")));
                AnimationClip hidden = show.stateMachine != null ? show.stateMachine.defaultState.motion as AnimationClip : null;
                AnimationClip shown = show.stateMachine != null ? show.stateMachine.defaultState.transitions[0].destinationState.motion as AnimationClip : null;
                Check(hidden != null && shown != null, "on: the show layer is there, locked first");
                string P(Component c) => AnimationUtility.CalculateTransformPath(c.transform, copy.transform);
                MeshRenderer scythe = copy.GetComponentsInChildren<MeshRenderer>(true).First(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount == 6);
                MeshRenderer jacket = copy.GetComponentsInChildren<MeshRenderer>(true).First(r => r.GetComponent<MeshFilter>().sharedMesh.vertexCount == 5);
                ParticleSystem sparks = copy.GetComponentsInChildren<ParticleSystem>(true)[0];
                Light glow = copy.GetComponentsInChildren<Light>(true)[0];
                AudioSource hum = copy.GetComponentsInChildren<AudioSource>(true)[0];
                Animator wings = copy.GetComponentsInChildren<Animator>(true).First(a => a.gameObject != copy);
                foreach ((Component c, string what) in new (Component, string)[]
                         {
                             (scythe, "the static prop"), (sparks.GetComponent<ParticleSystemRenderer>(), "the particles"),
                             (glow, "the light"), (hum, "the sound"),
                         })
                {
                    Check(Value(hidden, P(c), c.GetType(), "m_Enabled") == 0f && Value(shown, P(c), c.GetType(), "m_Enabled") == 1f,
                          "locked: " + what + " is off, and back on with the key (" + c.GetType().Name + ")");
                }
                Check(Value(hidden, P(body), typeof(SkinnedMeshRenderer), "m_Enabled") == 0f, "locked: the scrambled body is off, as before");
                Check(Value(hidden, P(jacket), typeof(MeshRenderer), "m_Enabled") < 0f && Value(shown, P(jacket), typeof(MeshRenderer), "m_Enabled") < 0f,
                      "the jacket the avatar's own toggle switches is never touched, so the toggle still works with the key");
                Check(reports.Any(r => r.Contains("keep running while it is locked")), "and the app is told what was left to the avatar");
                Check(Value(hidden, posterPath, typeof(GameObject), "m_IsActive") == 1f && Value(shown, posterPath, typeof(GameObject), "m_IsActive") == 0f,
                      "locked: the poster is on, and off with the key");

                // FROZEN: the freeze layer's states
                AnimatorStateMachine freeze = fx.layers[fx.layers.Length - 1].stateMachine;
                AnimatorState frozen = freeze.states.Select(s => s.state).First(s => s.behaviours.OfType<VRCAnimatorLocomotionControl>().Any(b => b.disableLocomotion));
                AnimatorState thaw = freeze.states.Select(s => s.state).First(s => s.behaviours.OfType<VRCAnimatorLocomotionControl>().Any(b => !b.disableLocomotion));
                var stopLayers = frozen.behaviours.OfType<VRCPlayableLayerControl>().ToArray();
                var goLayers = thaw.behaviours.OfType<VRCPlayableLayerControl>().ToArray();
                Check(stopLayers.Length == 3 && stopLayers.All(b => Mathf.Approximately(b.goalWeight, 0f)) &&
                      new[] { VRC_PlayableLayerControl.BlendableLayer.Gesture, VRC_PlayableLayerControl.BlendableLayer.Additive, VRC_PlayableLayerControl.BlendableLayer.Action }
                          .All(l => stopLayers.Any(b => b.layer == l)),
                      "frozen: hands, breathing and emotes stop (Gesture, Additive and Action to nothing)");
                Check(goLayers.Length == 2 && goLayers.All(b => Mathf.Approximately(b.goalWeight, 1f) && b.layer != VRC_PlayableLayerControl.BlendableLayer.Action),
                      "thaw: Gesture and Additive come back; Action is left to the avatar's own states");
                var stopClip = frozen.motion as AnimationClip;
                var goClip = thaw.motion as AnimationClip;
                Check(Value(stopClip, P(wings), typeof(Animator), "m_Enabled") == 0f && Value(goClip, P(wings), typeof(Animator), "m_Enabled") == 1f,
                      "frozen: the avatar's own Animator below the root stops, and starts again on thaw");
                Check(Value(stopClip, P(sparks), typeof(ParticleSystem), "EmissionModule.enabled") == 0f &&
                      Value(goClip, P(sparks), typeof(ParticleSystem), "EmissionModule.enabled") == 1f,
                      "frozen: the particles stop being emitted, and start again on thaw");

                // every property the lock writes is one Unity can really animate on that object
                int bad = 0;
                var animatable = new HashSet<string>();
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
                    foreach (EditorCurveBinding b in AnimationUtility.GetAnimatableBindings(t.gameObject, copy))
                        animatable.Add(b.path + "|" + b.type.FullName + "|" + b.propertyName);
                foreach (AnimationClip clip in new[] { hidden, shown, stopClip, goClip })
                    foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                        if (b.path != "_" && !animatable.Contains(b.path + "|" + b.type.FullName + "|" + b.propertyName))
                        {
                            bad++;
                            log.AppendLine("      not animatable: " + b.path + " " + b.type.Name + "." + b.propertyName);
                        }
                Check(bad == 0, "every property the lock and the freeze write exists on the avatar (" + bad + " that do not)");

                // AFTER THE BUILD: the SDK's postprocess callbacks, in their order
                new KSAntiRipAssetNamesRestore().OnPostprocessAvatar();
                new KSAntiRipBuildCleanup().OnPostprocessAvatar();
                Check(BuildFolders() == 0, "after the build: the cleanup leaves no build files in the project");
            }
            catch (Exception e)
            {
                log.AppendLine("FAIL  threw: " + e);
                failures++;
            }
            finally
            {
                KSAntiRipBuildHook.PlanSourceForTests = null;
                KSAntiRipBuildHook.ReportForTests = null;
                foreach (GameObject g in made) if (g != null) UnityEngine.Object.DestroyImmediate(g);
                KSAntiRipBuildHook.CleanBuildFolders();
                if (AssetDatabase.IsValidFolder(Work)) AssetDatabase.DeleteAsset(Work);
                AssetDatabase.Refresh();
            }

            ok = failures == 0;
            log.AppendLine(ok ? "KS ANTI-RIP HOOK TEST: ALL PASSED" : $"KS ANTI-RIP HOOK TEST: {failures} FAILED");
            return log.ToString();
        }

        /// <summary>The value a clip sets a property to at time 0, or -1 when it does not drive it.</summary>
        static float Value(AnimationClip clip, string path, Type type, string property)
        {
            if (clip == null) return -1f;
            foreach (EditorCurveBinding b in AnimationUtility.GetCurveBindings(clip))
                if (b.path == path && b.type == type && b.propertyName == property)
                    return AnimationUtility.GetEditorCurve(clip, b).Evaluate(0f);
            return -1f;
        }

        static int BuildFolders()
        {
            string root = KawaiiStudioPaths.Workspace + "/Protected";
            return AssetDatabase.IsValidFolder(root)
                ? AssetDatabase.GetSubFolders(root).Count(s => s.Substring(s.LastIndexOf('/') + 1).StartsWith("_build_", StringComparison.Ordinal))
                : 0;
        }

        /// <summary>Everything about an avatar the hook could change, as text: two equal snapshots are the same avatar.</summary>
        static string Snapshot(GameObject root)
        {
            var sb = new StringBuilder();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                sb.Append(AnimationUtility.CalculateTransformPath(t, root.transform)).Append(" active=").Append(t.gameObject.activeSelf)
                  .Append(" pos=").Append(t.localPosition).Append(" scale=").Append(t.localScale).Append('\n');
                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    sb.Append("  ").Append(c.GetType().FullName);
                    if (c is Behaviour be) sb.Append(" enabled=").Append(be.enabled);
                    if (c is Renderer r)
                        sb.Append(" enabled=").Append(r.enabled).Append(" mats=").Append(string.Join(",", r.sharedMaterials.Select(m => m != null ? m.GetInstanceID().ToString() : "-")));
                    if (c is SkinnedMeshRenderer s && s.sharedMesh != null) sb.Append(" mesh=").Append(s.sharedMesh.GetInstanceID()).Append(" shapes=").Append(s.sharedMesh.blendShapeCount);
                    if (c is MeshFilter f && f.sharedMesh != null) sb.Append(" mesh=").Append(f.sharedMesh.GetInstanceID());
                    if (c is VRCAvatarDescriptor d)
                    {
                        foreach (var l in d.baseAnimationLayers) sb.Append(" ").Append(l.type).Append('=').Append(l.animatorController != null ? l.animatorController.GetInstanceID() : 0);
                        sb.Append(" params=").Append(d.expressionParameters != null ? d.expressionParameters.GetInstanceID() : 0);
                    }
                    sb.Append('\n');
                }
            }
            return sb.ToString();
        }

        /// <summary>A test avatar with what a real one carries.</summary>
        static GameObject Avatar(string tag, List<GameObject> made)
        {
            var root = new GameObject("KS Hook Test " + tag);
            made.Add(root);
            var descriptor = root.AddComponent<VRCAvatarDescriptor>();
            var pipeline = root.GetComponent<PipelineManager>() ?? root.AddComponent<PipelineManager>();
            pipeline.blueprintId = "avtr_00000000-0000-0000-0000-0000hooktest";
            descriptor.ViewPosition = new Vector3(0f, 1.5f, 0.1f);

            // the body: a skinned mesh with a shape of its own
            var bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(root.transform, false);
            var body = bodyObject.AddComponent<SkinnedMeshRenderer>();
            const int n = 300;
            var rng = new System.Random(5);
            var verts = new Vector3[n];
            var delta = new Vector3[n];
            var tris = new int[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
                delta[i] = new Vector3(0f, 0.02f, 0f);
                tris[i] = i;
            }
            var mesh = new Mesh { name = "KS_Hook_Body_" + tag, vertices = verts, triangles = tris };
            mesh.AddBlendShapeFrame("Viseme_AA", 100f, delta, null, null);
            body.sharedMesh = mesh;

            // a static prop (6 corners), and a jacket (5 corners) the avatar's own FX toggles
            MeshRenderer Static(string name, int corners)
            {
                var o = new GameObject(name);
                o.transform.SetParent(root.transform, false);
                var m = new Mesh { name = "KS_Hook_" + name + "_" + tag };
                var v = new Vector3[corners];
                for (int i = 0; i < corners; i++) v[i] = new Vector3(i * 0.1f, i % 2 * 0.1f, 0f);
                m.vertices = v;
                m.triangles = new[] { 0, 1, 2 };
                o.AddComponent<MeshFilter>().sharedMesh = m;
                return o.AddComponent<MeshRenderer>();
            }
            Static("Scythe", 6);
            Static("Jacket", 5);

            var sparks = new GameObject("Sparks");
            sparks.transform.SetParent(root.transform, false);
            sparks.AddComponent<ParticleSystem>();
            var glow = new GameObject("Glow");
            glow.transform.SetParent(root.transform, false);
            glow.AddComponent<Light>();
            var hum = new GameObject("Hum");
            hum.transform.SetParent(root.transform, false);
            hum.AddComponent<AudioSource>();
            var wings = new GameObject("Wings");
            wings.transform.SetParent(root.transform, false);
            wings.AddComponent<Animator>();

            // its FX: layer 0 and a toggle that switches the jacket's renderer
            var fx = AnimatorController.CreateAnimatorControllerAtPath(Work + "/fx_" + tag + ".controller");
            fx.AddParameter("Jacket", AnimatorControllerParameterType.Bool);
            fx.AddLayer("Jacket");
            AnimatorControllerLayer[] layers = fx.layers;
            layers[1].defaultWeight = 1f;
            fx.layers = layers;
            var off = new AnimationClip { name = "JacketOff" };
            off.SetCurve("Jacket", typeof(MeshRenderer), "m_Enabled", AnimationCurve.Constant(0f, 1f / 60f, 0f));
            var on = new AnimationClip { name = "JacketOn" };
            on.SetCurve("Jacket", typeof(MeshRenderer), "m_Enabled", AnimationCurve.Constant(0f, 1f / 60f, 1f));
            AssetDatabase.CreateAsset(off, Work + "/JacketOff_" + tag + ".anim");
            AssetDatabase.CreateAsset(on, Work + "/JacketOn_" + tag + ".anim");
            AnimatorStateMachine sm = fx.layers[1].stateMachine;
            AnimatorState sOff = sm.AddState("Off");
            sOff.motion = off;
            AnimatorState sOn = sm.AddState("On");
            sOn.motion = on;
            sOff.AddTransition(sOn).AddCondition(AnimatorConditionMode.If, 0f, "Jacket");
            sOn.AddTransition(sOff).AddCondition(AnimatorConditionMode.IfNot, 0f, "Jacket");

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Additive, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Action, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = fx },
            };
            return root;
        }
    }
}
