// <copyright file="MaterialBase.cs" company="kurotu">
// Copyright (c) kurotu.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.
// </copyright>

using System.Collections.Generic;
using System.Linq;
using KawaiiStudio.QuestConverter.Utils;
using UnityEngine;

namespace KawaiiStudio.QuestConverter.Models.Unity
{
    /// <summary>
    /// Abstract class for material wrapper.
    /// </summary>
    internal abstract class MaterialBase
    {
        /// <summary>
        /// Internal material.
        /// </summary>
        internal readonly Material Material;

        /// <summary>
        /// Initializes a new instance of the <see cref="MaterialBase"/> class.
        /// </summary>
        /// <param name="material">Material.</param>
        internal MaterialBase(Material material)
        {
            Material = material;
        }

        /// <summary>
        /// Gets shader to bake Toon Lit texture.
        /// </summary>
        internal abstract Shader ToonLitBakeShader { get; }

        /// <summary>
        /// Gets the main texture scale. Override to extract UV tiling from shader-specific properties.
        /// </summary>
        internal virtual Vector2 MainTextureScale => Material.mainTextureScale;

        /// <summary>
        /// Gets the main texture offset. Override to extract UV tiling from shader-specific properties.
        /// </summary>
        internal virtual Vector2 MainTextureOffset => Material.mainTextureOffset;

        /// <summary>
        /// Convert internal material to Toon Lit.
        /// </summary>
        /// <returns>Converted material.</returns>
        internal Material ConvertToToonLit()
        {
            var newShader = Shader.Find("VRChat/Mobile/Toon Lit");
            var hasMainTexProp = Material.GetTexturePropertyNames().Contains("_MainTex");
            var newMat = new Material(newShader)
            {
                doubleSidedGI = Material.doubleSidedGI,
                enableInstancing = true, // https://docs.vrchat.com/docs/quest-content-optimization#avatars-and-worlds
                globalIlluminationFlags = Material.globalIlluminationFlags,
                hideFlags = Material.hideFlags,
                name = $"{Material.name}_{newShader.name.Split('/').Last()}",
                renderQueue = Material.renderQueue,
                shader = newShader,
                shaderKeywords = null,
            };
            if (hasMainTexProp)
            {
                newMat.mainTexture = Material.mainTexture;
                newMat.mainTextureOffset = MainTextureOffset;
                newMat.mainTextureScale = MainTextureScale;
            }
            return newMat;
        }

        /// <summary>
        /// Gets the platform override settings for Toon Lit texture.
        /// </summary>
        /// <returns>Platform override settings, or null if none.</returns>
        internal virtual (int MaxTextureSize, TextureFormat Format)? GetToonLitPlatformOverride()
        {
            // Just use the platform override from main texture
            return TextureUtility.GetBestPlatformOverrideSettings(Material.mainTexture);
        }

        /// <summary>
        /// Generates an image for Toon Lit main texture.
        /// </summary>
        /// <param name="settings">Setting object.</param>
        /// <param name="completion">Completion action.</param>
        /// <returns>Request to wait.</returns>
        private static readonly string[] KsColorNames = { "_MainColor", "_BaseColor", "_Tint", "_TintColor", "_AlbedoColor", "_MainTint", "_DiffuseColor" };
        private static readonly string[] KsTextureNames = { "_BaseMap", "_BaseColorMap", "_MainTexture", "_AlbedoMap", "_Albedo", "_AlbedoTex", "_BaseTexture", "_DiffuseMap", "_Diffuse", "_DiffuseTex", "_ColorMap" };

        // KS: the albedo of an unknown shader that keeps it under another name than _MainTex
        private Texture KsMainTexture()
        {
            if (Material.mainTexture != null || GetType() != typeof(StandardMaterial)) return Material.mainTexture;
            foreach (var n in KsTextureNames) if (Material.HasProperty(n) && Material.GetTexture(n) is Texture2D t) return t;
            return null;
        }

        // KS: the colour (and albedo) of an unknown shader that keeps them under other names, for the Toon Lit bake
        private void KsBakeColor(Material baker, Texture bakeMainTexture)
        {
            if (!Material.HasProperty("_MainTex") || Material.GetTexture("_MainTex") == null)
            {
                if (bakeMainTexture != null && bakeMainTexture != Texture2D.whiteTexture) baker.SetTexture("_MainTex", bakeMainTexture);
            }
            if (Material.HasProperty("_Color")) return;
            foreach (var n in KsColorNames)
            {
                if (!Material.HasProperty(n)) continue;
                Color c;
                try { c = Material.GetColor(n); } catch { continue; }
                if (c.a <= 0.001f) c.a = 1f; // an opaque shader that leaves the alpha of its colour at 0
                baker.SetColor("_Color", c);
                return;
            }
        }

