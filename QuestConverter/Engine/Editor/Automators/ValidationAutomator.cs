// <copyright file="ValidationAutomator.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using KawaiiStudio.QuestConverter.Models;
using KawaiiStudio.QuestConverter.Models.Validators;
using KawaiiStudio.QuestConverter.Models.VRChat;
using KawaiiStudio.QuestConverter.Utils;
using KawaiiStudio.QuestConverter.Views;
using UnityEditor;
using UnityEngine.SceneManagement;

namespace KawaiiStudio.QuestConverter.Automators
{
    /// <summary>
    /// Automator for validation.
    /// </summary>
    [InitializeOnLoad]
    internal static class ValidationAutomator
    {
        static ValidationAutomator()
        {
            EditorApplication.playModeStateChanged += PlayModeStateChanged;
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                PlayModeStateChanged(PlayModeStateChange.EnteredEditMode);
            }
        }

        private static void PlayModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingEditMode:
                    EditorApplication.hierarchyChanged -= Update;
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    EditorApplication.delayCall += Update;
                    EditorApplication.hierarchyChanged += Update;
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.ExitingPlayMode:
                    break;
                default:
                    throw new System.NotImplementedException($"Case {state} is not implemented.");
            }
        }

        private static void Update()
        {
            if (!VRCQuestToolsSettings.IsValidationAutomatorEnabled)
            {
                return;
            }

            var avatars = VRCSDKUtility.GetAvatarsFromLoadedScenes();
            foreach (var avatar in avatars)
            {
                foreach (var rule in AvatarValidationRules.Rules)
                {
                    var result = rule.Validate(new VRChatAvatar(avatar));
                    var key = $"{rule.GetType().FullName}-{ObjectIdentity.GetId(avatar.gameObject)}";
                    if (result != null)
                    {
                        NotificationWindow.instance.RegisterNotification(key, result);
                    }
                    else
                    {
                        NotificationWindow.instance.RemoveNotification(key);
                    }
                }
            }
        }
    }
}
