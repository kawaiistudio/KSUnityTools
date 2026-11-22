using KawaiiStudio.QuestConverter.Components;
using KawaiiStudio.QuestConverter.Models;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Inspector
{
    /// <summary>
    /// Editor for NetworkIDAssigner.
    /// </summary>
    [CustomEditor(typeof(NetworkIDAssigner))]
    internal class NetworkIDAssignerEditor : VRCQuestToolsEditorOnlyEditorBase<NetworkIDAssigner>
    {
        /// <inheritdoc/>
        protected override string Description => VRCQuestToolsSettings.I18nResource.NetworkIDAssignerEditorDescription;

        /// <inheritdoc />
        public override void OnInspectorGUIInternal()
        {
            // Do nothing
        }
    }
}
