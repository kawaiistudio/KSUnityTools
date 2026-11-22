// <copyright file="VRCQuestTools.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using System.IO;
using KawaiiStudio.QuestConverter.Models.Unity;
using KawaiiStudio.QuestConverter.Models.VRChat;
using KawaiiStudio.QuestConverter.Services;
using KawaiiStudio.QuestConverter.Utils;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.QuestConverter
{
    /// <summary>
    /// VRCQuestTools configuration.
    /// </summary>
    [InitializeOnLoad]
    public static class VRCQuestTools
    {
        /// <summary>
        /// Tool name.
        /// </summary>
        public const string Name = "KS Quest Converter";

        /// <summary>
        /// VRCQuestTools version (semver).
        /// </summary>
        public const string Version = "3.0.3-ks.1";

        /// <summary>
        /// GitHub repository (username/reponame).
        /// </summary>
        internal const string GitHubRepository = "kawaiistudio/KSUnityTools";

        /// <summary>
        /// Booth URL.
        /// </summary>
        internal const string BoothURL = "https://github.com/kawaiistudio/KSUnityTools";

        /// <summary>
        /// ComponentRemover object to use in application.
        /// </summary>
        internal static Models.ComponentRemover ComponentRemover = new Models.ComponentRemover();

        /// <summary>
        /// AvatarConverter object to use in application.
        /// </summary>
        internal static AvatarConverter AvatarConverter = new AvatarConverter(new MaterialWrapperBuilder());

        /// <summary>
        /// VPM service instance.
        /// </summary>
        internal static VPMService VPM = new VPMService();

        /// <summary>
        /// VPM repository URL.
        /// </summary>
        internal static string VPMRepositoryURL = "https://kawaiistudio.github.io/KSUnityTools/index.json";

        /// <summary>
        /// Documentation URL.
        /// </summary>
        internal static string DocsURL = "https://github.com/kawaiistudio/KSUnityTools";

        /// <summary>
        /// package name.
        /// </summary>
        internal static string PackageName = "com.kawaiistudio.ksunitytools";

        private const string PackageJsonGUID = "19e83855fadfa16d0615df5587a61028";

        static VRCQuestTools()
        {
            PrintProjectInfo();
        }

        /// <summary>
        /// Gets root folder in Assets.
        /// </summary>
        internal static string AssetRoot => Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(PackageJsonGUID));

        /// <summary>
        /// Gets a value indicating whether VRCQuestTools is imported as a package.
        /// </summary>
        internal static bool IsImportedAsPackage => AssetRoot.StartsWith("Packages");

        /// <summary>
        /// Export as .unitypackage for release.
        /// </summary>
        /// <param name="filename">File to export.</param>
        public static void ExportUnityPackage(string filename)
        {
            AssetDatabase.ExportPackage(AssetRoot, filename, ExportPackageOptions.Recurse);
        }

        private static void Export()
        {
            ExportUnityPackage("VRCQuestTools.unitypackage");
        }

        private static void PrintProjectInfo()
        {
#if VRC_SDK_VRCSDK2
            Logger.LogError($"VRCSDK2 is no longer supported. (VRC_SDK_VRCSDK2 is defined)");
#endif
        }
    }
}
