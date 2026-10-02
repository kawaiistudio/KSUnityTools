using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;

// KS Unity Tools: the polygon reducer of the Prefab Optimizer (same algorithm as the Kawaii Studio editor's optimizer).
namespace KawaiiStudio
{

/// <summary>
/// Polygon reduction that never invents a vertex: each step collapses an edge P→Q onto Q, an EXISTING vertex (the way
/// meshoptimizer's simplifier works), chosen by the quadric error it costs. So every surviving vertex keeps its exact
/// position, normal, tangent, UVs, colour, skin weights and blendshape deltas — the mesh is only a subset of its own
/// vertices with new triangles, which is what makes it safe to write back into a compiled bundle.
///
/// Only "interior" vertices are ever removed: every edge shared by exactly two triangles, one sub-mesh (material),
/// not locked. That keeps UV seams, hard edges (split vertices), open borders (clothing hems, cut-outs) and material
/// boundaries exactly where they are. A collapse is refused when it would break the manifold (link condition) or flip
/// / squash a triangle. Skin weights add a cost when P and Q are driven by different bones, so joints keep their loops.
/// </summary>
public static class KSMeshSimplify
{
    public sealed class Input
    {
        public float[] Positions = Array.Empty<float>();   // xyz per vertex
        public List<int[]> SubMeshes = new List<int[]>();  // triangle lists, absolute vertex indices
        public bool[] Locked;                             // never removed (blendshape-driven vertices…)
        public float[] Weights;                           // 4 per vertex (optional)
        public int[] Bones;                               // 4 per vertex
        /// <summary>The most a collapse may move the surface, as a fraction of the mesh's bounding-box diagonal: past it
        /// a collapse is refused even if the triangle target isn't reached (the target is a maximum, never a reason to
        /// deform).</summary>
        public double MaxError = 0.01;
    }

    public sealed class Result
    {
        public List<int[]> SubMeshes = new List<int[]>();
        public bool[] Used = Array.Empty<bool>();
        public int TrianglesBefore, TrianglesAfter, Collapses;
    }

    private const double MinCos = 0.25;          // a collapsed triangle may turn by at most ~75°
    private const double WeightCost = 1.0;       // skin weight difference, in units of "area × edge²"

