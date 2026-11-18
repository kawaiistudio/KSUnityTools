using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// A REAL copy of an animator controller: the controller and every object its graph is built from -- state
    /// machines, states, transitions, blend trees and state-machine behaviours -- copied into ONE new .controller
    /// asset, with every reference between them repointed at the copies.
    ///
    /// Why not AssetDatabase.CopyAsset. CopyAsset copies a FILE, and at BUILD time a controller is not one file.
    /// After Modular Avatar / NDMF merge the avatar, the controllers come from NDMF's AssetSaver, which packs every
    /// generated object 256 at a time into shared "assets N.asset" containers (nadena.dev.ndmf AssetSaver). So one
    /// controller's state machines, states and transitions are scattered across several of those files, and several
    /// controllers share each file. CopyAsset copies a whole 256-object container; LoadAssetAtPath&lt;AnimatorController&gt;
    /// then hands back whichever controller happens to be first in the copied container -- often the WRONG one -- and
    /// any state machine or state that lived in a neighbouring container is still the ORIGINAL object, so the graph is
    /// half copied. That is exactly what the owner's 2026-11-17 upload of LAYLA hit:
    ///   * the Gesture slot ended up on a controller whose layer 0 has no avatar mask, and VRChat validation stops
    ///     with "Gesture Layer needs valid mask on first animator layer" -- the one blocking error;
    ///   * the console filled with "transitions to StateMachine '...' which does not exist in the same StateMachine",
    ///     the signature of a graph whose transitions point half at copies and half at originals;
    ///   * the name obfuscator, walking that half-copied graph, wrote into NDMF's own generated assets in place.
    ///
    /// Copying the graph object by object makes all of that impossible whatever the files look like. Nothing the copy
    /// can reach is shared with the source except things that are only ever READ: animation clips (which the caller
    /// remaps to its own output copies through <paramref name="mapClip"/>), avatar masks, and the MonoScripts behind
    /// behaviours. Keeping the avatar mask BY REFERENCE is what preserves layer 0's mask and clears the Gesture error.
    ///
    /// The method is deliberately generic -- it discovers and repoints references through SerializedObject rather than
    /// naming fields -- so it carries everything Unity serialises without a field-by-field list that could miss one:
    /// per-layer avatarMask / blendingMode / defaultWeight / iKPass / syncedLayerIndex / syncedLayerAffectsTiming and
    /// a synced layer's per-state override motions and behaviours; controller parameters; a state machine's
    /// defaultState, anyStatePosition, entry/exit/any-state transitions and -- the "does not exist in the same
    /// StateMachine" case -- its m_StateMachineTransitions map; transition conditions; and blend-tree children and
    /// parameters. Written for Unity 2019.4 through Unity 6, so C# 7.3 and public animation APIs only.
    /// </summary>
    public static class KSAntiRipAnimatorCopy
    {
        /// <param name="source">the controller to copy; it is only ever read, never written.</param>
        /// <param name="path">a ".controller" asset path to create the copy at.</param>
        /// <param name="mapClip">
        /// Given each AnimationClip the graph references, returns the motion the copy should use in its place -- the
        /// caller's own remapped copy. Return the clip unchanged (or null for no mapping) to keep it by reference;
        /// the caller may then remap clips itself in a later pass. Masks and every other read-only asset are always
        /// kept by reference and never passed here.
        /// </param>
        /// <returns>the new controller, or null if it could not be created.</returns>
        public static AnimatorController Copy(AnimatorController source, string path, Func<AnimationClip, Motion> mapClip = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));

            // 1. Every object the graph is built from, reached through the same serialized references Unity follows.
            //    A sub-state machine, a synced layer's override motions, an AnyState transition or a blend tree that
            //    lives in another file is found exactly like one in this file -- which is the whole point.
            var owned = new List<Object>();
            var seen = new HashSet<Object>();
            var pending = new Stack<Object>();
            pending.Push(source);
            seen.Add(source);
            while (pending.Count > 0)
            {
                Object o = pending.Pop();
                owned.Add(o);
                foreach (Object r in References(o))
                {
                    if (!IsOwned(r)) continue;          // clips, masks, scripts: kept by reference, never recursed
                    if (seen.Add(r)) pending.Push(r);
                }
            }

            // 2. A fresh, empty object for each -- NOT Object.Instantiate(controller), which deep-copies the whole
            //    graph into hidden embedded objects we could neither track nor repoint. CopySerialized carries every
            //    field over, with object references left pointing at the source for step 4 to repoint.
            var map = new Dictionary<Object, Object>();
            foreach (Object o in owned)
            {
                Object copy = NewLike(o);
                if (copy == null)
                    throw new InvalidOperationException("KS Anti-Rip cannot copy an animator object of type " + o.GetType());
                EditorUtility.CopySerialized(o, copy);
                map[o] = copy;
            }

            // 3. Write the controller, then fold every other copy into it as a sub-asset, so the whole graph is one
            //    self-contained file -- not scattered across containers the way the source was.
            var controllerCopy = (AnimatorController)map[source];
            AssetDatabase.CreateAsset(controllerCopy, path);
            foreach (Object o in owned)
            {
                if (o == source) continue;
                Object copy = map[o];
                copy.hideFlags = HideFlags.HideInHierarchy;   // the SDK and the inspector hide sub-assets this way
                AssetDatabase.AddObjectToAsset(copy, controllerCopy);
            }

            // 4. Repoint every reference between the copies, and hand each clip to the caller. Done through
            //    SerializedObject so the map's keys/values (m_StateMachineTransitions), synced overrides and every
            //    other PPtr are reached uniformly, with no field left out.
            foreach (Object o in owned)
                Repoint(map[o], map, mapClip);

            // 5. Every state machine's default state, set again now that the WHOLE graph points at the copies. A default
            //    state may live in a NESTED sub-state machine (GoGo Loco's Base and Action layers do this). Unity
            //    validates m_DefaultState when a state machine is written in step 4, and at that moment the nested
            //    machines below it could still list the source's states, so Unity dropped the default and took the first
            //    state it found instead. Measured 2026-11-17 on a real avatar: two GoGo Loco sub-state machines entered
            //    at the wrong state once protected.
            foreach (Object o in owned)
            {
                var sourceMachine = o as AnimatorStateMachine;
                if (sourceMachine == null) continue;
                var copyMachine = (AnimatorStateMachine)map[o];
                AnimatorState wanted = sourceMachine.defaultState;
                Object mapped;
                if (wanted != null && map.TryGetValue(wanted, out mapped) && copyMachine.defaultState != mapped)
                    copyMachine.defaultState = (AnimatorState)mapped;
            }

            EditorUtility.SetDirty(controllerCopy);
            return controllerCopy;
        }

        /// <summary>True for the objects a controller OWNS and that must be copied; false for everything kept by reference.</summary>
        static bool IsOwned(Object o)
        {
            return o is AnimatorStateMachine
                || o is AnimatorState
                || o is AnimatorTransitionBase          // AnimatorStateTransition and AnimatorTransition both
                || o is BlendTree
                || o is StateMachineBehaviour;
            // Deliberately NOT: AnimationClip (handled by mapClip), AvatarMask and any other read-only asset
            // (kept by reference), and AnimatorController (only ever the root, already in the set).
        }

        /// <summary>A new, empty object of the same concrete type, ready for CopySerialized.</summary>
        static Object NewLike(Object o)
        {
            // Explicit construction, not Activator/Instantiate: these public constructors exist on every supported
            // Unity version (NDMF builds state machines the same way) and never deep-copy a sub-graph behind our back.
            if (o is AnimatorController) return new AnimatorController();
            if (o is AnimatorStateMachine) return new AnimatorStateMachine();
            if (o is AnimatorState) return new AnimatorState();
            if (o is BlendTree) return new BlendTree();
            if (o is AnimatorStateTransition) return new AnimatorStateTransition();
            if (o is AnimatorTransition) return new AnimatorTransition();
            if (o is StateMachineBehaviour) return ScriptableObject.CreateInstance(o.GetType());
            return null;
        }

        /// <summary>Every non-null object a given object references, discovered through its serialized properties.</summary>
        static IEnumerable<Object> References(Object o)
        {
            var so = new SerializedObject(o);
            SerializedProperty p = so.GetIterator();
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object r = p.objectReferenceValue;
                if (r != null) yield return r;
            }
        }

        /// <summary>
        /// Rewrites one copy's references: a reference to any owned object becomes its copy; a clip is handed to
        /// <paramref name="mapClip"/>; anything else (mask, script) is left pointing where it was.
        /// </summary>
        static void Repoint(Object copy, Dictionary<Object, Object> map, Func<AnimationClip, Motion> mapClip)
        {
            var so = new SerializedObject(copy);
            SerializedProperty p = so.GetIterator();
            bool changed = false;
            while (p.Next(true))
            {
                if (p.propertyType != SerializedPropertyType.ObjectReference) continue;
                Object cur = p.objectReferenceValue;
                if (cur == null) continue;

                if (map.TryGetValue(cur, out Object replacement))
                {
                    p.objectReferenceValue = replacement;
                    changed = true;
                }
                else if (mapClip != null && cur is AnimationClip clip)
                {
                    Motion mapped = mapClip(clip);
                    if (mapped != (Object)clip)
                    {
                        p.objectReferenceValue = mapped;
                        changed = true;
                    }
                }
            }
            if (changed) so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
