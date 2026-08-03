using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace KawaiiStudio.VRC
{
    public class KS_Obfuscator : EditorWindow
    {
        private GameObject avatarRoot;
        private string folderPath;
        private uint secretEncryptionKey;
        private float scrambleStrength = 5.0f;
        private const string SecKeyParam = "KS_Obfuscation_Key";

        private Dictionary<Object, Object> assetMap = new Dictionary<Object, Object>();
        private Dictionary<string, string> nameMap = new Dictionary<string, string>();

        [MenuItem("Kawaii Studio/VRC/KS Obfuscator", false, 21)]
        public static void ShowWindow()
        {
            var window = GetWindow<KS_Obfuscator>("KS Obfuscator");
            window.minSize = new Vector2(400, 550);
            window.Show();
        }

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
        }

        private void OnGUI()
        {
            // 1. Fond de fenêtre dégradé professionnel
            KawaiiStudioGUI.DrawWindowBackground(position);

            // 2. Bannière avec Logo et Branding
            KawaiiStudioGUI.DrawBanner(
                "KS OBFUSCATOR", 
                "Advanced Military Grade Anti-Rip", 
                "1.2.0", 
                KawaiiStudioBranding.Logo, 
                KawaiiStudioBranding.Banner
            );

            GUILayout.Space(10);

            // 3. Section de configuration
            KawaiiStudioGUI.DrawSection("PROTECTION SETTINGS", () =>
            {
                avatarRoot = (GameObject)EditorGUILayout.ObjectField("Avatar Root", avatarRoot, typeof(GameObject), true);
                
                GUILayout.Space(5);
                scrambleStrength = EditorGUILayout.Slider("Distortion Strength", scrambleStrength, 2f, 15f);
                
                EditorGUILayout.HelpBox("Higher distortion makes the 'ripped' mesh more unrecognizable but requires a compatible shader.", MessageType.None);
            });

            GUILayout.Space(10);

            // 4. Section Actions
            KawaiiStudioGUI.DrawSection("EXECUTION", () =>
            {
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("🛡️ GENERATE ENCRYPTED BUILD", KawaiiStudioGUI.ButtonStyle))
                {
                    if (avatarRoot == null) { EditorUtility.DisplayDialog("Error", "Please select an avatar first!", "OK"); return; }
                    ExecuteObfuscation();
                }
                GUI.backgroundColor = Color.white;
            });

            // 5. Footer avec liens Sociaux
            KawaiiStudioGUI.DrawFooter();
        }

        // --- LOGIQUE DE PROTECTION (IDENTIQUE MAIS NETTOYÉE) ---

        void ExecuteObfuscation()
        {
            assetMap.Clear();
            nameMap.Clear();
            secretEncryptionKey = (uint)Random.Range(1000000, int.MaxValue);
            
            // Strip characters that are illegal in an asset path; an avatar named
            // "Foo/Bar" used to throw or silently create a nested folder.
            string safeName = string.Join("_", avatarRoot.name.Split(Path.GetInvalidFileNameChars()));
            folderPath = "Assets/KS_Protected_" + safeName + "_" + Random.Range(100, 999);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
                // Directory.CreateDirectory alone leaves the AssetDatabase unaware of the
                // folder, so every CreateAsset below failed with "Couldn't create asset file".
                AssetDatabase.Refresh();
            }

            GameObject newAvatar = Instantiate(avatarRoot);
            newAvatar.name = "KS_PROTECTED_" + avatarRoot.name;

            try
            {
                EditorUtility.DisplayProgressBar("KS Obfuscator", "Hardening Mesh & Shaders...", 0.3f);
                
                BuildNameMap(newAvatar.transform);
                InjectVRCParams(newAvatar);

                var renderers = newAvatar.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var ren = renderers[i];
                    Material[] mats = ren.sharedMaterials;
                    for (int j = 0; j < mats.Length; j++)
                    {
                        if (mats[j] != null) mats[j] = ProcessMaterial(mats[j]);
                    }
                    ren.sharedMaterials = mats;

                    if (ren is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                        smr.sharedMesh = EncryptMesh(smr.sharedMesh);
                    else if (ren is MeshRenderer)
                    {
                        MeshFilter mf = ren.GetComponent<MeshFilter>();
                        if (mf != null && mf.sharedMesh != null) mf.sharedMesh = EncryptMesh(mf.sharedMesh);
                    }
                }

                ProcessAnimators(newAvatar);
                ApplyRenaming(newAvatar.transform);

                // Write the prefab first, then flush: SaveAssets() used to run before
                // SaveAsPrefabAsset and nothing saved afterwards.
                PrefabUtility.SaveAsPrefabAsset(newAvatar, folderPath + "/Avatar_PROTECTED.prefab", out bool saved);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // Report the real outcome. This dialog used to be shown unconditionally,
                // so a failed save still told the user "Encryption Complete!".
                if (saved)
                {
                    EditorUtility.DisplayDialog("KS SHIELD",
                        "Encryption Complete!\n\nYour protected avatar is in:\n" + folderPath, "Perfect");
                }
                else
                {
                    EditorUtility.DisplayDialog("KS SHIELD",
                        "Could not write the protected prefab to:\n" + folderPath +
                        "\n\nNothing was produced. See the Console for details.", "OK");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError("[KS OBFUSCATOR] Error: " + e.Message);
                EditorUtility.DisplayDialog("KS SHIELD",
                    "Obfuscation failed:\n" + e.Message + "\n\nSee the Console for the full error.", "OK");
            }
            finally
            {
                // Both of these used to leak: an exception left the modal progress bar up
                // (locking the editor) and left the instantiated clone in the scene.
                EditorUtility.ClearProgressBar();
                if (newAvatar != null) DestroyImmediate(newAvatar);
            }
        }

        Mesh EncryptMesh(Mesh original)
        {
            if (assetMap.ContainsKey(original)) return (Mesh)assetMap[original];
            Mesh copy = Instantiate(original);
            Vector3[] verts = copy.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                uint h = (uint)i ^ secretEncryptionKey;
                h += (h << 10); h ^= (h >> 6); h += (h << 3); h ^= (h >> 11); h += (h << 15);
                Vector3 off = new Vector3(((h & 0xFFFF) / 65535.0f) - 0.5f, (((h >> 16) & 0xFFFF) / 65535.0f) - 0.5f, (((h ^ (h >> 8)) & 0xFFFF) / 65535.0f) - 0.5f);
                verts[i] += off * scrambleStrength;
            }
            copy.vertices = verts;
            copy.UploadMeshData(true);
            AssetDatabase.CreateAsset(copy, folderPath + "/MSH_" + System.Guid.NewGuid().ToString().Substring(0, 8) + ".asset");
            assetMap[original] = copy;
            return copy;
        }

        Material ProcessMaterial(Material mat)
        {
            if (assetMap.ContainsKey(mat)) return (Material)assetMap[mat];
            Material newMat = new Material(mat);
            Shader s = mat.shader;
            string path = AssetDatabase.GetAssetPath(s);
            if (!string.IsNullOrEmpty(path) && path.EndsWith(".shader"))
            {
                string content = File.ReadAllText(path);
                string newID = "KS_S_" + System.Guid.NewGuid().ToString().Substring(0, 8);
                string decryptLogic = "\nfloat _" + SecKeyParam + ";\nvoid KS_Decrypt(inout float4 vertex, uint vid) {\n if (_" + SecKeyParam + " < 0.5) return;\n uint key = " + secretEncryptionKey + "u;\n uint h = vid ^ key; h += (h << 10); h ^= (h >> 6); h += (h << 3); h ^= (h >> 11); h += (h << 15);\n float3 off = float3(((h & 0xFFFF) / 65535.0) - 0.5, (((h >> 16) & 0xFFFF) / 65535.0) - 0.5, (((h ^ (h >> 8)) & 0xFFFF) / 65535.0) - 0.5);\n vertex.xyz -= off * " + scrambleStrength.ToString("F2") + ";\n}\n";
                content = Regex.Replace(content, @"Shader\s+""[^""]+""", $"Shader \"Hidden/KS_Obfuscator/{newID}\"");
                content = Regex.Replace(content, @"Properties\s*\{", "Properties {\n        _" + SecKeyParam + " (\"KS Shield\", Float) = 0");
                content = content.Replace("CGPROGRAM", "CGPROGRAM" + decryptLogic);
                if (!content.Contains("SV_VertexID")) content = Regex.Replace(content, @"struct\s+appdata\s*\{", "struct appdata {\n                uint vid : SV_VertexID;");
                content = Regex.Replace(content, @"(\bvert\b\s*\(appdata\s+([a-zA-Z0-9_]+)\)\s*\{)", "$1\n                KS_Decrypt($2.vertex, $2.vid);");
                string newSPath = folderPath + "/" + newID + ".shader";
                File.WriteAllText(newSPath, content);
                AssetDatabase.ImportAsset(newSPath);
                newMat.shader = AssetDatabase.LoadAssetAtPath<Shader>(newSPath);
            }
            newMat.SetFloat("_" + SecKeyParam, 1.0f);
            AssetDatabase.CreateAsset(newMat, folderPath + "/MAT_" + Random.Range(1000, 9999) + ".mat");
            assetMap[mat] = newMat;
            return newMat;
        }

        void BuildNameMap(Transform t)
        {
            foreach (Transform c in t) {
                if (c != avatarRoot.transform && c.name != "Armature" && c.name != "Hips")
                    nameMap[c.name] = "KS_NODE_" + System.Guid.NewGuid().ToString().Substring(0, 8);
                BuildNameMap(c);
            }
        }

        void ApplyRenaming(Transform t)
        {
            foreach (Transform c in t) {
                if (nameMap.ContainsKey(c.name)) c.name = nameMap[c.name];
                ApplyRenaming(c);
            }
        }

        void ProcessAnimators(GameObject root)
        {
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (!desc) return;
            for (int i = 0; i < desc.baseAnimationLayers.Length; i++) {
                if (desc.baseAnimationLayers[i].animatorController != null) {
                    string oldPath = AssetDatabase.GetAssetPath(desc.baseAnimationLayers[i].animatorController);
                    string newPath = folderPath + "/CTRL_" + i + ".controller";
                    AssetDatabase.CopyAsset(oldPath, newPath);
                    AnimatorController ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(newPath);
                    foreach (var layer in ctrl.layers) ProcessStateMachine(layer.stateMachine);
                    desc.baseAnimationLayers[i].animatorController = ctrl;
                }
            }
        }

        void ProcessStateMachine(AnimatorStateMachine sm)
        {
            foreach (var state in sm.states) {
                if (state.state.motion is AnimationClip clip) state.state.motion = ObfuscateClip(clip);
                else if (state.state.motion is BlendTree bt) ProcessBlendTree(bt);
            }
            foreach (var sub in sm.stateMachines) ProcessStateMachine(sub.stateMachine);
        }

        void ProcessBlendTree(BlendTree bt)
        {
            ChildMotion[] motions = bt.children;
            for (int i = 0; i < motions.Length; i++)
                if (motions[i].motion is AnimationClip c) motions[i].motion = ObfuscateClip(c);
            bt.children = motions;
        }

        AnimationClip ObfuscateClip(AnimationClip original)
        {
            if (assetMap.ContainsKey(original)) return (AnimationClip)assetMap[original];
            AnimationClip newClip = Instantiate(original);
            var bindings = AnimationUtility.GetCurveBindings(original);
            newClip.ClearCurves();
            foreach (var b in bindings) {
                string[] parts = b.path.Split('/');
                for (int i = 0; i < parts.Length; i++)
                    if (nameMap.ContainsKey(parts[i])) parts[i] = nameMap[parts[i]];
                newClip.SetCurve(string.Join("/", parts), b.type, b.propertyName, AnimationUtility.GetEditorCurve(original, b));
            }
            AssetDatabase.CreateAsset(newClip, folderPath + "/AN_" + System.Guid.NewGuid().ToString().Substring(0, 8) + ".anim");
            assetMap[original] = newClip;
            return newClip;
        }

        void InjectVRCParams(GameObject root)
        {
            var desc = root.GetComponent<VRCAvatarDescriptor>();
            if (!desc || !desc.expressionParameters) return;
            VRCExpressionParameters newP = Instantiate(desc.expressionParameters);
            var list = new List<VRCExpressionParameters.Parameter>(newP.parameters);
            if (!list.Exists(x => x.name == SecKeyParam)) {
                list.Add(new VRCExpressionParameters.Parameter { name = SecKeyParam, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 1f });
                newP.parameters = list.ToArray();
            }
            AssetDatabase.CreateAsset(newP, folderPath + "/VRC_Params.asset");
            desc.expressionParameters = newP;
        }
    }
}