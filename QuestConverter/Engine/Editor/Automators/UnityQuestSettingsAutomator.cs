// <copyright file="UnityQuestSettingsAutomator.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using KawaiiStudio.QuestConverter.ViewModels;
using KawaiiStudio.QuestConverter.Views;
using UnityEditor;

namespace KawaiiStudio.QuestConverter.Automators
{
    /// <summary>
    /// Automates UnityQuestSettings.
    /// </summary>
    [InitializeOnLoad]
    internal static class UnityQuestSettingsAutomator
    {
        /// <summary>
        /// Initializes static members of the <see cref="UnityQuestSettingsAutomator"/> class.
        /// </summary>
        static UnityQuestSettingsAutomator()
        {
            EditorApplication.delayCall += DelayCall;
        }

        private static void DelayCall()
        {
            var viewModel = new UnityQuestSettingsViewModel();
            if (viewModel.ShowWindowOnLoad && !viewModel.AllSettingsValid)
            {
                UnityQuestSettingsWindow.ShowWindow();
            }
        }
    }
}
