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
    /// 3.16: closes a mesh, and its main texture, through a lilToon material's own shader -- the lilToon side of what
    /// KSAntiRipShaderPatch does for locked Poiyomi. Like the rest of KS Anti-Rip here, it only pastes what the app
    /// sends (Plan.shaderCode, Plan.textureCode); the mesh and the texture are built by KSAntiRipShaderPatch.
    ///
    /// Why lilToon needs its own file: a lilToon material points at "lilToon" (lts.shader), which carries no vertex or
    /// fragment code. Its passes come in by UsePass from a shared ltspass_*.shader, and their code lives in shared
    /// package includes. So, once per build, lilToon's Includes/ folder is copied into the build folder and that copy
    /// is edited (the id channel on TEXCOORD7, the restore call in every vert(appdata input), the reads of the main
    /// texture and of the outline texture); then each lts*.shader the avatar uses is flattened into one shader that
    /// inlines its UsePass passes and points at the edited copy, under "Hidden/Locked/KS/…" with a Hidden VRChat
    /// fallback. The package and the project are never edited.
    ///
    /// Not handled (the mesh keeps the restore shapes, safely): tessellation shaders, a material whose ID mask reads
    /// UV7, and lilToon's own avatar encryption.
    /// </summary>
    public static class KSAntiRipLilToon
    {
        public sealed class FlattenResult
        {
            public string ShaderPath;       // the self-contained copy, or null when it could not be made
            public string IncludeDir;       // project-relative folder the copy's "Includes/…" now point at
            public string Why = "";
            public int Passes;              // how many UsePass passes were inlined
            public int Entries;             // vertex functions that call the restore
            public bool TextureOk;          // the main texture's reads go through the texture function
            public int TextureSites;
            public string TextureWhy = "";
            internal bool TextureTried;
        }

        /// <summary>Slots lilToon declares for the other render pipelines and never samples in the built-in one;
        /// lilToon copies the main texture into them, so they are cleared on a patched copy.</summary>
        public static readonly string[] UnreadSlots = { "_BaseMap", "_BaseColorMap" };

        /// <summary>The outline reads its own texture, very often the main one. Its reads go through the texture
        /// function too, with this id (0 = read plainly), set only when the outline wears the laid-out main texture.</summary>
        public const string OutlineSlot = "_OutlineTex";
        public static string OutlineTidProperty(string tidProperty) => tidProperty + "_o";

        // ── which materials this recognises ──────────────────────────────────────────────────────

        /// <summary>A lilToon lts-family shader (lts*, ltsl*, ltsmulti*): an imported .shader under a lilToon package.</summary>
        public static bool IsLilToon(Shader shader)
        {
            if (shader == null) return false;
            string path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path)) return false;
            string file = Path.GetFileName(path);
            return path.IndexOf("liltoon", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   file.StartsWith("lts", StringComparison.OrdinalIgnoreCase) &&
                   !file.StartsWith("ltspass", StringComparison.OrdinalIgnoreCase) &&
                   file.EndsWith(".shader", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Whether a lilToon material's mesh can be closed by its shader. Not with tessellation (its domain stage calls
        /// vert() on interpolated vertices, whose ids mean nothing), and not when its ID mask reads UV7 (the id channel).
        /// </summary>
        public static bool CanPatch(Material m)
        {
            if (m == null || !IsLilToon(m.shader)) return false;
            string file = Path.GetFileName(AssetDatabase.GetAssetPath(m.shader));
            if (file.IndexOf("tess", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (m.HasProperty("_IDMaskFrom") && Mathf.RoundToInt(m.GetFloat("_IDMaskFrom")) == 7) return false;
            return true;
        }

        // ── entry points ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A self-contained copy of <paramref name="shaderPath"/> (a lts*.shader) that closes the mesh with the app's
        /// restore function and, when the texture arguments are given, reads the laid-out main texture back. If the copy
        /// with the texture rewrite does not compile, it is made again without it, so the vertices do not pay for it.
        /// </summary>
        public static FlattenResult PatchFlatten(string shaderPath, int index, string folder,
                                                 string shaderCode, string shaderCall, string shaderIdField,
                                                 IList<string> shaderProperties,
                                                 string textureCode, string textureFn, string tidProperty)
        {
            bool wantTexture = !string.IsNullOrEmpty(textureCode) && !string.IsNullOrEmpty(textureFn) && !string.IsNullOrEmpty(tidProperty);
            FlattenResult r = PatchOnce(shaderPath, index, folder, shaderCode, shaderCall, shaderIdField, shaderProperties,
                                        wantTexture ? textureCode : null, textureFn, tidProperty);
            if (r.ShaderPath == null && r.TextureTried)
            {
                string why = r.Why;
                string sub = folder + "/kss" + index;
                if (AssetDatabase.IsValidFolder(sub)) AssetDatabase.DeleteAsset(sub);
                r = PatchOnce(shaderPath, index, folder, shaderCode, shaderCall, shaderIdField, shaderProperties, null, null, null);
                r.TextureWhy = "with the main texture rewritten it did not compile (" + why + ")";
            }
            return r;
        }

        /// <summary>Flatten only, with lilToon's includes unchanged (bench: proves the flatten alone changes nothing on screen).</summary>
        public static FlattenResult Flatten(string shaderPath, int index, string folder)
            => PatchOnce(shaderPath, index, folder, null, null, null, Array.Empty<string>(), null, null, null);

        static FlattenResult PatchOnce(string shaderPath, int index, string folder,
                                       string shaderCode, string shaderCall, string shaderIdField, IList<string> shaderProperties,
                                       string textureCode, string textureFn, string tidProperty)
        {
            var r = new FlattenResult();
            try
            {
                if (!File.Exists(shaderPath)) { r.Why = "the lilToon shader file is gone"; return r; }
                string dir = Path.GetDirectoryName(shaderPath).Replace('\\', '/');
                bool restore = !string.IsNullOrEmpty(shaderCode) && !string.IsNullOrEmpty(shaderCall) && !string.IsNullOrEmpty(shaderIdField);
                bool texture = restore && !string.IsNullOrEmpty(textureCode);
                string name = !restore ? "_lilInc" : texture ? "_lilIncT" : "_lilIncV";

                Includes inc = PrepareIncludes(dir + "/Includes", folder, name, restore ? shaderCode : null, shaderCall, shaderIdField,
                                               texture ? textureCode : null, textureFn, tidProperty);
                r.IncludeDir = inc.Dir;
                if (inc.Why != null) { r.Why = inc.Why; return r; }
                r.Entries = inc.Entries;
                r.TextureSites = inc.TextureSites;
                r.TextureWhy = inc.TextureWhy;
                r.TextureTried = inc.TextureOk;

                var props = new List<string>(shaderProperties ?? Array.Empty<string>());
                if (inc.TextureOk) { props.Add(tidProperty); props.Add(OutlineTidProperty(tidProperty)); }
                if (!FlattenInto(shaderPath, index, folder, inc.Dir, props, inc.TextureOk, r)) return r;
                r.TextureOk = inc.TextureOk;
                return r;
            }
            catch (Exception e)
            {
                r.Why = "the lilToon patch threw: " + e.Message;
                return r;
            }
        }

        // ── the shared includes, edited once per build ───────────────────────────────────────────

        sealed class Includes
        {
            public string Dir;
            public string Why;              // null when usable
            public int Entries;
            public bool TextureOk;
            public int TextureSites;
            public string TextureWhy = "";
        }

        // per build folder (each build has its own), so a second lilToon shader of the same build reuses the edit
        static readonly Dictionary<string, Includes> Prepared = new Dictionary<string, Includes>(StringComparer.Ordinal);

        static Includes PrepareIncludes(string srcInc, string folder, string name, string shaderCode, string shaderCall,
                                        string idField, string textureCode, string textureFn, string tidProperty)
        {
            string root = folder + "/" + name, incDir = root + "/Includes";
            if (Prepared.TryGetValue(incDir, out Includes known) && AssetDatabase.IsValidFolder(incDir)) return known;
            if (AssetDatabase.IsValidFolder(root)) AssetDatabase.DeleteAsset(root);

            var inc = new Includes { Dir = incDir };
            if (!Directory.Exists(srcInc)) { inc.Why = "lilToon's Includes folder is not where it should be"; return inc; }

            var files = new Dictionary<string, string>(StringComparer.Ordinal);   // relative path -> text, the .hlsl/.cginc
            foreach (string f in Directory.GetFiles(srcInc, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                string rel = f.Substring(srcInc.Length).TrimStart('/', '\\').Replace('\\', '/');
                if (rel.EndsWith(".hlsl", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".cginc", StringComparison.OrdinalIgnoreCase))
                    files[rel] = File.ReadAllText(f);
            }

            if (!string.IsNullOrEmpty(shaderCode))
                inc.Why = EditIncludes(files, shaderCode, shaderCall, idField, textureCode, textureFn, tidProperty, inc);

            if (inc.Why == null)
            {
                foreach (string f in Directory.GetFiles(srcInc, "*", SearchOption.AllDirectories))
                {
                    if (f.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string rel = f.Substring(srcInc.Length).TrimStart('/', '\\').Replace('\\', '/');
                    string dst = incDir + "/" + rel;
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (files.TryGetValue(rel, out string text)) File.WriteAllText(dst, text);
                    else File.Copy(f, dst, true);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            Prepared[incDir] = inc;
            return inc;
        }

        static readonly Regex EncryptionOnRx = new Regex(@"^\s*#define\s+LIL_FEATURE_ENCRYPTION\b", RegexOptions.Multiline);
        static readonly Regex DefineTex7Rx = new Regex(@"^(\s*)#define\s+LIL_APP_TEXCOORD7\b", RegexOptions.Multiline);
        static readonly Regex AppdataOpenRx = new Regex(@"struct\s+(appdata|appdataCopy)\s*\{");
        // the two conversion functions start with "appdata(Copy) o;": the id is copied right after that declaration
        static readonly Regex AppdataCopyFnRx = new Regex(@"appdata(?:Copy)?\s+appdata(?:OriginalToCopy|CopyToOriginal)\s*\([^)]*\)\s*\{\s*appdata(?:Copy)?\s+o\s*;");
        static readonly Regex VertSignatureRx = new Regex(@"\b\w+\s+vert\s*\(\s*appdata\s+input\s*\)");
        static readonly Regex SetupInstanceRx = new Regex(@"LIL_SETUP_INSTANCE_ID\s*\(\s*input\s*\)\s*;");
        static readonly Regex MainReadRx = new Regex(@"\bLIL_SAMPLE_2D(_POM)?\s*\(\s*_MainTex\s*,");
        static readonly Regex OutlineReadRx = new Regex(@"\bLIL_SAMPLE_2D\s*\(\s*_OutlineTex\s*,");
        static readonly Regex TexNameRx = new Regex(@"\b(_MainTex|_OutlineTex)\b");

        static readonly string[] VertFiles = { "lil_common_vert.hlsl", "lil_common_vert_fur.hlsl", "lil_pass_forward_fakeshadow.hlsl" };

        /// <summary>Edits <paramref name="files"/> in memory; null on success, else why nothing can be used.</summary>
        static string EditIncludes(Dictionary<string, string> files, string shaderCode, string call, string idField,
                                   string textureCode, string textureFn, string tidProperty, Includes inc)
        {
            const string appdataFile = "lil_common_appdata.hlsl";
            if (!files.TryGetValue(appdataFile, out string appdata)) return appdataFile + " is missing";
            if (files.TryGetValue("lil_replace_keywords.hlsl", out string keywords) && EncryptionOnRx.IsMatch(keywords))
                return "lilToon's avatar encryption is on, and it already uses UV7";

            // 1) the vertex input: TEXCOORD7 freed for the id channel (lilToon's uv7 only serves the ID mask from UV7 and
            // the avatar encryption, both refused above), normal and tangent always present (the restore reads both, and
            // some passes leave the tangent out), the id field on both structs and copied by both conversions.
            appdata = DefineTex7Rx.Replace(appdata, "$1//#define LIL_APP_TEXCOORD7");
            int firstStruct = AppdataOpenRx.Match(appdata).Index;
            if (!AppdataOpenRx.IsMatch(appdata)) return "struct appdata was not found";
            appdata = appdata.Insert(firstStruct,
                "#if defined(LIL_APP_TEXCOORD7)\n    #undef LIL_APP_TEXCOORD7\n#endif\n" +
                "#if !defined(LIL_APP_NORMAL)\n    #define LIL_APP_NORMAL\n#endif\n" +
                "#if !defined(LIL_APP_TANGENT)\n    #define LIL_APP_TANGENT\n#endif\n\n");
            int structs = 0;
            appdata = AppdataOpenRx.Replace(appdata, m => { structs++; return m.Value + "\n    float4 " + idField + " : TEXCOORD7;"; });
            if (structs != 2) return "the vertex input structs are not the expected two (" + structs + ")";
            int copies = 0;
            appdata = AppdataCopyFnRx.Replace(appdata, m => { copies++; return m.Value + "\n    o." + idField + " = i." + idField + ";"; });
            if (copies != 2) return "the vertex input conversions are not the expected two (" + copies + ")";

            // 2) the restore call, first thing after the instance setup of every vert(appdata input) -- and only there
            // (fakeshadow's fragment function sets up its instance id the same way).
            foreach (string vf in VertFiles)
            {
                if (!files.TryGetValue(vf, out string text)) continue;
                var sigs = VertSignatureRx.Matches(text).Cast<Match>().ToList();
                for (int s = sigs.Count - 1; s >= 0; s--)
                {
                    int from = sigs[s].Index + sigs[s].Length;
                    int bodyEnd = text.IndexOf("\n}", from, StringComparison.Ordinal);
                    Match setup = SetupInstanceRx.Match(text, from);
                    if (!setup.Success || (bodyEnd >= 0 && setup.Index > bodyEnd))
                        return vf + ": a vertex function does not set up its instance id where expected";
                    text = text.Insert(setup.Index + setup.Length, "\n    " + call + "_appdata(input);");
                    inc.Entries++;
                }
                files[vf] = text;
            }
            if (inc.Entries == 0) return "lilToon's vertex function was not found";

            // 3) the reads of the main texture (and of the outline texture), all or none.
            var tail = new StringBuilder();
            if (!string.IsNullOrEmpty(textureCode))
            {
                string why = RewriteTextures(files, textureFn, tidProperty, out Dictionary<string, string> rewritten, out int sites);
                if (why != null) inc.TextureWhy = why;
                else if (sites == 0) inc.TextureWhy = "lilToon never reads the main texture";
                else
                {
                    foreach (var kv in rewritten) files[kv.Key] = kv.Value;
                    inc.TextureOk = true;
                    inc.TextureSites = sites;
                    bool declared(string n) => files.Values.Any(t => Regex.IsMatch(t, @"float4\s+" + n + @"\b"));
                    if (!declared("_MainTex_TexelSize")) tail.Append("float4 _MainTex_TexelSize;\n");
                    if (!declared("_OutlineTex_TexelSize")) tail.Append("float4 _OutlineTex_TexelSize;\n");
                    tail.Append("float ").Append(OutlineTidProperty(tidProperty)).Append(";\n");
                    tail.Append(textureCode).Append('\n');
                }
            }

            // the app's code, once, at the end of the vertex-input file: every pass includes it before vert and frag
            tail.Append(Substitute(shaderCode)).Append('\n');
            int guardEnd = appdata.LastIndexOf("#endif", StringComparison.Ordinal);
            if (guardEnd < 0) return appdataFile + " has no include guard";
            appdata = appdata.Insert(guardEnd, "\n" + tail + "\n");
            files[appdataFile] = appdata;
            return null;
        }

        /// <summary>
        /// Every LIL_SAMPLE_2D(_POM)(_MainTex, …) and LIL_SAMPLE_2D(_OutlineTex, …) through the texture function, or
        /// nothing and a reason: any other use of either texture that this does not recognise means it is not safe.
        /// </summary>
        static string RewriteTextures(Dictionary<string, string> files, string fn, string tid,
                                      out Dictionary<string, string> rewritten, out int sites)
        {
            rewritten = new Dictionary<string, string>(StringComparer.Ordinal);
            sites = 0;
            string otid = OutlineTidProperty(tid);
            foreach (var kv in files)
            {
                string text = kv.Value;
                bool changed = false;
                foreach (bool outline in new[] { false, true })
                {
                    Regex rx = outline ? OutlineReadRx : MainReadRx;
                    var sb = new StringBuilder(text.Length + 256);
                    int pos = 0;
                    foreach (Match m in rx.Matches(text))
                    {
                        if (m.Index < pos) continue;
                        int open = text.IndexOf('(', m.Index);
                        if (!SplitArgs(text, open, out List<string> args, out int close))
                            return kv.Key + ": a texture read could not be parsed";
                        bool pom = !outline && m.Groups[1].Success && m.Groups[1].Value == "_POM";
                        if (args.Count != (pom ? 5 : 3)) return kv.Key + ": an unexpected texture read";
                        string tex = outline ? "_OutlineTex" : "_MainTex";
                        sb.Append(text, pos, m.Index - pos);
                        sb.Append(fn).Append('(').Append(tex).Append(", ").Append(args[1]).Append(", ").Append(tex).Append("_TexelSize, ")
                          .Append(outline ? otid : tid).Append(", (").Append(args[2]).Append("))");
                        pos = close + 1;
                        sites++;
                        changed = true;
                    }
                    sb.Append(text, pos, text.Length - pos);
                    text = sb.ToString();
                }
                if (changed) rewritten[kv.Key] = text;
            }

            // what is left naming either texture must be its declaration or a call just written
            foreach (var kv in files)
            {
                string text = rewritten.TryGetValue(kv.Key, out string t) ? t : kv.Value;
                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    int c = line.IndexOf("//", StringComparison.Ordinal);
                    if (c >= 0) line = line.Substring(0, c);
                    foreach (Match m in TexNameRx.Matches(line))
                    {
                        string before = line.Substring(0, m.Index).TrimEnd();
                        if (before.EndsWith("TEXTURE2D(", StringComparison.Ordinal)) continue;
                        if (before.EndsWith(fn + "(", StringComparison.Ordinal)) continue;
                        return "lilToon uses " + m.Value + " in a way this cannot rewrite (" + kv.Key + ":" + (i + 1) + ")";
                    }
                }
            }
            return null;
        }

        static string Substitute(string code) =>
            (code ?? string.Empty)
                .Replace("__KS_STRUCT__", "appdata")
                .Replace("__KS_POS__", "positionOS")
                .Replace("__KS_NOR__", "normalOS")
                .Replace("__KS_TAN__", "tangentOS");

        // ── flattening one lts*.shader ───────────────────────────────────────────────────────────

        static readonly Regex ShaderNameRx = new Regex("^(\\s*Shader\\s+\")([^\"]+)(\")", RegexOptions.Multiline);
        static readonly Regex FallbackTagRx = new Regex("\"VRCFallback\"\\s*=\\s*\"[^\"]*\"");
        // "Hidden/ltspass_opaque/FORWARD": the shader name itself has a slash, so split at the LAST one
        static readonly Regex UsePassRx = new Regex("UsePass\\s+\"([^\"]+)/([^\"/]+)\"");
        static readonly Regex IncludeRx = new Regex("(#include\\s+\")Includes/([^\"]+\")");
        static readonly Regex PropertiesRx = new Regex("(Properties\\s*\\{\\s*\\r?\\n)");
        static readonly Regex AnyMainReadRx = new Regex("\\b[A-Za-z_][A-Za-z0-9_]*\\s*\\(\\s*_MainTex\\s*,");

        /// <summary>Writes the flattened copy into <paramref name="folder"/>/kss&lt;index&gt; and compiles every pass.</summary>
        static bool FlattenInto(string shaderPath, int index, string folder, string incDir, IList<string> props, bool texture, FlattenResult r)
        {
            string dir = Path.GetDirectoryName(shaderPath).Replace('\\', '/');
            string lts = File.ReadAllText(shaderPath);

            // The passes lts pulls in by name, grouped by the shader that defines them. Gem, fur, refraction-blur and
            // lilToonMulti carry their own passes (no UsePass): nothing to inline, only the include redirect and rename.
            var uses = UsePassRx.Matches(lts).Cast<Match>().ToList();
            var sources = new Dictionary<string, SourceShader>(StringComparer.Ordinal);
            var inlined = new StringBuilder();
            foreach (Match u in uses)
            {
                string sourceName = u.Groups[1].Value, passName = u.Groups[2].Value;
                if (!sources.TryGetValue(sourceName, out SourceShader src))
                {
                    // "Hidden/ltspass_opaque" lives in ltspass_opaque.shader; "Hidden/lilToonFur" (fur-only shaders) is the
                    // NAME of another lilToon shader, found by name
                    string file = dir + "/" + sourceName.Substring(sourceName.LastIndexOf('/') + 1) + ".shader";
                    if (!File.Exists(file))
                    {
                        Shader named = Shader.Find(sourceName);
                        string namedPath = named != null ? AssetDatabase.GetAssetPath(named) : null;
                        if (!string.IsNullOrEmpty(namedPath) && namedPath.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) &&
                            Path.GetDirectoryName(namedPath).Replace('\\', '/') == dir)
                            file = namedPath;
                    }
                    if (!File.Exists(file)) { r.Why = "a UsePass source shader is missing: " + sourceName; return false; }
                    src = SourceShader.Parse(File.ReadAllText(file));
                    sources[sourceName] = src;
                }
                if (!src.Passes.TryGetValue(passName, out string passBody))
                { r.Why = "a named pass was not found in " + sourceName + ": " + passName; return false; }
                // a UsePass'd pass compiles with the HLSLINCLUDE of the shader that DEFINES it: bring it along
                inlined.Append(InjectHlslInclude(passBody, src.HlslInclude)).Append('\n');
            }
            string outText = lts;
            if (uses.Count > 0)
            {
                int first = uses.First().Index, last = uses.Last().Index + uses.Last().Length;
                outText = lts.Substring(0, first) + inlined + lts.Substring(last);
            }
            r.Passes = uses.Count;

            if (texture && AnyMainReadRx.IsMatch(outText))
            { r.Why = "the shader reads the main texture outside lilToon's shared files"; return false; }

            outText = IncludeRx.Replace(outText, m => m.Groups[1].Value + incDir + "/" + m.Groups[2].Value);
            // Hidden/Locked/ like a Poiyomi locked copy; no "lilToon" in the name, so lilToon's own editor and build
            // steps (which pick shaders by that word) leave the copy and its materials alone.
            outText = ShaderNameRx.Replace(outText, m => m.Groups[1].Value + "Hidden/Locked/KS/" + index + "_lil_" + ShortName(shaderPath) + m.Groups[3].Value, 1);
            // whoever blocks the wearer's shaders gets VRChat's fallback: Hidden, so the poster shows instead
            if (FallbackTagRx.IsMatch(outText)) outText = FallbackTagRx.Replace(outText, "\"VRCFallback\" = \"Hidden\"");
            else
            {
                int sub = outText.IndexOf("SubShader", StringComparison.Ordinal);
                Match tags = sub >= 0 ? new Regex("Tags\\s*\\{").Match(outText, sub) : Match.Empty;
                if (tags.Success) outText = outText.Insert(tags.Index + tags.Length, " \"VRCFallback\" = \"Hidden\" ");
            }
            if (props.Count > 0)
            {
                var block = new StringBuilder();
                foreach (string p in props) block.Append("        [HideInInspector] ").Append(p).Append(" (\"\", Float) = 0\n");
                outText = PropertiesRx.Replace(outText, m => m.Groups[1].Value + block, 1);
            }

            // one line ending throughout (the inlined blocks were joined with \n)
            outText = outText.Replace("\r\n", "\n");
            if (lts.Contains("\r\n")) outText = outText.Replace("\n", "\r\n");

            string subDir = folder + "/kss" + index;
            if (!AssetDatabase.IsValidFolder(subDir)) AssetDatabase.CreateFolder(folder, "kss" + index);
            string outPath = subDir + "/" + ShortName(shaderPath) + ".shader";
            File.WriteAllText(outPath, outText);
            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceSynchronousImport);

            Shader flat = AssetDatabase.LoadAssetAtPath<Shader>(outPath);
            if (flat == null) { r.Why = "the flattened lilToon shader did not import"; return false; }
            // Import only parses ShaderLab; an error in an include surfaces when a pass is really compiled.
            var probe = new Material(flat);
            try { for (int p = 0; p < flat.passCount; p++) ShaderUtil.CompilePass(probe, p, true); }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
            var errs = ShaderUtil.GetShaderMessages(flat)
                                 .Where(x => x.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).ToList();
            if (errs.Count > 0 || ShaderUtil.ShaderHasError(flat))
            {
                r.Why = "the patched lilToon shader did not compile" +
                        (errs.Count > 0 ? ": " + errs[0].message + " (" + errs[0].file + ":" + errs[0].line + ")" : "");
                return false;
            }
            r.ShaderPath = outPath;
            return true;
        }

        // ── parsing a ltspass_*.shader ──────────────────────────────────────────────────────────────

        sealed class SourceShader
        {
            // What Unity prepends to every program of the source's passes: its Shader-level HLSLINCLUDE (lilToon puts
            // the render mode there: #define LIL_RENDER 0 opaque / 1 cutout / 2 transparent), then its SubShader-level one.
            public string HlslInclude = "";
            public readonly Dictionary<string, string> Passes = new Dictionary<string, string>(StringComparer.Ordinal);

            public static SourceShader Parse(string text)
            {
                var s = new SourceShader();
                int sub = text.IndexOf("SubShader", StringComparison.Ordinal);
                if (sub < 0) return s;
                int body = text.IndexOf('{', sub);
                if (body < 0) return s;
                int end = MatchBrace(text, body);
                if (end < 0) end = text.Length;

                var shaderLevel = new StringBuilder();
                for (int from = 0; ;)
                {
                    int hs = text.IndexOf("HLSLINCLUDE", from, StringComparison.Ordinal);
                    if (hs < 0 || hs > sub) break;
                    int he = text.IndexOf("ENDHLSL", hs, StringComparison.Ordinal);
                    if (he < 0) break;
                    shaderLevel.Append(text, hs + "HLSLINCLUDE".Length, he - hs - "HLSLINCLUDE".Length).Append('\n');
                    from = he + "ENDHLSL".Length;
                }

                string subLevel = "";
                int hi = text.IndexOf("HLSLINCLUDE", body, StringComparison.Ordinal);
                int firstPass = NextPass(text, body, end);
                if (hi >= 0 && (firstPass < 0 || hi < firstPass))
                {
                    int he = text.IndexOf("ENDHLSL", hi, StringComparison.Ordinal);
                    if (he > hi) subLevel = text.Substring(hi + "HLSLINCLUDE".Length, he - hi - "HLSLINCLUDE".Length);
                }
                s.HlslInclude = shaderLevel + subLevel;

                int at = body;
                while (true)
                {
                    int p = NextPass(text, at, end);
                    if (p < 0) break;
                    int open = text.IndexOf('{', p);
                    if (open < 0) break;
                    int close = MatchBrace(text, open);
                    if (close < 0) break;
                    string block = text.Substring(p, close - p + 1);
                    Match nm = new Regex("Name\\s+\"([^\"]+)\"").Match(block);
                    if (nm.Success) s.Passes[nm.Groups[1].Value] = block;
                    at = close + 1;
                }
                return s;
            }
        }

        /// <summary>The index of the next top-level <c>Pass</c> keyword in [from,end), or -1.</summary>
        static int NextPass(string text, int from, int end)
        {
            var rx = new Regex("\\bPass\\b");
            for (Match m = rx.Match(text, from); m.Success; m = m.NextMatch())
            {
                if (m.Index >= end) return -1;
                int j = m.Index + 4;
                while (j < text.Length && (text[j] == ' ' || text[j] == '\t' || text[j] == '\r' || text[j] == '\n')) j++;
                if (j < text.Length && text[j] == '{') return m.Index;
            }
            return -1;
        }

        /// <summary>The index of the '}' that closes the '{' at <paramref name="open"/>, skipping strings and comments.</summary>
        static int MatchBrace(string text, int open)
        {
            int depth = 0;
            bool inStr = false, inLine = false, inBlock = false;
            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                char n = i + 1 < text.Length ? text[i + 1] : '\0';
                if (inLine) { if (c == '\n') inLine = false; continue; }
                if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } continue; }
                if (inStr) { if (c == '"') inStr = false; continue; }
                if (c == '/' && n == '/') { inLine = true; i++; continue; }
                if (c == '/' && n == '*') { inBlock = true; i++; continue; }
                if (c == '"') { inStr = true; continue; }
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        /// <summary>Puts <paramref name="hlslInclude"/> at the top of the pass's HLSLPROGRAM (after the opening token).</summary>
        static string InjectHlslInclude(string pass, string hlslInclude)
        {
            if (string.IsNullOrEmpty(hlslInclude)) return pass;
            int hp = pass.IndexOf("HLSLPROGRAM", StringComparison.Ordinal);
            if (hp < 0) return pass;
            int after = hp + "HLSLPROGRAM".Length;
            return pass.Substring(0, after) + "\n" + hlslInclude + "\n" + pass.Substring(after);
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
                else if (c == ';' || c == '{' || c == '}') return false;
            }
            return false;
        }

        static string ShortName(string path) => Path.GetFileNameWithoutExtension(path).Replace(" ", "");
    }
}
