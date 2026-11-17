using UnityEngine;
using VRC.SDKBase;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// "Protect this avatar when it is uploaded." The Kawaii Studio app adds this component to an avatar (through
    /// Kawaii Studio Link) when you turn protection on for it, and removes it when you turn it off — so KS Anti-Rip
    /// is driven entirely from the app, never from a Unity window (owner, 2026-11-16: "tout contrôlable via
    /// l'application, pas Unity").
    ///
    /// It carries the settings and does nothing on its own. At BUILD time KSAntiRipBuildHook sees it, protects the
    /// avatar VRChat is uploading, and strips the component. Nothing is written back to your scene, and no copy is
    /// made — the protection happens on the throwaway build avatar, like DexProtect's non-destructive mode.
    ///
    /// It implements <see cref="IEditorOnly"/>, so even if the hook does not run the VRChat SDK strips it before
    /// the bundle: this marker never ships.
    /// </summary>
    [AddComponentMenu("")]        // the app manages it; it is not something you add by hand
    [DisallowMultipleComponent]
    public sealed class KSAntiRipMark : MonoBehaviour, IEditorOnly
    {
        [Tooltip("The app turns this off instead of removing the component when you pause protection for an avatar.")]
        public bool enabled_ = true;

        [Range(2f, 15f)] public float strength = 5f;
        [Range(1, 6)] public int restoreGroups = 3;

        [Tooltip("Others see the avatar unlocked (the key is synced). Off: only the wearer ever sees it whole.")]
        public bool syncKey = true;
        public bool hideWhileLocked = true;
        public bool obfuscateNames = true;

        [Tooltip("How much bigger the protected avatar may get. The scramble is spread over as many vertices as " +
                 "this budget buys; the rest of the mesh is left in place so a near-200-MB avatar still fits.")]
        public int maxAddedMegabytes = 12;
    }
}
