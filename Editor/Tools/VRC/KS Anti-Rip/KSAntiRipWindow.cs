using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// What is left of KS ANTI-RIP inside Unity: a signpost.
    ///
    /// Up to 3.9.0 this window carried the whole protection -- an avatar field, the settings, a PROTECT button. The
    /// owner's rule since 2026-11-16 ("sert à rien d'avoir ça"): the whole of KS ANTI-RIP is driven from the Kawaii
    /// Studio app, which lists the linked projects and the avatars in their open scenes by itself, and Unity is only
    /// reached through Kawaii Studio Link. Every control that was here lives on the app's page. The menu entry stays so
    /// that anyone who knows it is told where to go instead of finding nothing.
    /// </summary>
    public class KSAntiRipWindow : EditorWindow
    {
        [MenuItem("Kawaii Studio/VRC/KS Anti-Rip", false, 21)]
        public static void ShowWindow()
        {
            var w = GetWindow<KSAntiRipWindow>("KS Anti-Rip");
            w.minSize = new Vector2(430, 320);
            w.Show();
        }

        void OnEnable() => KawaiiStudioGUI.Initialize();

        void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            KawaiiStudioGUI.DrawBanner(
                "KS ANTI-RIP",
                "Controlled from the Kawaii Studio app",
                KSAntiRipProcessor.Version,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner);

            GUILayout.Space(8);

            KawaiiStudioGUI.DrawSection("WHERE IT IS NOW", () =>
            {
                EditorGUILayout.LabelField(
                    "Open the Kawaii Studio app, then UNITY TOOLS > KS Anti-Rip. Your linked projects and the avatars " +
                    "in their open scenes appear there by themselves: pick one, check it, protect it.",
                    EditorStyles.wordWrappedLabel);
                GUILayout.Space(4);
                EditorGUILayout.LabelField(
                    "Nothing to do in Unity. It only applies what the app decides, through Kawaii Studio Link: when " +
                    "you turn on Protect on upload for an avatar and upload it, the build is protected -- your avatar " +
                    "itself is never modified.",
                    EditorStyles.wordWrappedMiniLabel);
                GUILayout.Space(8);
                if (GUILayout.Button("Open Kawaii Studio Link"))
                    EditorApplication.delayCall += OpenLinkWindow;
            });

            KawaiiStudioGUI.DrawFooter();
        }

        /// <summary>
        /// Opens the Kawaii Studio Link window. The Link lives in a sibling package, so it is reached by type (no
        /// assembly reference needed) and after the current GUI, via delayCall -- ExecuteMenuItem fails with "no
        /// menu named ..." when it is called before the Link assembly's menu is registered (a domain reload, a fresh
        /// import), which is exactly when someone opens this window.
        /// </summary>
        static void OpenLinkWindow()
        {
            var type = System.Type.GetType("KawaiiStudio.KawaiiStudioLinkWindow, KawaiiStudio.Link.Editor");
            if (type != null)
            {
                GetWindow(type, false, "Kawaii Studio Link").Show();
                return;
            }
            if (!EditorApplication.ExecuteMenuItem("Kawaii Studio/Kawaii Studio Link"))
                Debug.LogWarning("[KS ANTI-RIP] Open it from the menu: Kawaii Studio > Kawaii Studio Link.");
        }
    }
}
