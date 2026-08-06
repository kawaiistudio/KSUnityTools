// Studio Manager -- the Kawaii Studio hub.
//
// This used to double as a package manager: it downloaded individual .cs files from
// raw.githubusercontent and overwrote them in place to "update" tools. That mechanism is
// gone. Installation and updates are the VRChat Creator Companion's job now (the package
// is published to a VPM listing), so the Manager is purely a launcher: it lists every tool
// and opens it, shows the one shared version, and links out. No network calls, no writing
// to the user's project.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    public class KawaiiStudioManager : EditorWindow
    {
        private const string VERSION = KawaiiStudioVersion.Current;
        private const string GITHUB_URL = "https://github.com/kawaiistudio/KSUnityTools";
        private const string DISCORD_URL = "https://discord.gg/xAeJrSAgqG";
        private const string LISTING_URL = "https://kawaiistudio.github.io/KSUnityTools/";

        private struct Tool
        {
            public string Name;
            public string Description;
            public string MenuPath;
            public Tool(string name, string description, string menuPath)
            {
                Name = name; Description = description; MenuPath = menuPath;
            }
        }

        // Core tools compile without the VRChat SDK.
        private static readonly Tool[] CoreTools =
        {
            new Tool("Prefab Optimizer", "Compress textures, meshes and audio on a prefab or avatar",
                "Kawaii Studio/Prefab Optimizer"),
            new Tool("Video Animator", "Turn a video into a looping animated texture sheet",
                "Kawaii Studio/Video Animator"),
            new Tool("GLB to FBX Converter", "Convert .glb models to .fbx through Blender",
                "Kawaii Studio/GLB to FBX Converter"),
            new Tool("Kawaii Exporter", "Export a prefab and its dependencies to a .unitypackage",
                "Kawaii Studio/Universal Tools/Kawaii Exporter"),
        };

        // These live in the VRChat-gated assembly; their menu items only exist when the SDK is.
        private static readonly Tool[] VrcTools =
        {
            new Tool("Ultimate Constraint Tool", "Batch-manage VRC constraints across a hierarchy",
                "Kawaii Studio/✨ Ultimate Constraint Tool"),
            new Tool("Tail to PhysBones", "Convert Tail Animator setups to VRC PhysBones",
                "Kawaii Studio/Universal Tools/Tail to PhysBones Converter"),
            new Tool("Contact Scanner", "Find every VRC Contact Receiver/Sender on an avatar",
                "Kawaii Studio/Contact Scanner Window"),
            new Tool("KS Obfuscator", "Anti-rip mesh obfuscation for avatars",
                "Kawaii Studio/VRC/KS Obfuscator"),
            new Tool("NSFW Detector", "Scan an avatar for NSFW content before upload",
                "Kawaii Studio/VRC/NSFW Detector"),
        };

        private Vector2 _scroll;
        private bool _sdkPresent;

        [MenuItem("Kawaii Studio/Studio Manager", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<KawaiiStudioManager>("Kawaii Studio");
            window.minSize = new Vector2(420, 520);
            window.Show();
        }

        private void OnEnable()
        {
            // The SDK ships its editor code in an assembly named "VRC.SDK3.*". If one is
            // loaded, the VRChat tools' menu items are registered and safe to invoke.
            _sdkPresent = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name.StartsWith("VRC.SDK3", StringComparison.Ordinal));
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            KawaiiStudioGUI.DrawBanner(
                "STUDIO MANAGER",
                "Your Kawaii Studio toolbox",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner);

            KawaiiStudioGUI.DrawSection("Tools", () =>
            {
                foreach (var tool in CoreTools)
                    DrawToolRow(tool, true);
            });

            KawaiiStudioGUI.DrawSection("VRChat Tools", () =>
            {
                if (!_sdkPresent)
                    KawaiiStudioGUI.Banner(
                        "These tools need the VRChat SDK. Install it (via VCC) and they light up here.",
                        KawaiiStudioGUI.MessageKind.Info);

                foreach (var tool in VrcTools)
                    DrawToolRow(tool, _sdkPresent);
            });

            KawaiiStudioGUI.DrawSection("Updates", () =>
            {
                KawaiiStudioGUI.Banner(
                    "Updates are handled by the VRChat Creator Companion. When a new version " +
                    "ships, VCC offers it automatically -- nothing to download here.",
                    KawaiiStudioGUI.MessageKind.Info);

                EditorGUILayout.BeginHorizontal();
                if (KawaiiStudioGUI.SecondaryButton("Open Install Page"))
                    Application.OpenURL(LISTING_URL);
                if (KawaiiStudioGUI.SecondaryButton("GitHub"))
                    Application.OpenURL(GITHUB_URL);
                EditorGUILayout.EndHorizontal();
            });

            KawaiiStudioGUI.DrawSection("Community", () =>
            {
                EditorGUILayout.BeginHorizontal();
                if (KawaiiStudioGUI.PrimaryButton("Discord"))
                    Application.OpenURL(DISCORD_URL);
                if (KawaiiStudioGUI.SecondaryButton("GitHub"))
                    Application.OpenURL(GITHUB_URL);
                EditorGUILayout.EndHorizontal();
            });

            KawaiiStudioGUI.DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolRow(Tool tool, bool enabled)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.BeginVertical();
            GUILayout.Label(tool.Name, KawaiiStudioGUI.H3);
            GUILayout.Label(tool.Description, KawaiiStudioGUI.InfoLabelStyle);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            using (new EditorGUI.DisabledScope(!enabled))
            {
                if (KawaiiStudioGUI.SecondaryButton("Open", GUILayout.Width(84)))
                {
                    if (!EditorApplication.ExecuteMenuItem(tool.MenuPath))
                        Debug.LogWarning($"[Kawaii Studio] Could not open \"{tool.Name}\". " +
                                         "Its menu item wasn't found -- is the VRChat SDK installed?");
                }
            }

            EditorGUILayout.EndHorizontal();
            KawaiiStudioGUI.Separator();
        }
    }
}
