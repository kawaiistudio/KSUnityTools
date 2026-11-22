// KS Quest Converter: the Kawaii Studio shaders converted by our own code. The engine asks this hook first
// (MaterialWrapperBuilder.KsWrapperHook); a shader it does not know goes to the engine's own wrappers.
using KawaiiStudio.QuestConverter.Models.Unity;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.KS
{
    [InitializeOnLoad]
    internal static class KSShaderWrappers
    {
        static KSShaderWrappers()
        {
            MaterialWrapperBuilder.KsWrapperHook = Wrap;
        }

        internal static MaterialBase Wrap(Material material)
        {
            if (material == null || material.shader == null) return null;
            switch (material.shader.name)
            {
                case "Kawaii Studio/KSHairRealistic":
                    return new KSHairRealisticMaterial(material);
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// KS Hair Realistic: on PC most of its colour is the tinted specular reflection of a smooth, metallic surface plus a
    /// masked emission; the plain albedo is nearly black there. Its Quest texture is baked with the same formula
    /// (Hidden/KawaiiStudio/QuestConverter/KSHairRealistic) under a neutral environment.
    /// </summary>
    internal sealed class KSHairRealisticMaterial : MaterialBase
    {
        internal KSHairRealisticMaterial(Material material)
            : base(material)
        {
        }

        internal override Shader ToonLitBakeShader => Shader.Find("Hidden/KawaiiStudio/QuestConverter/KSHairRealistic");
    }
}
