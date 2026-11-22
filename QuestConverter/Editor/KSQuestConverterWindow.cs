// KS Quest Converter: one window, one click, a Quest (Android) version of a PC avatar.
// The conversion engine is VRCQuestTools by kurotu (MIT), carried in QuestConverter/Engine under our names
// (see QuestConverter/Engine/UPSTREAM.txt and LICENSE.txt).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using KawaiiStudio.QuestConverter.Components;
using KawaiiStudio.QuestConverter.Utils;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase.Validation.Performance;
using VRC.SDKBase.Validation.Performance.Stats;

namespace KawaiiStudio.QuestConverter.KS
{
    internal sealed class KSQuestConverterWindow : EditorWindow
    {
        private const string MenuPath = "Kawaii Studio/VRC/PC to Quest Converter";

        private VRCAvatarDescriptor avatar;
        private VRCAvatarDescriptor questAvatar;
        private Perf pcPerf, questPerf;
        private string lastMessage;
        private bool lastFailed;
        private Vector2 scroll;

        private static string T(string key) => KawaiiStudioLocalization.T(key);

        [MenuItem(MenuPath, false, 30)]
        internal static void Open()
        {
            var w = GetWindow<KSQuestConverterWindow>(false, "PC to Quest", true);
            w.minSize = new Vector2(420, 520);
            w.PickFromSelection();
            w.Show();
        }

        private void OnEnable() => PickFromSelection();

        private void OnSelectionChange()
        {
            if (avatar == null) { PickFromSelection(); Repaint(); }
        }

        private void PickFromSelection()
        {
            if (avatar != null) return;
            var sel = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<VRCAvatarDescriptor>(true) : null;
            if (sel == null)
            {
                var all = FindObjectsOfType<VRCAvatarDescriptor>(true).Where(d => d.GetComponent<ConvertedAvatar>() == null).ToArray();
                if (all.Length == 1) sel = all[0];
            }
            if (sel != null && sel.GetComponent<ConvertedAvatar>() == null) SetAvatar(sel);
        }

        private void SetAvatar(VRCAvatarDescriptor d)
        {
            avatar = d;
            questAvatar = null;
            questPerf = null;
            lastMessage = null;
            pcPerf = d != null ? Perf.Measure(d.gameObject) : null;
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.BeginVertical();
            KawaiiStudioGUI.DrawBanner("PC to Quest", T("A Quest version of your avatar in one click"), KawaiiStudioVersion.Current,
                KawaiiStudioBranding.Logo, KawaiiStudioBranding.Banner);
            GUILayout.Space(KawaiiStudioGUI.Space2);

            DrawAvatar();
            if (avatar != null) DrawConvert();
            if (questAvatar != null) DrawResult();
            if (!string.IsNullOrEmpty(lastMessage))
                KawaiiStudioGUI.StatusBanner(lastFailed ? T("Conversion failed") : T("Done"), lastMessage,
                    lastFailed ? KawaiiStudioGUI.ErrorColor : KawaiiStudioGUI.SuccessColor);
            DrawCredits();

            KawaiiStudioGUI.DrawFooter();
            GUILayout.EndVertical();
            GUILayout.EndScrollView();
        }

        private void DrawAvatar()
        {
            KawaiiStudioGUI.DrawSection(T("PC avatar"), KawaiiStudioGUI.AccentCyan, () =>
            {
                var picked = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(T("Avatar in the scene"), avatar, typeof(VRCAvatarDescriptor), true);
                if (picked != avatar)
                {
                    if (picked != null && (EditorUtility.IsPersistent(picked) || picked.GetComponent<ConvertedAvatar>() != null))
                    {
                        EditorUtility.DisplayDialog("PC to Quest", T("Pick the PC avatar from the scene (not a prefab file, not a Quest version)."), "OK");
                    }
                    else
                    {
                        SetAvatar(picked);
                    }
                }
                if (avatar == null)
                {
                    GUILayout.Label(T("Select your avatar in the Hierarchy, or drag it here."), KawaiiStudioGUI.InfoLabelStyle);
                    return;
                }
                if (pcPerf != null)
                {
                    PerfLine(T("On PC now"), pcPerf.PcOverall);
                    PerfLine(T("On Quest as it is"), pcPerf.QuestOverall);
                    GUILayout.Label(pcPerf.Summary, KawaiiStudioGUI.InfoLabelStyle);
                }
            });
        }

        private void DrawConvert()
        {
            KawaiiStudioGUI.DrawSection(T("Quest version"), KawaiiStudioGUI.AccentPink, () =>
            {
                GUILayout.Label(T("Your PC avatar is not changed: a copy is made beside it and converted for Quest " +
                                  "(Quest shaders, textures sized for Android, unsupported components removed)."), KawaiiStudioGUI.InfoLabelStyle);
                GUILayout.Space(4);
                if (!AndroidSupported())
                {
                    KawaiiStudioGUI.StatusBanner(T("Android Build Support is missing"),
                        T("Add the Android Build Support module to this Unity version in Unity Hub to upload the Quest version."),
                        KawaiiStudioGUI.WarningColor);
                }
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (KawaiiStudioGUI.PrimaryButton(T("MAKE THE QUEST VERSION"), GUILayout.Height(34)))
                    {
                        EditorApplication.delayCall += Convert;
                    }
                }
            });
        }

