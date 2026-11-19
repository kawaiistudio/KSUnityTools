using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// 3.16.2 (owner, 2026-11-19: "un rapport aussi"): what one protected upload gave each part of the avatar, in words
    /// a creator can act on. Filled by KSAntiRipProcessor, printed in the Unity console and sent to the Kawaii Studio app
    /// (KSAntiRipClient.Coverage), which shows the last one on its KS Anti-Rip page.
    ///
    /// The kinds, strongest first:
    ///   shader          the mesh only comes back with the key, through its own shader
    ///   shader+shapes   part of the mesh by its shader, the rest by restore shapes
    ///   shader+left     a static mesh: part by its shader, the rest (on other materials) as it was
    ///   shapes          restore shapes (the usual method)
    ///   left            not protected at all (a static mesh the shader cannot take)
    /// Names are the avatar's own, taken before names are obfuscated.
    /// </summary>
    public sealed class KSAntiRipCoverage
    {
        public sealed class Part
        {
            public string name = "";
            public string kind = "";      // shader, shader+shapes, shapes, left
            public bool isStatic;
            public int vertices;
            public string why = "";       // for anything weaker than "shader": the reason
        }

        public sealed class Texture
        {
            public string name = "";
            public bool laidOut;
            public string why = "";       // when it stays readable
        }

        public readonly List<Part> parts = new List<Part>();
        public readonly List<Texture> textures = new List<Texture>();
        public bool synced;              // the key is sent to the other players
        public bool longKeyAsked;        // the "long key" option was on
        public int keyValues;            // key parameters the avatar carries
        public int shaderKeyBits;        // what guessing the shader key takes (0 = no shader key / older app)

        public void Add(string name, string kind, bool isStatic, int vertices, string why = "")
            => parts.Add(new Part { name = name ?? "", kind = kind ?? "", isStatic = isStatic, vertices = vertices, why = why ?? "" });

        public void AddTexture(string name, bool laidOut, string why = "")
        {
            if (textures.Any(t => t.name == name)) return;
            textures.Add(new Texture { name = name ?? "", laidOut = laidOut, why = why ?? "" });
        }

        int Count(string kind) => parts.Count(p => p.kind == kind);

        /// <summary>One line: how much of the avatar each method covers, the textures and the key.</summary>
        public string Summary()
        {
            int total = Math.Max(1, parts.Count);
            long verts = Math.Max(1, parts.Sum(p => (long)p.vertices));
            long strong = parts.Where(p => p.kind == "shader").Sum(p => (long)p.vertices);
            var sb = new StringBuilder();
            sb.Append(Count("shader")).Append(" part(s) closed by their shader");
            if (Count("shader+shapes") + Count("shader+left") > 0)
                sb.Append(", ").Append(Count("shader+shapes") + Count("shader+left")).Append(" partly");
            sb.Append(", ").Append(Count("shapes")).Append(" by restore shapes");
            if (Count("left") > 0) sb.Append(", ").Append(Count("left")).Append(" NOT protected");
            sb.Append(" (").Append((100.0 * strong / verts).ToString("0", CultureInfo.InvariantCulture)).Append(" % of the vertices by the shader)");
            if (textures.Count > 0)
                sb.Append("; textures laid out ").Append(textures.Count(t => t.laidOut)).Append('/').Append(textures.Count);
            if (shaderKeyBits > 0)
                sb.Append("; shader key ~").Append(shaderKeyBits).Append(" bits, ").Append(synced ? "synced" : "kept on your PC");
            return sb.ToString();
        }

        /// <summary>The detail, weakest parts first, one line each.</summary>
        public List<string> Lines()
        {
            var lines = new List<string>();
            foreach (Part p in parts.OrderBy(Rank).ThenBy(p => p.name, StringComparer.OrdinalIgnoreCase))
                lines.Add(Label(p.kind) + "  " + p.name + (p.isStatic ? " (static)" : "") +
                          (string.IsNullOrEmpty(p.why) ? "" : " -- " + p.why));
            foreach (Texture t in textures.OrderBy(t => t.laidOut).ThenBy(t => t.name, StringComparer.OrdinalIgnoreCase))
                lines.Add((t.laidOut ? "TEXTURE LAID OUT  " : "TEXTURE READABLE  ") + t.name +
                          (string.IsNullOrEmpty(t.why) ? "" : " -- " + t.why));
            return lines;
        }

        static int Rank(Part p) => p.kind == "left" ? 0 : p.kind == "shapes" ? 1 : p.kind == "shader+left" ? 2 : p.kind == "shader+shapes" ? 3 : 4;

        static string Label(string kind) =>
            kind == "shader" ? "SHADER        " :
            kind == "shader+shapes" ? "SHADER+SHAPES " :
            kind == "shader+left" ? "SHADER+LEFT   " :
            kind == "shapes" ? "RESTORE SHAPES" : "NOT PROTECTED ";

        /// <summary>For the app (POST /unity/antirip/coverage).</summary>
        public string ToJson(string avatarId, string avatarName)
        {
            var sb = new StringBuilder();
            sb.Append("{\"avatarId\":").Append(Q(avatarId)).Append(",\"avatarName\":").Append(Q(avatarName))
              .Append(",\"synced\":").Append(synced ? "true" : "false")
              .Append(",\"longKey\":").Append(longKeyAsked ? "true" : "false")
              .Append(",\"keyValues\":").Append(keyValues)
              .Append(",\"shaderKeyBits\":").Append(shaderKeyBits)
              .Append(",\"summary\":").Append(Q(Summary()))
              .Append(",\"parts\":[");
            for (int i = 0; i < parts.Count; i++)
            {
                Part p = parts[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Q(p.name)).Append(",\"kind\":").Append(Q(p.kind))
                  .Append(",\"static\":").Append(p.isStatic ? "true" : "false")
                  .Append(",\"vertices\":").Append(p.vertices).Append(",\"why\":").Append(Q(p.why)).Append('}');
            }
            sb.Append("],\"textures\":[");
            for (int i = 0; i < textures.Count; i++)
            {
                Texture t = textures[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Q(t.name)).Append(",\"laidOut\":").Append(t.laidOut ? "true" : "false")
                  .Append(",\"why\":").Append(Q(t.why)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Q(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
