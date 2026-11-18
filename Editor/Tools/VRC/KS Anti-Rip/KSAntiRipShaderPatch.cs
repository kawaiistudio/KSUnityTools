using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// 3.14: closes a mesh through the avatar's OWN shader instead of a restore blend shape.
    ///
    /// Like every other part of KS Anti-Rip here, this file knows how to USE what the app sends, not how the app
    /// made it. The app hands over the restore function as TEXT (KSAntiRipClient.Plan.shaderCode) and, per vertex,
    /// an id and two amounts (MeshPlan.shaderIds / shaderAmounts). This file:
    ///   * pastes that text into a COPY of each locked shader, once, resolving four placeholders to what that
    ///     shader calls its vertex input, position, normal and tangent, and calls it first thing in every vertex
    ///     function (PatchShader);
    ///   * writes the ids into a spare UV channel and moves each vertex off along its normal and tangent, so the
    ///     shader puts it back when it is handed the key (BuildScrambledMesh).
    /// Nothing that closes the mesh ships: the amounts are a hash of the key, which is on the Kawaii Studio server.
    ///
    /// It never touches a shader that is not a locked-Poiyomi copy, and a copy keeps a name under "Hidden/Locked/"
    /// so Poiyomi's own build step does not strip it. It reads shader source but writes only COPIES, in the build
    /// folder, exactly like the rest of the protection.
    ///
    /// Proven on the bench before it was a package file (KSShaderRestoreAll, 2026-11-18): the patched shader closes
    /// the mesh with the right key at rest and bent, 13 renderers; a wrong key or the scrambled mesh on the
    /// original shader is garbage; and the half-precision a bundle stores the normals at is handled by computing
    /// the offset from those same half values (BuildScrambledMesh quantizes first).
    /// </summary>
    public static class KSAntiRipShaderPatch
    {
        public sealed class PatchResult
        {
            public Shader Shader;          // null when the shader could not be patched; Why says why
            public string Why = "";
            public int Entries;            // how many vertex functions were patched
            public int Files;              // the shader plus the local .cginc files copied with it
            // 3.14, textures: whether the main texture's reads go through the texture function, and why not
            public bool TextureOk;
            public int TextureSites;
            public string TextureWhy = "";
            internal bool TextureTried;    // the texture function was pasted in (so a compile failure may be its fault)
        }

        // ── which shaders this can close ──────────────────────────────────────────────────────────

        /// <summary>
        /// A locked-Poiyomi shader: Poiyomi locks a material into a single generated shader under
        /// "Hidden/Locked/…/.poiyomi/…". Those are self-contained text we can patch; an unlocked ".poiyomi/Poiyomi"
        /// material points at the package's uber shader, which we never rewrite.
        /// </summary>
        public static bool IsLockedPoiyomi(Shader shader) =>
            shader != null &&
            shader.name.StartsWith("Hidden/Locked/", StringComparison.Ordinal) &&
            shader.name.IndexOf(".poiyomi", StringComparison.OrdinalIgnoreCase) >= 0;

        // ── the mesh ──────────────────────────────────────────────────────────────────────────────

        static float HalfOf(float v) => Mathf.HalfToFloat(Mathf.FloatToHalf(v));
        static Vector3 HalfOf(Vector3 v) => new Vector3(HalfOf(v.x), HalfOf(v.y), HalfOf(v.z));

        /// <summary>
        /// A scrambled copy of <paramref name="source"/>: the vertices the mask marks move off along their own
        /// normal and tangent by the amounts, and carry their id in UV7 (x, y) with the strength in UV7.z. A vertex
        /// the mask leaves false keeps its place and gets id 0, which the shader reads as "do not move me" — that is
        /// how a sub-mesh on an un-patched material is left alone. The offset is computed from the HALF-precision
        /// normal and tangent, because that is what the bundle stores and the shader will read.
        /// </summary>
        public static Mesh BuildScrambledMesh(Mesh source, bool[] move, Vector2[] ids, Vector2[] amounts,
                                              float strength, out int moved)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int n = source.vertexCount;
            if (move == null || move.Length != n) throw new ArgumentException("the move mask is not sized for the mesh");
            if (ids == null || ids.Length != n || amounts == null || amounts.Length != n)
                throw new ArgumentException("the shader numbers are not sized for the mesh");

            Mesh copy = UnityEngine.Object.Instantiate(source);
            copy.name = source.name;
            Bounds originalBounds = source.bounds;

            Vector3[] pos = copy.vertices, nor = source.normals;
            Vector4[] tan = source.tangents;
            bool haveFrames = nor.Length == n && tan.Length == n;

            var uv7 = new List<Vector4>(n);
            var aArr = new float[n]; var bArr = new float[n];
            // The mesh SHIPS its normal and tangent already at half precision, and the offset is computed from those
            // same values: the shader then always reads exactly what the offset used, whether or not the bundle
            // quantises them again (half of half is half). Lighting changes by less than one step of 255.
            var norOut = haveFrames ? new Vector3[n] : null;
            var tanOut = haveFrames ? new Vector4[n] : null;
            moved = 0;
            for (int i = 0; i < n; i++)
            {
                if (!haveFrames) { uv7.Add(Vector4.zero); continue; }
                Vector3 norH = HalfOf(nor[i]);
                Vector3 tanH = HalfOf(new Vector3(tan[i].x, tan[i].y, tan[i].z));
                norOut[i] = norH;
                tanOut[i] = new Vector4(tanH.x, tanH.y, tanH.z, tan[i].w);
                if (!move[i]) { uv7.Add(Vector4.zero); continue; }
                float a = amounts[i].x * strength, b = amounts[i].y * strength;
                aArr[i] = a; bArr[i] = b;
                pos[i] += a * norH + b * tanH;
                // round(id) in the shader, so a half-float is read back exactly for an id below 2048 per component
                uv7.Add(new Vector4(Mathf.Round(ids[i].x), Mathf.Round(ids[i].y), strength, 0f));
                moved++;
            }
            copy.vertices = pos;
            if (haveFrames) { copy.normals = norOut; copy.tangents = tanOut; }
            copy.SetUVs(7, uv7);

            // A moved vertex ships at full precision; Unity re-quantises a compressed mesh and the key would no
            // longer close it (same reason as KSAntiRipMesh).
            if (moved > 0) MeshUtility.SetMeshCompression(copy, ModelImporterMeshCompression.Off);

            // Blend shapes that turn the normal/tangent move the offset with them, so the shader's subtraction still
            // cancels at any weight -- using the same half values.
            var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
            copy.ClearBlendShapes();
            for (int s = 0; s < source.blendShapeCount; s++)
            {
                string name = source.GetBlendShapeName(s);
                int frames = source.GetBlendShapeFrameCount(s);
                for (int f = 0; f < frames; f++)
                {
                    float w = source.GetBlendShapeFrameWeight(s, f);
                    source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                    var dv2 = new Vector3[n];
                    bool turns = false;
                    for (int i = 0; i < n; i++)
                    {
                        if (move[i] && (dn[i] != Vector3.zero || dt[i] != Vector3.zero))
                        {
                            dv2[i] = dv[i] + aArr[i] * HalfOf(dn[i]) + bArr[i] * HalfOf(dt[i]);
                            turns = true;
                        }
                        else dv2[i] = dv[i];
                    }
                    copy.AddBlendShapeFrame(name, w, dv2, dn, dt);
                    _ = turns;
                }
            }
            copy.bounds = originalBounds;
            return copy;
        }

        // ── the patcher ───────────────────────────────────────────────────────────────────────────

        static readonly Regex IncludeRx = new Regex("#include\\s+\"([^\"]+)\"");
        static readonly Regex PragmaVertexRx = new Regex("#pragma\\s+vertex\\s+(\\w+)");
        static readonly Regex StructRx = new Regex("struct\\s+(\\w+)\\s*\\{([^{}]*)\\}");
        static readonly Regex ShaderNameRx = new Regex("^(\\s*Shader\\s+\")([^\"]+)(\")", RegexOptions.Multiline);
        static readonly Regex FallbackRx = new Regex("\"VRCFallback\"\\s*=\\s*\"[^\"]*\"");
        static readonly Regex PropertiesRx = new Regex("(Properties\\s*\\{\\s*\\r?\\n)");

        sealed class InputStruct { public string Name, Pos, Nor, Tan; }

        static string Field(string body, string semantic)
        {
            Match m = new Regex("(?:float|half|fixed|min16float)[1-4]?\\s+(\\w+)\\s*:\\s*" + semantic + "\\b").Match(body);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Pastes the app's restore function into a copy of <paramref name="shaderPath"/>, resolving its four
        /// placeholders per vertex input struct, calling it in every vertex function, and adding the id channel and
        /// the key properties. Returns the compiled copy, or a reason it was left alone.
        /// </summary>
        public static PatchResult PatchShader(string shaderPath, string shaderCode, string call, string idField,
                                              IList<string> properties, int index, string folder,
                                              string textureCode = null, string textureFn = null, string tidProperty = null)
        {
            bool wantTexture = !string.IsNullOrEmpty(textureCode) && !string.IsNullOrEmpty(textureFn) && !string.IsNullOrEmpty(tidProperty);
            PatchResult r = PatchOnce(shaderPath, shaderCode, call, idField, properties, index, folder,
                                      wantTexture ? textureCode : null, textureFn, tidProperty);
            // The texture rewrite is the riskier half. If the copy WITH it does not compile, the vertices must not pay
            // for it: try again without, and keep the mesh closed by the shader.
            if (r.Shader == null && r.TextureTried)
            {
                string why = r.Why;
                string sub = folder + "/kss" + index;
                if (AssetDatabase.IsValidFolder(sub)) AssetDatabase.DeleteAsset(sub);
                r = PatchOnce(shaderPath, shaderCode, call, idField, properties, index, folder, null, null, null);
                r.TextureWhy = "with the main texture rewritten it did not compile (" + why + ")";
            }
            return r;
        }

        static PatchResult PatchOnce(string shaderPath, string shaderCode, string call, string idField,
                                     IList<string> properties, int index, string folder,
                                     string textureCode, string textureFn, string tidProperty)
        {
            var res = new PatchResult();
            string dir = Path.GetDirectoryName(shaderPath).Replace('\\', '/');
            string main = Path.GetFileName(shaderPath);

            // the shader and the include files it takes from its own folder (Poiyomi 7.x splits into .cginc)
            var files = new Dictionary<string, string> { [main] = File.ReadAllText(shaderPath) };
            var queue = new Queue<string>(); queue.Enqueue(main);
            while (queue.Count > 0)
            {
                string f = queue.Dequeue();
                foreach (Match m in IncludeRx.Matches(files[f]))
                {
                    string inc = m.Groups[1].Value;
                    string name = inc.TrimStart('/');
                    if (!File.Exists(Path.Combine(dir, name))) continue;   // Unity built-in or package include, unchanged
                    if (inc.Contains("..") || name.Contains("/")) { res.Why = "an include is in another folder: " + inc; return res; }
                    if (!files.ContainsKey(name)) { files[name] = File.ReadAllText(Path.Combine(dir, name)); queue.Enqueue(name); }
                }
            }
            string all = string.Join("\n", files.Values);

            // 3.14, textures: every read of the main texture goes through the app's texture function, or none does.
            bool tex = false;
            var texFiles = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(textureCode))
            {
                if (!TexelSizeRx.IsMatch(all) || !MainTexDeclRx.IsMatch(all))
                    res.TextureWhy = "the shader does not declare the main texture the usual way";
                else
                {
                    string texWhy = RewriteMainTex(files, textureFn, tidProperty, out int sites, texFiles);
                    if (texWhy != null) res.TextureWhy = texWhy;
                    else if (sites == 0) res.TextureWhy = "the shader never reads the main texture";
                    else { tex = true; res.TextureSites = sites; }
                }
                if (tex)
                {
                    // A .cginc that reads the texture gets the function at its top, in case it is included before the
                    // vertex function (where it is pasted too); the guards keep one copy per pass.
                    foreach (string f in texFiles)
                        if (f.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase)) files[f] = textureCode + "\n" + files[f];
                    all = string.Join("\n", files.Values);
                }
            }
            res.TextureTried = tex;

            var entries = new HashSet<string>(PragmaVertexRx.Matches(all).Cast<Match>().Select(m => m.Groups[1].Value));
            if (entries.Count == 0) { res.Why = "the shader has no #pragma vertex"; return res; }

            // vertex input structs: those with a POSITION and a NORMAL
            var structs = new Dictionary<string, InputStruct>();
            foreach (Match m in StructRx.Matches(all))
            {
                string body = m.Groups[2].Value;
                string p = Field(body, "POSITION"), nn = Field(body, "NORMAL");
                if (p == null || nn == null) continue;
                if (new Regex(":\\s*TEXCOORD7\\b").IsMatch(body)) { res.Why = "a vertex input already uses TEXCOORD7"; return res; }
                var st = new InputStruct { Name = m.Groups[1].Value, Pos = p, Nor = nn, Tan = Field(body, "TANGENT") ?? "ksTan" };
                if (structs.TryGetValue(st.Name, out InputStruct prev) && (prev.Pos != st.Pos || prev.Nor != st.Nor || prev.Tan != st.Tan))
                { res.Why = "a vertex input is declared twice differently"; return res; }
                structs[st.Name] = st;
            }

            var patchedPerEntry = entries.ToDictionary(e => e, e => 0);
            var usedStructs = new HashSet<string>();
            string why = null;
            foreach (string key in files.Keys.ToList())
            {
                string text = files[key];
                foreach (string e in entries)
                {
                    var defRx = new Regex("\\b\\w+\\s+" + e + "\\s*\\(([^(){};]*)\\)\\s*(?::\\s*\\w+\\s*)?\\{(\\s*UNITY_SETUP_INSTANCE_ID\\s*\\(\\s*\\w+\\s*\\)\\s*;)?");
                    text = defRx.Replace(text, m =>
                    {
                        string pars = m.Groups[1].Value;
                        string head = pars.Contains("#else") ? pars.Substring(0, pars.IndexOf("#else", StringComparison.Ordinal)) : pars;
                        InputStruct st = null; string var = null;
                        foreach (InputStruct s in structs.Values)
                        {
                            Match pm = new Regex("\\b" + s.Name + "\\s+(\\w+)").Match(head);
                            if (pm.Success) { st = s; var = pm.Groups[1].Value; break; }
                        }
                        if (st == null) { why = why ?? ("a vertex function takes an input this cannot read: " + e); return m.Value; }
                        string callLine = call + "_" + st.Name + "(" + var + ");";
                        if (pars.Contains("#"))
                        {
                            Match g = new Regex("#ifndef\\s+(\\w+)\\s+(?:in\\s+|inout\\s+)?" + st.Name + "\\s+" + var + "\\s+#else").Match(pars);
                            if (!g.Success) { why = why ?? ("a vertex function's conditional input is not understood: " + e); return m.Value; }
                            callLine = "\n#ifndef " + g.Groups[1].Value + "\n" + callLine + "\n#endif\n";
                        }
                        patchedPerEntry[e]++;
                        usedStructs.Add(st.Name);
                        return (tex ? textureCode + "\n" : "") + Substitute(shaderCode, st) + "\n" + m.Value + "\n\t\t\t\t" + callLine;
                    });
                }
                files[key] = text;
            }
            if (why != null) { res.Why = why; return res; }
            string missing = string.Join(", ", patchedPerEntry.Where(kv => kv.Value == 0).Select(kv => kv.Key));
            if (missing.Length > 0) { res.Why = "a vertex function was not found: " + missing; return res; }

            // the id channel (and a tangent, if the struct had none) on every struct a patched entry uses
            foreach (string key in files.Keys.ToList())
                files[key] = StructRx.Replace(files[key], m =>
                {
                    if (!usedStructs.Contains(m.Groups[1].Value) || !structs.ContainsKey(m.Groups[1].Value)) return m.Value;
                    InputStruct s = structs[m.Groups[1].Value];
                    string extra = "\n\t\t\t\tfloat4 " + idField + " : TEXCOORD7;" + (s.Tan == "ksTan" ? "\n\t\t\t\tfloat4 ksTan : TANGENT;" : "");
                    return "struct " + m.Groups[1].Value + "\n{" + extra + m.Groups[2].Value + "}";
                });

            // keep the "Hidden/Locked/" name so Poiyomi's build step does not strip the copy, and add the key
            // properties so the animator can drive them.
            string sh = files[main];
            sh = ShaderNameRx.Replace(sh, m => m.Groups[1].Value + "Hidden/Locked/KS/" + index + "_" + ShortName(shaderPath) + m.Groups[3].Value, 1);
            // Someone who blocks the wearer's shaders gets VRChat's fallback. "Hidden" draws nothing of the protected
            // mesh for them, so they see the Kawaii Studio poster instead of scrambled geometry (owner's rule, 2026-11-18).
            sh = FallbackRx.Replace(sh, "\"VRCFallback\" = \"Hidden\"");
            var props = new StringBuilder();
            foreach (string p in properties) props.Append("\t\t[HideInInspector] ").Append(p).Append(" (\"\", Float) = 0\n");
            if (tex) props.Append("\t\t[HideInInspector] ").Append(tidProperty).Append(" (\"\", Float) = 0\n");
            sh = PropertiesRx.Replace(sh, m => m.Groups[1].Value + props, 1);
            files[main] = sh;

            string sub = folder + "/kss" + index;
            AssetDatabase.CreateFolder(folder, "kss" + index);
            foreach (var kv in files) File.WriteAllText(sub + "/" + kv.Key, kv.Value);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader patched = AssetDatabase.LoadAssetAtPath<Shader>(sub + "/" + main);
            if (patched == null) { res.Why = "the patched shader did not import"; return res; }
            // Import only parses the shader; an error in the code of a pass (an undefined name in an included .cginc,
            // say) surfaces when that pass is really compiled -- the bench saw a Poiyomi 7 copy "pass" the import and
            // then draw as the error shader. So compile every pass now, synchronously, and read the messages after.
            var probe = new Material(patched);
            try
            {
                for (int p = 0; p < patched.passCount; p++) ShaderUtil.CompilePass(probe, p, true);
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            var errs = ShaderUtil.GetShaderMessages(patched)
                                 .Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToList();
            if (errs.Count > 0 || ShaderUtil.ShaderHasError(patched))
            {
                res.Why = "the patched shader did not compile" + (errs.Count > 0 ? ": " + errs[0].message + " (line " + errs[0].line + ")" : "");
                return res;
            }
            res.Shader = patched;
            res.Entries = patchedPerEntry.Values.Sum();
            res.Files = files.Count;
            res.TextureOk = tex;
            return res;
        }

        // ── 3.14, rewriting the reads of the main texture ─────────────────────────────────────────

        static readonly Regex TexelSizeRx = new Regex("float4\\s+_MainTex_TexelSize\\b");
        static readonly Regex MainTexDeclRx = new Regex("UNITY_DECLARE_TEX2D\\s*\\(\\s*_MainTex\\s*\\)|SamplerState\\s+sampler_MainTex\\b");
        static readonly Regex MainTexCallRx = new Regex("\\b([A-Za-z_][A-Za-z0-9_]*)\\s*\\(\\s*_MainTex\\s*,");

        /// <summary>
        /// Replaces every read of the main texture with the texture function, in all the files, or changes nothing
        /// and says why. Known reads: Poiyomi's POI2D_SAMPLER(_PAN)(_STOCHASTIC) -- stochastic only when it is a
        /// literal 0, the moved coordinates of stochastic sampling would not match -- and Unity's
        /// UNITY_SAMPLE_TEX2D(_SAMPLER). Anything else that reads it as a texture means the rewrite is not safe.
        /// </summary>
        static string RewriteMainTex(Dictionary<string, string> files, string fn, string tid, out int sites, HashSet<string> touched)
        {
            sites = 0;
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in files)
            {
                string text = kv.Value;
                var sb = new StringBuilder(text.Length + 256);
                int pos = 0;
                foreach (Match m in MainTexCallRx.Matches(text))
                {
                    if (m.Index < pos) continue;
                    int lineStart = text.LastIndexOf('\n', Math.Max(0, m.Index - 1)) + 1;
                    if (text.Substring(lineStart, m.Index - lineStart).TrimStart().StartsWith("#", StringComparison.Ordinal))
                        continue;   // a #define's body names its own parameters, never _MainTex itself
                    string mac = m.Groups[1].Value;
                    int open = text.IndexOf('(', m.Index);
                    if (!SplitArgs(text, open, out List<string> args, out int close))
                        return "a read of the main texture (" + mac + ") could not be parsed";
                    string uv;
                    switch (mac)
                    {
                        case "POI2D_SAMPLER_PAN_STOCHASTIC":
                            if (args.Count != 5) return "an unexpected " + mac + " read";
                            if (!IsZero(args[4])) return "the main texture uses stochastic sampling";
                            uv = "POI_PAN_UV(" + args[2] + ", " + args[3] + ")";
                            break;
                        case "POI2D_SAMPLER_PAN":
                            if (args.Count != 4) return "an unexpected " + mac + " read";
                            uv = "POI_PAN_UV(" + args[2] + ", " + args[3] + ")";
                            break;
                        case "POI2D_SAMPLER":
                        case "UNITY_SAMPLE_TEX2D_SAMPLER":
                            if (args.Count != 3) return "an unexpected " + mac + " read";
                            uv = args[2];
                            break;
                        case "UNITY_SAMPLE_TEX2D":
                            if (args.Count != 2) return "an unexpected " + mac + " read";
                            uv = args[1];
                            break;
                        default:
                            return "the main texture is read by " + mac + ", which this cannot rewrite";
                    }
                    sb.Append(text, pos, m.Index - pos);
                    sb.Append(fn).Append("(_MainTex, sampler_MainTex, _MainTex_TexelSize, ").Append(tid)
                      .Append(", (").Append(uv).Append("))");
                    pos = close + 1;
                    sites++;
                    touched.Add(kv.Key);
                }
                sb.Append(text, pos, text.Length - pos);
                result[kv.Key] = sb.ToString();
            }
            foreach (var kv in result) files[kv.Key] = kv.Value;   // commit only once every file went through
            return null;
        }

        /// <summary>The arguments of the call whose '(' is at <paramref name="open"/>, split at top-level commas.</summary>
        static bool SplitArgs(string text, int open, out List<string> args, out int close)
        {
            args = new List<string>();
            close = -1;
            if (open < 0 || open >= text.Length || text[open] != '(') return false;
            int depth = 0, start = open + 1;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']')
                {
                    depth--;
                    if (depth == 0) { args.Add(text.Substring(start, i - start).Trim()); close = i; return true; }
                }
                else if (c == ',' && depth == 1) { args.Add(text.Substring(start, i - start).Trim()); start = i + 1; }
                else if (c == ';' || c == '{' || c == '}') return false;   // ran out of the call
            }
            return false;
        }

        /// <summary>A literal zero, however Poiyomi wrote it: 0, 0.0, 0.0f, (0.0 /*_MainTexStochastic*/).</summary>
        static bool IsZero(string arg)
        {
            string s = Regex.Replace(arg, "/\\*.*?\\*/", "").Replace("(", "").Replace(")", "").Trim().TrimEnd('f', 'F');
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) && v == 0.0;
        }

        // ── 3.14, laying out a texture ───────────────────────────────────────────────────────────────

        static readonly int[][] Swizzle = { new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 } };

        /// <summary>
        /// Whether <paramref name="t"/> can be laid out at all: an imported 2D texture of the Default type, both sides a
        /// power of two and at least two blocks across. Sizes are the IMPORTED ones, what ships.
        /// </summary>
        public static bool CanLayOut(Texture2D t, int block)
        {
            if (t == null || block <= 0) return false;
            var imp = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            if (imp == null || imp.textureType != TextureImporterType.Default) return false;
            int w = t.width, h = t.height;
            return w >= block * 2 && h >= block * 2 && (w & (w - 1)) == 0 && (h & (h - 1)) == 0;
        }

        /// <summary>
        /// A laid-out copy of <paramref name="source"/>, imported next to the build's other assets with the source's
        /// settings, so the patched material shows it back with the key. Block i moves to dest[i] and is turned by
        /// state[i]. Reads the IMPORTED texels (what ships), so the import options that already changed them
        /// (alpha dilation, alpha from grey) are switched off on the copy, and its mips are box-filtered so no mip
        /// texel mixes two blocks. Null with a reason when it cannot be made.
        /// </summary>
        public static Texture2D BuildScrambledTexture(Texture2D source, uint[] dest, byte[] state, int block, string folder, out string why)
        {
            why = "";
            string srcPath = AssetDatabase.GetAssetPath(source);
            var srcImp = AssetImporter.GetAtPath(srcPath) as TextureImporter;
            if (srcImp == null) { why = "it is not an imported texture"; return null; }
            int w = source.width, h = source.height, nx = w / block, ny = h / block;
            if (nx * block != w || ny * block != h || dest == null || state == null || nx * ny != dest.Length || dest.Length != state.Length)
            { why = "its size does not match the layout"; return null; }

            Color32[] src = ReadTexels(source, w, h, srcImp.sRGBTexture);
            var dst = new Color32[src.Length];
            var o = new byte[3];
            for (int idx = 0; idx < dest.Length; idx++)
            {
                int bx = idx % nx, by = idx / nx;
                int d = (int)dest[idx];
                if (d < 0 || d >= dest.Length) { why = "the layout points outside the texture"; return null; }
                int dx = d % nx, dy = d / nx;
                byte s = state[idx];
                bool fx = (s & 1) != 0, fy = (s & 2) != 0, tr = (s & 4) != 0;
                int[] sw = Swizzle[(s >> 3) % 6];
                for (int ly = 0; ly < block; ly++)
                    for (int lx = 0; lx < block; lx++)
                    {
                        int px = fx ? block - 1 - lx : lx, py = fy ? block - 1 - ly : ly;
                        if (tr) { int t = px; px = py; py = t; }
                        Color32 c = src[(by * block + ly) * w + bx * block + lx];
                        // the shader reads original[j] = stored[sw[j]]
                        o[sw[0]] = c.r; o[sw[1]] = c.g; o[sw[2]] = c.b;
                        dst[(dy * block + py) * w + dx * block + px] = new Color32(o[0], o[1], o[2], c.a);
                    }
            }

            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, !srcImp.sRGBTexture);
            tex.SetPixels32(dst);
            tex.Apply(false);
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            string path = folder + "/TEX_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".png";
            File.WriteAllBytes(path, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) { why = "the laid-out copy did not import"; return null; }
            var settings = new TextureImporterSettings();
            srcImp.ReadTextureSettings(settings);
            imp.SetTextureSettings(settings);
            imp.alphaIsTransparency = false;                         // already applied to the texels we read
            imp.alphaSource = TextureImporterAlphaSource.FromInput;  // the alpha we read is the final one
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.mipmapFilter = TextureImporterMipFilter.BoxFilter;   // a wider kernel would mix neighbouring blocks
            imp.borderMipmap = false;
            imp.fadeout = false;
            imp.maxTextureSize = Math.Max(srcImp.maxTextureSize, Math.Max(w, h));
            imp.textureCompression = srcImp.textureCompression;
            imp.compressionQuality = srcImp.compressionQuality;
            // Crunch is kept when the source has it. Measured on the bench (2026-11-18, wings 4096 + claws 2048): a second
            // Crunch pass costs little (mean error 6.2/255 instead of 5.1 without it, both invisible at normal size),
            // while dropping it would add tens of MB to an avatar that has to stay under VRChat's size limits.
            imp.crunchedCompression = srcImp.crunchedCompression;
            // the VRChat SDK refuses a mip-mapped texture without streaming mips
            if (imp.mipmapEnabled) imp.streamingMipmaps = true;
            TextureImporterPlatformSettings pc = srcImp.GetPlatformTextureSettings("Standalone");
            if (pc != null && pc.overridden)
            {
                pc.maxTextureSize = Math.Max(pc.maxTextureSize, Math.Max(w, h));
                imp.SetPlatformTextureSettings(pc);
            }
            imp.SaveAndReimport();
            Texture2D result = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (result == null || result.width != w || result.height != h) { why = "the laid-out copy came back at another size"; return null; }
            return result;
        }

        /// <summary>The stored texels of a texture that may not be readable, as the GPU holds them (sRGB-encoded for a
        /// colour texture), at its imported size.</summary>
        static Color32[] ReadTexels(Texture2D t, int w, int h, bool srgb)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                                                srgb ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            Graphics.Blit(t, rt);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(w, h, TextureFormat.RGBA32, false, !srgb);
            tmp.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tmp.Apply(false);
            Color32[] px = tmp.GetPixels32();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            UnityEngine.Object.DestroyImmediate(tmp);
            return px;
        }

        /// <summary>The app's code with its four placeholders filled in for one vertex input struct.</summary>
        static string Substitute(string code, InputStruct s) =>
            code.Replace("__KS_STRUCT__", s.Name)
                .Replace("__KS_POS__", s.Pos)
                .Replace("__KS_NOR__", s.Nor)
                .Replace("__KS_TAN__", s.Tan);

        static string ShortName(string path) =>
            Path.GetFileName(Path.GetDirectoryName(path)).Replace(" ", "") + "_" +
            Path.GetFileNameWithoutExtension(path).Replace(" ", "");
    }
}
