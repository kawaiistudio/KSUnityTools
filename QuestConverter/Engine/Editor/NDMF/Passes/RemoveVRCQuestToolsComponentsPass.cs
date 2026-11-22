using KawaiiStudio.QuestConverter.Components;
using nadena.dev.ndmf;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Ndmf
{
    /// <summary>
    /// Remove VRCQuestTools components in NDMF.
    /// </summary>
    internal class RemoveVRCQuestToolsComponentsPass : Pass<RemoveVRCQuestToolsComponentsPass>
    {
        /// <inheritdoc/>
        public override string DisplayName => "Remove KS Quest Converter components";

        /// <summary>
        /// Runs this pass directly for EditMode tests, bypassing the NDMF pass pipeline.
        /// </summary>
        /// <param name="context">BuildContext.</param>
        internal void RunForTest(BuildContext context)
        {
            Execute(context);
        }

        /// <inheritdoc/>
        protected override void Execute(BuildContext context)
        {
            var components = context.AvatarRootObject.GetComponentsInChildren<VRCQuestToolsEditorOnly>(true);
            foreach (var component in components)
            {
                Object.DestroyImmediate(component);
            }
        }
    }
}
