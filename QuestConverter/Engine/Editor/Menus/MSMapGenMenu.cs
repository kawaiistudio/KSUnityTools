// <copyright file="MSMapGenMenu.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using KawaiiStudio.QuestConverter.Views;
using UnityEditor;

namespace KawaiiStudio.QuestConverter.Menus
{
    /// <summary>
    /// Defines MSMapGen menu.
    /// </summary>
    internal static class MSMapGenMenu
    {
        [MenuItem(VRCQuestToolsMenus.MenuPaths.MSMapGenerator, false, (int)VRCQuestToolsMenus.MenuPriorities.MSMapGenerator)]
        private static void InitOnMenu()
        {
            MSMapGenWindow.ShowWindow();
        }
    }
}
