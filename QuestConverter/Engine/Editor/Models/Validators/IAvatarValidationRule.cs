// <copyright file="IAvatarValidationRule.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using KawaiiStudio.QuestConverter.Models.VRChat;

namespace KawaiiStudio.QuestConverter.Models.Validators
{
    /// <summary>
    /// Avatar validation rule interface.
    /// </summary>
    internal interface IAvatarValidationRule
    {
        /// <summary>
        /// Validate a avatar with this rule.
        /// </summary>
        /// <param name="avatar">Avatar to validate.</param>
        /// <returns>null for valid.</returns>
        NotificationItem Validate(VRChatAvatar avatar);
    }
}
