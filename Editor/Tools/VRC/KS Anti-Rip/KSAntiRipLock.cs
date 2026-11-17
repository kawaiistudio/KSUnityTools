using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// The keyed lock: restore shape k of every protected mesh follows key parameter k, and nothing else.
    ///
    /// WHAT CHANGED IN 5.1, AND WHY. Up to 5.0 this was a bool combination lock in front of an "open" clip that
    /// set every restore shape to its exact weight. Both halves shipped in the bundle: the weights were written
    /// in that clip, and the combination was spelled out in the lock's own transition conditions. Anybody who
    /// opened the bundle and read the FX controller had the answer without searching for anything.
    ///
    /// Now the animator holds no answer at all. One Direct BlendTree: child k is a clip that puts shape k at 100,
    /// weighted by float parameter k. So the shape's weight IS the parameter times 100 -- and the parameter's
    /// value comes from the `.key`, which KS OSC KEY sends into VRChat. Every curve in the bundle reads 0 or 100.
    /// Without the key, the parameters sit at 0 and the mesh stays scrambled; finding the values from the bundle
    /// alone is a search over every shape at once (the dense decomposition, AntiRipEngine.PlanMesh).
    ///
    /// SYNCED OR LOCAL. Synced (the default): VRChat replicates the values from the wearer, so everyone sees the
    /// avatar whole -- and a modified client in the same instance can read them, which no client-side scheme
    /// avoids. Local: the values never leave the wearer's PC, so nobody can read them, and nobody else sees the
    /// avatar whole either. That is VRChat's model; the choice is the creator's.
    ///
    /// The values are on VRChat's 8-bit float grid (k/127, AntiRipEngine.SyncSteps), so what remote players
    /// receive is exactly what was sent.
    /// </summary>
    public static class KSAntiRipLock
    {
        /// <summary>Readable layer names when obfuscation is off. With it on, every name is random.</summary>
        public const string LayerName = "KS ANTI-RIP";
        public const string ShowLayerName = "KS ANTI-RIP Show";

        public class ShapeTarget
        {
            public string path;        // hierarchy path of the SkinnedMeshRenderer, relative to the avatar root
            public string shapeName;
            public int slot;           // which key parameter drives it
        }

        /// <summary>
        /// Adds the restore layer (and the show layer when renderers are hidden while locked) to the avatar's
        /// FX controller, and the key parameters to its expression parameters.
        /// </summary>
        public static void Build(
            GameObject avatar, VRCAvatarDescriptor descriptor, AnimatorController fx,
            IList<ShapeTarget> targets, IList<string> hideRendererPaths, IList<string> keyParameters,
            bool syncKey, string folder, System.Random rng, bool obfuscate = false)
        {
            if (keyParameters == null || keyParameters.Count == 0)
                throw new System.ArgumentException("the lock needs the key parameters", nameof(keyParameters));
            if (targets.Any(t => t.slot < 0 || t.slot >= keyParameters.Count))
                throw new System.ArgumentException("a restore shape points at a key parameter that does not exist");

            // With obfuscation on, nothing announces itself. 5.0 named things "KS_<tag>_L1234", which was a
            // search term in its own right.
            string Name(string readable) => obfuscate ? Neutral(rng, 9) : readable;

            // 1. the key parameters, as floats in the controller
            foreach (string p in keyParameters)
                if (!fx.parameters.Any(x => x.name == p))
                    fx.AddParameter(p, AnimatorControllerParameterType.Float);

            // 2. the restore layer: one Direct BlendTree, one child per key slot
            var tree = new BlendTree
            {
                name = Name("KS Restore"),
                blendType = BlendTreeType.Direct,
                hideFlags = HideFlags.HideInHierarchy,
            };
            AssetDatabase.AddObjectToAsset(tree, fx);

            var children = new ChildMotion[keyParameters.Count];
            for (int k = 0; k < keyParameters.Count; k++)
            {
                var clip = new AnimationClip { name = Name("KS_Restore_" + k) };
                foreach (ShapeTarget t in targets.Where(t => t.slot == k))
                    clip.SetCurve(t.path, typeof(SkinnedMeshRenderer), "blendShape." + t.shapeName, Const(100f));
                AssetDatabase.CreateAsset(clip, folder + "/" + clip.name + ".anim");
                children[k] = new ChildMotion
                {
                    motion = clip,
                    directBlendParameter = keyParameters[k],
                    timeScale = 1f,
                };
            }
            tree.children = children;

            var restoreMachine = new AnimatorStateMachine { name = Name(LayerName), hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(restoreMachine, fx);
            var restoreState = restoreMachine.AddState(Name("Restore"));
            restoreState.motion = tree;
            // A Direct BlendTree needs Write Defaults ON: with it off, a child whose weight drops back to 0 leaves
            // its last value behind. Safe here even in a WD-off controller, because this layer animates only the
            // restore shapes, which nothing else touches.
            restoreState.writeDefaultValues = true;
            restoreMachine.defaultState = restoreState;

            fx.AddLayer(new AnimatorControllerLayer
            {
                name = restoreMachine.name,
                stateMachine = restoreMachine,
                defaultWeight = 1f,
            });

            // 3. the show layer: renderers hidden until the key has arrived, so nobody is shown an exploded mesh.
            //    "Arrived" is "key parameter 0 is not zero" -- deliberately NOT "the key is right", because testing
            //    for the right value would write the value into the bundle again.
            if (hideRendererPaths != null && hideRendererPaths.Count > 0)
            {
                bool writeDefaults = DominantWriteDefaults(fx);
                var hidden = new AnimationClip { name = Name("KS_Hidden") };
                var shown = new AnimationClip { name = Name("KS_Shown") };
                // Driven in BOTH states: with Write Defaults off, a property animated in one state and dropped in
                // the other keeps its last value, which would leave the avatar invisible forever.
                foreach (string path in hideRendererPaths)
                {
                    hidden.SetCurve(path, typeof(SkinnedMeshRenderer), "m_Enabled", Const(0f));
                    shown.SetCurve(path, typeof(SkinnedMeshRenderer), "m_Enabled", Const(1f));
                }
                AssetDatabase.CreateAsset(hidden, folder + "/" + hidden.name + ".anim");
                AssetDatabase.CreateAsset(shown, folder + "/" + shown.name + ".anim");

                var showMachine = new AnimatorStateMachine { name = Name(ShowLayerName), hideFlags = HideFlags.HideInHierarchy };
                AssetDatabase.AddObjectToAsset(showMachine, fx);
                var hiddenState = showMachine.AddState(Name("Hidden"));
                hiddenState.motion = hidden;
                hiddenState.writeDefaultValues = writeDefaults;
                var shownState = showMachine.AddState(Name("Shown"));
                shownState.motion = shown;
                shownState.writeDefaultValues = writeDefaults;
                showMachine.defaultState = hiddenState;

                var toShown = hiddenState.AddTransition(shownState);
                toShown.hasExitTime = false;
                toShown.duration = 0f;
                toShown.AddCondition(AnimatorConditionMode.Greater, 0.001f, keyParameters[0]);

                var toHidden = shownState.AddTransition(hiddenState);
                toHidden.hasExitTime = false;
                toHidden.duration = 0f;
                toHidden.AddCondition(AnimatorConditionMode.Less, 0.001f, keyParameters[0]);

                fx.AddLayer(new AnimatorControllerLayer
                {
                    name = showMachine.name,
                    stateMachine = showMachine,
                    defaultWeight = 1f,
                });
            }

            // 4. the expression parameters
            AddExpressionParameters(descriptor, keyParameters, syncKey, folder);
        }

        static void AddExpressionParameters(VRCAvatarDescriptor descriptor, IList<string> keyParameters,
                                            bool syncKey, string folder)
        {
            if (descriptor == null) return;

            VRCExpressionParameters source = descriptor.expressionParameters;
            VRCExpressionParameters copy = source != null
                ? Object.Instantiate(source)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            if (copy.parameters == null) copy.parameters = new VRCExpressionParameters.Parameter[0];

            var list = new List<VRCExpressionParameters.Parameter>(copy.parameters);
            foreach (string name in keyParameters)
            {
                if (list.Any(p => p != null && p.name == name)) continue;
                list.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,     // locked until the key arrives
                    saved = false,         // a fresh session starts locked
                    networkSynced = syncKey,
                });
            }
            copy.parameters = list.ToArray();

            AssetDatabase.CreateAsset(copy, folder + "/KS_Parameters.asset");
            descriptor.expressionParameters = copy;

            // VRChat's own avatar validator refuses an upload where one of the expressions pair is set and the
            // other is not (VRCSdkControlPanelAvatarBuilder: "VRCExpressionsMenu object reference is missing").
            // We have just given the avatar parameters, so an avatar that arrived with neither would leave here
            // unuploadable because of us. An empty menu costs nothing and shows nothing in game.
            if (descriptor.expressionsMenu == null)
            {
                var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                AssetDatabase.CreateAsset(menu, folder + "/KS_Menu.asset");
                descriptor.expressionsMenu = menu;
            }
        }

        /// <summary>Majority Write Defaults across the controller's existing states; true when it has none.</summary>
        public static bool DominantWriteDefaults(AnimatorController fx)
        {
            int on = 0, off = 0;
            foreach (var layer in fx.layers)
                CountWriteDefaults(layer.stateMachine, ref on, ref off);
            if (on == 0 && off == 0) return true;
            return on >= off;
        }

        static void CountWriteDefaults(AnimatorStateMachine sm, ref int on, ref int off)
        {
            if (sm == null) return;
            foreach (var s in sm.states)
            {
                if (s.state == null) continue;
                if (s.state.writeDefaultValues) on++; else off++;
            }
            foreach (var sub in sm.stateMachines) CountWriteDefaults(sub.stateMachine, ref on, ref off);
        }

        static string Neutral(System.Random rng, int length)
        {
            const string Letters = "abcdefghijklmnopqrstuvwxyz";
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = Letters[rng.Next(Letters.Length)];
            return new string(chars);
        }

        static AnimationCurve Const(float v)
        {
            // Two keys: a one-key curve is legal but some importers drop it.
            return AnimationCurve.Linear(0f, v, 1f / 60f, v);
        }
    }
}
