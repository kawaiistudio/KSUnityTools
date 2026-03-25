using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    internal static class KawaiiStudioBranding
    {
        internal const string LogoPath = "Assets/Kawaii Studio/References/logo.png";
        internal const string BannerPath = "Assets/Kawaii Studio/References/banner.png";

        private static Texture2D _logo;
        private static Texture2D _banner;

        internal static Texture2D Logo
        {
            get
            {
                if (_logo == null) _logo = LoadTexture(LogoPath);
                return _logo;
            }
        }

        internal static Texture2D Banner
        {
            get
            {
                if (_banner == null) _banner = LoadTexture(BannerPath);
                return _banner;
            }
        }

        private static Texture2D LoadTexture(string assetPath)
        {
            // Works only in Editor; these scripts are under an Editor folder.
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
    }
}


