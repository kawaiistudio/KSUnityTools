using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    [Serializable]
    public class KSTranslationEntry
    {
        public string key;
        public string value;
    }

    [Serializable]
    public class KSTranslationFile
    {
        public List<KSTranslationEntry> entries;
    }

    /// <summary>
    /// One shared translation table for the whole toolset.
    ///
    /// Every tool used to carry its own [Serializable] entry/file pair, its own
    /// Dictionary, its own LoadLanguage() and its own hardcoded
    /// "Assets/Kawaii Studio/Languages" literal. They all read the same seven JSON
    /// files, so they now share one cache that loads once per language.
    /// </summary>
    public static class KawaiiStudioLocalization
    {
        public const string PrefsLanguage = "KawaiiStudio.Language";

        /// <summary>Language codes shipped in the Languages folder.</summary>
        public static readonly string[] AvailableLanguages = { "en", "fr", "es", "de", "ja", "ru", "zh" };

        public static readonly string[] LanguageDisplayNames =
        {
            "English", "Français", "Español", "Deutsch", "日本語", "Русский", "中文"
        };

        private static Dictionary<string, string> _translations;
        private static string _loadedLanguage;

        public static string CurrentLanguage
        {
            get => EditorPrefs.GetString(PrefsLanguage, "en");
            set
            {
                if (string.IsNullOrEmpty(value) || value == CurrentLanguage) return;
                EditorPrefs.SetString(PrefsLanguage, value);
                Reload();
            }
        }

        /// <summary>Drop the cache so the next lookup reloads from disk.</summary>
        public static void Reload()
        {
            _translations = null;
            _loadedLanguage = null;
        }

        /// <summary>Translate a key; returns the key itself when untranslated.</summary>
        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            EnsureLoaded();
            return _translations.TryGetValue(key, out string value) ? value : key;
        }

        private static void EnsureLoaded()
        {
            string language = CurrentLanguage;
            if (_translations != null && _loadedLanguage == language) return;

            _translations = new Dictionary<string, string>();
            _loadedLanguage = language;

            if (!LoadInto(_translations, language) && language != "en")
            {
                // A partial or missing file must not blank the entire UI.
                LoadInto(_translations, "en");
            }
        }

        private static bool LoadInto(Dictionary<string, string> target, string language)
        {
            string assetPath = $"{KawaiiStudioPaths.Languages}/{language}.json";
            string absolute = KawaiiStudioPaths.ToAbsolute(assetPath);
            if (string.IsNullOrEmpty(absolute) || !File.Exists(absolute)) return false;

            try
            {
                KSTranslationFile file = JsonUtility.FromJson<KSTranslationFile>(File.ReadAllText(absolute));
                if (file?.entries == null) return false;

                foreach (KSTranslationEntry entry in file.entries)
                {
                    if (entry == null || string.IsNullOrEmpty(entry.key) || string.IsNullOrEmpty(entry.value))
                        continue;
                    target[entry.key] = entry.value;
                }
                return target.Count > 0;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Kawaii Studio] Could not read {assetPath}: {e.Message}");
                return false;
            }
        }
    }
}
