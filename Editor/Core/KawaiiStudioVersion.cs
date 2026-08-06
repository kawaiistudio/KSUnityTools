namespace KawaiiStudio
{
    /// <summary>
    /// One version number for the whole toolset.
    ///
    /// Before the merge the numbering contradicted itself: the published package
    /// declared VERSION.md 1.4 and every tool inside it said "v1.4", while the git
    /// repository's copies of the same tools said v2.0 and the Manager said v2.2.
    /// Neither line was a strict superset of the other, so neither number could
    /// simply win.
    ///
    /// The merged toolset is a superset of both, so it takes 3.0.0: unambiguously
    /// above every number in circulation (1.4, 2.0, 2.2), and a major bump is
    /// honest about the shared Core layer and the reorganised UI.
    ///
    /// This constant is the single source of truth. package.json and VERSION.md
    /// must match it.
    /// </summary>
    public static class KawaiiStudioVersion
    {
        public const string Current = "3.1.0";
    }
}
