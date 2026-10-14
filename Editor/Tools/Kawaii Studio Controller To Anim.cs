// Controller To Anim -- bake a chain of a controller layer's states into one safe clip.
// Ported into the package in 3.4.0 from the owner's project tools (was project-only until then);
// output goes to the Kawaii Studio workspace.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    public class KawaiiStudioControllerToAnimWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;
        // the workspace's Animations folder (Assets/Kawaii Studio/Animations/Controller To Anim)
        private static string DEFAULT_OUTPUT_FOLDER => KawaiiStudioPaths.Animations + "/Controller To Anim";

        private AnimatorController sourceController;
        private int selectedLayerIndex;
        private int selectedStateIndex;
        private int sampleRate = 60;
        private bool includeObjectReferenceCurves;
        private bool resetMissingCurvesBetweenSteps = true;
        private Vector2 scrollPosition;

        private string outputAssetPath = DEFAULT_OUTPUT_FOLDER + "/Controller_Baked.anim";
        private string analysisMessage = "Drop a controller, choose a layer, then analyze it.";
        private MessageType analysisMessageType = MessageType.Info;
        private string generationMessage = string.Empty;
        private MessageType generationMessageType = MessageType.Info;

        private readonly List<LayerStateInfo> availableStates = new List<LayerStateInfo>();
        private readonly List<SequenceStep> sequenceSteps = new List<SequenceStep>();
        private string[] layerNames = Array.Empty<string>();
        private string[] stateNames = Array.Empty<string>();

        [MenuItem("Kawaii Studio/Universal Tools/Controller To Anim")]
        public static void ShowWindow()
        {
            var window = GetWindow<KawaiiStudioControllerToAnimWindow>("Controller To Anim");
            window.minSize = new Vector2(760, 760);
            window.Show();
        }

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
            RefreshControllerData();
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.Initialize();
            KawaiiStudioGUI.DrawWindowBackground(position);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(12, 12, 12, 12) });

            KawaiiStudioGUI.DrawBanner(
                "CONTROLLER TO ANIM",
                "Bake safe clip chains from a controller layer",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner
            );

            GUILayout.Space(10);

            DrawTutorialSection();
            DrawSourceSection();
            DrawAnalysisSection();
            DrawSequenceSection();
            DrawOutputSection();
            DrawGenerateSection();

            GUILayout.Space(16);
            KawaiiStudioGUI.DrawFooter();

            EditorGUILayout.EndVertical();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTutorialSection()
        {
            KawaiiStudioGUI.DrawSection("HOW THIS TOOL WORKS", () =>
            {
                EditorGUILayout.LabelField("1. Drop an Animator Controller.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("2. Pick one layer to inspect.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("3. Auto-fill or manually build the clip order.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("4. Review clip order, controller speed and bake duration.", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("5. Generate one .anim built from that sequence.", KawaiiStudioGUI.InfoLabelStyle);

                GUILayout.Space(8);

                EditorGUILayout.HelpBox(
                    "SAFE BY DESIGN: this tool is for dance / motion controllers made of AnimationClips. " +
                    "It does NOT rebuild full controller logic, FX systems, Any State logic, Blend Trees, parameter-driven speed, or avatar layer merges.",
                    MessageType.Warning);

                EditorGUILayout.HelpBox(
                    "Do NOT use this as a generic VRChat FX-to-clip converter. If people feed avatar FX layers into it, results can be wrong. " +
                    "This tool is meant for simple clip chains like dance controllers.",
                    MessageType.Error);
            });
        }

        private void DrawSourceSection()
        {
            KawaiiStudioGUI.DrawSection("SOURCE CONTROLLER", () =>
            {
                EditorGUI.BeginChangeCheck();
                sourceController = (AnimatorController)EditorGUILayout.ObjectField(
                    "Animator Controller",
                    sourceController,
                    typeof(AnimatorController),
                    false);
                if (EditorGUI.EndChangeCheck())
                {
                    selectedLayerIndex = 0;
                    selectedStateIndex = 0;
                    RefreshControllerData();
                }

                using (new EditorGUI.DisabledScope(sourceController == null || layerNames.Length == 0))
                {
                    EditorGUI.BeginChangeCheck();
                    selectedLayerIndex = EditorGUILayout.Popup("Layer", selectedLayerIndex, layerNames);
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedStateIndex = 0;
                        RefreshControllerData();
                    }
                }

                sampleRate = EditorGUILayout.IntSlider("Bake Sample Rate", sampleRate, 15, 120);
                includeObjectReferenceCurves = EditorGUILayout.Toggle("Include Object Reference Curves", includeObjectReferenceCurves);
                resetMissingCurvesBetweenSteps = EditorGUILayout.Toggle("Reset Missing Curves Between Steps", resetMissingCurvesBetweenSteps);

                EditorGUILayout.HelpBox(
                    "Object reference curves are OFF by default because they are common in UI / FX workflows. Leave this off for dance controllers unless you know exactly what you are baking.",
                    MessageType.Info);

                EditorGUILayout.HelpBox(
                    "Reset Missing Curves Between Steps is ON by default. This makes the baked clip behave more like one clip at a time instead of stacking leftover pose data from the previous step.",
                    MessageType.Info);

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                    if (GUILayout.Button("REFRESH ANALYSIS", KawaiiStudioGUI.ButtonStyle))
                    {
                        RefreshControllerData();
                    }
                    GUI.backgroundColor = Color.white;

                    using (new EditorGUI.DisabledScope(!CanAnalyzeCurrentLayer()))
                    {
                        GUI.backgroundColor = KawaiiStudioGUI.SuccessColor;
                        if (GUILayout.Button("AUTO-FILL FROM DEFAULT PATH", KawaiiStudioGUI.ButtonStyle))
                        {
                            AutoFillSequenceFromDefaultPath();
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }
            });
        }

        private void DrawAnalysisSection()
        {
            KawaiiStudioGUI.DrawSection("LAYER ANALYSIS", () =>
            {
                EditorGUILayout.HelpBox(analysisMessage, analysisMessageType);

                if (availableStates.Count == 0)
                {
                    EditorGUILayout.LabelField("No states loaded for the selected layer.", KawaiiStudioGUI.InfoLabelStyle);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(stateNames.Length == 0))
                    {
                        selectedStateIndex = EditorGUILayout.Popup("Manual Add", selectedStateIndex, stateNames);
                    }

                    using (new EditorGUI.DisabledScope(stateNames.Length == 0 || !availableStates[Mathf.Clamp(selectedStateIndex, 0, availableStates.Count - 1)].IsSupported))
                    {
                        if (GUILayout.Button("ADD STATE", GUILayout.Width(120)))
                        {
                            AddStateToSequence(availableStates[selectedStateIndex]);
                        }
                    }
                }

                GUILayout.Space(6);

                foreach (var stateInfo in availableStates)
                {
                    EditorGUILayout.BeginVertical(KawaiiStudioGUI.BoxStyle);
                    EditorGUILayout.LabelField(stateInfo.StateName, KawaiiStudioGUI.LabelStyle);
                    EditorGUILayout.ObjectField("Motion", stateInfo.Clip, typeof(AnimationClip), false);
                    EditorGUILayout.LabelField("Controller Speed", stateInfo.ControllerSpeed.ToString("0.###"), KawaiiStudioGUI.InfoLabelStyle);
                    EditorGUILayout.LabelField("Suggested Duration", stateInfo.SuggestedBakeDuration.ToString("0.###") + " sec", KawaiiStudioGUI.InfoLabelStyle);
                    EditorGUILayout.LabelField("Transitions", stateInfo.TransitionSummary, KawaiiStudioGUI.InfoLabelStyle);

                    if (stateInfo.IsSupported)
                    {
                        if (!string.IsNullOrEmpty(stateInfo.Note))
                        {
                            EditorGUILayout.HelpBox(stateInfo.Note, MessageType.Info);
                        }
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(stateInfo.UnsupportedReason, MessageType.Warning);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(4);
                }
            });
        }

        private void DrawSequenceSection()
        {
            KawaiiStudioGUI.DrawSection("SEQUENCE TO BAKE", () =>
            {
                if (sequenceSteps.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No bake sequence yet. Use AUTO-FILL FROM DEFAULT PATH or ADD STATE to build the order manually.",
                        MessageType.Info);
                }

                for (int index = 0; index < sequenceSteps.Count; index++)
                {
                    var step = sequenceSteps[index];

                    EditorGUILayout.BeginVertical(KawaiiStudioGUI.BoxStyle);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField((index + 1) + ". " + step.StateName, KawaiiStudioGUI.LabelStyle);

                        using (new EditorGUI.DisabledScope(index == 0))
                        {
                            if (GUILayout.Button("UP", GUILayout.Width(45)))
                            {
                                MoveStep(index, -1);
                            }
                        }

                        using (new EditorGUI.DisabledScope(index == sequenceSteps.Count - 1))
                        {
                            if (GUILayout.Button("DOWN", GUILayout.Width(55)))
                            {
                                MoveStep(index, 1);
                            }
                        }

                        if (GUILayout.Button("REMOVE", GUILayout.Width(70)))
                        {
                            sequenceSteps.RemoveAt(index);
                            GUIUtility.ExitGUI();
                        }
                    }

                    EditorGUILayout.ObjectField("Clip", step.Clip, typeof(AnimationClip), false);
                    EditorGUILayout.LabelField("Controller Speed", step.ControllerSpeed.ToString("0.###"), KawaiiStudioGUI.InfoLabelStyle);
                    EditorGUILayout.LabelField("Clip Length", step.Clip.length.ToString("0.###") + " sec", KawaiiStudioGUI.InfoLabelStyle);

                    step.BakeDuration = Mathf.Max(0.001f, EditorGUILayout.FloatField("Bake Duration", step.BakeDuration));

                    if (!string.IsNullOrEmpty(step.Note))
                    {
                        EditorGUILayout.HelpBox(step.Note, MessageType.Info);
                    }

                    EditorGUILayout.EndVertical();
                    GUILayout.Space(4);
                }

                if (sequenceSteps.Count > 0)
                {
                    GUILayout.Space(4);
                    EditorGUILayout.LabelField("Estimated Result Length", GetTotalBakeDuration().ToString("0.###") + " sec", KawaiiStudioGUI.LabelStyle);

                    if (GUILayout.Button("CLEAR SEQUENCE", GUILayout.Height(28)))
                    {
                        sequenceSteps.Clear();
                    }
                }
            });
        }

        private void DrawOutputSection()
        {
            KawaiiStudioGUI.DrawSection("OUTPUT", () =>
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    outputAssetPath = EditorGUILayout.TextField("Output .anim", outputAssetPath);

                    if (GUILayout.Button("BROWSE", GUILayout.Width(90)))
                    {
                        PickOutputAssetPath();
                    }
                }

                EditorGUILayout.HelpBox(
                    "The generated clip is a new asset. This tool does not modify the controller, the avatar, or the original clips.",
                    MessageType.Info);

                if (!string.IsNullOrEmpty(generationMessage))
                {
                    EditorGUILayout.HelpBox(generationMessage, generationMessageType);
                }
            });
        }

        private void DrawGenerateSection()
        {
            KawaiiStudioGUI.DrawSection("GENERATE", () =>
            {
                using (new EditorGUI.DisabledScope(!CanGenerateClip()))
                {
                    GUI.backgroundColor = KawaiiStudioGUI.SuccessColor;
                    if (GUILayout.Button("CREATE BAKED .ANIM", KawaiiStudioGUI.ButtonStyle))
                    {
                        GenerateAnimationClip();
                    }
                    GUI.backgroundColor = Color.white;
                }

                EditorGUILayout.HelpBox(
                    "Best results: use simple dance controllers, one chosen layer, direct AnimationClip motions, and a reviewed sequence. " +
                    "If the controller is branchy or FX-heavy, bake it manually or with a recorder workflow instead.",
                    MessageType.Warning);
            });
        }

        private bool CanAnalyzeCurrentLayer()
        {
            return sourceController != null && layerNames.Length > 0 && selectedLayerIndex >= 0 && selectedLayerIndex < layerNames.Length;
        }

        private bool CanGenerateClip()
        {
            return sequenceSteps.Count > 0 && sequenceSteps.All(step => step.Clip != null && step.BakeDuration > 0f && step.ControllerSpeed > 0f) &&
                   IsAssetPath(outputAssetPath);
        }

        private void RefreshControllerData()
        {
            availableStates.Clear();
            sequenceSteps.RemoveAll(step => step == null || step.Clip == null);
            layerNames = Array.Empty<string>();
            stateNames = Array.Empty<string>();

            if (sourceController == null)
            {
                analysisMessage = "Drop a controller to inspect its layers.";
                analysisMessageType = MessageType.Info;
                return;
            }

            layerNames = sourceController.layers.Select(layer => layer.name).ToArray();
            if (layerNames.Length == 0)
            {
                analysisMessage = "This controller has no layers.";
                analysisMessageType = MessageType.Warning;
                return;
            }

            selectedLayerIndex = Mathf.Clamp(selectedLayerIndex, 0, layerNames.Length - 1);
            selectedStateIndex = 0;

            var layer = sourceController.layers[selectedLayerIndex];
            var stateMachine = layer.stateMachine;
            var warnings = new List<string>();

            if (layer.avatarMask != null)
            {
                warnings.Add("Avatar Mask detected: the tool ignores layer masks and only bakes the chosen sequence.");
            }

            if (layer.syncedLayerIndex >= 0)
            {
                warnings.Add("Synced layer detected: this tool only reads the visible state list and does not rebuild sync behavior.");
            }

            if (stateMachine.anyStateTransitions.Length > 0)
            {
                warnings.Add("Any State transitions exist: they are not reproduced in the baked clip.");
            }

            if (stateMachine.stateMachines.Length > 0)
            {
                warnings.Add("Sub-state machines detected: only top-level states are listed by this tool.");
            }

            foreach (var childState in stateMachine.states)
            {
                availableStates.Add(BuildStateInfo(childState.state));
            }

            stateNames = availableStates.Count == 0
                ? Array.Empty<string>()
                : availableStates.Select(state => state.StateName + (state.IsSupported ? string.Empty : " [UNSUPPORTED]")).ToArray();

            int supportedCount = availableStates.Count(state => state.IsSupported);
            string warningBlock = warnings.Count == 0 ? string.Empty : "\n\nWarnings:\n- " + string.Join("\n- ", warnings);

            analysisMessage =
                "Layer '" + layer.name + "' loaded. Supported states: " + supportedCount + "/" + availableStates.Count + "." +
                warningBlock;
            analysisMessageType = warnings.Count == 0 ? MessageType.Info : MessageType.Warning;
        }

        private LayerStateInfo BuildStateInfo(AnimatorState state)
        {
            var info = new LayerStateInfo
            {
                State = state,
                StateName = state.name,
                ControllerSpeed = state.speed
            };

            if (state.motion == null)
            {
                info.UnsupportedReason = "This state has no motion.";
                info.TransitionSummary = BuildTransitionSummary(state);
                return info;
            }

            if (!(state.motion is AnimationClip clip))
            {
                info.UnsupportedReason = "Only direct AnimationClip motions are supported. Blend Trees and custom motions are blocked.";
                info.TransitionSummary = BuildTransitionSummary(state);
                return info;
            }

            if (state.speed <= 0f)
            {
                info.UnsupportedReason = "Zero or negative state speed is not supported.";
                info.Clip = clip;
                info.TransitionSummary = BuildTransitionSummary(state);
                return info;
            }

            if (state.speedParameterActive || state.timeParameterActive || state.mirrorParameterActive || state.cycleOffsetParameterActive)
            {
                info.UnsupportedReason = "Parameter-driven speed / time / mirror / cycle offset is not supported by this baker.";
                info.Clip = clip;
                info.TransitionSummary = BuildTransitionSummary(state);
                return info;
            }

            info.Clip = clip;
            info.TransitionSummary = BuildTransitionSummary(state);
            info.SuggestedBakeDuration = GuessBakeDuration(state, clip, out string note);
            info.Note = note;

            var objectReferenceBindings = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            if (objectReferenceBindings.Length > 0)
            {
                info.Note = AppendNote(info.Note, "Object reference curves detected. They are ignored unless 'Include Object Reference Curves' is enabled.");
            }

            info.IsSupported = true;
            return info;
        }

        private static float GuessBakeDuration(AnimatorState state, AnimationClip clip, out string note)
        {
            note = "Uses controller speed and the state's first valid transition when possible.";
            float clipDuration = clip.length / state.speed;

            var outgoingTransitions = state.transitions.Where(transition => transition.destinationState != null).ToArray();
            if (outgoingTransitions.Length == 0)
            {
                note = "No outgoing transition found. Full clip duration will be used.";
                return Mathf.Max(clipDuration, 0.001f);
            }

            if (outgoingTransitions.Length > 1)
            {
                note = "Multiple outgoing transitions found. Full clip duration is used by default. Review this step manually.";
                return Mathf.Max(clipDuration, 0.001f);
            }

            var transition = outgoingTransitions[0];
            if (!transition.hasExitTime || transition.exitTime <= 0f)
            {
                note = "Transition has no usable exit time. Full clip duration is used.";
                return Mathf.Max(clipDuration, 0.001f);
            }

            float duration = (clip.length * transition.exitTime) / state.speed;
            if (transition.duration > 0f)
            {
                note = "Exit time found. Transition blend duration is reported but not rebuilt exactly inside the baked clip.";
            }
            else
            {
                note = "Exit time found. Suggested duration follows the transition's exit time.";
            }

            return Mathf.Max(duration, 0.001f);
        }

        private static string BuildTransitionSummary(AnimatorState state)
        {
            var destinations = state.transitions
                .Where(transition => transition.destinationState != null)
                .Select(transition => transition.destinationState.name)
                .ToArray();

            if (destinations.Length == 0)
            {
                return "No outgoing transition.";
            }

            return destinations.Length == 1
                ? "Next: " + destinations[0]
                : "Branching: " + string.Join(", ", destinations);
        }

        private void AutoFillSequenceFromDefaultPath()
        {
            sequenceSteps.Clear();

            if (!CanAnalyzeCurrentLayer())
            {
                generationMessage = "Select a valid controller layer first.";
                generationMessageType = MessageType.Warning;
                return;
            }

            var stateMachine = sourceController.layers[selectedLayerIndex].stateMachine;
            if (stateMachine.defaultState == null)
            {
                generationMessage = "This layer has no default state.";
                generationMessageType = MessageType.Warning;
                return;
            }

            var visitedStates = new HashSet<AnimatorState>();
            AnimatorState currentState = stateMachine.defaultState;
            string stopReason = "Sequence auto-filled.";

            while (currentState != null)
            {
                if (visitedStates.Contains(currentState))
                {
                    stopReason = "Loop detected. Auto-fill stopped before revisiting a state.";
                    break;
                }

                visitedStates.Add(currentState);

                LayerStateInfo stateInfo = availableStates.FirstOrDefault(info => info.State == currentState);
                if (stateInfo == null)
                {
                    stopReason = "A state from the path is not available in the current top-level analysis.";
                    break;
                }

                if (!stateInfo.IsSupported)
                {
                    stopReason = "Auto-fill stopped on unsupported state: " + stateInfo.StateName + ".";
                    break;
                }

                AddStateToSequence(stateInfo);

                var nextTransitions = currentState.transitions.Where(transition => transition.destinationState != null).ToArray();
                if (nextTransitions.Length == 0)
                {
                    stopReason = "Reached the end of the path.";
                    break;
                }

                if (nextTransitions.Length > 1)
                {
                    stopReason = "Branch detected on state '" + currentState.name + "'. Auto-fill stopped there so you can choose the rest manually.";
                    break;
                }

                currentState = nextTransitions[0].destinationState;
            }

            generationMessage = stopReason;
            generationMessageType = MessageType.Info;
        }

        private void AddStateToSequence(LayerStateInfo stateInfo)
        {
            if (stateInfo == null || !stateInfo.IsSupported || stateInfo.Clip == null)
            {
                generationMessage = "Only supported AnimationClip states can be added to the bake sequence.";
                generationMessageType = MessageType.Warning;
                return;
            }

            sequenceSteps.Add(new SequenceStep
            {
                StateName = stateInfo.StateName,
                Clip = stateInfo.Clip,
                ControllerSpeed = stateInfo.ControllerSpeed,
                BakeDuration = stateInfo.SuggestedBakeDuration,
                Note = stateInfo.Note
            });
        }

        private void MoveStep(int index, int direction)
        {
            int newIndex = index + direction;
            if (index < 0 || index >= sequenceSteps.Count || newIndex < 0 || newIndex >= sequenceSteps.Count)
            {
                return;
            }

            SequenceStep temp = sequenceSteps[index];
            sequenceSteps[index] = sequenceSteps[newIndex];
            sequenceSteps[newIndex] = temp;
        }

        private float GetTotalBakeDuration()
        {
            return sequenceSteps.Sum(step => step.BakeDuration);
        }

        private void PickOutputAssetPath()
        {
            EnsureAssetFolderExists(DEFAULT_OUTPUT_FOLDER);

            string currentFolder = Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/') ?? DEFAULT_OUTPUT_FOLDER;
            if (!IsAssetPath(currentFolder))
            {
                currentFolder = DEFAULT_OUTPUT_FOLDER;
            }

            string suggestedName = Path.GetFileNameWithoutExtension(outputAssetPath);
            if (string.IsNullOrWhiteSpace(suggestedName))
            {
                suggestedName = "Controller_Baked";
            }

            string selectedPath = EditorUtility.SaveFilePanelInProject(
                "Save baked animation clip",
                SanitizeFileName(suggestedName),
                "anim",
                "Choose where to save the generated AnimationClip.",
                currentFolder);

            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                outputAssetPath = selectedPath.Replace('\\', '/');
            }
        }

        private void GenerateAnimationClip()
        {
            generationMessage = string.Empty;

            if (!CanGenerateClip())
            {
                generationMessage = "The sequence or output path is not valid yet.";
                generationMessageType = MessageType.Warning;
                return;
            }

            EnsureAssetFolderExists(Path.GetDirectoryName(outputAssetPath)?.Replace('\\', '/') ?? DEFAULT_OUTPUT_FOLDER);

            var floatBindings = sequenceSteps
                .SelectMany(step => AnimationUtility.GetCurveBindings(step.Clip))
                .Distinct(EditorCurveBindingComparer.Instance)
                .ToList();

            var objectReferenceBindings = includeObjectReferenceCurves
                ? sequenceSteps.SelectMany(step => AnimationUtility.GetObjectReferenceCurveBindings(step.Clip))
                    .Distinct(EditorCurveBindingComparer.Instance)
                    .ToList()
                : new List<EditorCurveBinding>();

            if (floatBindings.Count == 0 && objectReferenceBindings.Count == 0)
            {
                generationMessage = "No supported curves were found in the selected sequence.";
                generationMessageType = MessageType.Error;
                return;
            }

            var outputClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputAssetPath);
            bool clipAlreadyExists = outputClip != null;
            if (outputClip == null)
            {
                outputClip = new AnimationClip();
            }
            else
            {
                outputClip.ClearCurves();
            }

            outputClip.name = Path.GetFileNameWithoutExtension(outputAssetPath);
            outputClip.frameRate = sampleRate;
            outputClip.wrapMode = WrapMode.Once;

            foreach (var binding in floatBindings)
            {
                List<Keyframe> bakedKeys = BakeFloatBinding(binding);
                if (bakedKeys.Count == 0)
                {
                    continue;
                }

                var curve = new AnimationCurve(bakedKeys.ToArray());
                for (int keyIndex = 0; keyIndex < curve.length; keyIndex++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(curve, keyIndex, AnimationUtility.TangentMode.Linear);
                }

                AnimationUtility.SetEditorCurve(outputClip, binding, curve);
            }

            foreach (var binding in objectReferenceBindings)
            {
                ObjectReferenceKeyframe[] bakedKeys = BakeObjectReferenceBinding(binding);
                if (bakedKeys.Length == 0)
                {
                    continue;
                }

                AnimationUtility.SetObjectReferenceCurve(outputClip, binding, bakedKeys);
            }

            if (!clipAlreadyExists)
            {
                AssetDatabase.CreateAsset(outputClip, outputAssetPath);
            }

            EditorUtility.SetDirty(outputClip);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = outputClip;
            EditorGUIUtility.PingObject(outputClip);

            generationMessage =
                "Baked clip created: " + outputClip.name + " (" + GetTotalBakeDuration().ToString("0.###") + " sec, " +
                sequenceSteps.Count + " step(s), sample rate " + sampleRate + " fps).";

            if (!includeObjectReferenceCurves && sequenceSteps.Any(step => AnimationUtility.GetObjectReferenceCurveBindings(step.Clip).Length > 0))
            {
                generationMessage += " Object reference curves were intentionally ignored for safety.";
            }

            generationMessageType = MessageType.Info;
        }

        private List<Keyframe> BakeFloatBinding(EditorCurveBinding binding)
        {
            var bakedKeys = new List<Keyframe>();
            bool hasHeldValue = false;
            float heldValue = 0f;
            float clipOffset = 0f;

            foreach (var step in sequenceSteps)
            {
                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(step.Clip, binding);
                if (sourceCurve == null)
                {
                    if (resetMissingCurvesBetweenSteps)
                    {
                        float resetValue = GetDefaultFloatValue(binding);
                        AddFloatSpan(bakedKeys, clipOffset, step.BakeDuration, resetValue);
                        heldValue = resetValue;
                        hasHeldValue = true;
                        clipOffset += step.BakeDuration;
                        continue;
                    }

                    if (hasHeldValue)
                    {
                        AddFloatSpan(bakedKeys, clipOffset, step.BakeDuration, heldValue);
                    }

                    clipOffset += step.BakeDuration;
                    continue;
                }

                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(step.BakeDuration * sampleRate));
                for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
                {
                    float localOutputTime = (step.BakeDuration * sampleIndex) / sampleCount;
                    float sourceTime = GetSourceClipTime(step, localOutputTime);
                    float sampledValue = sourceCurve.Evaluate(sourceTime);

                    AddFloatKey(bakedKeys, clipOffset + localOutputTime, sampledValue);
                    heldValue = sampledValue;
                    hasHeldValue = true;
                }

                clipOffset += step.BakeDuration;
            }

            return bakedKeys;
        }

        private ObjectReferenceKeyframe[] BakeObjectReferenceBinding(EditorCurveBinding binding)
        {
            var bakedKeys = new List<ObjectReferenceKeyframe>();
            bool hasHeldValue = false;
            Object heldValue = null;
            float clipOffset = 0f;

            foreach (var step in sequenceSteps)
            {
                ObjectReferenceKeyframe[] sourceFrames = AnimationUtility.GetObjectReferenceCurve(step.Clip, binding);
                if (sourceFrames == null || sourceFrames.Length == 0)
                {
                    if (resetMissingCurvesBetweenSteps)
                    {
                        AddObjectSpan(bakedKeys, clipOffset, step.BakeDuration, null);
                        heldValue = null;
                        hasHeldValue = true;
                        clipOffset += step.BakeDuration;
                        continue;
                    }

                    if (hasHeldValue)
                    {
                        AddObjectSpan(bakedKeys, clipOffset, step.BakeDuration, heldValue);
                    }

                    clipOffset += step.BakeDuration;
                    continue;
                }

                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(step.BakeDuration * sampleRate));
                Object lastSample = null;
                bool hasLastSample = false;

                for (int sampleIndex = 0; sampleIndex <= sampleCount; sampleIndex++)
                {
                    float localOutputTime = (step.BakeDuration * sampleIndex) / sampleCount;
                    float sourceTime = GetSourceClipTime(step, localOutputTime);
                    Object sampledValue = EvaluateObjectReference(sourceFrames, sourceTime);

                    if (!hasLastSample || sampledValue != lastSample || sampleIndex == 0 || sampleIndex == sampleCount)
                    {
                        AddObjectKey(bakedKeys, clipOffset + localOutputTime, sampledValue);
                    }

                    lastSample = sampledValue;
                    hasLastSample = true;
                    heldValue = sampledValue;
                    hasHeldValue = true;
                }

                clipOffset += step.BakeDuration;
            }

            return bakedKeys.ToArray();
        }

        private static float GetSourceClipTime(SequenceStep step, float localOutputTime)
        {
            float clipLength = Mathf.Max(step.Clip.length, 0.0001f);
            float sourceDuration = step.BakeDuration * step.ControllerSpeed;
            float absoluteSourceTime = Mathf.Max(localOutputTime, 0f) * step.ControllerSpeed;

            if (absoluteSourceTime >= sourceDuration - 0.0001f)
            {
                float exactEndTime = sourceDuration % clipLength;
                if (Mathf.Approximately(exactEndTime, 0f) && sourceDuration > 0f)
                {
                    return clipLength;
                }

                return Mathf.Clamp(exactEndTime, 0f, clipLength);
            }

            if (sourceDuration > clipLength)
            {
                float wrappedTime = Mathf.Repeat(absoluteSourceTime, clipLength);
                if (Mathf.Approximately(wrappedTime, 0f) && absoluteSourceTime > 0f)
                {
                    return clipLength;
                }

                return Mathf.Clamp(wrappedTime, 0f, clipLength);
            }

            return Mathf.Clamp(absoluteSourceTime, 0f, clipLength);
        }

        private static Object EvaluateObjectReference(IReadOnlyList<ObjectReferenceKeyframe> keyframes, float time)
        {
            if (keyframes == null || keyframes.Count == 0)
            {
                return null;
            }

            Object value = keyframes[0].value;
            for (int index = 0; index < keyframes.Count; index++)
            {
                if (keyframes[index].time > time)
                {
                    break;
                }

                value = keyframes[index].value;
            }

            return value;
        }

        private void AddFloatSpan(List<Keyframe> keys, float startTime, float duration, float value)
        {
            AddFloatKey(keys, startTime, value);
            AddFloatKey(keys, startTime + duration, value);
        }

        private void AddObjectSpan(List<ObjectReferenceKeyframe> keys, float startTime, float duration, Object value)
        {
            AddObjectKey(keys, startTime, value);
            AddObjectKey(keys, startTime + duration, value);
        }

        private static float GetDefaultFloatValue(EditorCurveBinding binding)
        {
            if (binding.propertyName == "m_LocalScale.x" ||
                binding.propertyName == "m_LocalScale.y" ||
                binding.propertyName == "m_LocalScale.z" ||
                binding.propertyName == "localScale.x" ||
                binding.propertyName == "localScale.y" ||
                binding.propertyName == "localScale.z")
            {
                return 1f;
            }

            if (binding.propertyName == "m_LocalRotation.w" || binding.propertyName == "localRotation.w")
            {
                return 1f;
            }

            if (binding.propertyName.StartsWith("blendShape.", StringComparison.Ordinal))
            {
                return 0f;
            }

            return 0f;
        }

        private static void AddFloatKey(List<Keyframe> keys, float time, float value)
        {
            if (keys.Count > 0 && Mathf.Approximately(keys[keys.Count - 1].time, time))
            {
                Keyframe lastKey = keys[keys.Count - 1];
                if (!Mathf.Approximately(lastKey.value, value))
                {
                    lastKey.time = Mathf.Max(0f, time - 0.0001f);
                    keys[keys.Count - 1] = lastKey;
                    keys.Add(new Keyframe(time, value));
                    return;
                }

                lastKey.value = value;
                keys[keys.Count - 1] = lastKey;
                return;
            }

            keys.Add(new Keyframe(time, value));
        }

        private static void AddObjectKey(List<ObjectReferenceKeyframe> keys, float time, Object value)
        {
            if (keys.Count > 0 && Mathf.Approximately(keys[keys.Count - 1].time, time))
            {
                ObjectReferenceKeyframe lastKey = keys[keys.Count - 1];
                if (lastKey.value != value)
                {
                    lastKey.time = Mathf.Max(0f, time - 0.0001f);
                    keys[keys.Count - 1] = lastKey;
                    keys.Add(new ObjectReferenceKeyframe { time = time, value = value });
                    return;
                }

                keys[keys.Count - 1] = new ObjectReferenceKeyframe { time = time, value = value };
                return;
            }

            keys.Add(new ObjectReferenceKeyframe { time = time, value = value });
        }

        private static void EnsureAssetFolderExists(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string normalized = assetFolder.Replace('\\', '/');
            if (!normalized.StartsWith("Assets", StringComparison.Ordinal))
            {
                return;
            }

            // the workspace (Assets/Kawaii Studio) first, then every level of the requested folder
            KawaiiStudioWorkspace.EnsureInstalled();
            KawaiiStudioPaths.EnsureFolder(normalized);
        }

        private static bool IsAssetPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && path.Replace('\\', '/').StartsWith("Assets/", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return string.IsNullOrWhiteSpace(value) ? "Controller_Baked" : value.Trim();
        }

        private static string AppendNote(string existing, string value)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return value;
            }

            return existing + " " + value;
        }

        private sealed class LayerStateInfo
        {
            public AnimatorState State;
            public string StateName;
            public AnimationClip Clip;
            public float ControllerSpeed;
            public float SuggestedBakeDuration;
            public string TransitionSummary;
            public string Note;
            public bool IsSupported;
            public string UnsupportedReason;
        }

        private sealed class SequenceStep
        {
            public string StateName;
            public AnimationClip Clip;
            public float ControllerSpeed;
            public float BakeDuration;
            public string Note;
        }

        private sealed class EditorCurveBindingComparer : IEqualityComparer<EditorCurveBinding>
        {
            public static readonly EditorCurveBindingComparer Instance = new EditorCurveBindingComparer();

            public bool Equals(EditorCurveBinding left, EditorCurveBinding right)
            {
                return left.path == right.path && left.propertyName == right.propertyName && left.type == right.type;
            }

            public int GetHashCode(EditorCurveBinding binding)
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 23) + (binding.path == null ? 0 : binding.path.GetHashCode());
                    hash = (hash * 23) + (binding.propertyName == null ? 0 : binding.propertyName.GetHashCode());
                    hash = (hash * 23) + (binding.type == null ? 0 : binding.type.GetHashCode());
                    return hash;
                }
            }
        }
    }
}