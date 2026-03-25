using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Animations;
using System.Linq;

#if VRC_SDK_VRCSDK3
using VRC.SDK3.Dynamics.Constraint.Components;
#endif

namespace KawaiiStudio
{
    public class KawaiiUltimateConstraintTool : EditorWindow
    {
        private const string VERSION = "1.0";

        private GameObject sourceObj;
        private GameObject destObj;
        private List<Component> sourceList = new List<Component>();
        private List<Component> destList = new List<Component>();
        private Vector2 scrollLeft, scrollRight;
        private bool autoRemove = true;

        [MenuItem("Kawaii Studio/\u2728 Ultimate Constraint Tool")]
        public static void ShowWindow() => GetWindow<KawaiiUltimateConstraintTool>("Constraint Tool");

        private void OnEnable()
        {
            KawaiiStudioGUI.Initialize();
            minSize = new Vector2(500, 600);
        }

        private void OnGUI()
        {
            KawaiiStudioGUI.DrawWindowBackground(position);

            EditorGUILayout.BeginScrollView(Vector2.zero);

            KawaiiStudioGUI.DrawBanner(
                "CONSTRAINT TOOL",
                "Hierarchical Constraint Remapper",
                VERSION,
                KawaiiStudioBranding.Logo,
                KawaiiStudioBranding.Banner
            );

            GUILayout.Space(10);

            KawaiiStudioGUI.DrawSection("CONFIGURATION", () =>
            {
                sourceObj = (GameObject)EditorGUILayout.ObjectField("Source (Avatar A)", sourceObj, typeof(GameObject), true);
                destObj = (GameObject)EditorGUILayout.ObjectField("Destination (Avatar B)", destObj, typeof(GameObject), true);
            });

            KawaiiStudioGUI.DrawSection("CONSTRAINTS", () =>
            {
                EditorGUILayout.BeginHorizontal();
                DrawColumn("\ud83d\udce1 SOURCE", sourceObj, ref sourceList, ref scrollLeft);
                GUILayout.Space(5);
                DrawColumn("\ud83d\udee0\ufe0f DESTINATION", destObj, ref destList, ref scrollRight);
                EditorGUILayout.EndHorizontal();
            });

            KawaiiStudioGUI.DrawSection("ACTIONS", () =>
            {
                autoRemove = KawaiiStudioGUI.DrawToggle("Clean Destination before copy", autoRemove);
                GUILayout.Space(10);
                GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
                if (GUILayout.Button("\ud83d\udee0\ufe0f REPAIR (HIERARCHY SYNC)", KawaiiStudioGUI.ButtonStyle))
                {
                    if (sourceObj == null || destObj == null)
                        EditorUtility.DisplayDialog("Error", "Assign Source and Destination!", "OK");
                    else
                        ExecuteFullRepair();
                }
                GUI.backgroundColor = Color.white;
            });

            KawaiiStudioGUI.DrawFooter();

            EditorGUILayout.EndScrollView();
        }

    // --- LOGIQUE DE RÉPARATION PRINCIPALE ---

    private void ExecuteFullRepair()
    {
        sourceList = Scan(sourceObj);

        // 1. Nettoyage (Optionnel)
        if (autoRemove)
        {
            var toClean = Scan(destObj);
            foreach (var c in toClean) Undo.DestroyObjectImmediate(c);
        }

        int successCount = 0;
        int failCount = 0;
        int remappedBones = 0;

        // 2. Copie et Remapping
        foreach (var srcComp in sourceList)
        {
            // On cherche où placer la contrainte sur la destination (Même chemin)
            string path = AnimationUtility.CalculateTransformPath(srcComp.transform, sourceObj.transform);
            Transform targetT = destObj.transform.Find(path);

            // Fallback : Si le chemin n'existe pas, on cherche par nom
            if (targetT == null)
            {
                targetT = FindBoneRecursively(destObj.transform, srcComp.transform.name);
            }

            if (targetT != null)
            {
                // Copie du composant
                UnityEditorInternal.ComponentUtility.CopyComponent(srcComp);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(targetT.gameObject);
                
                // Récupération du nouveau composant créé
                Component newComp = targetT.GetComponents(srcComp.GetType()).Last();
                
                // LE CŒUR DU SYSTÈME : REMAPPING
                int remappedCount = RemapToLocalHierarchy(newComp, sourceObj.transform, destObj.transform);
                
                if (remappedCount > 0) remappedBones += remappedCount;
                successCount++;
            }
            else
            {
                failCount++;
                Debug.LogWarning($"❌ Impossible de trouver l'objet porteur : {srcComp.gameObject.name} (Chemin: {path})");
            }
        }

        destList = Scan(destObj);
        EditorUtility.DisplayDialog("Résultat", 
            $"✅ Contraintes copiées : {successCount}\n" +
            $"🦴 Os remappés vers Dest : {remappedBones}\n" +
            $"❌ Échecs placement : {failCount}\n\nVérifie la Console pour les détails !", "Super !");
    }

