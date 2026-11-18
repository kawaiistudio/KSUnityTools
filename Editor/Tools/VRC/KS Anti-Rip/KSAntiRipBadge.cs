using System.Collections.Generic;
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
    /// ONE GameObject under the avatar root, one mesh in two parts:
    ///   * a real 3D heart ("Kawaii Studio/Anti-Rip Heart"): pink, glowing, turning slowly, beating;
    ///   * in front of it, the panel with the words and the logo (Runtime/AntiRipBadge/KSLockBadge.png on
    ///     "Kawaii Studio/Anti-Rip Badge"), which always faces whoever looks.
    /// It is ACTIVE by default -- an avatar starts locked -- and the lock's show layer switches it off the moment the
    /// key arrives (KSAntiRipLock.Build). Someone wearing the avatar without the key is not an empty space any more:
    /// they are a sign saying who to ask.
    ///
    /// Added to the build copy only, like everything else here. Cost: one mesh, two materials, about 1,800 triangles.
    /// </summary>
    public static class KSAntiRipBadge
    {
        public const string ObjectName = "KS Anti-Rip Badge";
        public const string ShaderName = "Kawaii Studio/Anti-Rip Badge";
        public const string HeartShaderName = "Kawaii Studio/Anti-Rip Heart";
        public const string AssetFolder = "Packages/com.kawaiistudio.ksunitytools/Runtime/AntiRipBadge";
        public const string TexturePath = AssetFolder + "/KSLockBadge.png";
        public const string ShaderPath = AssetFolder + "/KSAntiRipBadge.shader";
        public const string HeartShaderPath = AssetFolder + "/KSAntiRipHeart.shader";

        // The badge in its own space, one unit wide: the heart above, the panel under it and towards the viewer.
        const float HeartY = 0.27f;          // the heart's centre
        const float HeartWidth = 0.5f;
        const float HeartHalfDepth = 0.115f;
        const float PanelY = -0.17f;         // the panel's centre
        const float PanelHeight = 0.5f;      // it is one unit wide; the picture is 2:1

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
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            Shader panelShader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (panelShader == null) panelShader = Shader.Find(ShaderName);
            Shader heartShader = AssetDatabase.LoadAssetAtPath<Shader>(HeartShaderPath);
            if (heartShader == null) heartShader = Shader.Find(HeartShaderName);
            if (texture == null || panelShader == null || heartShader == null)
            {
                if (warnings != null)
                    warnings.Add("The locked badge was left out: its " + (texture == null ? "picture" : "shader") +
                                 " is missing from KS Unity Tools. Reinstall the package to get it back.");
                return null;
            }

            var heartMaterial = new Material(heartShader) { name = assetName + "_h" };
            heartMaterial.SetFloat("_HeartY", HeartY);
            AssetDatabase.CreateAsset(heartMaterial, folder + "/" + assetName + "_h.mat");
            var panelMaterial = new Material(panelShader) { name = assetName + "_p" };
            panelMaterial.mainTexture = texture;
            AssetDatabase.CreateAsset(panelMaterial, folder + "/" + assetName + "_p.mat");

            Mesh mesh = BuildMesh();
            mesh.name = assetName;
            AssetDatabase.CreateAsset(mesh, folder + "/" + assetName + ".asset");

            var badge = new GameObject(string.IsNullOrEmpty(objectName) ? ObjectName : objectName);
            badge.transform.SetParent(root.transform, false);
            badge.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = badge.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = new[] { heartMaterial, panelMaterial };   // submesh 0 the heart, 1 the panel
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
        /// The heart (submesh 0) and the panel's quad (submesh 1).
        ///
        /// The heart is the classic heart curve, inflated like a cushion: every ring is the outline pulled towards
        /// the middle while it rises off the plane, front and back, so it is round from every side and closes on a
        /// single point at the middle of each face.
        /// </summary>
        public static Mesh BuildMesh()
        {
            const int Around = 72;     // points on the outline
            const int Rings = 7;       // from the outline to the middle of a face

            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var heart = new List<int>();

            // the outline, centred, HeartWidth wide
            var outline = new Vector2[Around];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < Around; i++)
            {
                float t = 2f * Mathf.PI * i / Around;
                float sin = Mathf.Sin(t);
                outline[i] = new Vector2(16f * sin * sin * sin,
                                         13f * Mathf.Cos(t) - 5f * Mathf.Cos(2f * t) - 2f * Mathf.Cos(3f * t) - Mathf.Cos(4f * t));
                minY = Mathf.Min(minY, outline[i].y);
                maxY = Mathf.Max(maxY, outline[i].y);
            }
            float k = HeartWidth / 32f;
            float midY = (minY + maxY) * 0.5f;
            // the point the cushion closes on: the curve's own origin, which every point of the outline sees
            Vector2 middle = new Vector2(0f, -midY) * k;
            for (int i = 0; i < Around; i++)
                outline[i] = new Vector2(outline[i].x, outline[i].y - midY) * k;

            // ring 0 is the outline itself, shared by the two faces
            for (int i = 0; i < Around; i++)
            {
                vertices.Add(new Vector3(outline[i].x, HeartY + outline[i].y, 0f));
                uv.Add(new Vector2(0.5f, 0.5f));
            }
            for (int face = 0; face < 2; face++)
            {
                float sign = face == 0 ? -1f : 1f;      // -Z is the front (towards the viewer of an unturned badge)
                int previous = 0;                        // where the previous ring starts
                for (int ring = 1; ring < Rings; ring++)
                {
                    float phi = ring / (float)Rings * Mathf.PI * 0.5f;
                    float pull = Mathf.Cos(phi), rise = Mathf.Sin(phi);
                    int start = vertices.Count;
                    for (int i = 0; i < Around; i++)
                    {
                        Vector2 p = middle + (outline[i] - middle) * pull;
                        vertices.Add(new Vector3(p.x, HeartY + p.y, sign * HeartHalfDepth * rise));
                        uv.Add(new Vector2(0.5f, 0.5f));
                    }
                    for (int i = 0; i < Around; i++)
                    {
                        int a = previous + i, b = previous + (i + 1) % Around;
                        int c = start + i, d = start + (i + 1) % Around;
                        if (face == 0) { heart.Add(a); heart.Add(b); heart.Add(c); heart.Add(b); heart.Add(d); heart.Add(c); }
                        else { heart.Add(a); heart.Add(c); heart.Add(b); heart.Add(b); heart.Add(c); heart.Add(d); }
                    }
                    previous = start;
                }
                // the middle of the face
                int tip = vertices.Count;
                vertices.Add(new Vector3(middle.x, HeartY + middle.y, sign * HeartHalfDepth));
                uv.Add(new Vector2(0.5f, 0.5f));
                for (int i = 0; i < Around; i++)
                {
                    int a = previous + i, b = previous + (i + 1) % Around;
                    if (face == 0) { heart.Add(a); heart.Add(b); heart.Add(tip); }
                    else { heart.Add(a); heart.Add(tip); heart.Add(b); }
                }
            }

            // the panel: one quad, turned to the viewer by its shader
            int q = vertices.Count;
            float half = PanelHeight * 0.5f;
            vertices.Add(new Vector3(-0.5f, PanelY - half, 0f)); uv.Add(new Vector2(0f, 0f));
            vertices.Add(new Vector3(0.5f, PanelY - half, 0f)); uv.Add(new Vector2(1f, 0f));
            vertices.Add(new Vector3(-0.5f, PanelY + half, 0f)); uv.Add(new Vector2(0f, 1f));
            vertices.Add(new Vector3(0.5f, PanelY + half, 0f)); uv.Add(new Vector2(1f, 1f));
            var panel = new[] { q, q + 2, q + 1, q + 2, q + 3, q + 1 };

            var mesh = new Mesh { name = "KS_Badge" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(heart, 0);
            mesh.SetTriangles(panel, 1);
            mesh.RecalculateNormals();
            // A box, not the mesh's own thin shape: the heart turns and the panel is drawn turned to the viewer and
            // pushed towards them, and a tight box would let either be culled at the edge of the view.
            mesh.bounds = new Bounds(new Vector3(0f, 0.05f, 0f), new Vector3(1.7f, 1.3f, 1.7f));
            return mesh;
        }
    }
}
