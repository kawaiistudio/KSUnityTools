using UnityEditor;
using UnityEngine;
using VRC.Core;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>The KS ANTI-RIP window. Successor to KS Obfuscator (1.x), which is removed on upgrade.</summary>
    public class KSAntiRipWindow : EditorWindow
    {
        GameObject avatar;
        readonly KSAntiRipProcessor.Settings settings = new KSAntiRipProcessor.Settings();
        KSAntiRipProcessor.Result last;
        Vector2 scroll;

        [MenuItem("Kawaii Studio/VRC/KS Anti-Rip", false, 21)]
        public static void ShowWindow()
        {
            var w = GetWindow<KSAntiRipWindow>("KS Anti-Rip");
            w.minSize = new Vector2(430, 600);
            w.Show();
        }

        void OnEnable() => KawaiiStudioGUI.Initialize();

        void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            KawaiiStudioGUI.DrawBanner(
                "KS ANTI-RIP",
                "Avatar protection — the key never ships in the bundle",
                KSAntiRipProcessor.Version,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner);

            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection("AVATAR", () =>
            {
                avatar = (GameObject)EditorGUILayout.ObjectField("Avatar", avatar, typeof(GameObject), true);
                DrawAvatarStatus();
            });

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection("PROTECTION", () =>
            {
                settings.strength = EditorGUILayout.Slider("Scramble Strength", settings.strength, 2f, 15f);
                settings.restoreGroups = EditorGUILayout.IntSlider("Restore Shapes", settings.restoreGroups, 1, 6);
                settings.syncKey = EditorGUILayout.Toggle("Others See It Unlocked", settings.syncKey);
                settings.hideWhileLocked = EditorGUILayout.Toggle("Hide Until Unlocked", settings.hideWhileLocked);
                settings.obfuscateNames = EditorGUILayout.Toggle("Obfuscate Names", settings.obfuscateNames);

                EditorGUILayout.HelpBox(
                    settings.syncKey
                        ? "Each restore shape is one synced key value: " + settings.KeyBits + " bits of your " +
                          "parameter budget. Everyone sees you normally; someone in your instance with a modified " +
                          "client could read the key."
                        : "The key stays on this PC: no synced bits, and nobody can read it — but only you will see " +
                          "the avatar whole. Everyone else sees it scrambled.",
                    MessageType.None);
            });

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection("WHAT THIS DOES", () =>
            {
                EditorGUILayout.LabelField(
                    "Your mesh ships scrambled, and the bundle holds no value that repairs it. Those values are " +
                    "the key: they live in a .key file on this PC and KS OSC KEY sends them to VRChat while you " +
                    "wear the avatar.",
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "A downloaded bundle is scrambled geometry, and reading its animator gives nothing away. It is " +
                    "not unbreakable — a solver written against this scheme can still be pointed at it. No " +
                    "client-side protection can be.",
                    EditorStyles.wordWrappedMiniLabel);
            });

            GUILayout.Space(10);

            KawaiiStudioGUI.DrawSection("PROTECT", () =>
            {
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                using (new EditorGUI.DisabledScope(avatar == null))
                {
                    if (GUILayout.Button("PROTECT THIS AVATAR", KawaiiStudioGUI.ButtonStyle))
                        Run();
                }
                GUI.backgroundColor = Color.white;
            });

            DrawResult();

            GUILayout.EndScrollView();
            KawaiiStudioGUI.DrawFooter();
        }

        void DrawAvatarStatus()
        {
            if (avatar == null) return;

            if (avatar.GetComponent<VRCAvatarDescriptor>() == null)
            {
                EditorGUILayout.HelpBox("No VRC Avatar Descriptor on this object.", MessageType.Error);
                return;
            }

            var pipeline = avatar.GetComponent<PipelineManager>();
            if (pipeline == null || string.IsNullOrEmpty(pipeline.blueprintId))
            {
                EditorGUILayout.HelpBox(
                    "No Blueprint ID yet. Upload this avatar once normally first — the key is named after the " +
                    "avatar id, and that is how KS OSC KEY knows which key to send.", MessageType.Warning);
                return;
            }

            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            int used = descriptor.expressionParameters != null ? descriptor.expressionParameters.CalcTotalCost() : 0;
            int max = VRCExpressionParameters.MAX_PARAMETER_COST;
            int after = used + settings.KeyBits;

            EditorGUILayout.LabelField("Avatar ID", pipeline.blueprintId, EditorStyles.miniLabel);
            EditorGUILayout.LabelField("Parameters", $"{used} used + {settings.KeyBits} for the key = {after} / {max}",
                                       EditorStyles.miniLabel);
            if (after > max)
                EditorGUILayout.HelpBox(
                    $"Not enough parameter space: {after} of {max}. Use fewer restore shapes or free up parameters.",
                    MessageType.Error);
        }

        void DrawResult()
        {
            if (last == null) return;
            GUILayout.Space(10);
            KawaiiStudioGUI.DrawSection(last.ok ? "PROTECTED" : "FAILED", () =>
            {
                EditorGUILayout.LabelField(last.message, EditorStyles.wordWrappedLabel);
                if (!last.ok) return;

                EditorGUILayout.LabelField($"{last.meshesProtected} mesh(es), {last.verticesProtected:n0} vertices",
                                           EditorStyles.miniLabel);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Key file", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(last.keyPath, EditorStyles.textField, GUILayout.Height(18));
                EditorGUILayout.HelpBox(
                    "Keep this file private — it is the only thing that unlocks your avatar. It is stored " +
                    "outside your Unity project so it is not swept up when you zip or share the project.",
                    MessageType.Info);

                if (GUILayout.Button("Show the key folder"))
                    EditorUtility.RevealInFinder(last.keyPath);
                if (GUILayout.Button("Select the protected prefab"))
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(last.prefabPath);

                foreach (string w in last.warnings)
                    EditorGUILayout.HelpBox(w, MessageType.Warning);

                EditorGUILayout.HelpBox(
                    "Upload the KS_PROTECTED prefab, not your original. Your original is untouched.",
                    MessageType.None);
            });
        }

        void Run()
        {
            last = KSAntiRipProcessor.Protect(avatar, settings);
            if (!last.ok)
                EditorUtility.DisplayDialog("KS ANTI-RIP", last.message, "OK");
            Repaint();
        }
    }
}
