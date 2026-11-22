using UnityEngine;
using VRC.SDKBase;

namespace KawaiiStudio.QuestConverter.Components
{
    /// <summary>
    /// Component to mark converted avatar.
    /// </summary>
    [AddComponentMenu("Kawaii Studio/Quest Converter/Converted Avatar")]
    [DisallowMultipleComponent]
    [HelpURL("https://kurotu.github.io/VRCQuestTools/docs/references/components/converted-avatar?lang=auto")]
    public class ConvertedAvatar : VRCQuestToolsEditorOnly
    {
        /// <summary>
        /// Serialized schema version for forward compatibility.
        /// </summary>
        [SerializeField]
#pragma warning disable CS0414
        private int serializedVersion = 1;
#pragma warning restore CS0414
    }
}
