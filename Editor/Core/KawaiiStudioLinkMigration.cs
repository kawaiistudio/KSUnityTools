using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// 3.6.0: the Kawaii Studio Link left KS Unity Tools' Editor/Link for its own package (com.kawaiistudio.link,
    /// "Assets/Kawaii Studio/Link" when installed from the .unitypackage). Importing the 3.6.0 .unitypackage over 3.5.0
    /// adds the new Link but cannot delete the old scripts, and two Links would fight over the same pairing and the same
    /// menus. Once the new Link is in the project, this deletes the 3.5.0 copy (these three scripts only, in KS Unity Tools'
    /// own Editor/Link). VCC replaces the whole package folder on update, so there it finds nothing to do.
    /// </summary>
    [InitializeOnLoad]
    internal static class KawaiiStudioLinkMigration
    {
        private static readonly string[] OldScripts = { "KawaiiStudioLink.cs", "KawaiiStudioLinkWindow.cs", "KawaiiStudioTestBuild.cs" };

        static KawaiiStudioLinkMigration()
        {
            EditorApplication.delayCall += Run;
        }

        private static void Run()
        {
            try
            {
                string oldFolder = KawaiiStudioPaths.Editor + "/Link";
                if (!KawaiiStudioPaths.FolderExists(oldFolder)) return;
                // only when the NEW Link is there (its own assembly), or this would remove the project's only Link
                if (Type.GetType("KawaiiStudio.KawaiiStudioLinkVersion, KawaiiStudio.Link.Editor", false) == null) return;
                int removed = 0;
                foreach (string file in OldScripts)
                {
                    string path = oldFolder + "/" + file;
                    string abs = KawaiiStudioPaths.ToAbsolute(path);
                    if (abs == null || !File.Exists(abs)) continue;
                    if (AssetDatabase.DeleteAsset(path)) removed++;
                }
                string folderAbs = KawaiiStudioPaths.ToAbsolute(oldFolder);
                if (folderAbs != null && Directory.Exists(folderAbs) && !Directory.EnumerateFileSystemEntries(folderAbs).Any(e => !e.EndsWith(".meta")))
                    AssetDatabase.DeleteAsset(oldFolder);
                if (removed > 0)
                    Debug.Log("[KS Unity Tools] Removed the 3.5.0 copy of the Kawaii Studio Link (" + removed + " script(s) in " + oldFolder +
                              "): the Link is its own package now.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[KS Unity Tools] Kawaii Studio Link migration: " + e.Message);
            }
        }
    }
}
