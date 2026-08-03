using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Resolves where Kawaii Studio actually lives on disk.
    ///
    /// The toolset ships two ways: as a .unitypackage that lands in
    /// "Assets/Kawaii Studio", and as a UPM/VPM package that lands in
    /// "Packages/com.kawaiistudio.ksunitytools". Every hardcoded
    /// "Assets/Kawaii Studio/..." literal breaks in the second case, so all
    /// folder lookups go through here instead.
    /// </summary>
    public static class KawaiiStudioPaths
    {
        private const string LegacyRoot = "Assets/Kawaii Studio";
        private const string PackageRoot = "Packages/com.kawaiistudio.ksunitytools";

        private static string _root;

        /// <summary>Root folder of the install, as a Unity asset path, no trailing slash.</summary>
        public static string Root
        {
            get
            {
                if (!string.IsNullOrEmpty(_root) && FolderExists(_root)) return _root;
                _root = ResolveRoot();
                return _root;
            }
        }

        public static string Languages => Combine(Root, "Languages");
        public static string References => Combine(Root, "References");
        public static string Shaders => Combine(Root, "Shaders");
        public static string Materials => Combine(Root, "Materials");
        public static string Editor => Combine(Root, "Editor");
        public static string Tools => Combine(Editor, "Tools");

        /// <summary>Forget the cached root; call after moving the folder.</summary>
        public static void Invalidate() => _root = null;

        private static string ResolveRoot()
        {
            // Locate this very script and walk up out of Editor/Core. This works no
            // matter what the folder is called or where the user dragged it.
            foreach (string guid in AssetDatabase.FindAssets("KawaiiStudioPaths t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path)) continue;
                if (!path.EndsWith("/KawaiiStudioPaths.cs", StringComparison.OrdinalIgnoreCase)) continue;

                string core = GetParent(path);       // <root>/Editor/Core
                string editor = GetParent(core);     // <root>/Editor
                string root = GetParent(editor);     // <root>
                if (!string.IsNullOrEmpty(root)) return root;
            }

            if (FolderExists(LegacyRoot)) return LegacyRoot;
            if (FolderExists(PackageRoot)) return PackageRoot;

            // Last resort: keep the historical literal so behaviour never gets worse
            // than it was before this class existed.
            return LegacyRoot;
        }

        private static string GetParent(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            int slash = assetPath.LastIndexOf('/');
            return slash <= 0 ? null : assetPath.Substring(0, slash);
        }

        private static bool FolderExists(string assetPath)
        {
            return !string.IsNullOrEmpty(assetPath) && AssetDatabase.IsValidFolder(assetPath);
        }

        private static string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return a + "/" + b;
        }

        /// <summary>Absolute filesystem path for a Unity asset path, or null.</summary>
        public static string ToAbsolute(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            if (Path.IsPathRooted(assetPath)) return assetPath;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return null;

            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
