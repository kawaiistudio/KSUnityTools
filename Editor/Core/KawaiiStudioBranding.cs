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
    /// The banner is optional. Older installs shipped a 13 MB banner.png; it is no
    /// longer bundled, and <see cref="KawaiiStudioGUI.DrawBanner"/> falls back to a
    /// generated accent gradient when this returns null.
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

        /// <summary>Optional background art; null on current installs.</summary>
        public static Texture2D Banner
        {
            get
            {
                if (_banner != null) return _banner;

                // Probe once. A missing banner is the normal case now, so this must
                // not hit the AssetDatabase on every repaint.
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
