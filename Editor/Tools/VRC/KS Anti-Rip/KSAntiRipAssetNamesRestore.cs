using System;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// When the VRChat SDK has finished building, the imported assets get their real names back.
    ///
    /// A protected build ships every texture, mesh and sound under a random name. Materials are renamed on COPIES, so
    /// they need nothing undone, but textures, imported meshes and sounds come from an importer and are renamed IN
    /// MEMORY for the build -- copying hundreds of MB to rename them would be absurd. This gives those their names
    /// back the moment the bundle is packed.
    ///
    /// A SHELL, like the other SDK-facing types: the interface has to be compiled into this package so the SDK finds
    /// it at editor load (see <see cref="KSAntiRipModuleLoader"/>), while the renaming and its undo live in the module
    /// the app delivers. With no module loaded nothing was ever renamed, so there is nothing to undo.
    /// </summary>
    public sealed class KSAntiRipAssetNamesRestore : IVRCSDKPostprocessAvatarCallback
    {
        public int callbackOrder => 0;

        public void OnPostprocessAvatar()
        {
            if (!KSAntiRipModuleLoader.Loaded) return;
            try
            {
                KSAntiRipModuleLoader.RestoreAssetNames();
            }
            catch (Exception e)
            {
                // Never fail a finished upload over this. The names are only changed in memory, so the next domain
                // reload reads them back off disk anyway.
                Debug.LogWarning("[KS ANTI-RIP] could not give the renamed assets their names back: " + e.Message);
            }
        }
    }
}