    public static Result Run(Input inp, int targetTriangles, CancellationToken ct = default)
    {
        int n = inp.Positions.Length / 3;
        var res = new Result { Used = new bool[n] };
        // ---- triangles
        int total = inp.SubMeshes.Sum(s => s.Length / 3);
        var tv = new int[total * 3];
        var tsub = new int[total];
        var alive = new bool[total];
        int t0 = 0;
        for (int s = 0; s < inp.SubMeshes.Count; s++)
        {
            var sm = inp.SubMeshes[s];
            for (int i = 0; i + 2 < sm.Length; i += 3, t0++)
            {
                tv[t0 * 3] = sm[i]; tv[t0 * 3 + 1] = sm[i + 1]; tv[t0 * 3 + 2] = sm[i + 2];
                tsub[t0] = s; alive[t0] = true;
            }
        }
        res.TrianglesBefore = total;
        var locked = new bool[n];
        if (inp.Locked != null) Array.Copy(inp.Locked, locked, Math.Min(n, inp.Locked.Length));
        var vt = new List<int>[n];
        for (int v = 0; v < n; v++) vt[v] = new List<int>(6);
        var degenerate = new bool[total];
        for (int t = 0; t < total; t++)
        {
            int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
            if (a < 0 || b < 0 || c < 0 || a >= n || b >= n || c >= n) throw new InvalidDataException($"triangle {t} points past the {n} vertices");
            if (a == b || b == c || a == c) { degenerate[t] = true; locked[a] = locked[b] = locked[c] = true; continue; }   // kept as it is, never touched
            vt[a].Add(t); vt[b].Add(t); vt[c].Add(t);
        }

        // ---- quadrics (area-weighted planes) and the area around each vertex
        var q = new double[n * 10];
        var area = new double[n];
        for (int t = 0; t < total; t++)
        {
            if (degenerate[t]) continue;
            int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
            var (nx, ny, nz, len) = Normal(inp.Positions, a, b, c);
            if (len < 1e-20) continue;
            double A = len * 0.5;
            nx /= len; ny /= len; nz /= len;
            double d = -(nx * inp.Positions[a * 3] + ny * inp.Positions[a * 3 + 1] + nz * inp.Positions[a * 3 + 2]);
            foreach (int v in new[] { a, b, c })
            {
                int o = v * 10;
                q[o] += A * nx * nx; q[o + 1] += A * nx * ny; q[o + 2] += A * nx * nz; q[o + 3] += A * nx * d;
                q[o + 4] += A * ny * ny; q[o + 5] += A * ny * nz; q[o + 6] += A * ny * d;
                q[o + 7] += A * nz * nz; q[o + 8] += A * nz * d; q[o + 9] += A * d * d;
                area[v] += A / 3;
            }
        }

        // ---- the error ceiling: (MaxError × diagonal)², compared with the area-weighted cost / the area it spreads over
        double bx0 = double.MaxValue, by0 = double.MaxValue, bz0 = double.MaxValue, bx1 = double.MinValue, by1 = double.MinValue, bz1 = double.MinValue;
        for (int v = 0; v < n; v++)
        {
            double x = inp.Positions[v * 3], y = inp.Positions[v * 3 + 1], z = inp.Positions[v * 3 + 2];
            bx0 = Math.Min(bx0, x); by0 = Math.Min(by0, y); bz0 = Math.Min(bz0, z); bx1 = Math.Max(bx1, x); by1 = Math.Max(by1, y); bz1 = Math.Max(bz1, z);
        }
        double diag = n == 0 ? 0 : Math.Sqrt((bx1 - bx0) * (bx1 - bx0) + (by1 - by0) * (by1 - by0) + (bz1 - bz0) * (bz1 - bz0));
        double maxErr2 = Math.Pow(Math.Max(0, inp.MaxError) * diag, 2);

        // ---- the collapse queue: (cost, P, target Q, stamp of P)
        var stamp = new int[n];
        var dead = new bool[n];
        var pq = new KSMinHeap<(int P, int Q, int Stamp)>();
        for (int v = 0; v < n; v++)
            { var c = Best(v); if (c.HasValue) pq.Enqueue((v, c.Value.Q, stamp[v]), c.Value.Cost); }

        int liveTris = total;
        while (liveTris > targetTriangles && pq.TryDequeue(out var item, out double cost))
        {
            if (res.Collapses % 1024 == 0) ct.ThrowIfCancellationRequested();
            int p = item.P;
            if (dead[p] || item.Stamp != stamp[p]) continue;
            var best = Best(p);
            if (best == null) continue;
            if (best.Value.Cost > cost * 1.0001 + 1e-18 || best.Value.Q != item.Q) { pq.Enqueue((p, best.Value.Q, stamp[p]), best.Value.Cost); continue; }
            int qv = best.Value.Q;
            // collapse p -> qv
            var ring = Neighbours(p);
            foreach (int t in vt[p].ToArray())
            {
                int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
                if (a == qv || b == qv || c == qv)
                {
                    alive[t] = false; liveTris--;
                    foreach (int v in new[] { a, b, c }) if (v != p) vt[v].Remove(t);
                }
                else
                {
                    for (int k = 0; k < 3; k++) if (tv[t * 3 + k] == p) tv[t * 3 + k] = qv;
                    vt[qv].Add(t);
                }
            }
            vt[p].Clear();
            dead[p] = true;
            for (int k = 0; k < 10; k++) q[qv * 10 + k] += q[p * 10 + k];
            area[qv] += area[p];
            res.Collapses++;
            foreach (int v in ring.Append(qv))
            {
                if (dead[v]) continue;
                stamp[v]++;
                { var c = Best(v); if (c.HasValue) pq.Enqueue((v, c.Value.Q, stamp[v]), c.Value.Cost); }
            }
        }

        // ---- result
        var outs = new List<int>[inp.SubMeshes.Count];
        for (int s = 0; s < outs.Length; s++) outs[s] = new List<int>();
        for (int t = 0; t < total; t++)
        {
            if (!alive[t]) continue;
            int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
            outs[tsub[t]].Add(a); outs[tsub[t]].Add(b); outs[tsub[t]].Add(c);
            res.Used[a] = res.Used[b] = res.Used[c] = true;
        }
        res.SubMeshes = outs.Select(l => l.ToArray()).ToList();
        res.TrianglesAfter = res.SubMeshes.Sum(s => s.Length / 3);
        return res;

        HashSet<int> Neighbours(int v)
        {
            var hs = new HashSet<int>();
            foreach (int t in vt[v])
                for (int k = 0; k < 3; k++) { int u = tv[t * 3 + k]; if (u != v) hs.Add(u); }
            return hs;
        }

        bool Interior(int v)
        {
            if (locked[v] || dead[v] || vt[v].Count < 3) return false;
            int sub = tsub[vt[v][0]];
            var edgeUse = new Dictionary<int, int>();
            foreach (int t in vt[v])
            {
                if (tsub[t] != sub) return false;
                for (int k = 0; k < 3; k++) { int u = tv[t * 3 + k]; if (u != v) { edgeUse.TryGetValue(u, out int cu); edgeUse[u] = cu + 1; } }
            }
            foreach (var c in edgeUse.Values) if (c != 2) return false;
            return true;
        }

        (int Q, double Cost)? Best(int p)
        {
            if (!Interior(p)) return null;
            var ring = Neighbours(p);
            (int Q, double Cost)? best = null;
            foreach (int cand in ring)
            {
                double c = Cost(p, cand);
                if (best != null && c >= best.Value.Cost) continue;
                if (c > maxErr2 * Math.Max(area[p] + area[cand], 1e-30)) continue;   // would move the surface too far
                if (!Valid(p, cand, ring)) continue;
                best = (cand, c);
            }
            return best;
        }

        double Cost(int p, int to)
        {
            double x = inp.Positions[to * 3], y = inp.Positions[to * 3 + 1], z = inp.Positions[to * 3 + 2];
            double e = 0;
            foreach (int v in new[] { p, to })
            {
                int o = v * 10;
                e += q[o] * x * x + 2 * q[o + 1] * x * y + 2 * q[o + 2] * x * z + 2 * q[o + 3] * x
                   + q[o + 4] * y * y + 2 * q[o + 5] * y * z + 2 * q[o + 6] * y
                   + q[o + 7] * z * z + 2 * q[o + 8] * z + q[o + 9];
            }
            e = Math.Max(0, e);
            if (inp.Weights != null && inp.Bones != null)
            {
                double dx = inp.Positions[p * 3] - x, dy = inp.Positions[p * 3 + 1] - y, dz = inp.Positions[p * 3 + 2] - z;
                e += WeightCost * WeightDiff(p, to) * (dx * dx + dy * dy + dz * dz) * Math.Max(area[p], 1e-12);
            }
            return e;
        }

        double WeightDiff(int a, int b)
        {
            double diff = 0;
            for (int i = 0; i < 4; i++)
            {
                float wa = inp.Weights![a * 4 + i];
                if (wa == 0) continue;
                int bone = inp.Bones![a * 4 + i];
                float wb = 0;
                for (int j = 0; j < 4; j++) if (inp.Bones[b * 4 + j] == bone) wb += inp.Weights[b * 4 + j];
                diff += Math.Abs(wa - wb);
            }
            for (int j = 0; j < 4; j++)
            {
                float wb = inp.Weights![b * 4 + j];
                if (wb == 0) continue;
                int bone = inp.Bones![b * 4 + j];
                bool shared = false;
                for (int i = 0; i < 4; i++) if (inp.Bones[a * 4 + i] == bone && inp.Weights[a * 4 + i] != 0) shared = true;
                if (!shared) diff += wb;
            }
            return diff;
        }

        bool Valid(int p, int to, HashSet<int> ringP)
        {
            // link condition: the edge's two opposite vertices are the only common neighbours
            int common = 0;
            foreach (int u in Neighbours(to)) if (ringP.Contains(u) && ++common > 2) return false;
            if (common != 2) return false;
            // no triangle flips or collapses to a sliver
            foreach (int t in vt[p])
            {
                int a = tv[t * 3], b = tv[t * 3 + 1], c = tv[t * 3 + 2];
                if (a == to || b == to || c == to) continue;
                var (ox, oy, oz, ol) = Normal(inp.Positions, a, b, c);
                int na = a == p ? to : a, nb = b == p ? to : b, nc = c == p ? to : c;
                var (mx, my, mz, ml) = Normal(inp.Positions, na, nb, nc);
                if (ml < 1e-20 || ol < 1e-20) return false;
                if ((ox * mx + oy * my + oz * mz) / (ol * ml) < MinCos) return false;
            }
            return true;
        }
    }