    private int RemapToLocalHierarchy(Component comp, Transform sourceRoot, Transform destRoot)
    {
        SerializedObject so = new SerializedObject(comp);
        so.Update();

        int changes = 0;

        // 1. GESTION DES SOURCES (Le tableau des cibles de la contrainte)
        SerializedProperty sourcesProp = so.FindProperty("m_Sources") ?? so.FindProperty("Sources");
        if (sourcesProp != null && sourcesProp.isArray)
        {
            for (int i = 0; i < sourcesProp.arraySize; i++)
            {
                SerializedProperty element = sourcesProp.GetArrayElementAtIndex(i);
                // Unity = sourceTransform, VRC = SourceTransform
                SerializedProperty transProp = element.FindPropertyRelative("sourceTransform") ?? element.FindPropertyRelative("SourceTransform");

                if (transProp != null && transProp.objectReferenceValue is Transform oldSource)
                {
                    // ON CHERCHE L'ÉQUIVALENT SUR L'AVATAR DE DESTINATION
                    Transform newSource = FindCorrespondingTransform(oldSource, sourceRoot, destRoot);
                    
                    if (newSource != null && newSource != oldSource)
                    {
                        transProp.objectReferenceValue = newSource;
                        changes++;
                        // Debug.Log($"[Remap Source] {oldSource.name} -> {newSource.name}");
                    }
                }
            }
        }

        // 2. GESTION DU WORLD UP (Pour Aim/LookAt)
        SerializedProperty worldUpProp = so.FindProperty("m_WorldUpObject") ?? so.FindProperty("WorldUpTransform");
        if (worldUpProp != null && worldUpProp.objectReferenceValue is Transform oldWorldUp)
        {
            Transform newWorldUp = FindCorrespondingTransform(oldWorldUp, sourceRoot, destRoot);
            if (newWorldUp != null && newWorldUp != oldWorldUp)
            {
                worldUpProp.objectReferenceValue = newWorldUp;
                changes++;
                // Debug.Log($"[Remap WorldUp] {oldWorldUp.name} -> {newWorldUp.name}");
            }
        }

        so.ApplyModifiedProperties();
        return changes;
    }

    // --- LOGIQUE INTELLIGENTE DE RECHERCHE ---
    private Transform FindCorrespondingTransform(Transform target, Transform sourceRoot, Transform destRoot)
    {
        if (target == null) return null;

        // 1. Est-ce que l'os cible fait partie de l'avatar Source ?
        if (target.IsChildOf(sourceRoot))
        {
            // MÉTHODE A : Chemin Hiérarchique Exact (Le plus précis)
            // Ex: Armature/Hips/Spine/Chest/Arm.R/Wrist.R
            string path = AnimationUtility.CalculateTransformPath(target, sourceRoot);
            Transform foundByPath = destRoot.Find(path);

            if (foundByPath != null) return foundByPath;

            // MÉTHODE B : Smart Fallback par Nom (Si la hiérarchie diffère un peu)
            // Si on ne trouve pas le chemin, on cherche juste un os qui a le même nom dans Destination
            Transform foundByName = FindBoneRecursively(destRoot, target.name);
            
            if (foundByName != null)
            {
                // Debug.LogWarning($"⚠️ Remap par nom (Chemin échoué) : {target.name}");
                return foundByName;
            }
        }

        // Si l'objet n'est pas dans l'avatar source (ex: un objet externe), on le garde tel quel
        return target; 
    }

    // Recherche récursive (Profondeur)
    private Transform FindBoneRecursively(Transform current, string nameToFind)
    {
        if (current.name == nameToFind) return current;
        foreach (Transform child in current)
        {
            Transform result = FindBoneRecursively(child, nameToFind);
            if (result != null) return result;
        }
        return null;
    }

    // --- GUI & BOILERPLATE ---

    private void DrawColumn(string title, GameObject obj, ref List<Component> list, ref Vector2 scroll)
    {
        EditorGUILayout.BeginVertical(KawaiiStudioGUI.BoxStyle, GUILayout.Width(position.width / 2 - 25), GUILayout.ExpandHeight(true));
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(title, KawaiiStudioGUI.LabelStyle);
        GUI.backgroundColor = KawaiiStudioGUI.AccentColor;
        if (GUILayout.Button("Scan", GUILayout.Width(50))) list = Scan(obj);
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        float progress = Mathf.Clamp01(list.Count / 100f);
        EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 15), progress, $"{list.Count} found");
        GUILayout.Space(5);

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        if (list.Count == 0)
        {
            EditorGUILayout.LabelField("Empty...", KawaiiStudioGUI.InfoLabelStyle);
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (c == null) continue;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                string icon = c.GetType().Name.StartsWith("VRC") ? "\ud83d\udfe3" : "\ud83d\udfe2";
                string shortName = c.gameObject.name.Length > 20 ? c.gameObject.name.Substring(0, 17) + "..." : c.gameObject.name;
                if (GUILayout.Button($"{icon} {shortName}", EditorStyles.label))
                {
                    EditorGUIUtility.PingObject(c.gameObject);
                    Selection.activeGameObject = c.gameObject;
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private List<Component> Scan(GameObject root)
    {
        if (root == null) return new List<Component>();
        var components = new List<Component>();
        components.AddRange(root.GetComponentsInChildren<ParentConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<PositionConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<RotationConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<ScaleConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<LookAtConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<AimConstraint>(true));
#if VRC_SDK_VRCSDK3
        components.AddRange(root.GetComponentsInChildren<VRCParentConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<VRCRotationConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<VRCPositionConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<VRCLookAtConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<VRCAimConstraint>(true));
        components.AddRange(root.GetComponentsInChildren<VRCScaleConstraint>(true));
#endif
        return components.OrderBy(c => c.gameObject.name).ToList();
    }
    }
}