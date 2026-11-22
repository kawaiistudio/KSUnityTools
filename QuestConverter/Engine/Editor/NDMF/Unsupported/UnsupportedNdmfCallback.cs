#pragma warning disable SA1300 // Elements should should begin with an uppercase letter

using KawaiiStudio.QuestConverter.Models;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace KawaiiStudio.QuestConverter.Ndmf
{
    /// <summary>
    /// Callback object for unsupported NDMF.
    /// </summary>
    internal class UnsupportedNdmfCallback : IVRCSDKPreprocessAvatarCallback
    {
#if KSQ_NDMF_LEGACY
        private const string RequiredNdmfVersion = "1.5.0";
#endif
#if KSQ_NDMF_BREAKING
        private const string BreakingNdmfVersion = "2.0.0";
#endif

        /// <summary>
        /// Gets execution order to process before NDMF.
        /// </summary>
        public int callbackOrder => int.MinValue;

        /// <summary>
        /// Execute before build an avatar.
        /// </summary>
        /// <param name="avatarGameObject">Avatar to be built.</param>
        /// <returns>true.</returns>
        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var i18n = VRCQuestToolsSettings.I18nResource;
#if KSQ_NDMF_LEGACY
            var message = i18n.LegacyPackageExceptionMessage("NDMF", RequiredNdmfVersion);
#elif KSQ_NDMF_BREAKING
            var message = i18n.BreakingPackageExceptionMessage("NDMF", BreakingNdmfVersion);
#endif
            Logger.LogError(message);
            EditorUtility.DisplayDialog(VRCQuestTools.Name, message, "OK");
            return false;
        }
    }
}
