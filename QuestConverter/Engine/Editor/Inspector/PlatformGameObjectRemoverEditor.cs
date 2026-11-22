using KawaiiStudio.QuestConverter.Components;
using KawaiiStudio.QuestConverter.Models;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Inspector
{
    /// <summary>
    /// Editor for PlatformGameObjectRemover.
    /// </summary>
    [CustomEditor(typeof(PlatformGameObjectRemover))]
    internal class PlatformGameObjectRemoverEditor : VRCQuestToolsEditorOnlyEditorBase<PlatformGameObjectRemover>
    {
        /// <inheritdoc/>
        protected override string Description => VRCQuestToolsSettings.I18nResource.PlatformGameObjectRemoverEditorDescription;

        /// <inheritdoc/>
        public override void OnInspectorGUIInternal()
        {
            var i18n = VRCQuestToolsSettings.I18nResource;

            var so = new SerializedObject(target);
            so.Update();

            var removeOnPC = so.FindProperty("removeOnPC");
            Views.EditorGUIUtility.InvertedBoolPropertyField(removeOnPC, new GUIContent(i18n.PlatformGameObjectRemoverEditorKeepOnPCLabel));

            var removeOnAndroid = so.FindProperty("removeOnAndroid");
            Views.EditorGUIUtility.InvertedBoolPropertyField(removeOnAndroid, new GUIContent(i18n.PlatformGameObjectRemoverEditorKeepOnMobileLabel));

            so.ApplyModifiedProperties();
        }
    }
}
