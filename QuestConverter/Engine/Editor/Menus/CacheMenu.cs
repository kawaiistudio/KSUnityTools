using KawaiiStudio.QuestConverter.Utils;
using UnityEditor;

namespace KawaiiStudio.QuestConverter.Menus
{
    /// <summary>
    /// Menu for cache.
    /// </summary>
    internal static class CacheMenu
    {
        [MenuItem(VRCQuestToolsMenus.MenuPaths.ClearTextureCache, false, (int)VRCQuestToolsMenus.MenuPriorities.ClearTextureCache)]
        private static void ClearCache()
        {
            CacheManager.Texture.Clear();
        }
    }
}
