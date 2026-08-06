using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Shared branding artwork.
    ///
    /// Paths resolve through <see cref="KawaiiStudioPaths"/> so the toolset works
    /// whether it was installed into "Assets/Kawaii Studio" from the .unitypackage
    /// or into "Packages/..." by VCC.
    ///
    /// Public rather than internal: the VRChat-dependent tools may compile into a
    /// separate assembly, and internal would not reach across it.
    ///
    /// The banner ships in every install (both the .unitypackage and the VCC zip),
    /// resized to 1024px so it costs under a megabyte. <see cref="Banner"/> still
    /// tolerates it being absent -- <see cref="KawaiiStudioGUI.DrawBanner"/> falls back
    /// to a generated accent gradient -- so a stripped install degrades instead of
    /// throwing.
    /// </summary>
    public static class KawaiiStudioBranding
    {
        public static string LogoPath => KawaiiStudioPaths.References + "/logo.png";
        public static string BannerPath => KawaiiStudioPaths.References + "/banner.png";

        private static Texture2D _logo;
        private static Texture2D _banner;
        private static bool _bannerProbed;

        public static Texture2D Logo
        {
            get
            {
                if (_logo == null) _logo = LoadTexture(LogoPath);
                return _logo;
            }
        }

        /// <summary>Header background art. Present in normal installs; may be null if
        /// the file was stripped, in which case the header uses its gradient fallback.</summary>
        public static Texture2D Banner
        {
            get
            {
                if (_banner != null) return _banner;

                // Probe once so a stripped install doesn't hit the AssetDatabase on
                // every repaint looking for a file that isn't there.
                if (_bannerProbed) return null;
                _bannerProbed = true;
                _banner = LoadTexture(BannerPath);
                return _banner;
            }
        }

        /// <summary>Drop cached textures, e.g. after the folder was moved.</summary>
        public static void Invalidate()
        {
            _logo = null;
            _banner = null;
            _bannerProbed = false;
            KawaiiStudioPaths.Invalidate();
        }

        private static Texture2D LoadTexture(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}
