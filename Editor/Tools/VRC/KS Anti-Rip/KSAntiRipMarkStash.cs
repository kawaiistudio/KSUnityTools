using System.Collections.Generic;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// 3.18.1: notes, at the very start of a build, that the avatar being built is marked for protection.
    ///
    /// THE BUG IT FIXES (found 2026-11-20 on a project without Modular Avatar). The VRChat SDK's own
    /// RemoveAvatarEditorOnly (VRCSDKBase-Editor, callbackOrder -1024) destroys every IEditorOnly component of the build
    /// copy, and <see cref="KSAntiRipMark"/> is one. <see cref="KSAntiRipBuildHook"/> runs far later (int.MaxValue - 1024),
    /// so it found no mark and treated the avatar as not protected: the build went out WITHOUT its protection and said
    /// nothing. It only ever worked where NDMF (Modular Avatar) was installed, because NDMF moves that strip to the very
    /// end of the build.
    ///
    /// This runs before everything else, keeps the mark's values for that build copy, and the hook puts the mark back
    /// when it is gone (<see cref="Restore"/>). With NDMF the mark is still there and the note is simply dropped.
    /// </summary>
    public sealed class KSAntiRipMarkStash : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue + 64;

        static readonly Dictionary<int, string> Noted = new Dictionary<int, string>();

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return true;
            var mark = avatarGameObject.GetComponent<KSAntiRipMark>();
            int id = avatarGameObject.GetInstanceID();
            lock (Noted)
            {
                if (mark != null && mark.enabled_) Noted[id] = JsonUtility.ToJson(mark);
                else Noted.Remove(id);
            }
            return true;
        }

        /// <summary>The mark the VRChat SDK stripped from this build copy, put back with the values it had; null when
        /// the avatar was not marked at the start of the build.</summary>
        public static KSAntiRipMark Restore(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return null;
            string json;
            int id = avatarGameObject.GetInstanceID();
            lock (Noted)
            {
                if (!Noted.TryGetValue(id, out json)) return null;
                Noted.Remove(id);
            }
            var mark = avatarGameObject.AddComponent<KSAntiRipMark>();
            JsonUtility.FromJsonOverwrite(json, mark);
            return mark;
        }

        /// <summary>The mark was still there (NDMF defers the strip): the note is not needed.</summary>
        public static void Forget(GameObject avatarGameObject)
        {
            if (avatarGameObject == null) return;
            lock (Noted) Noted.Remove(avatarGameObject.GetInstanceID());
        }
    }
}
