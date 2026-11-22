using KawaiiStudio.QuestConverter.Models;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Components
{
    /// <summary>
    /// NetworkIdAssigner assigns network IDs to physbones which don't have ID.
    /// This would be useful to keep physbones to be synchronized between PC and Mobile.
    /// </summary>
    [AddComponentMenu("Kawaii Studio/Quest Converter/Network ID Assigner")]
    [HelpURL("https://kurotu.github.io/VRCQuestTools/docs/references/components/network-id-assigner?lang=auto")]
    [DisallowMultipleComponent]
    public class NetworkIDAssigner : VRCQuestToolsEditorOnly
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
