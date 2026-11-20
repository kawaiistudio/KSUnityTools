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
    /// 3.16.6, THE UNIVERSAL SHADER PATCH (owner, 2026-11-19: "rends mon truc universel, c'est hyper tough mais faisable sans
    /// détruire les shaders"). An attacker's test of his own upload showed every part left on restore shapes comes back
    /// with a least-squares solve; most of them were there only because their shader was not a locked Poiyomi or a lilToon
    /// (effects, eyes, hair, rings: 39 of the 42 other shaders on his avatar). This closes a mesh through ANY shader whose
    /// source is in the project, without rewriting how it draws:
    ///   - the restore runs FIRST in the vertex input, so the shader's own code receives exactly the data it always did;
    ///   - the vertex input struct only GAINS fields (the id in TEXCOORD7, and a NORMAL / TANGENT when it had none);
    ///   - a surface shader gets a vertex modifier (its own, or one added) on an input struct with the same fields, and
    ///     "addshadow" so its shadow is drawn from the restored mesh too;
    ///   - a vertex/fragment shader that took its shadow from a Fallback (or a shadow UsePass) gets a restored shadow pass
    ///     instead -- the Fallback's would cast the scrambled mesh on the floor;
    ///   - every file it includes is copied next to the patched copy (relative, "Assets/", "Packages/" and, only when
    ///     needed, Unity's own CGIncludes), so nothing outside the build folder is ever written.
    /// Every pass is compiled before the copy is used; any failure leaves the mesh on its current method.
    /// </summary>
    public static class KSAntiRipShaderUniversal
    {
        /// <summary>A shader this may try: its source is a .shader file of the project (not built in), and it is not one the
        /// dedicated paths handle (locked Poiyomi, lilToon) or a package uber shader (unlocked Poiyomi).</summary>
        public static bool CanTry(Shader shader)
        {
            if (shader == null) return false;
            string name = shader.name;
            if (name.StartsWith("Hidden/Locked/KS/", StringComparison.Ordinal)) return false;
            if (KSAntiRipShaderPatch.IsLockedPoiyomi(shader) || KSAntiRipLilToon.IsLilToon(shader)) return false;
            if (name.IndexOf(".poiyomi", StringComparison.OrdinalIgnoreCase) >= 0) return false;   // unlocked: lock it in Poiyomi first
            string path = AssetDatabase.GetAssetPath(shader);
            return !string.IsNullOrEmpty(path) && path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) && File.Exists(path);
        }

        static readonly Regex IncludeRx = new Regex("#include(?:_with_pragmas)?\\s+\"([^\"]+)\"");
        static readonly Regex PragmaVertexRx = new Regex("#pragma\\s+vertex\\s+(\\w+)");
        static readonly Regex PragmaSurfaceRx = new Regex("#pragma\\s+surface\\s+[^\\r\\n]*");
        static readonly Regex StructRx = new Regex("struct\\s+(\\w+)\\s*\\{([^{}]*)\\}");
        static readonly Regex ShaderNameRx = new Regex("^(\\s*Shader\\s+\")([^\"]+)(\")", RegexOptions.Multiline);
        static readonly Regex FallbackTagRx = new Regex("\"VRCFallback\"\\s*=\\s*\"[^\"]*\"");
        static readonly Regex FallbackRx = new Regex("^\\s*Fallback\\s+(?:\"([^\"]*)\"|(Off))[ \\t]*(?://[^\\r\\n]*)?\\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        static readonly Regex UsePassRx = new Regex("^\\s*UsePass\\s+\"([^\"]+)\"\\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        sealed class Src { public string Abs, Key, Text; public bool Builtin; }
        sealed class InStruct { public string Name, Pos, Nor, Tan; public bool AddNor, AddTan; }

        static string Field(string body, string semantic)
        {
            // NORMAL_centroid and the like are the same semantic for a duplicate check
            Match m = new Regex("(?:float|half|fixed|min16float|real|int|uint)[1-4]?\\s+(\\w+)\\s*:\\s*" + semantic + "(?:_\\w+)?\\b").Match(body);
            return m.Success ? m.Groups[1].Value : null;
        }

        static string BuiltinDir => Path.Combine(EditorApplication.applicationContentsPath, "CGIncludes");

        /// <summary>Where an include line points, or null for one that cannot be found (left as it is).</summary>
        static string Resolve(string includer, string inc, out bool builtin)
        {
            builtin = false;
            string rel = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(includer), inc));
            if (File.Exists(rel)) return rel;
            if (inc.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string p = Path.GetFullPath(inc);
                if (File.Exists(p)) return p;
            }
            if (inc.StartsWith("Packages/", StringComparison.Ordinal))
            {
                var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(inc);
                if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                {
                    string inside = inc.Substring(("Packages/" + info.name).Length).TrimStart('/');
                    string p = Path.GetFullPath(Path.Combine(info.resolvedPath, inside));
                    if (File.Exists(p)) return p;
                }
            }
            string b = Path.Combine(BuiltinDir, inc);
            if (File.Exists(b)) { builtin = true; return Path.GetFullPath(b); }
            return null;
        }

        public static KSAntiRipShaderPatch.PatchResult Patch(string shaderPath, string shaderCode, string call, string idField,
                                                             IList<string> properties, int index, string folder)
        {
            var res = new KSAntiRipShaderPatch.PatchResult();
            try { return PatchCore(res, shaderPath, shaderCode, call, idField, properties, index, folder, false) ?? res; }
            catch (Exception e) { res.Why = "the universal patch failed: " + e.Message; return res; }
        }

        static KSAntiRipShaderPatch.PatchResult PatchCore(KSAntiRipShaderPatch.PatchResult res, string shaderPath, string shaderCode,
                                                           string call, string idField, IList<string> properties, int index,
                                                           string folder, bool withBuiltins)
        {
            // ── the files: the shader and everything it includes, each under a flat local name ──
            string mainAbs = Path.GetFullPath(shaderPath);
            var byAbs = new Dictionary<string, Src>(StringComparer.OrdinalIgnoreCase);
            var order = new List<Src>();
            int n = 0;
            Src Add(string abs, bool builtin)
            {
                if (byAbs.TryGetValue(abs, out Src s)) return s;
                bool unity = builtin || abs.StartsWith(Path.GetFullPath(BuiltinDir), StringComparison.OrdinalIgnoreCase);
                s = new Src { Abs = abs, Builtin = unity, Text = File.ReadAllText(abs),
                              // Unity's own includes keep their name, so every "#include \"UnityCG.cginc\"" in the copy (and in
                              // the code Unity generates for a surface shader) finds the same file
                              Key = unity ? Path.GetFileName(abs) : (abs == mainAbs ? Path.GetFileName(abs) : "ks" + (n++) + "_" + Path.GetFileName(abs)) };
                byAbs[abs] = s; order.Add(s);
                return s;
            }
            Src main = Add(mainAbs, false);
            var queue = new Queue<Src>(); queue.Enqueue(main);
            var builtinSeen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // abs -> include text
            while (queue.Count > 0)
            {
                Src f = queue.Dequeue();
                foreach (Match m in IncludeRx.Matches(f.Text))
                {
                    string target = Resolve(f.Abs, m.Groups[1].Value, out bool builtin);
                    if (target == null) continue;
                    if (builtin && !withBuiltins) { builtinSeen[target] = m.Groups[1].Value; continue; }
                    if (!byAbs.ContainsKey(target)) queue.Enqueue(Add(target, builtin));
                }
            }
            // every include of a copied file now names the local copy
            foreach (Src s in order)
                s.Text = IncludeRx.Replace(s.Text, m =>
                {
                    string target = Resolve(s.Abs, m.Groups[1].Value, out _);
                    return target != null && byAbs.TryGetValue(target, out Src t) ? m.Value.Replace(m.Groups[1].Value, t.Key) : m.Value;
                });
            string All() => string.Join("\n", order.Select(s => s.Text));

            // ── the vertex entries ──
            var entries = new HashSet<string>(PragmaVertexRx.Matches(All()).Cast<Match>().Select(m => m.Groups[1].Value));
            bool surface = PragmaSurfaceRx.IsMatch(main.Text);
            if (entries.Count == 0 && !surface) { res.Why = "the shader has no #pragma vertex or #pragma surface"; return res; }

            int patched = 0, procedural = 0;
            string why = null;
            // Every change is COLLECTED on the untouched text and applied afterwards, from the end of each file backwards:
            // positions found on the original text stay right (a shader with several passes, each with its own struct).
            var edits = new List<(Src file, int at, int len, string text)>();
            var structPatched = new HashSet<(Src, int)>();
            bool Overlaps(Src f, int at, int len) => edits.Any(x => x.file == f && at < x.at + x.len && x.at < at + len);

            // the fields a struct definition has (null when it has no POSITION)
            InStruct Read(Match def)
            {
                string body = def.Groups[2].Value;
                string pos = Field(body, "POSITION");
                if (pos == null) return null;
                string nor = Field(body, "NORMAL"), tan = Field(body, "TANGENT");
                return new InStruct { Name = def.Groups[1].Value, Pos = pos, Nor = nor ?? "ksNor", Tan = tan ?? "ksTan", AddNor = nor == null, AddTan = tan == null };
            }
            // the struct definition gains the id and, when it lacks them, the frame fields under the names the restore uses
            string PatchStruct(Src f, Match def, InStruct st)
            {
                if (!structPatched.Add((f, def.Index))) return null;
                string body = def.Groups[2].Value;
                if (Regex.IsMatch(body, ":\\s*TEXCOORD7\\b")) return "the vertex input " + st.Name + " already uses TEXCOORD7";
                string pos = Field(body, "POSITION"), nor = Field(body, "NORMAL"), tan = Field(body, "TANGENT");
                if (pos != st.Pos) return "the vertex input " + st.Name + " is declared with different fields";
                if (nor != null && nor != st.Nor || tan != null && tan != st.Tan) return "the vertex input " + st.Name + " is declared with different fields";
                // a NORMAL or TANGENT declared only in some variants (Unity's shadow VertexInput) is made unconditional: the
                // restore reads it in every variant, and the mesh always has it
                body = Hoist(body, "NORMAL", out string norDecl);
                body = Hoist(body, "TANGENT", out string tanDecl);
                string extra = "\n\tfloat4 " + idField + " : TEXCOORD7;" +
                               (nor == null ? "\n\tfloat3 " + st.Nor + " : NORMAL;" : "\n\t" + norDecl) +
                               (tan == null ? "\n\tfloat4 " + st.Tan + " : TANGENT;" : "\n\t" + tanDecl);
                edits.Add((f, def.Index, def.Length, "struct " + def.Groups[1].Value + "\n{" + extra + body + "}"));
                return null;
            }

            // the struct a function in `file` at `before` takes: the nearest definition before it in its own file (each
            // pass of a shader may define its own); otherwise EVERY definition of that name elsewhere (a vertex function in
            // a shared include is compiled with each pass's own struct), patched alike
            InStruct StructFor(string type, Src file, int before, out string err)
            {
                err = null;
                Match near = null;
                foreach (Match m in StructRx.Matches(file.Text))
                    if (m.Groups[1].Value == type && m.Index < before) near = m;
                if (near != null)
                {
                    InStruct st = Read(near);
                    if (st == null) return null;
                    err = PatchStruct(file, near, st);
                    return st;
                }
                InStruct first = null;
                foreach (Src o in order)
                    foreach (Match m in StructRx.Matches(o.Text))
                    {
                        if (m.Groups[1].Value != type) continue;
                        InStruct st = Read(m);
                        if (st == null) continue;
                        first = first ?? st;
                        err = err ?? PatchStruct(o, m, first);
                    }
                return first;
            }

            // vertex/fragment entries: the call goes first in the function's body
            var paramRx = new Regex("(?:\\b(?:in|inout)\\s+)?\\b(\\w+)\\s+(\\w+)(?:\\s*:\\s*\\w+)?\\s*(?:,|$)");
            foreach (string e in entries)
            {
                bool found = false;
                var defRx = new Regex("\\b[\\w<>]+\\s+" + e + "\\s*\\(([^(){};]*)\\)\\s*(?::\\s*\\w+\\s*)?\\{(\\s*UNITY_SETUP_INSTANCE_ID\\s*\\(\\s*\\w+\\s*\\)\\s*;)?");
                foreach (Src s in order)
                {
                    foreach (Match m in defRx.Matches(s.Text))
                    {
                        if (Overlaps(s, m.Index, m.Length)) { found = true; continue; }   // the same function, for another entry
                        string pars = m.Groups[1].Value;
                        int firstHash = pars.IndexOf('#');
                        string head = pars.Contains("#else") ? pars.Substring(0, pars.IndexOf("#else", StringComparison.Ordinal)) : pars;
                        // preprocessor lines out of the way of the parameter parse (Unity's "VertexInput v\n #ifdef ...")
                        string headClean = Regex.Replace(head, "^[ \\t]*#[^\\r\\n]*", "", RegexOptions.Multiline);
                        InStruct st = null; string var = null; string err = null; bool unconditional = true;
                        foreach (Match pm in paramRx.Matches(headClean))
                        {
                            st = StructFor(pm.Groups[1].Value, s, m.Index, out err);
                            if (st != null)
                            {
                                var = pm.Groups[2].Value;
                                unconditional = firstHash < 0 ||
                                                Regex.IsMatch(pars.Substring(0, firstHash), "\\b" + pm.Groups[1].Value + "\\s+" + var + "\\b");
                                break;
                            }
                        }
                        if (err != null) { why = why ?? err; continue; }
                        if (st == null)
                        {
                            // loose inputs ("float4 vertex : POSITION, ..."): gathered into a struct of ours for the restore
                            string loose = LooseCall(pars, e, shaderCode, call, idField, out string newPars);
                            if (loose != null)
                            {
                                edits.Add((s, m.Index, m.Length, LooseDefs(e, shaderCode, idField) + m.Value.Replace("(" + pars + ")", "(" + newPars + ")") + "\n" + loose));
                                found = true; patched++;
                                continue;
                            }
                            // a vertex function that reads no mesh input at all (its vertices come from SV_VertexID): the
                            // scrambled mesh draws exactly as the original, nothing to restore
                            if (IsProcedural(headClean)) { found = true; procedural++; continue; }
                            // its input is one of Unity's own (appdata_full, VertexInput, ...): copy those includes and try again
                            if (!withBuiltins && builtinSeen.Count > 0)
                                return PatchCore(new KSAntiRipShaderPatch.PatchResult(), shaderPath, shaderCode, call, idField, properties, index, folder, true);
                            why = why ?? ("the vertex function " + e + " takes no input with a POSITION");
                            continue;
                        }
                        string callLine = call + "_" + st.Name + "(" + var + ");";
                        // an input declared BEFORE any conditional is always there; one inside "#ifndef X ... #else" only then
                        if (!unconditional)
                        {
                            Match g = new Regex("#ifndef\\s+(\\w+)\\s+(?:in\\s+|inout\\s+)?" + st.Name + "\\s+" + var + "\\s+#else").Match(pars);
                            if (!g.Success) { why = why ?? ("the conditional input of " + e + " is not understood"); continue; }
                            callLine = "\n#ifndef " + g.Groups[1].Value + "\n" + callLine + "\n#endif\n";
                        }
                        edits.Add((s, m.Index, m.Length, Substitute(shaderCode, st) + "\n" + m.Value + "\n\t" + callLine));
                        found = true; patched++;
                    }
                }
                if (!found)
                {
                    // defined in one of Unity's own includes (Standard's vertBase, ...): copy those and try again
                    if (!withBuiltins && builtinSeen.Count > 0)
                        return PatchCore(new KSAntiRipShaderPatch.PatchResult(), shaderPath, shaderCode, call, idField, properties, index, folder, true);
                    why = why ?? ("the vertex function " + e + " was not found");
                }
            }
            if (why != null) { res.Why = why; return res; }

            // apply, from the end of each file backwards
            foreach (var g in edits.GroupBy(x => x.file))
            {
                string t = g.Key.Text;
                foreach (var x in g.OrderByDescending(x => x.at)) t = t.Substring(0, x.at) + x.text + t.Substring(x.at + x.len);
                g.Key.Text = t;
            }

            // surface shaders: the vertex modifier (its own, or one added) gets the restore
            if (surface)
            {
                string surfWhy = PatchSurface(main, order, shaderCode, call, idField, ref patched);
                if (surfWhy != null) { res.Why = surfWhy; return res; }
            }
            // a shader whose every vertex function ignores the mesh's positions needs no restore: its copy is only renamed
            if (patched == 0 && procedural == 0) { res.Why = "no vertex input could be patched"; return res; }

            // ── the main file: name, VRChat fallback, key properties, a restored shadow instead of a Fallback's ──
            string sh = main.Text;
            if (UsePassRx.Matches(sh).Cast<Match>().Any(m => m.Groups[1].Value.IndexOf("SHADOWCASTER", StringComparison.OrdinalIgnoreCase) < 0))
            { res.Why = "it uses a pass of another shader (UsePass)"; return res; }
            bool ownShadow = Regex.IsMatch(sh, "\"LightMode\"\\s*=\\s*\"ShadowCaster\"", RegexOptions.IgnoreCase);
            bool shadowUsePass = UsePassRx.IsMatch(sh);
            Match fb = FallbackRx.Match(sh);
            bool fallbackShadow = fb.Success && fb.Groups[2].Value.Length == 0 && fb.Groups[1].Value.Length > 0;
            if (!surface && !ownShadow && (fallbackShadow || shadowUsePass))
            {
                bool cutout = (fb.Success && fb.Groups[1].Value.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0) ||
                              (shadowUsePass && UsePassRx.Match(sh).Groups[1].Value.IndexOf("Cutout", StringComparison.OrdinalIgnoreCase) >= 0);
                string pass = ShadowPass(shaderCode, call, idField, cutout && Regex.IsMatch(sh, "\\b_Cutoff\\s*\\(") && Regex.IsMatch(sh, "\\b_MainTex\\s*\\("));
                sh = UsePassRx.Replace(sh, "");
                // in every SubShader that has no shadow of its own: the GPU runs the first one it can
                foreach (int end in SubShaderEnds(sh).OrderByDescending(x => x))
                    sh = sh.Substring(0, end) + pass + "\n" + sh.Substring(end);
                if (!SubShaderEnds(sh).Any()) { res.Why = "its SubShader could not be found"; return res; }
            }
            if (fb.Success) sh = FallbackRx.Replace(sh, "\tFallback Off", 1);
            sh = ShaderNameRx.Replace(sh, m => m.Groups[1].Value + "Hidden/Locked/KS/" + index + "_" + ShortName(shaderPath) + m.Groups[3].Value, 1);
            sh = FallbackTagRx.Replace(sh, "\"VRCFallback\" = \"Hidden\"");
            var props = new StringBuilder();
            foreach (string p in properties) props.Append("\t\t[HideInInspector] ").Append(p).Append(" (\"\", Float) = 0\n");
            Match pr = Regex.Match(sh, "\\bProperties\\s*\\{");
            if (pr.Success) sh = sh.Substring(0, pr.Index + pr.Length) + "\n" + props + sh.Substring(pr.Index + pr.Length);
            else
            {
                Match head = ShaderNameRx.Match(sh);
                int brace = head.Success ? sh.IndexOf('{', head.Index) : -1;
                if (brace < 0) { res.Why = "its Shader block could not be found"; return res; }
                sh = sh.Substring(0, brace + 1) + "\n\tProperties\n\t{\n" + props + "\t}\n" + sh.Substring(brace + 1);
            }
            main.Text = sh;

            // ── write, import, compile every pass ──
            string sub = folder + "/kss" + index;
            if (AssetDatabase.IsValidFolder(sub)) AssetDatabase.DeleteAsset(sub);
            AssetDatabase.CreateFolder(folder, "kss" + index);
            foreach (Src s in order) File.WriteAllText(Path.Combine(sub, s.Key), s.Text);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Shader patchedShader = AssetDatabase.LoadAssetAtPath<Shader>(sub + "/" + main.Key);
            if (patchedShader == null) { res.Why = "the patched shader did not import"; return res; }
            var probe = new Material(patchedShader);
            try { for (int p = 0; p < patchedShader.passCount; p++) ShaderUtil.CompilePass(probe, p, true); }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            var errs = ShaderUtil.GetShaderMessages(patchedShader)
                                 .Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToList();
            if (errs.Count > 0 || ShaderUtil.ShaderHasError(patchedShader))
            {
                if (!withBuiltins && builtinSeen.Count > 0 && errs.Any(x => x.message.IndexOf("undeclared", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                          x.message.IndexOf("redefinition", StringComparison.OrdinalIgnoreCase) >= 0))
                    return PatchCore(new KSAntiRipShaderPatch.PatchResult(), shaderPath, shaderCode, call, idField, properties, index, folder, true);
                res.Why = "the patched shader did not compile" + (errs.Count > 0 ? ": " + errs[0].message + " (line " + errs[0].line + ")" : "");
                return res;
            }
            res.Shader = patchedShader;
            res.Entries = patched;
            res.Files = order.Count;
            return res;
        }

        /// <summary>
        /// A surface shader: its "vertex:V" modifier, when it has one, takes the restore first (its input struct gains the
        /// id); otherwise a modifier is added on an input struct with appdata_full's fields plus the id. "addshadow" makes
        /// Unity generate the shadow pass from the same, restored, vertex input (unless the shader asked for none).
        /// </summary>
        static string PatchSurface(Src main, List<Src> order, string shaderCode, string call, string idField, ref int patched)
        {
            Match pm = PragmaSurfaceRx.Match(main.Text);
            string pragma = pm.Value;
            Match vm = Regex.Match(pragma, "\\bvertex:(\\w+)");
            string newPragma = pragma;
            // Tessellated: Unity calls the modifier AFTER tessellation, on new vertices whose id is a blend of three ids,
            // so the restore cannot run there. Without Phong smoothing and with a modifier that does not move the
            // vertices, tessellation only splits flat triangles: the copy drops it and draws the same. Otherwise the shape
            // is made after the restore could run, and the mesh keeps restore shapes.
            Match tm = Regex.Match(pragma, "\\btessellate:\\w+");
            if (tm.Success)
            {
                if (Regex.IsMatch(pragma, "\\btessphong:")) return "it tessellates with Phong smoothing";
                if (vm.Success && MovesVertices(order, vm.Groups[1].Value)) return "it displaces its tessellated vertices";
                newPragma = Regex.Replace(newPragma, "\\s*\\btessellate:\\w+", "");
            }
            if (!Regex.IsMatch(pragma, "\\b(?:addshadow|noshadow)\\b")) newPragma += " addshadow";
            if (vm.Success)
            {
                string fn = vm.Groups[1].Value;
                bool done = false;
                foreach (Src s in order)
                {
                    var defRx = new Regex("\\bvoid\\s+" + fn + "\\s*\\(\\s*inout\\s+(\\w+)\\s+(\\w+)([^)]*)\\)\\s*\\{");
                    Match d = defRx.Match(s.Text);
                    if (!d.Success) continue;
                    string type = d.Groups[1].Value, var = d.Groups[2].Value;
                    InStruct st;
                    if (type == "appdata_full" || type == "appdata_base" || type == "appdata_tan")
                    {
                        // Unity's own input: the same fields in a struct of ours, plus the id. Every other use of the type
                        // in the shader's own code follows (a tessellation function takes the same input).
                        st = new InStruct { Name = "ks_" + type, Pos = "vertex", Nor = "normal", Tan = "tangent" };
                        // the call is written with the NEW name: "\b" + type + "\b" does not reach inside "<call>_appdata_full"
                        string body = s.Text.Substring(0, d.Index) + "void " + fn + "(inout " + type + " " + var + d.Groups[3].Value + ")\n{\n\t" +
                                      "__KS_CALL__(" + var + ");" + s.Text.Substring(d.Index + d.Length);
                        body = Regex.Replace(body, "\\b" + type + "\\b", st.Name).Replace("__KS_CALL__", call + "_" + st.Name);
                        // declared before its first use (a tessellation function can come before the modifier)
                        int first = Regex.Match(body, "\\b" + st.Name + "\\b").Index;
                        int at = body.LastIndexOf('\n', Math.Max(0, first - 1)) + 1;
                        s.Text = body.Substring(0, at) + AppdataStruct(st.Name, idField) + "\n" + Substitute(shaderCode, st) + "\n" + body.Substring(at);
                    }
                    else
                    {
                        Match def = null;
                        foreach (Match m in StructRx.Matches(s.Text)) if (m.Groups[1].Value == type && m.Index < d.Index) def = m;
                        if (def == null) return "the vertex modifier " + fn + "'s input " + type + " was not found";
                        string bodyS = def.Groups[2].Value;
                        string pos = Field(bodyS, "POSITION"), nor = Field(bodyS, "NORMAL"), tan = Field(bodyS, "TANGENT");
                        if (pos == null) return "the vertex modifier " + fn + " takes no input with a POSITION";
                        if (Regex.IsMatch(bodyS, ":\\s*TEXCOORD7\\b")) return "the vertex input " + type + " already uses TEXCOORD7";
                        st = new InStruct { Name = type, Pos = pos, Nor = nor ?? "ksNor", Tan = tan ?? "ksTan" };
                        string fnText = Substitute(shaderCode, st) + "\n" + d.Value + "\n\t" + call + "_" + st.Name + "(" + var + ");";
                        string structText = "struct " + type + "\n{\n\tfloat4 " + idField + " : TEXCOORD7;" +
                                            (nor == null ? "\n\tfloat3 ksNor : NORMAL;" : "") + (tan == null ? "\n\tfloat4 ksTan : TANGENT;" : "") + bodyS + "}";
                        // the function comes after its struct: replace it first, then the struct, so positions hold
                        s.Text = s.Text.Substring(0, d.Index) + fnText + s.Text.Substring(d.Index + d.Length);
                        s.Text = s.Text.Substring(0, def.Index) + structText + s.Text.Substring(def.Index + def.Length);
                    }
                    done = true; patched++;
                    break;
                }
                if (!done) return "the surface shader's vertex modifier " + fn + " was not found";
            }
            else
            {
                var st = new InStruct { Name = "ks_appdata_full", Pos = "vertex", Nor = "normal", Tan = "tangent" };
                string fn = "ks_surfvert";
                newPragma += " vertex:" + fn;
                string def = AppdataStruct(st.Name, idField) + "\n" + Substitute(shaderCode, st) + "\nvoid " + fn + "(inout " + st.Name + " v)\n{\n\t" +
                             call + "_" + st.Name + "(v);\n}\n";
                // the modifier is defined before the pragma's block code that the generator appends to
                main.Text = main.Text.Substring(0, pm.Index) + def + main.Text.Substring(pm.Index);
                pm = PragmaSurfaceRx.Match(main.Text);
                patched++;
            }
            main.Text = main.Text.Substring(0, pm.Index) + newPragma + main.Text.Substring(pm.Index + pm.Length);
            return null;
        }

        /// <summary>
        /// Whether the surface vertex modifier fn can move its vertices: it writes the position (var.vertex), hands the
        /// whole input to another function, or is not found. Comments are ignored.
        /// </summary>
        static bool MovesVertices(List<Src> order, string fn)
        {
            foreach (Src s in order)
            {
                Match d = Regex.Match(s.Text, "\\bvoid\\s+" + fn + "\\s*\\(\\s*inout\\s+\\w+\\s+(\\w+)[^)]*\\)\\s*\\{");
                if (!d.Success) continue;
                int open = d.Index + d.Length - 1, depth = 0, end = -1;
                for (int i = open; i < s.Text.Length; i++)
                {
                    if (s.Text[i] == '{') depth++;
                    else if (s.Text[i] == '}' && --depth == 0) { end = i; break; }
                }
                if (end < 0) return true;
                string body = Regex.Replace(s.Text.Substring(open + 1, end - open - 1), "//[^\\n]*|/\\*.*?\\*/", "", RegexOptions.Singleline);
                string v = Regex.Escape(d.Groups[1].Value);
                return Regex.IsMatch(body, "\\b" + v + "\\s*\\.\\s*vertex\\b") || Regex.IsMatch(body, "[(,]\\s*" + v + "\\s*[,)]");
            }
            return true;
        }

        /// <summary>
        /// A vertex function with loose inputs ("float4 vertex : POSITION, float2 uv : TEXCOORD0"): the id (and a normal
        /// and tangent when it has none) become extra inputs, and the first lines gather them in a struct of ours, restore
        /// it and give the position back. Returns the text that replaces the function's head (struct, restore, head, first
        /// lines), and the new parameter list; null when there is no POSITION input or the inputs are conditional.
        /// </summary>
        static string LooseCall(string pars, string fn, string shaderCode, string call, string idField, out string newPars)
        {
            newPars = null;
            if (pars.Contains("#")) return null;
            Match p = Regex.Match(pars, "\\b(?:float|half|fixed)[34]\\s+(\\w+)\\s*:\\s*POSITION\\b");
            if (!p.Success) return null;
            Match nr = Regex.Match(pars, "\\b(?:float|half|fixed)[34]\\s+(\\w+)\\s*:\\s*NORMAL\\b");
            Match tg = Regex.Match(pars, "\\b(?:float|half|fixed)4\\s+(\\w+)\\s*:\\s*TANGENT\\b");
            string idIn = idField + "_in", nor = nr.Success ? nr.Groups[1].Value : "ks_nrm_in", tan = tg.Success ? tg.Groups[1].Value : "ks_tan_in";
            newPars = pars.TrimEnd() + (pars.Trim().Length > 0 ? ", " : "") + "float4 " + idIn + " : TEXCOORD7" +
                      (nr.Success ? "" : ", float3 ks_nrm_in : NORMAL") + (tg.Success ? "" : ", float4 ks_tan_in : TANGENT");
            string sname = "ks_loose_" + fn;
            var st = new InStruct { Name = sname, Pos = "p", Nor = "n", Tan = "t" };
            string pos = p.Groups[1].Value;
            // the head is rebuilt by the caller with newPars; this returns the lines that follow its "{"
            return "\t" + sname + " ksl; ksl.p = 0; ksl.p.xyz = " + pos + ".xyz; ksl.n = " + nor + ".xyz; ksl.t = 0; ksl.t.xyz = " + tan +
                   ".xyz; ksl." + idField + " = " + idIn + ";\n\t" + call + "_" + sname + "(ksl); " + pos + ".xyz = ksl.p.xyz;\n";
        }

        /// <summary>Takes the declaration of the field with <paramref name="semantic"/> out of a struct body (to put it back
        /// unconditionally at the top); the body unchanged and decl null when there is none.</summary>
        static string Hoist(string body, string semantic, out string decl)
        {
            Match m = new Regex("^[ \\t]*((?:float|half|fixed|min16float|real)[1-4]?\\s+\\w+\\s*:\\s*" + semantic + "(?:_\\w+)?\\s*;)[^\\r\\n]*\\r?\\n?",
                                RegexOptions.Multiline).Match(body);
            decl = m.Success ? m.Groups[1].Value : null;
            return m.Success ? body.Remove(m.Index, m.Length) : body;
        }

        /// <summary>Parameters that read nothing from the mesh: system values (SV_VertexID, SV_InstanceID) and outputs.</summary>
        static bool IsProcedural(string pars)
        {
            foreach (string raw in pars.Split(','))
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                if (p.StartsWith("out ", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(p, ":\\s*SV_\\w+\\s*$")) continue;
                return false;
            }
            return true;
        }

        /// <summary>The struct and restore function a loose-input entry needs, placed before it.</summary>
        static string LooseDefs(string fn, string shaderCode, string idField)
        {
            string sname = "ks_loose_" + fn;
            return "struct " + sname + " { float4 p; float3 n; float4 t; float4 " + idField + "; };\n" +
                   Substitute(shaderCode, new InStruct { Name = sname, Pos = "p", Nor = "n", Tan = "t" }) + "\n";
        }

        /// <summary>appdata_full's fields (UnityCG.cginc) plus the id, under another name.</summary>
        static string AppdataStruct(string name, string idField) =>
            "struct " + name + "\n{\n\tfloat4 vertex : POSITION;\n\tfloat4 tangent : TANGENT;\n\tfloat3 normal : NORMAL;\n" +
            "\tfloat4 texcoord : TEXCOORD0;\n\tfloat4 texcoord1 : TEXCOORD1;\n\tfloat4 texcoord2 : TEXCOORD2;\n\tfloat4 texcoord3 : TEXCOORD3;\n" +
            "\tfixed4 color : COLOR;\n\tfloat4 " + idField + " : TEXCOORD7;\n\tUNITY_VERTEX_INPUT_INSTANCE_ID\n};";

        /// <summary>A shadow caster pass of our own, restored, for a shader whose shadow came from a Fallback/UsePass.</summary>
        static string ShadowPass(string shaderCode, string call, string idField, bool cutout)
        {
            var st = new InStruct { Name = "ks_shadow_in", Pos = "vertex", Nor = "normal", Tan = "tangent" };
            var sb = new StringBuilder();
            sb.Append("\tPass\n\t{\n\t\tName \"KSSHADOW\"\n\t\tTags { \"LightMode\" = \"ShadowCaster\" }\n\t\tCGPROGRAM\n");
            sb.Append("\t\t#pragma vertex ks_shadow_vert\n\t\t#pragma fragment ks_shadow_frag\n\t\t#pragma multi_compile_shadowcaster\n");
            sb.Append("\t\t#include \"UnityCG.cginc\"\n");
            sb.Append("struct ks_shadow_in\n{\n\tfloat4 vertex : POSITION;\n\tfloat3 normal : NORMAL;\n\tfloat4 tangent : TANGENT;\n\tfloat2 texcoord : TEXCOORD0;\n\tfloat4 ")
              .Append(idField).Append(" : TEXCOORD7;\n};\n");
            sb.Append(Substitute(shaderCode, st)).Append('\n');
            if (cutout) sb.Append("sampler2D _MainTex; float4 _MainTex_ST; fixed _Cutoff;\n");
            sb.Append("struct ks_shadow_v2f { V2F_SHADOW_CASTER;").Append(cutout ? " float2 uv : TEXCOORD1;" : "").Append(" };\n");
            sb.Append("ks_shadow_v2f ks_shadow_vert(ks_shadow_in v)\n{\n\t").Append(call).Append("_ks_shadow_in(v);\n\tks_shadow_v2f o;\n")
              .Append(cutout ? "\to.uv = TRANSFORM_TEX(v.texcoord, _MainTex);\n" : "")
              .Append("\tTRANSFER_SHADOW_CASTER_NORMALOFFSET(o)\n\treturn o;\n}\n");
            sb.Append("float4 ks_shadow_frag(ks_shadow_v2f i) : SV_Target\n{\n")
              .Append(cutout ? "\tclip(tex2D(_MainTex, i.uv).a - _Cutoff);\n" : "")
              .Append("\tSHADOW_CASTER_FRAGMENT(i)\n}\n");
            sb.Append("\t\tENDCG\n\t}\n");
            return sb.ToString();
        }

        /// <summary>The index of the closing brace of every SubShader block (CG/HLSL code skipped: its braces are not ShaderLab's).</summary>
        static IEnumerable<int> SubShaderEnds(string sh)
        {
            foreach (Match sub in Regex.Matches(sh, "\\bSubShader\\s*\\{"))
            {
                int depth = 0;
                bool inBlock = false;
                for (int i = sub.Index + sub.Length - 1; i < sh.Length; i++)
                {
                    if (!inBlock && (Starts(sh, i, "CGPROGRAM") || Starts(sh, i, "HLSLPROGRAM") || Starts(sh, i, "CGINCLUDE") || Starts(sh, i, "HLSLINCLUDE"))) inBlock = true;
                    if (inBlock && (Starts(sh, i, "ENDCG") || Starts(sh, i, "ENDHLSL"))) { inBlock = false; i += 4; continue; }
                    if (inBlock) continue;
                    if (sh[i] == '{') depth++;
                    else if (sh[i] == '}') { depth--; if (depth == 0) { yield return i; break; } }
                }
            }
        }

        static bool Starts(string s, int at, string word) => string.CompareOrdinal(s, at, word, 0, word.Length) == 0;

        static string Substitute(string code, InStruct s) =>
            code.Replace("__KS_STRUCT__", s.Name).Replace("__KS_POS__", s.Pos).Replace("__KS_NOR__", s.Nor).Replace("__KS_TAN__", s.Tan);

        static string ShortName(string path) =>
            Regex.Replace(Path.GetFileNameWithoutExtension(path), "[^A-Za-z0-9_]", "");
    }
}