        // KS: what the unknown shader really emits, for the Toon Lit bake (vqt_standard adds _EmissionMap x _EmissionColor)
        private void KsBakeEmission(Material baker, CompositeDisposable disposables)
        {
            if (!baker.IsKeywordEnabled("_EMISSION"))
            {
                // a shader with no _EMISSION keyword at all but an emission map + colour emits all the time
                // (measured: RaivoVFX/Standard V2, the green Undead Scythe)
                bool keywordShader = Material.shader != null && Material.shader.keywordSpace.keywordNames.Contains("_EMISSION");
                if (!keywordShader && Material.HasProperty("_EmissionMap") && Material.GetTexture("_EmissionMap") != null
                    && Material.HasProperty("_EmissionColor") && Material.GetColor("_EmissionColor").maxColorComponent > 0.01f)
                    baker.EnableKeyword("_EMISSION");
                return;
            }
            if (Material.HasProperty("_EmissionMap") && Material.GetTexture("_EmissionMap") != null) return;
            bool off = Material.HasProperty("_UseEmission") && Material.GetFloat("_UseEmission") < 0.5f;
            bool maskOff = Material.HasProperty("_UseEmissionMask") && Material.GetFloat("_UseEmissionMask") < 0.5f;
            string maskName = off || maskOff ? null : Material.GetTexturePropertyNames().FirstOrDefault(n => n != "_EmissionMap"
                && n.IndexOf("emissi", System.StringComparison.OrdinalIgnoreCase) >= 0 && Material.GetTexture(n) is Texture2D t && !TextureUtility.IsNormalMapAsset(t));
            if (off) { baker.DisableKeyword("_EMISSION"); return; }
            if (maskOff) return; // the shader itself emits on the whole surface
            if (maskName == null) { baker.DisableKeyword("_EMISSION"); return; }
            var src = Material.GetTexture(maskName);
            var mask = TextureUtility.LoadUncompressedTexture(src);
            if (mask != src) disposables.Add(DisposableObject.New(mask));
            baker.SetTexture("_EmissionMap", mask);
            baker.SetTextureScale("_EmissionMap", Material.GetTextureScale(maskName));
            baker.SetTextureOffset("_EmissionMap", Material.GetTextureOffset(maskName));
        }

        internal virtual AsyncCallbackRequest GenerateToonLitImage(IToonLitConvertSettings settings, System.Action<Texture2D> completion)
        {
            var mainTexture = KsMainTexture() ?? Texture2D.whiteTexture;
            var bakeMainTexture = mainTexture;

            using (var disposables = new CompositeDisposable())
            using (var baker = DisposableObject.New(Object.Instantiate(Material)))
            {
                baker.Object.parent = null;
                baker.Object.shader = ToonLitBakeShader;
                baker.Object.SetFloat("_VQT_MainTexBrightness", settings.MainTextureBrightness);
                baker.Object.SetFloat("_VQT_GenerateShadow", settings.GenerateShadowFromNormalMap ? 1 : 0);

                // Reset _MainTex UV tiling on the baker so the bake produces the raw texture appearance
                // (no embedded tiling). UV tiling is applied at runtime via mainTextureScale/Offset on
                // the output Toon Lit material set by ConvertToToonLit(). Embedding tiling here and also
                // setting mainTextureScale would cause double-tiling at runtime.
                baker.Object.SetTextureScale("_MainTex", Vector2.one);
                baker.Object.SetTextureOffset("_MainTex", Vector2.zero);

                // Collect textures for platform override analysis
                var texturesForOverride = new List<Texture>();
                foreach (var name in Material.GetTexturePropertyNames())
                {
                    var t = Material.GetTexture(name);
                    if (t == null)
                    {
                        continue;
                    }
                    if (t is Cubemap)
                    {
                        continue;
                    }
                    if (TextureUtility.IsNormalMapAsset(t))
                    {
                        continue;
                    }
                    texturesForOverride.Add(t);
                    var tex = TextureUtility.LoadUncompressedTexture(t);
                    // The loader can return borrowed generated textures and runtime buffers.
                    if (tex != t)
                    {
                        disposables.Add(DisposableObject.New(tex));
                    }
                    baker.Object.SetTexture(name, tex);
                    if (t == mainTexture)
                    {
                        bakeMainTexture = tex;
                    }
                }

                // KS: masked emission on unknown shaders (see .github/quest_converter_sync.py)

                if (GetType() == typeof(StandardMaterial)) KsBakeEmission(baker.Object, disposables);

                if (GetType() == typeof(StandardMaterial)) KsBakeColor(baker.Object, bakeMainTexture);


                // Check platform override settings from source textures used in baking
                var platformOverride = TextureUtility.GetBestPlatformOverrideSettings(texturesForOverride.ToArray());
                var maxTextureSize = platformOverride?.MaxTextureSize ?? (int)settings.MaxTextureSize;

                var width = mainTexture.width;
                var height = mainTexture.height;
                if (maxTextureSize > 0)
                {
                    width = System.Math.Min(maxTextureSize, width);
                    height = System.Math.Min(maxTextureSize, height);
                }

                return TextureUtility.BakeTexture(bakeMainTexture, true, width, height, true, baker.Object, completion);
            }
        }
    }
}
