using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Editor.Api;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Adds the blacklist lines to a protected avatar's VRChat description once VRChat accepted the upload.
    ///
    /// Owner, 2026-11-18: avatar recovery services skip an avatar whose description carries their phrase -- "ARC
    /// BLACKLIST", and "vrcdp_blacklist" for VRCDataPack -- so every protected upload gets them, with "Protected by
    /// Kawaii Studio", after the creator's own text. A line already there is not added again.
    ///
    /// The SDK writes the panel's description before the bundle, and the panel does not know these lines: each
    /// protected upload adds them back.
    /// </summary>
    public static class KSAntiRipDescription
    {
        public static readonly string[] Marks = { "Protected by Kawaii Studio", "ARC BLACKLIST", "vrcdp_blacklist" };

        /// <summary>The description with every missing mark on its own line at the end; unchanged when none is missing.</summary>
        public static string WithMarks(string description)
        {
            string text = description ?? "";
            var missing = new List<string>();
            foreach (string mark in Marks)
                if (text.IndexOf(mark, StringComparison.Ordinal) < 0) missing.Add(mark);
            if (missing.Count == 0) return text;
            string head = text.TrimEnd();
            return (head.Length > 0 ? head + "\n\n" : "") + string.Join("\n", missing);
        }

        /// <summary>Reads the avatar back from VRChat and writes the marks into its description. Never throws.</summary>
        public static async void Apply(string avatarId)
        {
            if (string.IsNullOrEmpty(avatarId)) return;
            try
            {
                VRCAvatar avatar = await VRCApi.GetAvatar(avatarId, forceRefresh: true);
                string before = avatar.Description ?? "";
                string after = WithMarks(before);
                if (after == before)
                {
                    KSAntiRipClient.Report("the avatar description already carries the blacklist lines");
                    return;
                }
                avatar.Description = after;
                await VRCApi.UpdateAvatarInfo(avatarId, avatar);
                KSAntiRipClient.Report("added to the avatar description: " + string.Join(", ", Marks));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KS ANTI-RIP] could not add the blacklist lines to the avatar description: " + e.Message);
                KSAntiRipClient.Report("could not add the blacklist lines to the avatar description: " + e.Message);
            }
        }
    }
}