        private void DrawResult()
        {
            KawaiiStudioGUI.DrawSection(T("Result"), KawaiiStudioGUI.AccentGreen, () =>
            {
                EditorGUILayout.ObjectField(T("Quest version"), questAvatar, typeof(VRCAvatarDescriptor), true);
                if (questPerf != null)
                {
                    PerfLine(T("On Quest"), questPerf.QuestOverall);
                    GUILayout.Label(questPerf.Summary, KawaiiStudioGUI.InfoLabelStyle);
                    if (questPerf.QuestOverall == PerformanceRating.VeryPoor)
                    {
                        GUILayout.Label(T("Very Poor on Quest still uploads: people see it once they show your avatar (or a fallback until then)."),
                            KawaiiStudioGUI.InfoLabelStyle);
                    }
                }
                GUILayout.Space(6);
                GUILayout.Label(T("It keeps the PC avatar's ID: uploaded from Android, it becomes the Quest version of that same avatar."),
                    KawaiiStudioGUI.InfoLabelStyle);
                GUILayout.BeginHorizontal();
                bool onAndroid = EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android;
                using (new EditorGUI.DisabledScope(onAndroid || !AndroidSupported()))
                {
                    if (KawaiiStudioGUI.SecondaryButton(onAndroid ? T("Unity is on Android") : T("Switch Unity to Android")))
                    {
                        if (EditorUtility.DisplayDialog("PC to Quest",
                                T("Unity re-imports the project's textures for Android. On a big project it takes several minutes. Switch now?"),
                                T("Switch"), T("Cancel")))
                        {
                            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                        }
                    }
                }
                if (KawaiiStudioGUI.SecondaryButton(T("Open the VRChat SDK")))
                {
                    EditorApplication.ExecuteMenuItem("VRChat SDK/Show Control Panel");
                }
                GUILayout.EndHorizontal();
            });
        }

        private void DrawCredits()
        {
            KawaiiStudioGUI.DrawSection(T("Credits"), KawaiiStudioGUI.AccentBlue, () =>
            {
                GUILayout.Label("Conversion engine: VRCQuestTools by kurotu (MIT), github.com/kurotu/VRCQuestTools", KawaiiStudioGUI.InfoLabelStyle);
                GUILayout.Label("lilToon parts: lilxyzw (MIT). Full licences in QuestConverter/Engine.", KawaiiStudioGUI.InfoLabelStyle);
            });
        }

        private static void PerfLine(string label, PerformanceRating r)
        {
            var rect = EditorGUILayout.GetControlRect(false, 20);
            var labelRect = new Rect(rect.x, rect.y, rect.width - 110, rect.height);
            GUI.Label(labelRect, label, KawaiiStudioGUI.LabelStyle);
            KawaiiStudioGUI.DrawPill(new Rect(rect.xMax - 104, rect.y + 1, 104, rect.height - 2), Perf.Name(r), Perf.Tint(r));
        }

        private static bool AndroidSupported() => BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);

        // ------------------------------------------------------------------ conversion
        private void Convert()
        {
            if (avatar == null) return;
            lastMessage = null;
            lastFailed = false;
            var r = KSQuestConverter.MakeQuestVersion(avatar);
            if (r.Error != null)
            {
                lastFailed = true;
                lastMessage = r.Error;
            }
            else
            {
                questAvatar = r.Quest;
                questPerf = Perf.Measure(r.Quest.gameObject);
                lastMessage = string.Format(T("Quest version made in {0:0} s: \"{1}\". The PC avatar is hidden, not changed."), r.Seconds, r.Quest.name);
            }
            Repaint();
        }

        // ------------------------------------------------------------------ VRChat's own performance rank
        internal sealed class Perf
        {
            internal PerformanceRating PcOverall, QuestOverall;
            internal string Summary;

            internal static Perf Measure(GameObject go)
            {
                try
                {
                    // SDK 3.10.6+: the stats tables are filled on demand (batch mode or a fresh domain has not done it yet)
                    typeof(AvatarPerformanceStats).GetMethod("Initialize", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
                }
                catch { }
                try
                {
                    var pc = VRCSDKUtility.CalculatePerformanceStats(go, false);
                    var q = VRCSDKUtility.CalculatePerformanceStats(go, true);
                    var p = new Perf
                    {
                        PcOverall = pc.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall),
                        QuestOverall = q.GetPerformanceRatingForCategory(AvatarPerformanceCategory.Overall),
                    };
                    var parts = new List<string>();
                    parts.Add($"{q.polyCount ?? 0:N0} " + T("triangles"));
                    parts.Add($"{q.materialCount ?? 0} " + T("materials"));
                    parts.Add($"{q.textureMegabytes ?? 0:0.#} MB " + T("textures"));
                    if (q.physBone.HasValue) parts.Add($"{q.physBone.Value.componentCount} PhysBones");
                    if ((q.particleSystemCount ?? 0) > 0) parts.Add($"{q.particleSystemCount} " + T("particle systems"));
                    p.Summary = string.Join("  ·  ", parts);
                    return p;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[KS Quest Converter] performance not read: " + e.Message);
                    return null;
                }
            }

            internal static string Name(PerformanceRating r)
            {
                switch (r)
                {
                    case PerformanceRating.Excellent: return "Excellent";
                    case PerformanceRating.Good: return "Good";
                    case PerformanceRating.Medium: return "Medium";
                    case PerformanceRating.Poor: return "Poor";
                    case PerformanceRating.VeryPoor: return "Very Poor";
                    default: return r.ToString();
                }
            }

            internal static Color Tint(PerformanceRating r)
            {
                switch (r)
                {
                    case PerformanceRating.Excellent:
                    case PerformanceRating.Good: return KawaiiStudioGUI.SuccessColor;
                    case PerformanceRating.Medium: return KawaiiStudioGUI.AccentCyan;
                    case PerformanceRating.Poor: return KawaiiStudioGUI.WarningColor;
                    default: return KawaiiStudioGUI.ErrorColor;
                }
            }
        }
    }
}
