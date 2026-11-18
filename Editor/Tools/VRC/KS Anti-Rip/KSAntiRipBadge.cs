using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// THE LOCKED BADGE: the one thing shown of a protected avatar while it is locked.
    ///
    /// Owner, 2026-11-17: "dans VRChat si on prend un avatar mais qu'on a pas la clé on est invisible ... tu peux
    /// ajouter un seul GameObject de plus avec le logo qui se désactive quand l'avatar s'unlock ... ANTI-RIP PROTECTION
    /// POWERED BY KAWAII STUDIO ... ASK ME IF YOU WANT TO USE THE AVATAR ... un coeur avec un shader rose bloom", then
    /// "ça peut pas être de quoi en 3D pour le coeur ... et le texte avec le logo est par-dessus".
    ///
    /// THE POSTER ONLY (3.13.0). Owner, 2026-11-18, wearing the 3.12 badge in game: "remove le coeur en 3D actually,
    /// on mettra juste l'affiche". The 3D heart stood at eye level in front of the wearer and filled their own view.
    ///
    /// ONE GameObject under the avatar root, ONE quad: the poster with the words and the logo
    /// (Runtime/AntiRipBadge/KSLockBadge.png on "Kawaii Studio/Anti-Rip Badge"), which always faces whoever looks and
    /// reads the right way in a mirror too. It is ACTIVE by default -- an avatar starts locked -- and the lock's show
    /// layer switches it off the moment the key arrives (KSAntiRipLock.Build). Someone wearing the avatar without the
    /// key is not an empty space: they are a sign saying who to ask.
    ///
    /// Added to the build copy only, like everything else here. Cost: one quad, one material, two triangles.
    /// </summary>
    public static class KSAntiRipBadge
    {
        public const string ObjectName = "KS Anti-Rip Badge";
        public const string ShaderName = "Kawaii Studio/Anti-Rip Badge";
        public const string AssetFolder = "Packages/com.kawaiistudio.ksunitytools/Runtime/AntiRipBadge";
        public const string TexturePath = AssetFolder + "/KSLockBadge.png";
        public const string ShaderPath = AssetFolder + "/KSAntiRipBadge.shader";

        // The poster in its own space: one unit wide, centred on the object, the picture is 2:1.
        const float PanelHeight = 0.5f;

        /// <summary>
        /// Adds the badge under <paramref name="root"/> and returns its path from the root (what the show layer
        /// animates), or null when its picture or a shader is not in the project -- said in
        /// <paramref name="warnings"/>, never an error: the protection does not depend on it.
        /// </summary>
        public static string Add(GameObject root, VRCAvatarDescriptor descriptor, string folder, string objectName,
                                 IList<string> warnings, string assetName = "KS_Badge")
        {
            // With names obfuscated the caller passes a random one: the badge's own assets do not announce themselves
            // any more than the rest (its shaders and its picture do -- they are the signature).
            if (string.IsNullOrEmpty(assetName)) assetName = "KS_Badge";
            Texture2D texture = Picture(folder, assetName, warnings, out bool pictureUnusable);
            if (pictureUnusable) return null;
            Shader panelShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (panelShader == null) panelShader = Shader.Find(ShaderName);
            if (texture == null || panelShader == null)
            {
                if (warnings != null)
                    warnings.Add("The locked badge was left out: its " + (texture == null ? "picture" : "shader") +
                                 " is missing from KS Unity Tools. Reinstall the package to get it back.");
                return null;
            }

            var panelMaterial = new Material(panelShader) { name = assetName + "_p" };
            panelMaterial.mainTexture = texture;
            // No push towards the viewer: it only kept the panel in front of the old 3D heart, and for the wearer it
            // brought the poster closer to their own eyes.
            panelMaterial.SetFloat("_Push", 0f);
            AssetDatabase.CreateAsset(panelMaterial, folder + "/" + assetName + "_p.mat");

            Mesh mesh = BuildMesh();
            mesh.name = assetName;
            AssetDatabase.CreateAsset(mesh, folder + "/" + assetName + ".asset");

            var badge = new GameObject(string.IsNullOrEmpty(objectName) ? ObjectName : objectName);
            badge.transform.SetParent(root.transform, false);
            badge.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = badge.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = panelMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // In front of the chest and sized to the avatar: readable from across a room, and the wearer sees it by
            // looking down. The eye height is the one size the descriptor always knows.
            float eye = descriptor != null && descriptor.ViewPosition.y > 0.2f ? descriptor.ViewPosition.y : 1.5f;
            float size = Mathf.Clamp(eye * 0.62f, 0.4f, 1.4f);
            Transform r = root.transform;
            badge.transform.position = r.position + r.rotation * new Vector3(0f, eye * 0.62f, eye * 0.3f);
            // Its face towards the avatar's front: what a viewer on the fallback shaders sees, which do not turn it.
            badge.transform.rotation = r.rotation * Quaternion.Euler(0f, 180f, 0f);
            Vector3 s = r.lossyScale;
            badge.transform.localScale = new Vector3(size / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                                                     size / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                                                     size / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
            badge.SetActive(true);

            return AnimationUtility.CalculateTransformPath(badge.transform, root.transform);
        }

        /// <summary>
        /// The panel's picture, as THIS build's own copy in <paramref name="folder"/>, imported the way the VRChat SDK
        /// accepts it.
        ///
        /// WHY A COPY (3.12.2; owner, 2026-11-18, upload stopped on "Avatar validation failed"). The SDK validates the
        /// processed avatar after every preprocess callback, and refuses any texture whose importer has mip maps
        /// without "Streaming Mip Maps" (VRCSdkControlPanelAvatarBuilder.VerifyAvatarMipMapStreaming). The package's
        /// own KSLockBadge.png carries no .meta of ours -- the repository does not track them -- so every project
        /// imports it with Unity's defaults: mip maps on, streaming off. 3.12.0 put it straight on the badge, and every
        /// protected upload with the badge on stopped there; Editor.log said "This avatar has mipmapped textures
        /// without 'Streaming Mip Maps' enabled." (the 22:03 upload, before the badge, went through). The SDK's own
        /// "Auto Fix" never shows for it either: the scene avatar has no badge, only the build copy does.
        ///
        /// A copy imported here does not depend on whatever .meta a project made, and never touches the package. It
        /// lives in the build folder and goes with it once the bundle is built.
        ///
        /// <paramref name="unusable"/> is true when the picture exists but could not be made acceptable: the badge is
        /// then left out with a warning, because a badge must never stop an upload the protection itself allows.
        /// </summary>
        static Texture2D Picture(string folder, string assetName, IList<string> warnings, out bool unusable)
        {
            unusable = false;
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath) == null) return null;   // the caller says it is missing

            string copy = folder + "/" + assetName + "_t.png";
            if (!AssetDatabase.CopyAsset(TexturePath, copy))
            {
                // A package Unity will not copy from (read-only cache): the bytes, imported fresh.
                try
                {
                    File.Copy(Path.GetFullPath(TexturePath), Path.GetFullPath(copy), true);
                    AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                }
                catch (System.Exception e)
                {
                    Unusable(warnings, "it could not be copied into the build (" + e.Message + ")");
                    unusable = true;
                    return null;
                }
            }

            var importer = AssetImporter.GetAtPath(copy) as TextureImporter;
            if (importer == null)
            {
                Unusable(warnings, "Unity did not import it as a texture");
                unusable = true;
                return null;
            }
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;            // it is read from across a room: without mips it shimmers
            importer.streamingMipmaps = true;         // ...and the VRChat SDK refuses mips without streaming
            importer.streamingMipmapsPriority = 0;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();

            // The SDK's own test, on what was actually saved.
            importer = AssetImporter.GetAtPath(copy) as TextureImporter;
            if (importer == null || (importer.mipmapEnabled && !importer.streamingMipmaps))
            {
                Unusable(warnings, "its import settings could not be saved");
                unusable = true;
                return null;
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(copy);
        }

        static void Unusable(IList<string> warnings, string why)
        {
            if (warnings != null)
                warnings.Add("The locked badge was left out: its picture " + why + ". The avatar is protected all the same.");
        }

        /// <summary>The poster: one quad, one unit wide and centred, turned to the viewer by its shader.</summary>
        public static Mesh BuildMesh()
        {
            float half = PanelHeight * 0.5f;
            var mesh = new Mesh { name = "KS_Badge" };
            mesh.SetVertices(new List<Vector3>
            {
                new Vector3(-0.5f, -half, 0f), new Vector3(0.5f, -half, 0f),
                new Vector3(-0.5f, half, 0f), new Vector3(0.5f, half, 0f),
            });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) });
            mesh.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            mesh.RecalculateNormals();
            // A box, not the quad's own flat shape: the shader turns it to whoever looks, and a flat box would let it
            // be culled at the edge of the view once it has turned.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1.2f, 0.7f, 1.2f));
            return mesh;
        }
    }
}
