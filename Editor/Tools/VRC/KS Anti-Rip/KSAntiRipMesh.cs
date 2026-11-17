using System;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Applies a protection plan to a mesh. It knows how to USE the numbers; it does not know where they come
    /// from, and that is deliberate.
    ///
    /// Until 2026-11-16 the scramble itself lived here. KS Unity Tools ships as readable C# source, so the
    /// function, the group split and the weights were published with it — and a published scrambler is one
    /// somebody writes an automatic undo for. The maths moved into the Kawaii Studio app's core, which ships
    /// obfuscated and hash-sealed; this file now receives finished arrays over the loopback bridge and applies
    /// them. Nothing here reveals how a protected avatar was made.
    ///
    /// Blend shapes, not shaders, carry the repair: they work with Poiyomi, lilToon and the Quest mobile
    /// shaders alike, because no shader has to cooperate.
    /// </summary>
    public static class KSAntiRipMesh
    {
        /// <summary>
        /// A scrambled copy of <paramref name="source"/>: every vertex moved by <paramref name="offsets"/>,
        /// plus one blend shape per restore group. Setting shape k to its weight — and only that value —
        /// puts the mesh back exactly.
        /// </summary>
        public static Mesh Apply(Mesh source, Vector3[] offsets, System.Collections.Generic.IList<Vector3[]> groups,
                                 System.Collections.Generic.IList<string> shapeNames)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (offsets == null) throw new ArgumentNullException(nameof(offsets));
            if (groups == null || shapeNames == null || groups.Count != shapeNames.Count)
                throw new ArgumentException("one name per restore group is required");

            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            Bounds originalBounds = source.bounds;

            Vector3[] verts = copy.vertices;
            int n = verts.Length;
            if (offsets.Length != n)
                throw new ArgumentException($"the plan is for {offsets.Length} vertices, this mesh has {n}");

            for (int i = 0; i < n; i++) verts[i] += offsets[i];
            copy.vertices = verts;

            // The avatar's own shapes (visemes, expressions) are deltas relative to the base, so a uniform
            // per-vertex displacement leaves them valid. AddBlendShapeFrame appends, it does not replace.
            for (int k = 0; k < groups.Count; k++)
            {
                if (groups[k] == null || groups[k].Length != n)
                    throw new ArgumentException($"restore group {k} is not sized for this mesh");
                copy.AddBlendShapeFrame(shapeNames[k], 100f, groups[k], null, null);
            }

            // Keep the ORIGINAL bounds: once unlocked the geometry is back where it belongs, and that is the
            // state culling has to be right for. (While locked the renderers are hidden anyway.)
            copy.bounds = originalBounds;
            // Never UploadMeshData(true) before CreateAsset — it throws the CPU copy away and the saved asset
            // comes out empty. That was a real 1.x bug.
            return copy;
        }

        /// <summary>
        /// Applies the restore shapes on the CPU and returns the resulting positions. Test-only: this is just
        /// what any renderer does with a blend shape weight, so it gives nothing away.
        /// </summary>
        public static Vector3[] RestoreForTest(Mesh scrambled, float[] weights, string[] shapeNames)
        {
            Vector3[] result = scrambled.vertices;
            int n = result.Length;
            var d = new Vector3[n];
            for (int k = 0; k < shapeNames.Length; k++)
            {
                int idx = scrambled.GetBlendShapeIndex(shapeNames[k]);
                if (idx < 0) throw new InvalidOperationException("missing restore shape " + shapeNames[k]);
                scrambled.GetBlendShapeFrameVertices(idx, 0, d, null, null);
                float f = weights[k] / 100f;
                for (int i = 0; i < n; i++) result[i] += d[i] * f;
            }
            return result;
        }
    }
}
