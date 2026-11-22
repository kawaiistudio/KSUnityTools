// KS Quest Converter: the one entry point that makes a Quest (Android) version of a PC avatar. The window, the
// Kawaii Studio app (through the Link) and the tests all call MakeQuestVersion, so they all get the same result.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using KawaiiStudio.QuestConverter.Components;
using KawaiiStudio.QuestConverter.Models;
using KawaiiStudio.QuestConverter.Models.VRChat;
using KawaiiStudio.QuestConverter.Utils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace KawaiiStudio.QuestConverter.KS
{
    internal static class KSQuestConverter
    {
        internal const string OutputRoot = "Assets/Kawaii Studio/Quest Converter";
        internal const string Title = "PC to Quest";

        internal sealed class Result
        {
            internal VRCAvatarDescriptor Quest;
            internal double Seconds;
            internal string Error;
            internal string OutputFolder;
        }

        private static string T(string key) => KawaiiStudioLocalization.T(key);

        /// <summary>
        /// Duplicates the avatar (prefab links kept), converts the copy for Quest into
        /// Assets/Kawaii Studio/Quest Converter/&lt;avatar&gt;, hides the PC original (one Undo step brings everything back).
        /// The PC avatar itself is never modified, only hidden.
        /// </summary>
        internal static Result MakeQuestVersion(VRCAvatarDescriptor avatar)
        {
            var result = new Result();
            if (avatar == null) { result.Error = T("No avatar."); return result; }
            GameObject src = avatar.gameObject;
            if (EditorUtility.IsPersistent(src)) { result.Error = T("Pick the avatar in the scene, not a prefab file."); return result; }
            if (src.GetComponent<ConvertedAvatar>() != null) { result.Error = T("This avatar already is a Quest version."); return result; }

            var sw = Stopwatch.StartNew();
            GameObject copy = null;
            bool wasActive = src.activeSelf;
            try
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName(Title);

                // a copy that keeps the prefab links, like the Hierarchy's Duplicate
                Selection.activeGameObject = src;
                Unsupported.DuplicateGameObjectsUsingPasteboard();
                copy = Selection.activeGameObject;
                if (copy == null || copy == src) throw new InvalidOperationException("Unity did not duplicate the avatar.");
                copy.name = src.name + " (Quest)";
                Undo.RegisterCreatedObjectUndo(copy, Title);
                foreach (var old in copy.GetComponents<AvatarConverterSettings>()) UnityEngine.Object.DestroyImmediate(old);
                copy.AddComponent<AvatarConverterSettings>();

                var engineAvatar = new VRChatAvatar(copy.GetComponent<VRCAvatarDescriptor>());
                var progress = new AvatarConverter.ProgressCallback
                {
                    onTextureProgress = (total, index, m, _) => Bar(T("Quest materials"), index, total),
                    onAnimationClipProgress = (total, index, c, _) => Bar(T("Animations"), index, total),
                    onRuntimeAnimatorProgress = (total, index, c, _) => Bar(T("Animators"), index, total),
                };
                result.OutputFolder = OutputFolder(src.name);
                VRCQuestTools.AvatarConverter.PrepareConvertForQuestInPlace(engineAvatar);
                VRCQuestTools.AvatarConverter.ConvertForQuestInPlace(engineAvatar, VRCQuestTools.ComponentRemover, true, result.OutputFolder, progress);

                Undo.RecordObject(src, Title);
                src.SetActive(false); // the VRChat SDK panel then offers the Quest version; the PC one is only hidden
                copy.SetActive(true);
                Undo.CollapseUndoOperations(group);
                EditorSceneManager.MarkSceneDirty(copy.scene);
                AssetDatabase.SaveAssets();
                Selection.activeGameObject = copy;

                result.Quest = copy.GetComponent<VRCAvatarDescriptor>();
                result.Seconds = sw.Elapsed.TotalSeconds;
            }
            catch (Exception e)
            {
                result.Error = Explain(e);
                UnityEngine.Debug.LogException(e);
                if (copy != null && copy != src) UnityEngine.Object.DestroyImmediate(copy);
                if (src != null) src.SetActive(wasActive);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            return result;
        }

        private static void Bar(string what, int index, int total) =>
            EditorUtility.DisplayProgressBar(Title, $"{what} {index + 1}/{total}", total > 0 ? (float)index / total : 0f);

        internal static string Explain(Exception e)
        {
            var root = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
            if (root is PackageCompatibilityException pce) return pce.LocalizedMessage;
            if (root is MaterialConversionException mce)
            {
                var mat = mce.SourceObject;
                string where = mat != null ? $"{mat.name} ({(mat.shader != null ? mat.shader.name : "no shader")})" : "?";
                var inner = mce.InnerException is PackageCompatibilityException ip ? ip.LocalizedMessage : mce.InnerException?.Message;
                return T("A material could not be converted:") + " " + where + (string.IsNullOrEmpty(inner) ? "" : "\n" + inner);
            }
            return root.GetType().Name + ": " + root.Message;
        }

        internal static string OutputFolder(string avatarName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(avatarName.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim().TrimEnd('.', ' ');
            if (clean.Length == 0) clean = "Avatar";
            return OutputRoot + "/" + clean;
        }
    }
}
