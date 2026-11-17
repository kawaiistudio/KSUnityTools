using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// Asks the Kawaii Studio app for a protection plan.
    ///
    /// Everything that decides anything — the scramble, the lock combination, the key, the Premium check —
    /// happens inside the app's core, which ships obfuscated and hash-sealed. This file only asks, and gets
    /// back arrays of numbers it cannot interpret. That is the point: KS Unity Tools is distributed as
    /// readable C# source, so anything written here is published, and a published scrambler is a scrambler
    /// somebody writes an automatic undo for.
    ///
    /// It talks to the same loopback bridge the Kawaii Studio Link uses (the port and a rotating secret live
    /// in %LOCALAPPDATA%\KawaiiStudio\link\bridge.json), and pairs for its own token rather than reaching into
    /// the Link package's private state — so this keeps working if the Link changes, and works without it.
    /// </summary>
    public static class KSAntiRipClient
    {
        public class MeshInfo
        {
            public string id = "";
            public int vertexCount;
        }

        public class Plan
        {
            public bool ok;
            public string error = "";
            public bool needsPremium;
            public string session = "";
            public string tag = "";
            public string marker = "";
            public string keyPath = "";
            /// <summary>
            /// The key parameters' names, one per restore shape, in shape order: restore shape k of EVERY mesh
            /// follows parameters[k]. Names only. The values are the key, and since 5.1 they never reach Unity --
            /// they go into the .key file, and KS OSC KEY sends them into VRChat.
            /// </summary>
            public List<string> parameters = new List<string>();
            public List<MeshInfo> meshes = new List<MeshInfo>();
        }

        public class MeshPlan
        {
            public string id = "";
            public int vertexCount;
            public Vector3[] offsets = new Vector3[0];
            public List<Vector3[]> groups = new List<Vector3[]>();
            public List<string> shapeNames = new List<string>();
        }

        static string _token = "";
        static int _port;

        public static string LastError { get; private set; } = "";

        static string DiscoveryFolder
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("KS_LINK_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "KawaiiStudio", "link");
            }
        }

        [Serializable]
        class Discovery
        {
            public int port;
            public string secret = "";
        }

        /// <summary>Pairs if needed. False with LastError set when the app is not reachable.</summary>
        public static bool Connect()
        {
            if (!string.IsNullOrEmpty(_token) && _port > 0) return true;

            string file = Path.Combine(DiscoveryFolder, "bridge.json");
            if (!File.Exists(file))
            {
                LastError = "Kawaii Studio is not running. KS Anti-Rip needs the app: the protection itself lives there.";
                return false;
            }

            Discovery d;
            try { d = JsonUtility.FromJson<Discovery>(File.ReadAllText(file)); }
            catch (Exception e) { LastError = "Could not read the pairing file: " + e.Message; return false; }
            if (d == null || d.port <= 0 || string.IsNullOrEmpty(d.secret))
            {
                LastError = "The pairing file is incomplete. Restart Kawaii Studio.";
                return false;
            }
            _port = d.port;

            string body = "{\"project\":" + Quote(Application.dataPath) +
                          ",\"name\":" + Quote(Application.productName) +
                          ",\"unity\":" + Quote(Application.unityVersion) +
                          ",\"ks\":" + Quote("anti-rip " + KSAntiRipProcessor.Version) +
                          ",\"pid\":" + System.Diagnostics.Process.GetCurrentProcess().Id +
                          ",\"active\":true}";
            string reply = Send("POST", "/unity/pair", body, d.secret, 8000);
            if (reply == null) return false;

            Match token = Regex.Match(reply, "\"token\"\\s*:\\s*\"([0-9a-f]{64})\"");
            if (!token.Success)
            {
                LastError = "Kawaii Studio refused the pairing: " + Error(reply);
                return false;
            }
            _token = token.Groups[1].Value;
            return true;
        }

        /// <summary>Asks for a plan. The app checks the account; a refusal arrives as ok=false with a reason.</summary>
        public static Plan Begin(string avatarId, string avatarName, IList<MeshInfo> meshes,
                                 float strength, int restoreGroups)
        {
            var plan = new Plan();
            if (!Connect()) { plan.error = LastError; return plan; }

            var sb = new StringBuilder();
            sb.Append("{\"avatarId\":").Append(Quote(avatarId))
              .Append(",\"avatarName\":").Append(Quote(avatarName))
              .Append(",\"strength\":").Append(strength.ToString(System.Globalization.CultureInfo.InvariantCulture))
              .Append(",\"restoreGroups\":").Append(restoreGroups)
              .Append(",\"meshes\":[");
            for (int i = 0; i < meshes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Quote(meshes[i].id))
                  .Append(",\"vertexCount\":").Append(meshes[i].vertexCount).Append('}');
            }
            sb.Append("]}");

            string reply = Send("POST", "/unity/antirip/begin", sb.ToString(), null, 120000);
            if (reply == null) { plan.error = LastError; return plan; }
            return ParseBegin(reply);
        }

        /// <summary>
        /// The /unity/antirip/begin reply, read. Public so the proof test can feed it the bridge's exact bytes
        /// and go through THIS parser rather than one of its own -- the 2026-11-16 base64 bug lived in the
        /// parser, and a test with its own parser could not have seen it.
        /// </summary>
        public static Plan ParseBegin(string reply)
        {
            var plan = new Plan();
            if (!Flag(reply, "ok"))
            {
                plan.error = Error(reply);
                plan.needsPremium = Flag(reply, "needsPremium");
                return plan;
            }
            plan.ok = true;
            plan.session = Str(reply, "session");
            plan.tag = Str(reply, "tag");
            plan.marker = Str(reply, "marker");
            plan.keyPath = Str(reply, "keyPath");
            plan.parameters.AddRange(StrList(reply, "parameters"));
            if (plan.parameters.Count == 0)
            {
                plan.ok = false;
                plan.error = "The app sent a plan this version cannot read. Update Kawaii Studio and KS Unity Tools.";
            }
            return plan;
        }

        /// <summary>Collects one mesh's arrays. Returns null with LastError set.</summary>
        public static MeshPlan Mesh(string session, string id)
        {
            string reply = Send("GET", "/unity/antirip/mesh?session=" + Uri.EscapeDataString(session) +
                                       "&id=" + Uri.EscapeDataString(id), null, null, 120000);
            if (reply == null) return null;
            return ParseMesh(id, reply);
        }

        /// <summary>The /unity/antirip/mesh reply, read. Null with LastError set when it cannot be used.</summary>
        public static MeshPlan ParseMesh(string id, string reply)
        {
            if (!Flag(reply, "ok")) { LastError = Error(reply); return null; }

            var mesh = new MeshPlan { id = id, vertexCount = Int(reply, "vertexCount") };
            mesh.offsets = Unpack(Str(reply, "offsets"));
            foreach (string g in StrList(reply, "groups")) mesh.groups.Add(Unpack(g));
            mesh.shapeNames.AddRange(StrList(reply, "shapeNames"));

            if (mesh.offsets.Length != mesh.vertexCount || mesh.groups.Count == 0 ||
                mesh.groups.Count != mesh.shapeNames.Count)
            {
                LastError = "The app sent a plan this version cannot read. Update Kawaii Studio and KS Unity Tools.";
                return null;
            }
            return mesh;
        }

        /// <summary>A JSON array of strings, each decoded.</summary>
        static List<string> StrList(string json, string name)
        {
            var list = new List<string>();
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!m.Success) return list;
            foreach (Match s in Regex.Matches(m.Groups[1].Value, JsonString))
                list.Add(Unescape(s.Groups[1].Value));
            return list;
        }

        public static void End(string session)
        {
            if (string.IsNullOrEmpty(session)) return;
            Send("POST", "/unity/antirip/end", "{\"session\":" + Quote(session) + "}", null, 8000);
        }

        // ── plumbing ────────────────────────────────────────────────────────────────────────────

        /// <summary>base64 of raw little-endian float32 triples, straight back into Vector3[].</summary>
        static Vector3[] Unpack(string base64)
        {
            if (string.IsNullOrEmpty(base64)) return new Vector3[0];
            byte[] bytes = Convert.FromBase64String(base64);
            int count = bytes.Length / 12;
            var floats = new float[count * 3];
            Buffer.BlockCopy(bytes, 0, floats, 0, count * 12);
            var result = new Vector3[count];
            for (int i = 0; i < count; i++)
                result[i] = new Vector3(floats[i * 3], floats[i * 3 + 1], floats[i * 3 + 2]);
            return result;
        }

        static string Send(string method, string path, string body, string secret, int timeoutMs)
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
                request.Method = method;
                request.Timeout = timeoutMs;
                request.ReadWriteTimeout = timeoutMs;
                request.KeepAlive = false;
                // No Origin/Referer: the app's write routes are native-only and reject anything a browser sent.
                if (!string.IsNullOrEmpty(secret)) request.Headers.Add("X-KS-Link-Secret", secret);
                if (!string.IsNullOrEmpty(_token)) request.Headers.Add("X-KS-Link-Token", _token);

                if (body != null)
                {
                    byte[] payload = Encoding.UTF8.GetBytes(body);
                    request.ContentType = "application/json";
                    request.ContentLength = payload.Length;
                    using (Stream s = request.GetRequestStream()) s.Write(payload, 0, payload.Length);
                }

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException e)
            {
                if (e.Response is HttpWebResponse http && http.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _token = "";   // the app restarted: pair again on the next call
                    LastError = "Kawaii Studio restarted. Try again.";
                    return null;
                }
                // /begin only 404s when the app predates KS ANTI-RIP: the route does not exist yet. That is the
                // exact case between a KS Unity Tools release and the app update reaching someone, so say what
                // to do rather than pass "not found" through.
                if (e.Response is HttpWebResponse old && old.StatusCode == HttpStatusCode.NotFound &&
                    path.StartsWith("/unity/antirip/begin", StringComparison.Ordinal))
                {
                    LastError = "Your Kawaii Studio is too old for KS Anti-Rip. Update the app, then try again.";
                    return null;
                }
                try
                {
                    if (e.Response != null)
                        using (var reader = new StreamReader(e.Response.GetResponseStream(), Encoding.UTF8))
                            return reader.ReadToEnd();
                }
                catch { }
                LastError = "Kawaii Studio did not answer: " + e.Message;
                return null;
            }
            catch (Exception e)
            {
                LastError = "Kawaii Studio did not answer: " + e.Message;
                return null;
            }
        }

        static bool Flag(string json, string name) =>
            Regex.IsMatch(json ?? "", "\"" + name + "\"\\s*:\\s*true");

        /// <summary>
        /// One JSON string literal, escapes and all. The previous pattern, "([^"]*)", stopped at the first
        /// quote and handed back the RAW text between -- escapes included. That broke the first real round
        /// trip over the bridge: System.Text.Json escapes '+' as + by default, base64 is full of '+', and
        /// Convert.FromBase64String rejected the backslash. Found by the 2026-11-16 bridge test, not before:
        /// the library path parses with JsonUtility, which unescapes on its own.
        /// </summary>
        const string JsonString = "\"((?:[^\"\\\\]|\\\\.)*)\"";

        static string Str(string json, string name)
        {
            Match m = Regex.Match(json ?? "", "\"" + Regex.Escape(name) + "\"\\s*:\\s*" + JsonString);
            return m.Success ? Unescape(m.Groups[1].Value) : "";
        }

        /// <summary>The body of a JSON string literal, decoded: \" \\ \/ \b \f \n \r \t and \uXXXX.</summary>
        static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i + 1 >= s.Length) { sb.Append(c); continue; }
                char e = s[++i];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber,
                                         System.Globalization.CultureInfo.InvariantCulture, out int code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else sb.Append("\\u");
                        break;
                    default: sb.Append('\\').Append(e); break;
                }
            }
            return sb.ToString();
        }

        static int Int(string json, string name)
        {
            Match m = Regex.Match(json ?? "", "\"" + name + "\"\\s*:\\s*(-?\\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, out int v) ? v : 0;
        }

        static string Error(string json)
        {
            string e = Str(json, "error");
            return string.IsNullOrEmpty(e) ? "no reason given" : e;
        }

        static string Quote(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }
}
