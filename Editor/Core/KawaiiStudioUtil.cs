using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Helpers that used to be copy-pasted into nearly every Kawaii Studio tool
    /// (FormatBytes, MakeTex, asset-path to file-size). One implementation now.
    /// </summary>
    public static class KawaiiStudioUtil
    {
        private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB" };

        /// <summary>Human readable byte count, e.g. "12.4 MB".</summary>
        public static string FormatBytes(long bytes)
        {
            bool negative = bytes < 0;
            double len = negative ? -bytes : bytes;
            int order = 0;
            while (len >= 1024 && order < ByteUnits.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{(negative ? "-" : string.Empty)}{len:0.##} {ByteUnits[order]}";
        }

        // Solid-colour textures are requested constantly by the tools' GUI code. Cache
        // them by colour: creating one per OnGUI pass leaked thousands of textures.
        private static readonly Dictionary<Color, Texture2D> SolidCache = new Dictionary<Color, Texture2D>();

        /// <summary>
        /// A 1x1 solid colour texture, cached and flagged HideAndDontSave so Unity
        /// does not report it as leaked on every assembly reload.
        /// </summary>
        public static Texture2D SolidTexture(Color color)
        {
            if (SolidCache.TryGetValue(color, out Texture2D cached) && cached != null)
                return cached;

            Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            tex.SetPixel(0, 0, color);
            tex.Apply(false, false);

            SolidCache[color] = tex;
            return tex;
        }

        /// <summary>Backwards-compatible alias for the old per-tool MakeTex helper.</summary>
        public static Texture2D MakeTex(int width, int height, Color color)
        {
            // Size never mattered: every caller used a flat colour. The cached 1x1
            // stretches identically and costs nothing.
            return SolidTexture(color);
        }

        /// <summary>File size for a Unity asset path; 0 when the file is missing.</summary>
        public static long GetFileSize(string assetPath)
        {
            string absolute = KawaiiStudioPaths.ToAbsolute(assetPath);
            if (string.IsNullOrEmpty(absolute) || !File.Exists(absolute)) return 0;
            return new FileInfo(absolute).Length;
        }

        /// <summary>Clamped 0..1 ratio that never divides by zero.</summary>
        public static float SafeRatio(long part, long whole)
        {
            if (whole <= 0) return 0f;
            return Mathf.Clamp01((float)part / whole);
        }
    }
}
