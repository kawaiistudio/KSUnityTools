using KawaiiStudio.QuestConverter.Models;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Components
{
    /// <summary>
    /// PlatformTargetSettings is a component to control VQT Platform components.
    /// </summary>
    [AddComponentMenu("Kawaii Studio/Quest Converter/Platform Target Settings")]
    [HelpURL("https://kurotu.github.io/VRCQuestTools/docs/references/components/platform-target-settings?lang=auto")]
    [DisallowMultipleComponent]
    public class PlatformTargetSettings : VRCQuestToolsEditorOnly, INdmfComponent
    {
        /// <summary>
        /// Build target to control VQT Platform components.
        /// </summary>
        public BuildTarget buildTarget = BuildTarget.Auto;

        /// <summary>
        /// Serialized schema version for forward compatibility.
        /// </summary>
        [SerializeField]
#pragma warning disable CS0414
        private int serializedVersion = 1;
#pragma warning restore CS0414
    }
}