    private static (double X, double Y, double Z, double Len) Normal(float[] p, int a, int b, int c)
    {
        double ux = p[b * 3] - p[a * 3], uy = p[b * 3 + 1] - p[a * 3 + 1], uz = p[b * 3 + 2] - p[a * 3 + 2];
        double vx = p[c * 3] - p[a * 3], vy = p[c * 3 + 1] - p[a * 3 + 1], vz = p[c * 3 + 2] - p[a * 3 + 2];
        double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        return (nx, ny, nz, Math.Sqrt(nx * nx + ny * ny + nz * nz));
    }
}

    /// <summary>A binary min-heap (Unity's .NET has no PriorityQueue).</summary>
    internal sealed class KSMinHeap<T>
    {
        private readonly List<(T Item, double Priority)> _h = new List<(T, double)>();

        public void Enqueue(T item, double priority)
        {
            _h.Add((item, priority));
            int i = _h.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (_h[parent].Priority <= _h[i].Priority) break;
                (_h[parent], _h[i]) = (_h[i], _h[parent]);
                i = parent;
            }
        }

        public bool TryDequeue(out T item, out double priority)
        {
            if (_h.Count == 0) { item = default; priority = 0; return false; }
            item = _h[0].Item; priority = _h[0].Priority;
            var last = _h[_h.Count - 1];
            _h.RemoveAt(_h.Count - 1);
            if (_h.Count > 0)
            {
                _h[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < _h.Count && _h[l].Priority < _h[m].Priority) m = l;
                    if (r < _h.Count && _h[r].Priority < _h[m].Priority) m = r;
                    if (m == i) break;
                    (_h[m], _h[i]) = (_h[i], _h[m]);
                    i = m;
                }
            }
            return true;
        }
    }
}
