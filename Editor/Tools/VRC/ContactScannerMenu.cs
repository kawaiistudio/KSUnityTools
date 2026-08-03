using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.Contact.Components;
using System.Collections.Generic;

namespace KawaiiStudio
{
    public class ContactScannerWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;
        private Vector2 scrollPosition;
        private List<VRCContactReceiver> foundReceivers = new List<VRCContactReceiver>();
        private GameObject lastScannedObject;

        [MenuItem("Kawaii Studio/Contact Scanner Window")]
        public static void ShowWindow()
        {
            var window = GetWindow<ContactScannerWindow>("Contact Scanner");
            window.minSize = new Vector2(450, 500);
        }

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            KawaiiStudioGUI.DrawBanner(
                "CONTACT SCANNER",
                "VRChat Contact Receiver Finder",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner
            );

            GUILayout.Space(10);

            KawaiiStudioGUI.DrawSection("HOW TO USE", () =>
            {
                EditorGUILayout.LabelField("1. Select your avatar/prefab in the Hierarchy", KawaiiStudioGUI.InfoLabelStyle);
                EditorGUILayout.LabelField("2. Click the Scan button below", KawaiiStudioGUI.InfoLabelStyle);
            });

            KawaiiStudioGUI.DrawSection("SCAN", () =>
            {
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("🔍 SCAN SELECTED OBJECT", KawaiiStudioGUI.ButtonStyle))
                {
                    Scan();
                }
                GUI.backgroundColor = Color.white;

                if (lastScannedObject != null)
                {
                    GUILayout.Space(5);
                    EditorGUILayout.LabelField($"Results for: {lastScannedObject.name}", KawaiiStudioGUI.LabelStyle);
                }
            });

            KawaiiStudioGUI.DrawSection($"RESULTS ({foundReceivers.Count})", () =>
            {
                if (foundReceivers.Count > 0)
                {
                    foreach (VRCContactReceiver receiver in foundReceivers)
                    {
                        if (receiver == null) continue;

                        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

                        string tagInfo = receiver.collisionTags.Count > 0 ? receiver.collisionTags[0] : "No Tag";

                        if (GUILayout.Button($"{receiver.gameObject.name} (Tag: {tagInfo})", EditorStyles.label))
                        {
                            Selection.activeGameObject = receiver.gameObject;
                            EditorGUIUtility.PingObject(receiver.gameObject);
                        }

                        if (GUILayout.Button("Focus", GUILayout.Width(50)))
                        {
                            Selection.activeGameObject = receiver.gameObject;
                            SceneView.FrameLastActiveSceneView();
                        }

                        EditorGUILayout.EndHorizontal();
                    }
                }
                else
                {
                    EditorGUILayout.LabelField("No contacts found or scan not started.", KawaiiStudioGUI.InfoLabelStyle);
                }
            });

            KawaiiStudioGUI.DrawFooter();

            EditorGUILayout.EndScrollView();
        }

        void Scan()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected == null)
            {
                EditorUtility.DisplayDialog("Warning", "Please select an object in the hierarchy.", "OK");
                return;
            }

            lastScannedObject = selected;
            VRCContactReceiver[] results = selected.GetComponentsInChildren<VRCContactReceiver>(true);
            foundReceivers = new List<VRCContactReceiver>(results);
            foundReceivers.Sort((a, b) => string.Compare(a.gameObject.name, b.gameObject.name));
        }
    }
}