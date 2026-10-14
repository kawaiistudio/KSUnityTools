using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Where Kawaii Studio lives on disk, and where it works.
    ///
    /// Two places, deliberately kept apart:
    ///
    ///  * The PACKAGE: the code, the languages, the branding, and the SOURCE copies of
    ///    the shaders and materials. Installed by VCC under
    ///    "Packages/com.kawaiistudio.ksunitytools" (read-only for the user, invisible in
    ///    the Project window's Assets tree), or by the .unitypackage under
    ///    "Assets/Kawaii Studio" (the historical layout).
    ///
    ///  * The WORKSPACE: "Assets/Kawaii Studio". The user's side. The installed shaders and
    ///    materials (so materials can be assigned, edited, exported with an avatar), and
    ///    every folder a tool writes into: Videos, Animations, Prefabs, KS Exported,
    ///    Optimized, Protected, ThirdParty/FFMPEG. <see cref="KawaiiStudioWorkspace"/> creates
    ///    and refreshes it. Tools never read a shader from the package and never write a
    ///    file anywhere else.
    ///
    /// Every hardcoded "Assets/Kawaii Studio/..." literal used to break the moment the
    /// toolset was installed by VCC (the Video Animator could not find its own decoder
    /// shader, 3.3.0). All lookups go through here instead.
    /// </summary>
    public static class KawaiiStudioPaths
    {
        public const string WorkspaceRoot = "Assets/Kawaii Studio";
        private const string LegacyRoot = WorkspaceRoot;
        private const string VpmRoot = "Packages/com.kawaiistudio.ksunitytools";

        // Source folders inside the package. The trailing "~" makes Unity skip them: the
        // package itself imports no shader, so the only shader with a given GUID in the
        // project is the installed copy -- what every user material points at.
        public const string ShaderSourceFolder = "Shaders~";
        public const string MaterialSourceFolder = "Materials~";

        private static string _packageRoot;

        // ------------------------------------------------------------------ package side

        /// <summary>Root folder of the package install, as a Unity asset path, no trailing slash.</summary>
        public static string PackageRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(_packageRoot) && FolderExists(_packageRoot)) return _packageRoot;
                _packageRoot = ResolvePackageRoot();
                return _packageRoot;
            }
        }

        /// <summary>Kept for callers written against 3.3.0: the package root.</summary>
        public static string Root => PackageRoot;

        /// <summary>True when the code was installed from the .unitypackage, i.e. the package IS the workspace.</summary>
        public static bool IsLegacyLayout => string.Equals(PackageRoot, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

        public static string Languages => Combine(PackageRoot, "Languages");
        public static string References => Combine(PackageRoot, "References");
        public static string Editor => Combine(PackageRoot, "Editor");
        public static string Tools => Combine(Editor, "Tools");

        /// <summary>The package's shader sources ("Shaders~"; "Shaders" in the legacy layout).</summary>
        public static string ShaderSources => IsLegacyLayout ? Shaders : Combine(PackageRoot, ShaderSourceFolder);

        /// <summary>The package's material sources ("Materials~"; "Materials" in the legacy layout).</summary>
        public static string MaterialSources => IsLegacyLayout ? Materials : Combine(PackageRoot, MaterialSourceFolder);

        // ------------------------------------------------------------------ workspace side

        public static string Workspace => WorkspaceRoot;
        public static string Shaders => Combine(Workspace, "Shaders");
        public static string Materials => Combine(Workspace, "Materials");
        public static string Videos => Combine(Workspace, "Videos");
        public static string Animations => Combine(Workspace, "Animations");
        public static string Prefabs => Combine(Workspace, "Prefabs");
        public static string Models => Combine(Workspace, "Models");
        public static string Optimized => Combine(Workspace, "Optimized");
        public static string Exported => Combine(Workspace, "KS Exported");
        public static string Protected => Combine(Workspace, "Protected");
        public static string ThirdParty => Combine(Workspace, "ThirdParty");
        public static string FFmpeg => Combine(ThirdParty, "FFMPEG");

        /// <summary>Not imported by Unity (trailing "~"): where the workspace keeps the files it replaces.</summary>
        public static string Backups => Combine(Workspace, "Backups~");

        /// <summary>Forget the cached package root; call after moving the folder.</summary>
        public static void Invalidate() => _packageRoot = null;

        // ------------------------------------------------------------------ lookups

        /// <summary>
        /// A Kawaii Studio shader by file name ("KSVideoDecoder"), from the workspace. The
        /// workspace is installed first if it is not. Falls back to Shader.Find on the
        /// shader's declared name, so a project that keeps the shaders elsewhere still works.
        /// </summary>
        public static Shader Shader(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            KawaiiStudioWorkspace.EnsureInstalled();
            string path = Combine(Shaders, fileName.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".shader");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader != null) return shader;
            string declared = KawaiiStudioWorkspace.DeclaredShaderName(fileName);
            return string.IsNullOrEmpty(declared) ? null : UnityEngine.Shader.Find(declared);
        }

        /// <summary>A Kawaii Studio material by file name ("Video Screen Overlay"), from the workspace.</summary>
        public static Material Material(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            KawaiiStudioWorkspace.EnsureInstalled();
            string path = Combine(Materials, fileName.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".mat");
            return AssetDatabase.LoadAssetAtPath<Material>(path);
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

        /// <summary>A workspace output folder for a tool ("Videos/MyClip"): created on demand.</summary>
        public static string OutputFolder(string workspaceSubfolder, string name = null)
        {
            KawaiiStudioWorkspace.EnsureInstalled();
            string folder = Combine(Workspace, workspaceSubfolder);
            if (!string.IsNullOrEmpty(name)) folder = Combine(folder, SafeName(name));
            return EnsureFolder(folder);
        }

        /// <summary>A file-system-safe asset name: illegal characters replaced, never empty.</summary>
        public static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "Untitled";
            string safe = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();
            return safe.Length == 0 ? "Untitled" : safe;
        }

        // ------------------------------------------------------------------ helpers

        private static string ResolvePackageRoot()
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

        internal static bool FolderExists(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return false;
            if (AssetDatabase.IsValidFolder(assetPath)) return true;
            // a "~" folder is never an asset folder, but it is on disk
            string abs = ToAbsolute(assetPath);
            return abs != null && Directory.Exists(abs);
        }

        internal static string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b;
            return a + "/" + b;
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
                // a VCC / registry package lives in Library/PackageCache or wherever the
                // Package Manager resolved it: ask it rather than guess
                // an embedded package (what VCC installs) sits right under the project root
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
    }
}
