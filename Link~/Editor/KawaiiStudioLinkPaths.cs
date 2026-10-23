using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Where the Link package lives and where it writes. The same layout KS Unity Tools uses: the package under
    /// "Packages/com.kawaiistudio.link" (VCC) or "Assets/Kawaii Studio/Link" (the .unitypackage), and the files it
    /// imports under the workspace "Assets/Kawaii Studio/KS Exported" (kept apart from the package, so a package update
    /// never touches them). A trimmed copy of KS Unity Tools' KawaiiStudioPaths: the Link must not need KS Unity Tools.
    /// </summary>
    internal static class KawaiiStudioLinkPaths
    {
        public const string WorkspaceRoot = "Assets/Kawaii Studio";
        private const string VpmRoot = "Packages/com.kawaiistudio.link";
        private const string LegacyRoot = "Assets/Kawaii Studio/Link";
        private static string _packageRoot;
        private static Texture2D _logo;

        public static string Workspace => WorkspaceRoot;
        public static string Exported => Combine(Workspace, "KS Exported");

        /// <summary>Not imported by Unity (trailing "~"): where the workspace keeps the files it replaces.</summary>
        public static string Backups => Combine(Workspace, "Backups~");

        /// <summary>Root folder of the Link package, as a Unity asset path, no trailing slash.</summary>
        public static string PackageRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_packageRoot) && FolderExists(_packageRoot)) return _packageRoot;
                _packageRoot = ResolvePackageRoot();
                return _packageRoot;
            }
        }

        /// <summary>The Kawaii Studio logo shipped with the package (null when it cannot be loaded).</summary>
        public static Texture2D Logo
        {
            get
            {
                if (_logo == null) _logo = AssetDatabase.LoadAssetAtPath<Texture2D>(Combine(PackageRoot, "References/logo.png"));
                return _logo;
            }
        }

        /// <summary>A workspace output folder ("KS Exported"): created on demand.</summary>
        public static string OutputFolder(string workspaceSubfolder, string name = null)
        {
            string folder = Combine(Workspace, workspaceSubfolder);
            if (!string.IsNullOrEmpty(name)) folder = Combine(folder, SafeName(name));
            return EnsureFolder(folder);
        }

        /// <summary>
        /// Make sure a folder under Assets/ exists, creating every missing level through the
        /// AssetDatabase (so Unity knows about it at once). Returns the folder path.
        /// </summary>
        public static string EnsureFolder(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return assetPath;
            string norm = assetPath.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(norm)) return norm;
            string[] parts = norm.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets") throw new ArgumentException("not an Assets/ path: " + assetPath);
            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i])) continue;
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    // the folder may exist on disk without Unity having seen it yet
                    string abs = ToAbsolute(next);
                    if (abs != null && Directory.Exists(abs)) AssetDatabase.ImportAsset(next);
                    if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
            return current;
        }

        /// <summary>A file-system-safe asset name: illegal characters replaced, never empty.</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";
            string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
            return safe.Length == 0 ? "Untitled" : safe;
        }

        /// <summary>Absolute filesystem path for a Unity asset path (Assets/ or Packages/), or null.</summary>
        public static string ToAbsolute(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath)) return null;
            if (Path.IsPathRooted(assetPath)) return assetPath;

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrEmpty(projectRoot)) return null;

            if (assetPath.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                // an embedded package (what VCC installs) sits right under the project root; otherwise ask the Package Manager
                string full = Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
                if (Directory.Exists(full) || File.Exists(full)) return full;
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string rel = assetPath.Substring(info.assetPath.Length).TrimStart('/');
                    return Path.Combine(info.resolvedPath, rel.Replace('/', Path.DirectorySeparatorChar));
                }
            }

            return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolvePackageRoot()
        {
            // Locate this very script and walk up out of Editor/. Works whatever the folder is called.
            foreach (string guid in AssetDatabase.FindAssets("KawaiiStudioLinkPaths t:MonoScript"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.EndsWith("/KawaiiStudioLinkPaths.cs", StringComparison.OrdinalIgnoreCase)) continue;
                string editor = GetParent(path);
                string root = GetParent(editor);
                if (!string.IsNullOrEmpty(root)) return root;
            }
            if (FolderExists(VpmRoot)) return VpmRoot;
            if (FolderExists(LegacyRoot)) return LegacyRoot;
            return VpmRoot;
        }

        private static string GetParent(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            int slash = assetPath.LastIndexOf('/');
            return slash <= 0 ? null : assetPath.Substring(0, slash);
        }

        private static bool FolderExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (AssetDatabase.IsValidFolder(assetPath)) return true;
            string abs = ToAbsolute(assetPath);
            return abs != null && Directory.Exists(abs);
        }

        private static string Combine(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "/" + b;
    }

    /// <summary>The Link package's version: must match Link~/package.json (the release checks it).</summary>
    internal static class KawaiiStudioLinkVersion
    {
        public const string Current = "1.0.0";
    }
}
