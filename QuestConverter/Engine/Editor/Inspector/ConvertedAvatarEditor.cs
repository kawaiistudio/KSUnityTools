using KawaiiStudio.QuestConverter.Components;
using KawaiiStudio.QuestConverter.Models;
using UnityEditor;

namespace KawaiiStudio.QuestConverter.Inspector
{
    /// <summary>
    /// Editor for ConvertedAvatar.
    /// </summary>
    [CustomEditor(typeof(ConvertedAvatar))]
    internal class ConvertedAvatarEditor : VRCQuestToolsEditorOnlyEditorBase<ConvertedAvatar>
    {
        /// <inheritdoc />
        protected override string Description => VRCQuestToolsSettings.I18nResource.ConvertedAvatarEditorMessage;

        /// <inheritdoc />
        public override void OnInspectorGUIInternal()
        {
#if KSQ_HAS_NDMF
            var i18n = VRCQuestToolsSettings.I18nResource;
            EditorGUILayout.LabelField(i18n.ConvertedAvatarEditorNDMFMessage, EditorStyles.wordWrappedLabel);
#endif
        }
    }
}
