// Studio Manager -- the Kawaii Studio hub.
//
// This used to double as a package manager: it downloaded individual .cs files from
// raw.githubusercontent and overwrote them in place to "update" tools. That mechanism is
// gone. Installation and updates are the VRChat Creator Companion's job now (the package
// is published to a VPM listing), so the Manager is a launcher plus the one place that
// shows the state of the WORKSPACE: "Assets/Kawaii Studio", where the package installs its
// shaders and materials and where every tool writes (see KawaiiStudioWorkspace). No
// network calls.
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
            new Tool("Prefab Optimizer", "Make an avatar lighter without breaking it: textures, audio, polygons",
                "Kawaii Studio/Prefab Optimizer"),
            new Tool("Video Animator", "Turn a video into a looping animated texture sheet (KSVideoDecoder)",
                "Kawaii Studio/Video Animator"),
            new Tool("Controller To Anim", "Bake a chain of a controller layer's states into one safe animation clip",
                "Kawaii Studio/Universal Tools/Controller To Anim"),
            new Tool("GLB to FBX Converter", "Convert .glb models to .fbx through Blender",
                "Kawaii Studio/GLB to FBX Converter"),
            new Tool("Kawaii Exporter", "Export a prefab and its dependencies to a .unitypackage",
                "Kawaii Studio/Universal Tools/Kawaii Exporter"),
        };

        // These live in the VRChat-gated assembly; their menu items only exist when the SDK is.
        private static readonly Tool[] VrcTools =
        {
            new Tool("Menu Colorizer", "Gradient, rainbow and bold labels for your VRChat menu",
                "Kawaii Studio/VRC/Menu Colorizer"),
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

        // workspace state, read once per focus (the stamp is a file) and after every action here
        private bool _wsInstalled;
        private string _wsVersion = "";
        private KawaiiStudioWorkspace.Report _wsReport;

        [MenuItem("Kawaii Studio/Studio Manager", priority = 0)]
        public static void ShowWindow()
        {
            var window = GetWindow<KawaiiStudioManager>("Kawaii Studio");
            window.minSize = new Vector2(420, 560);
            window.Show();
        }

        private void OnEnable()
        {
            // The SDK ships its editor code in an assembly named "VRC.SDK3.*". If one is
            // loaded, the VRChat tools' menu items are registered and safe to invoke.
            _sdkPresent = AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name.StartsWith("VRC.SDK3", StringComparison.Ordinal));
            RefreshWorkspaceState();
        }

        private void OnFocus() => RefreshWorkspaceState();

        private void RefreshWorkspaceState()
        {
            try
            {
                _wsReport = KawaiiStudioWorkspace.EnsureInstalled();   // a no-op once checked this session
                _wsInstalled = KawaiiStudioWorkspace.IsInstalled;
                _wsVersion = KawaiiStudioWorkspace.InstalledVersion;
            }
            catch (Exception e)
            {
                _wsInstalled = false;
                _wsVersion = "";
                Debug.LogWarning("[Kawaii Studio] workspace state unavailable: " + e.Message);
            }
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

            KawaiiStudioGUI.DrawSection("Workspace", KawaiiStudioGUI.AccentPurple, DrawWorkspace);

            KawaiiStudioGUI.DrawSection("Tools", KawaiiStudioGUI.AccentCyan, () =>
            {
                foreach (var tool in CoreTools)
                    DrawToolRow(tool, true);
            });

            KawaiiStudioGUI.DrawSection("VRChat Tools", KawaiiStudioGUI.AccentPink, () =>
            {
                if (!_sdkPresent)
                    KawaiiStudioGUI.Banner(
                        "These tools need the VRChat SDK. Install it (via VCC) and they light up here.",
                        KawaiiStudioGUI.MessageKind.Info);

                foreach (var tool in VrcTools)
                    DrawToolRow(tool, _sdkPresent);
            });

            KawaiiStudioGUI.DrawSection("Updates", KawaiiStudioGUI.AccentOrange, () =>
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

            KawaiiStudioGUI.DrawSection("Community", KawaiiStudioGUI.AccentGreen, () =>
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

        // ------------------------------------------------------------------ workspace

        private void DrawWorkspace()
        {
            bool upToDate = _wsInstalled && _wsVersion == VERSION;
            string status = !_wsInstalled ? "Not installed"
                : upToDate ? "Installed, up to date"
                : $"Installed from {_wsVersion}, package is {VERSION}";

            KawaiiStudioGUI.KeyValueRow("Folder", KawaiiStudioPaths.Workspace);
            KawaiiStudioGUI.KeyValueRow("Status", status, upToDate ? KawaiiStudioGUI.SuccessColor : KawaiiStudioGUI.WarningColor);
            KawaiiStudioGUI.KeyValueRow("Package",
                KawaiiStudioPaths.IsLegacyLayout ? ".unitypackage install (the package is the workspace)" : KawaiiStudioPaths.PackageRoot);

            GUILayout.Space(4);
            KawaiiStudioGUI.Banner(
                "Shaders, materials and every tool's output live here: Videos, Animations, Prefabs, Models, " +
                "KS Exported, Optimized, Protected and ThirdParty/FFMPEG (drop ffmpeg there for the Video Animator). " +
                "Shaders follow package updates; materials are yours and are never replaced.",
                KawaiiStudioGUI.MessageKind.Info);

            if (_wsReport != null && _wsReport.Errors.Count > 0)
                KawaiiStudioGUI.Banner("Last check reported: " + string.Join(" | ", _wsReport.Errors), KawaiiStudioGUI.MessageKind.Error);

            // what the last look inside the project found (older installs, duplicate shaders,
            // pink materials, stray output folders, FFMPEG in the old place)
            var scan = KawaiiStudioProjectRepair.LastScan ?? _wsReport?.Project;
            if (scan != null && !scan.Clean)
            {
                KawaiiStudioGUI.Banner(scan.Summary(),
                    scan.FixableCount > 0 ? KawaiiStudioGUI.MessageKind.Warning : KawaiiStudioGUI.MessageKind.Info);
                foreach (var f in scan.Findings.Take(8))
                    GUILayout.Label("• " + f.Path + (f.Fixable ? "" : "  (left as is)"), KawaiiStudioGUI.InfoLabelStyle);
                if (scan.Findings.Count > 8)
                    GUILayout.Label($"… and {scan.Findings.Count - 8} more (the Console has the full list)", KawaiiStudioGUI.InfoLabelStyle);
            }
            else if (scan != null)
            {
                KawaiiStudioGUI.Banner("Project scan: nothing left behind by older installs.", KawaiiStudioGUI.MessageKind.Success);
            }

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (KawaiiStudioGUI.PrimaryButton(_wsInstalled ? "Repair / Update" : "Install"))
            {
                _wsReport = KawaiiStudioWorkspace.Reinstall();
                _wsInstalled = KawaiiStudioWorkspace.IsInstalled;
                _wsVersion = KawaiiStudioWorkspace.InstalledVersion;
                Debug.Log(_wsReport.ToString());
            }
            if (KawaiiStudioGUI.SecondaryButton("Scan Project"))
            {
                var s = KawaiiStudioProjectRepair.Scan();
                Debug.Log("[Kawaii Studio] " + s);
            }
            using (new EditorGUI.DisabledScope(scan == null || scan.FixableCount == 0))
            {
                if (KawaiiStudioGUI.SecondaryButton(scan != null && scan.FixableCount > 0 ? $"Repair {scan.FixableCount}" : "Repair"))
                {
                    var rep = KawaiiStudioProjectRepair.Repair(scan);
                    KawaiiStudioProjectRepair.Scan();
                    EditorUtility.DisplayDialog("Kawaii Studio", rep.ToString(), "OK");
                }
            }
            using (new EditorGUI.DisabledScope(!_wsInstalled))
            {
                if (KawaiiStudioGUI.SecondaryButton("Open Folder"))
                    KawaiiStudioWorkspace.MenuOpenFolder();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ------------------------------------------------------------------ tools

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
