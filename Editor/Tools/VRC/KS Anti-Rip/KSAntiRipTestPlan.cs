using System;
using System.Collections.Generic;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// A plan made up locally, so the Unity half can be tested with no Kawaii Studio app running.
    ///
    /// This is NOT the protection. The real plan comes from the app's core and its scramble is deliberately
    /// not published; this one is a deterministic stand-in whose only job is to be applied and checked.
    ///
    /// Like the real one since 5.1, it hands Unity the key parameters' NAMES only. The values -- what the real
    /// core writes into the .key -- are kept here in <see cref="KeyValues"/>, where only a test reads them.
    /// </summary>
    public class KSAntiRipTestPlan : KSAntiRipProcessor.IPlanSource
    {
        public const string Tag = "testpl";

        readonly float _strength;
        readonly int _groups;
        readonly Dictionary<string, KSAntiRipClient.MeshPlan> _meshes = new Dictionary<string, KSAntiRipClient.MeshPlan>();

        public bool Ended { get; private set; }
        public string LastError => "";
        public KSAntiRipClient.Plan Issued { get; private set; }

        /// <summary>The key: one value per restore shape, on VRChat's sync grid (k/127). Weight = value * 100.</summary>
        public float[] KeyValues { get; private set; } = Array.Empty<float>();

        public KSAntiRipTestPlan(float strength = 4f, int groups = 3)
        {
            _strength = strength;
            _groups = Math.Max(1, groups);
        }

        public KSAntiRipClient.Plan Begin(string avatarId, string avatarName,
                                          IList<KSAntiRipClient.MeshInfo> meshes,
                                          float strength, int restoreGroups)
        {
            var plan = new KSAntiRipClient.Plan
            {
                ok = true,
                session = "test-session",
                tag = Tag,
                marker = KSAntiRipProcessor.MarkerName,
                keyPath = "",
            };

            var rng = new System.Random(20261116);
            KeyValues = new float[_groups];
            for (int g = 0; g < _groups; g++)
            {
                KeyValues[g] = rng.Next(26, 115) / 127f;
                plan.parameters.Add("testkey" + g);
            }

            foreach (KSAntiRipClient.MeshInfo info in meshes)
            {
                plan.meshes.Add(new KSAntiRipClient.MeshInfo { id = info.id, vertexCount = info.vertexCount });
                _meshes[info.id] = MakeMesh(info.id, info.vertexCount);
            }

            Issued = plan;
            return plan;
        }

        public KSAntiRipClient.MeshPlan Mesh(string session, string id)
            => _meshes.TryGetValue(id, out KSAntiRipClient.MeshPlan m) ? m : null;

        public void End(string session) => Ended = true;

        KSAntiRipClient.MeshPlan MakeMesh(string id, int n)
        {
            var plan = new KSAntiRipClient.MeshPlan { id = id, vertexCount = n };
            var rng = new System.Random(id.GetHashCode() ^ 0x5EED);

            for (int g = 0; g < _groups; g++)
            {
                plan.shapeNames.Add("testshape_" + id + "_" + g);
                plan.groups.Add(new Vector3[n]);
            }

            var offsets = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var off = new Vector3(
                    (float)(rng.NextDouble() - 0.5) * _strength,
                    (float)(rng.NextDouble() - 0.5) * _strength,
                    (float)(rng.NextDouble() - 0.5) * _strength);
                offsets[i] = off;
                int g = i % _groups;
                // the delta that cancels `off` exactly when the shape sits at its key value
                plan.groups[g][i] = -off / KeyValues[g];
            }
            plan.offsets = offsets;
            return plan;
        }
    }
}
