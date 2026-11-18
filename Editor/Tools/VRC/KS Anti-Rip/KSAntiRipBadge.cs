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
        // 3.14: the poster only a viewer who blocks the wearer's shaders sees (KSAntiRipFallbackPoster.shader)
        public const string FallbackObjectName = "KS Anti-Rip Fallback Poster";
        public const string FallbackShaderName = "Kawaii Studio/Anti-Rip Fallback Poster";
        public const string FallbackShaderPath = AssetFolder + "/KSAntiRipFallbackPoster.shader";

        /// <summary>One copy of the picture per build folder: the badge and the fallback poster share it.</summary>
        static readonly Dictionary<string, Texture2D> PictureOf = new Dictionary<string, Texture2D>(System.StringComparer.Ordinal);

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

            // IN FRONT OF THE FACE, ON THE HEAD (3.13.1; owner, 2026-11-18: "mets-la devant mon visage, in head,
            // directement devant la vue ... le message sera passé directement ... évidemment ma caméra doit voir le
            // message correctement"). Whoever wears the avatar without its key reads "ask me if you want to use this
            // avatar" right in front of their eyes, and everyone else reads it where the face would be. Sized to the
            // avatar from its eye height, the one size the descriptor always knows.
            Transform r = root.transform;
            Vector3 view = descriptor != null && descriptor.ViewPosition.y > 0.2f ? descriptor.ViewPosition : new Vector3(0f, 1.5f, 0.1f);
            float eyeHeight = view.y * Mathf.Max(0.0001f, Mathf.Abs(r.lossyScale.y));
            float width = Mathf.Clamp(eyeHeight * 0.2f, 0.12f, 0.6f);        // 30 cm on an avatar 1.5 m to the eyes
            float distance = Mathf.Clamp(eyeHeight * 0.21f, 0.12f, 0.6f);    // and 31 cm in front of them
            badge.transform.position = r.TransformPoint(view) + r.forward * distance;
            // Its face towards the avatar's front: what a viewer on the fallback shaders sees, which do not turn it.
            badge.transform.rotation = r.rotation * Quaternion.Euler(0f, 180f, 0f);
            // On the head bone it follows the head, so it stays in front of the wearer's view; VRChat shrinks the head
            // to nothing in first person, so a VRC Head Chop keeps this one object at full size for the wearer.
            // Without a humanoid head, or a VRChat SDK without Head Chop, it stays on the root at the same place.
            Transform head = HeadOf(root);
            if (head != null && KeepInFirstPerson(badge, root)) badge.transform.SetParent(head, true);
            Vector3 s = badge.transform.parent.lossyScale;
            badge.transform.localScale = new Vector3(width / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                                                     width / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                                                     width / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
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
            if (PictureOf.TryGetValue(folder, out Texture2D made) && made != null) return made;
            Texture2D fresh = MakePicture(folder, assetName, warnings, out unusable);
            if (fresh != null) PictureOf[folder] = fresh;
            return fresh;
        }

        static Texture2D MakePicture(string folder, string assetName, IList<string> warnings, out bool unusable)
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

        /// <summary>
        /// 3.14: the poster for a viewer who blocks the wearer's shaders, when meshes are closed by the avatar's own
        /// shader. Its own shader draws nothing, so with shaders shown nobody sees it; VRChat's unlit cut-out fallback
        /// draws the picture for whoever blocks them. On the chest, facing the avatar's front (the fallback does not turn
        /// it to the viewer), double-sided. Enabled in the avatar: while the avatar is locked the lock's show layer turns
        /// it off like every other renderer, and the locked badge (whose own fallback draws the same picture) speaks.
        /// Returns its path from the root, or null with a warning when its picture or shader is missing.
        /// </summary>
        public static string AddFallbackPoster(GameObject root, VRCAvatarDescriptor descriptor, string folder, string objectName,
                                               IList<string> warnings, string assetName = "KS_Fallback")
        {
            if (string.IsNullOrEmpty(assetName)) assetName = "KS_Fallback";
            Texture2D texture = Picture(folder, assetName, warnings, out bool unusable);
            if (unusable) return null;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(FallbackShaderPath);
            if (shader == null) shader = Shader.Find(FallbackShaderName);
            if (texture == null || shader == null)
            {
                warnings?.Add("The poster for viewers who block shaders was left out: its " + (texture == null ? "picture" : "shader") +
                              " is missing from KS Unity Tools. Reinstall the package to get it back.");
                return null;
            }

            var material = new Material(shader) { name = assetName + "_f" };
            material.mainTexture = texture;
            if (material.HasProperty("_Cutoff")) material.SetFloat("_Cutoff", 0.35f);
            AssetDatabase.CreateAsset(material, folder + "/" + assetName + "_f.mat");

            Mesh mesh = BuildMesh();
            mesh.name = assetName + "_f";
            AssetDatabase.CreateAsset(mesh, folder + "/" + assetName + "_f.asset");

            var poster = new GameObject(string.IsNullOrEmpty(objectName) ? FallbackObjectName : objectName);
            poster.transform.SetParent(root.transform, false);
            poster.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = poster.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            // Sized and placed from the eye height, like the badge: about half of it wide, at chest height, just in front.
            Transform r = root.transform;
            Vector3 view = descriptor != null && descriptor.ViewPosition.y > 0.2f ? descriptor.ViewPosition : new Vector3(0f, 1.5f, 0.1f);
            float eyeHeight = view.y * Mathf.Max(0.0001f, Mathf.Abs(r.lossyScale.y));
            float width = Mathf.Clamp(eyeHeight * 0.55f, 0.3f, 1.4f);
            float forward = Mathf.Clamp(eyeHeight * 0.12f, 0.08f, 0.3f);
            poster.transform.position = r.TransformPoint(new Vector3(0f, view.y * 0.72f, view.z)) + r.forward * forward;
            poster.transform.rotation = r.rotation * Quaternion.Euler(0f, 180f, 0f);
            Transform chest = ChestOf(root);
            if (chest != null) poster.transform.SetParent(chest, true);
            Vector3 s = poster.transform.parent.lossyScale;
            poster.transform.localScale = new Vector3(width / Mathf.Max(0.0001f, Mathf.Abs(s.x)),
                                                      width / Mathf.Max(0.0001f, Mathf.Abs(s.y)),
                                                      width / Mathf.Max(0.0001f, Mathf.Abs(s.z)));
            poster.SetActive(true);
            return AnimationUtility.CalculateTransformPath(poster.transform, root.transform);
        }

        /// <summary>The chest (or spine, or hips) bone of a humanoid avatar, or null.</summary>
        static Transform ChestOf(GameObject root)
        {
            try
            {
                var animator = root.GetComponent<Animator>();
                if (animator == null || !animator.isHuman) return null;
                return animator.GetBoneTransform(HumanBodyBones.Chest) ?? animator.GetBoneTransform(HumanBodyBones.Spine) ??
                       animator.GetBoneTransform(HumanBodyBones.Hips);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The avatar's head bone, or null when it has no humanoid Animator.</summary>
        static Transform HeadOf(GameObject root)
        {
            try
            {
                var animator = root.GetComponent<Animator>();
                return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Keeps the poster at full size in first person: VRChat shrinks the head bone to nothing for the wearer, and
        /// everything under it with it. The way VRCFury's "Show in first person" does it: a VRC Head Chop bone at scale 1.
        /// On its own component when the avatar has room for one more, else added to one of the avatar's own; false
        /// when neither is possible (the poster then stays on the root).
        /// </summary>
        static bool KeepInFirstPerson(GameObject badge, GameObject root)
        {
#if KS_HAS_HEAD_CHOP
            var bone = new VRCHeadChop.HeadChopBone
            {
                transform = badge.transform,
                scaleFactor = 1f,
                applyCondition = VRCHeadChop.HeadChopBone.ApplyCondition.AlwaysApply,
            };
            VRCHeadChop[] existing = root.GetComponentsInChildren<VRCHeadChop>(true);
            VRCHeadChop chop = existing.Length < VRCHeadChop.MaxComponentCount
                ? badge.AddComponent<VRCHeadChop>()
                : System.Linq.Enumerable.FirstOrDefault(existing, c => c.targetBones == null || c.targetBones.Length < 32);
            if (chop == null) return false;
            var bones = new List<VRCHeadChop.HeadChopBone>(chop.targetBones ?? new VRCHeadChop.HeadChopBone[0]) { bone };
            chop.targetBones = bones.ToArray();
            return true;
#else
            return false;
#endif
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
