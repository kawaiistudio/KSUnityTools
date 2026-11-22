using UnityEngine;
using VRC.SDKBase;

namespace KawaiiStudio.QuestConverter.Components
{
    /// <summary>
    /// Base class for editor only components.
    /// </summary>
    [Icon("Packages/com.kawaiistudio.ksunitytools/References/logo.png")]
    public abstract class VRCQuestToolsEditorOnly : MonoBehaviour, IEditorOnly
    {
    }
}
