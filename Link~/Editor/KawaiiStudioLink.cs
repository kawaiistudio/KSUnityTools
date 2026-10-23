// Kawaii Studio Link -- this Unity project paired with the Kawaii Studio app.
//
// The app (VRChat Archive's Kawaii Studio) runs a loopback bridge on 127.0.0.1:8791 and writes
// %LOCALAPPDATA%\KawaiiStudio\link\bridge.json with its port and a pairing secret that changes on
// every start. Only a local process can read that file, which is the point: the bridge answers any
// web page (CORS *), so a browser must never be able to drive the app. Connect trades the secret for
// a token for this project; every later call carries the token. The app refuses any request that
// looks like it comes from a browser, whatever it carries.
//
// App -> Unity: this side long-polls the bridge (one request held up to 20 s, answered at once when
// the app queues something). Unity opens no port of its own: a listener would die with every domain
// reload, two open projects would fight over it, and it would be one more door a web page could
// knock on. The poll runs on a background thread; everything that touches the AssetDatabase runs on
// the main thread, in EditorApplication.update.
//
// What the app sends today: "import", from Send to Unity in its Scene Editor. The export (the same
// Unity 2022 project layout as its Export all) is written into this project's Temp folder, then
// moved here into Assets/Kawaii Studio/KS Exported/<name>, imported, selected and pinged. A previous
// copy of the same name is kept once in Assets/Kawaii Studio/Backups~/KS Exported/<name>.
//
// The account's gates (Scene Editor = VIP, Bundle Patcher = Legendary) are the server's. This side
// only shows what the app reports and never decides anything.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace KawaiiStudio
{
    [InitializeOnLoad]
    public static class KawaiiStudioLink
    {
        public enum LinkState { Off, NoApp, Linked, Failed }

        // ------------------------------------------------------------------ what the app sends (JsonUtility shapes)

        [Serializable]
        public class Gate
        {
            public string name;
            public string need;
            public int level;
            public bool ok;
        }

        [Serializable]
        public class AppStatus
        {
            public string app;
            public string version;
            public bool signedIn;
            public string user;
            public int level;
            public string levelName;
            public bool admin;
            public bool editorPack;
            public bool editorReady;
            public bool testInApp;
            public Gate[] gates;
            public bool target;
            public int links;
        }

        [Serializable]
        public class ImportMessage
        {
            public string id;
            public string kind;
            public string name;
            public string type;
            public string folder;
            public string assets;
            public string report;
            public string sentAt;
        }

        [Serializable]
        public class Transfer
        {
            public string id;
            public string name;
            public string type;
            public string asset;
            public string folder;
            public string when;
            public bool ok;
            public string message;
            public int files;
            public int missingScripts = -1;
            public float seconds;
        }

        [Serializable]
        private class Discovery
        {
            public string app;
            public int port;
            public int pid;
            public string secret;
            public string version;
        }

        [Serializable]
        private class PairReply
        {
            public bool ok;
            public string token;
            public string version;
            public string error;
            public AppStatus status;
        }

        [Serializable]
        private class PollReply
        {
            public bool ok;
            public string error;
            public AppStatus status;
            public ImportMessage[] messages;
        }

        [Serializable]
        private class Job
        {
            public ImportMessage message;
            public int step;
            public string dest;
            public string waitingFor;
            public long startedTicks;
        }

        [Serializable]
        private class Saved
        {
            public List<Job> jobs = new List<Job>();
            public List<string> seen = new List<string>();
            public List<string> log = new List<string>();
        }

        [Serializable]
        private class TransferList
        {
            public List<Transfer> items = new List<Transfer>();
        }

        // ------------------------------------------------------------------ settings

        // EditorUserSettings: this project, this user (UserSettings/, never committed with the project).
        private const string PrefEnabled = "KawaiiStudio.Link.Enabled";
        private const string PrefFront = "KawaiiStudio.Link.BringToFront";
        // SessionState: survives domain reloads (each compile, each Play), not an editor restart.
        private const string SessToken = "KawaiiStudio.Link.Token";
        private const string SessPort = "KawaiiStudio.Link.Port";
        private const string SessSaved = "KawaiiStudio.Link.Saved";

        private const int PollSeconds = 20;
        private const string TokenHeader = "X-KS-Link-Token";
        private const string SecretHeader = "X-KS-Link-Secret";

        // ------------------------------------------------------------------ state

        public static LinkState State { get; private set; }
        public static string Detail { get; private set; } = "";
        public static AppStatus Status { get; private set; }
        public static event Action Changed;

        private static readonly ConcurrentQueue<Action> MainQueue = new ConcurrentQueue<Action>();
        private static Thread _thread;
        private static volatile bool _run;
        private static volatile HttpWebRequest _poll;
        private static volatile string _token;
        private static volatile int _port;
        private static volatile bool _active;
        private static volatile bool _quitting;

        // read on the main thread once: Application.* may not be touched from the poll thread
        private static readonly string ProjectPath;
        private static readonly string ProjectName;
        private static readonly string UnityVersion;

        private static Saved _saved = new Saved();
        private static TransferList _history;
        private static int _exitAfter = -1;
        private static int _imported;
        private static double _exitAt;

        static KawaiiStudioLink()
        {
            ProjectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            ProjectName = Path.GetFileName(ProjectPath.TrimEnd('\\', '/'));
            UnityVersion = Application.unityVersion;
            _token = SessionState.GetString(SessToken, "");
            _port = SessionState.GetInt(SessPort, 0);
            try
            {
                string json = SessionState.GetString(SessSaved, "");
                if (!string.IsNullOrEmpty(json)) _saved = JsonUtility.FromJson<Saved>(json) ?? new Saved();
            }
            catch
            {
                _saved = new Saved();
            }
            State = LinkState.Off;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += StopThread;
            EditorApplication.quitting += OnQuit;
            ConsumeAutoConnectMarker();
            if (Enabled) StartThread();
        }

        // ------------------------------------------------------------------ public surface (the window)

        /// <summary>Linked for this project and this user: connects again each time the project opens.</summary>
        public static bool Enabled
        {
            get { return EditorUserSettings.GetConfigValue(PrefEnabled) == "1"; }
        }

        /// <summary>Bring Unity to the front when an export from the app is in.</summary>
        public static bool BringToFront
        {
            get { return EditorUserSettings.GetConfigValue(PrefFront) != "0"; }
            set { EditorUserSettings.SetConfigValue(PrefFront, value ? "1" : "0"); }
        }

        public static bool IsLinked => State == LinkState.Linked && !string.IsNullOrEmpty(_token);
        public static string Project => ProjectName;
        public static int Port => _port;
        public static IList<string> LogLines => _saved.log;
        public static bool Importing => _saved.jobs.Count > 0;
        public static string ImportingName => _saved.jobs.Count > 0 ? _saved.jobs[0].message.name : null;
        public static string ImportingWaitReason => _saved.jobs.Count > 0 ? _saved.jobs[0].waitingFor : null;

        public static IList<Transfer> History
        {
            get
            {
                if (_history == null) LoadHistory();
                return _history.items;
            }
        }

        public static string DiscoveryFolder
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("KS_LINK_DIR");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KawaiiStudio", "link");
            }
        }

        /// <summary>The marker file the Kawaii Studio app leaves in a project it made and set up (GET UNITY PROJECT, with the
        /// Link installed by the app): its first open links without a click. Read once and deleted; after that only the
        /// user's own Connect / Disconnect decide, and a project where they already chose keeps that choice.</summary>
        public const string AutoConnectMarker = "UserSettings/KawaiiStudioLink.autoconnect";

        private static void ConsumeAutoConnectMarker()
        {
            try
            {
                string marker = Path.Combine(ProjectPath, AutoConnectMarker.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(marker)) return;
                File.Delete(marker);
                if (!string.IsNullOrEmpty(EditorUserSettings.GetConfigValue(PrefEnabled))) return;
                EditorUserSettings.SetConfigValue(PrefEnabled, "1");
                AddLog("Set up by Kawaii Studio: linking this project automatically.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Kawaii Studio Link] auto-connect marker: " + e.Message);
            }
        }

        public static void Connect()
        {
            EditorUserSettings.SetConfigValue(PrefEnabled, "1");
            AddLog("Connecting to Kawaii Studio.");
            StartThread();
        }

        public static void Disconnect()
        {
            EditorUserSettings.SetConfigValue(PrefEnabled, "0");
            string token = _token;
            int port = _port;
            StopThread();
            if (!string.IsNullOrEmpty(token)) ThreadPool.QueueUserWorkItem(_ => TryUnpair(port, token, 2000));
            ForgetToken();
            SetState(LinkState.Off, "");
            AddLog("Disconnected from Kawaii Studio.");
        }

        /// <summary>
        /// Test in Kawaii Studio: hands a bundle this project just built to the app (POST /unity/open), off the main
        /// thread. The app copies it and opens it in its Scene Editor, playing when asked. done(ok, message) runs on
        /// the main thread.
        /// </summary>
        public static void OpenInApp(string bundle, string name, string type, bool play, Action<bool, string> done)
        {
            if (!IsLinked)
            {
                done?.Invoke(false, "not linked with Kawaii Studio. Open Kawaii Studio > Kawaii Studio Link and click Connect.");
                return;
            }
            string json = "{\"bundle\":" + Quote(bundle) + ",\"name\":" + Quote(name) + ",\"type\":" + Quote(type) + ",\"play\":" + (play ? "true" : "false") + "}";
            AddLog("Sending " + name + " to Kawaii Studio" + (play ? " to play it." : "."));
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool ok;
                string message;
                try
                {
                    // the app copies the bundle before answering: a large world takes a moment
                    Reply r = Request("POST", "/unity/open", json, 120000, null, false);
                    ok = r.Status == 200;
                    message = ok ? MessageOf(r.Body) : ErrorOf(r.Body);
                }
                catch (Exception e)
                {
                    ok = false;
                    message = Describe(e);
                }
                Post(() =>
                {
                    AddLog(ok ? "Kawaii Studio took " + name + ". " + message : "Kawaii Studio refused " + name + ": " + message);
                    Debug.Log("[Kawaii Studio Link] open in Kawaii Studio: " + (ok ? "accepted. " : "refused: ") + message);
                    done?.Invoke(ok, message);
                });
            });
        }

        /// <summary>
        /// Headless entry (tests, CI): -batchmode -executeMethod KawaiiStudio.KawaiiStudioLink.CommandLineServe
        /// without -quit. Links this project and keeps Unity up until KS_LINK_EXIT_AFTER imports are done
        /// (default 1), or KS_LINK_TIMEOUT seconds pass (default 600, exit code 2).
        /// </summary>
        public static void CommandLineServe()
        {
            int after, timeout;
            if (!int.TryParse(Environment.GetEnvironmentVariable("KS_LINK_EXIT_AFTER"), out after)) after = 1;
            if (!int.TryParse(Environment.GetEnvironmentVariable("KS_LINK_TIMEOUT"), out timeout)) timeout = 600;
            _exitAfter = after;
            _exitAt = EditorApplication.timeSinceStartup + timeout;
            Debug.Log("[Kawaii Studio Link] serving headless: exit after " + after + " import(s) or " + timeout + " s; pairing file in " + DiscoveryFolder);
            Connect();
        }

        // ------------------------------------------------------------------ the poll thread

        private static void StartThread()
        {
            if (_quitting || (_thread != null && _thread.IsAlive)) return;
            _run = true;
            _thread = new Thread(Loop) { IsBackground = true, Name = "Kawaii Studio Link" };
            _thread.Start();
        }

        private static void StopThread()
        {
            _run = false;
            try
            {
                HttpWebRequest poll = _poll;
                if (poll != null) poll.Abort();
            }
            catch
            {
            }
            Thread t = _thread;
            _thread = null;
            if (t != null && t != Thread.CurrentThread) t.Join(1500);
        }

        private static void OnQuit()
        {
            _quitting = true;
            string token = _token;
            int port = _port;
            StopThread();
            if (!string.IsNullOrEmpty(token)) TryUnpair(port, token, 1000);
        }

        private static void Loop()
        {
            int failures = 0;
            while (_run)
            {
                try
                {
                    if (string.IsNullOrEmpty(_token) && !Pair())
                    {
                        Pause(failures++ < 3 ? 2000 : 5000);
                        continue;
                    }
                    Reply r = Request("GET", "/unity/poll?wait=" + PollSeconds + "&active=" + (_active ? "1" : "0"), null, (PollSeconds + 15) * 1000, null, true);
                    if (!_run) break;
                    if (r.Status == 401)
                    {
                        // the app restarted (new secret, tokens gone): pair again at once
                        Post(ForgetToken);
                        _token = "";
                        continue;
                    }
                    if (r.Status != 200)
                    {
                        string why = "Kawaii Studio answered " + r.Status + ": " + ErrorOf(r.Body);
                        Post(() => SetState(LinkState.Failed, why));
                        Pause(3000);
                        continue;
                    }
                    failures = 0;
                    string body = r.Body;
                    Post(() => OnPoll(body));
                }
                catch (Exception e)
                {
                    if (!_run) break;
                    // connection refused or reset: the app closed or is restarting. Pair again when it is back,
                    // reading the pairing file again (it holds the port and the new secret).
                    _token = "";
                    string why = Describe(e);
                    Post(() =>
                    {
                        ForgetToken();
                        SetState(LinkState.NoApp, why);
                    });
                    Pause(failures++ < 3 ? 2000 : 5000);
                }
            }
        }

        /// <summary>What the app logs as "KS Unity Tools x.y.z": KS Unity Tools' version when it is in the project (read
        /// by reflection: the Link package does not reference it), else the Link's own ("Link 1.0.0", a world project).</summary>
        private static string KsVersion()
        {
            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var f = a.GetType("KawaiiStudio.KawaiiStudioVersion", false)?.GetField("Current",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (f != null && f.GetValue(null) is string v && v.Length > 0) return v;
                }
            }
            catch { }
            return "Link " + KawaiiStudioLinkVersion.Current;
        }

        private static bool Pair()
        {
            string why;
            Discovery d = ReadDiscovery(out why);
            if (d == null)
            {
                Post(() => SetState(LinkState.NoApp, why));
                return false;
            }
            _port = d.port;
            string body = "{\"project\":" + Quote(ProjectPath) + ",\"name\":" + Quote(ProjectName) + ",\"unity\":" + Quote(UnityVersion) +
                          ",\"ks\":" + Quote(KsVersion()) + ",\"pid\":" + Process.GetCurrentProcess().Id +
                          ",\"active\":" + (_active ? "true" : "false") + "}";
            Reply r = Request("POST", "/unity/pair", body, 8000, d.secret, false);
            Match token = Regex.Match(r.Body ?? "", "\"token\"\\s*:\\s*\"([0-9a-f]{64})\"");
            if (r.Status != 200 || !token.Success)
            {
                string refused = "Kawaii Studio refused the pairing: " + ErrorOf(r.Body);
                Post(() => SetState(LinkState.Failed, refused));
                return false;
            }
            _token = token.Groups[1].Value;
            string t = _token, reply = r.Body;
            int port = _port;
            Post(() =>
            {
                SessionState.SetString(SessToken, t);
                SessionState.SetInt(SessPort, port);
                PairReply p = Parse<PairReply>(reply);
                if (p != null && p.status != null) OnStatus(p.status);
                AddLog("Linked with Kawaii Studio v" + (p != null ? p.version : "?") + " on port " + port + ".");
                Debug.Log("[Kawaii Studio Link] linked with Kawaii Studio v" + (p != null ? p.version : "?") + " (port " + port + ")");
            });
            return true;
        }

        private static Discovery ReadDiscovery(out string why)
        {
            string file = Path.Combine(DiscoveryFolder, "bridge.json");
            if (!File.Exists(file))
            {
                why = "Kawaii Studio is not running (no pairing file yet). Start the app, the link follows on its own.";
                return null;
            }
            Discovery d;
            try
            {
                d = JsonUtility.FromJson<Discovery>(File.ReadAllText(file));
            }
            catch (Exception e)
            {
                why = "The pairing file could not be read: " + e.Message;
                return null;
            }
            if (d == null || d.port <= 0 || string.IsNullOrEmpty(d.secret))
            {
                why = "The pairing file is incomplete.";
                return null;
            }
            if (!ProcessAlive(d.pid))
            {
                why = "Kawaii Studio is not running. Start the app, the link follows on its own.";
                return null;
            }
            why = "";
            return d;
        }

        private struct Reply
        {
            public int Status;
            public string Body;
        }

        private static Reply Request(string method, string path, string body, int timeoutMs, string secret, bool isPoll)
        {
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + _port + path);
            req.Method = method;
            req.Proxy = null;            // never a system proxy for the loopback
            req.KeepAlive = false;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.ServicePoint.Expect100Continue = false;
            req.ServicePoint.ConnectionLimit = 16;
            if (secret != null) req.Headers[SecretHeader] = secret;
            else if (!string.IsNullOrEmpty(_token)) req.Headers[TokenHeader] = _token;
            if (isPoll) _poll = req;
            try
            {
                if (body != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(body);
                    req.ContentType = "application/json";
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return new Reply { Status = (int)resp.StatusCode, Body = ReadAll(resp) };
            }
            catch (WebException e) when (e.Response is HttpWebResponse)
            {
                using (var resp = (HttpWebResponse)e.Response)
                    return new Reply { Status = (int)resp.StatusCode, Body = ReadAll(resp) };
            }
            finally
            {
                if (isPoll) _poll = null;
            }
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8)) return reader.ReadToEnd();
        }

        private static void TryUnpair(int port, string token, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/unity/unpair");
                req.Method = "POST";
                req.Proxy = null;
                req.KeepAlive = false;
                req.Timeout = timeoutMs;
                req.ContentLength = 0;
                req.Headers[TokenHeader] = token;
                using (req.GetResponse()) { }
            }
            catch
            {
            }
        }

        // Fire and forget, off the main thread: an import must never wait on the network.
        private static void Send(string path, string json, bool wait = false)
        {
            if (string.IsNullOrEmpty(_token) || _port <= 0) return;
            if (wait)
            {
                try { Request("POST", path, json, 5000, null, false); } catch { }
                return;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { Request("POST", path, json, 10000, null, false); } catch { }
            });
        }

        private static void Pause(int ms)
        {
            for (int waited = 0; waited < ms && _run; waited += 100) Thread.Sleep(100);
        }

        private static void Post(Action a) => MainQueue.Enqueue(a);

        // ------------------------------------------------------------------ main thread

        private static void Update()
        {
            Action a;
            for (int n = 0; n < 64 && MainQueue.TryDequeue(out a); n++)
            {
                try { a(); }
                catch (Exception e) { Debug.LogException(e); }
            }

            bool active = InternalEditorUtility.isApplicationActive;
            if (active != _active)
            {
                _active = active;
                // the app sends to the project the user touched last
                if (active && _run && !string.IsNullOrEmpty(_token)) Send("/unity/hello", "{\"active\":true}");
            }

            if (_saved.jobs.Count > 0) StepJob();

            if (_exitAfter >= 0 && EditorApplication.timeSinceStartup > _exitAt)
            {
                Debug.LogError("[Kawaii Studio Link] headless serve timed out after " + _imported + " import(s)");
                EditorApplication.Exit(2);
            }
        }

        private static void OnPoll(string body)
        {
            PollReply p = Parse<PollReply>(body);
            if (p == null) return;
            if (p.status != null) OnStatus(p.status);
            if (State != LinkState.Linked) SetState(LinkState.Linked, "");
            if (p.messages == null) return;
            foreach (ImportMessage m in p.messages)
            {
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (_saved.seen.Contains(m.id))
                {
                    // our "received" was lost on the way: say it again, never run it twice
                    if (_saved.jobs.Any(j => j.message.id == m.id)) Ack(m.id, "received", "");
                    continue;
                }
                _saved.seen.Add(m.id);
                while (_saved.seen.Count > 50) _saved.seen.RemoveAt(0);
                if (m.kind != "import")
                {
                    Ack(m.id, "failed", "this KS Unity Tools does not know \"" + m.kind + "\": update it");
                    continue;
                }
                _saved.jobs.Add(new Job { message = m, startedTicks = DateTime.UtcNow.Ticks });
                Save();
                Ack(m.id, "received", _saved.jobs.Count > 1 ? "queued behind another import" : "");
                AddLog("Receiving " + m.name + " (" + m.type + ").");
            }
        }

        private static void OnStatus(AppStatus s)
        {
            Status = s;
            Changed?.Invoke();
        }

        private static void SetState(LinkState state, string detail)
        {
            if (state == State && detail == Detail) return;
            State = state;
            Detail = detail ?? "";
            if (state != LinkState.Linked) Status = state == LinkState.Off ? null : Status;
            Changed?.Invoke();
        }

        private static void ForgetToken()
        {
            _token = "";
            SessionState.EraseString(SessToken);
        }

        // ------------------------------------------------------------------ the import

        // One step per editor frame, so the window repaints and the acks get out between the slow parts.
        // step 0: wait for a quiet editor; 1: move the files in; 2: import; 3: select, ping, report.
        private static void StepJob()
        {
            Job job = _saved.jobs[0];
            ImportMessage m = job.message;
            string waiting = EditorApplication.isPlayingOrWillChangePlaymode ? "Play mode"
                : EditorApplication.isCompiling ? "script compilation"
                : EditorApplication.isUpdating ? "an asset import" : null;
            if (job.step < 3 && waiting != null)
            {
                if (job.waitingFor != waiting)
                {
                    job.waitingFor = waiting;
                    Save();
                    Ack(m.id, "progress", "waiting for Unity to finish " + waiting, 0.02f);
                    Changed?.Invoke();
                }
                return;
            }
            job.waitingFor = null;
            try
            {
                switch (job.step)
                {
                    case 0:
                        job.dest = MoveIn(m);
                        job.step = 1;
                        Save();
                        Ack(m.id, "progress", "files moved into " + job.dest + ", importing", 0.15f);
                        Changed?.Invoke();
                        break;
                    case 1:
                        job.step = 2;
                        Save();
                        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                        break;
                    case 2:
                        Finish(job);
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Fail(job, e.Message);
            }
        }

        private static string MoveIn(ImportMessage m)
        {
            if (string.IsNullOrEmpty(m.folder) || !Path.IsPathRooted(m.folder) || !Directory.Exists(m.folder))
                throw new InvalidOperationException("the export folder is gone: " + m.folder);
            string assets = (m.assets ?? "").Replace('\\', '/');
            if (!assets.StartsWith("Assets/", StringComparison.Ordinal) || assets.Contains(".."))
                throw new InvalidOperationException("unexpected export layout: " + m.assets);
            string source = Path.GetFullPath(Path.Combine(m.folder, assets));
            if (!source.StartsWith(Path.GetFullPath(m.folder), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))
                throw new InvalidOperationException("the export has no " + m.assets + " folder");

            string name = KawaiiStudioLinkPaths.SafeName(string.IsNullOrEmpty(m.name) ? Path.GetFileName(source) : m.name);
            string parent = KawaiiStudioLinkPaths.OutputFolder("KS Exported");
            string dest = parent + "/" + name;
            string destAbs = KawaiiStudioLinkPaths.ToAbsolute(dest);

            if (Directory.Exists(destAbs))
            {
                // the same name sent again: the copy it replaces is kept once, outside the AssetDatabase (Unity
                // ignores "~" folders). The new files carry the same GUIDs (the app derives them from the bundle),
                // so scenes and prefabs that used the old ones follow the new ones.
                string backup = KawaiiStudioLinkPaths.ToAbsolute(KawaiiStudioLinkPaths.Backups + "/KS Exported/" + name);
                if (Directory.Exists(backup)) Directory.Delete(backup, true);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                Directory.Move(destAbs, backup);
                AddLog("Previous " + name + " kept in " + KawaiiStudioLinkPaths.Backups + "/KS Exported.");
            }

            if (SameVolume(source, destAbs)) Directory.Move(source, destAbs);
            else CopyTree(source, destAbs);

            if (!string.IsNullOrEmpty(m.report))
            {
                string report = Path.Combine(m.folder, m.report);
                if (File.Exists(report)) File.Copy(report, Path.Combine(destAbs, "_EXPORT_REPORT.txt"), true);
            }
            // the rest (the raw dump, the loose FBX/OBJ, the project settings) stays out of the project
            try { Directory.Delete(m.folder, true); } catch { }
            return dest;
        }

        private static void Finish(Job job)
        {
            ImportMessage m = job.message;
            string path;
            Object main = MainAsset(job.dest, m.type, out path);
            int files = AssetDatabase.FindAssets("", new[] { job.dest }).Length;
            int missing = -1;
            var go = main as GameObject;
            if (go != null)
            {
                missing = 0;
                foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                    foreach (Component c in t.GetComponents<Component>())
                        if (c == null) missing++;
            }
            Object pick = main != null ? main : AssetDatabase.LoadAssetAtPath<Object>(job.dest);
            if (pick != null)
            {
                Selection.activeObject = pick;
                EditorUtility.FocusProjectWindow();
                EditorGUIUtility.PingObject(pick);
            }
            float seconds = (float)TimeSpan.FromTicks(DateTime.UtcNow.Ticks - job.startedTicks).TotalSeconds;
            string asset = path ?? job.dest;
            string summary = files + " assets in " + job.dest + (missing > 0 ? ", " + missing + " missing script(s)" : missing == 0 ? ", no missing script" : "");
            AddLog("Imported " + m.name + ": " + summary + ".");
            Debug.Log("[Kawaii Studio Link] imported " + m.name + " -> " + asset + " (" + summary + ", " + seconds.ToString("0.0") + " s)");
            Remember(new Transfer
            {
                id = m.id, name = m.name, type = m.type, asset = asset, folder = job.dest, when = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                ok = true, message = summary, files = files, missingScripts = missing, seconds = seconds,
            });
            _saved.jobs.RemoveAt(0);
            Save();
            bool exiting = _exitAfter >= 0 && ++_imported >= _exitAfter;
            Send("/unity/ack", "{\"id\":" + Quote(m.id) + ",\"state\":\"done\",\"asset\":" + Quote(asset) + ",\"folder\":" + Quote(job.dest) +
                               ",\"files\":" + files + ",\"missingScripts\":" + missing + ",\"seconds\":" + seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                               ",\"message\":" + Quote("imported into " + ProjectName + ": " + summary) + "}", exiting);
            Changed?.Invoke();
            if (exiting)
            {
                Debug.Log("[Kawaii Studio Link] headless serve done: " + _imported + " import(s)");
                EditorApplication.Exit(0);
                return;
            }
            if (BringToFront && !Application.isBatchMode) BringUnityForward();
        }

        private static void Fail(Job job, string why)
        {
            ImportMessage m = job.message;
            AddLog("Import of " + m.name + " failed: " + why);
            Remember(new Transfer { id = m.id, name = m.name, type = m.type, when = DateTime.Now.ToString("yyyy-MM-dd HH:mm"), ok = false, message = why });
            _saved.jobs.RemoveAt(0);
            Save();
            bool exiting = _exitAfter >= 0 && ++_imported >= _exitAfter;
            Ack(m.id, "failed", why, -1f, exiting);
            Changed?.Invoke();
            if (exiting) EditorApplication.Exit(1);
        }

        // The prefab or scene to select: a world's scene; an avatar's prefab holding the VRChat avatar
        // descriptor (looked up by name: this assembly compiles without the SDK), else its biggest prefab.
        private static Object MainAsset(string folder, string type, out string path)
        {
            path = null;
            if (type == "world")
            {
                string scene = AssetDatabase.FindAssets("t:Scene", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p.Length).FirstOrDefault();
                if (scene != null)
                {
                    path = scene;
                    return AssetDatabase.LoadAssetAtPath<SceneAsset>(scene);
                }
            }
            GameObject best = null;
            int bestScore = -1;
            foreach (string p in AssetDatabase.FindAssets("t:Prefab", new[] { folder }).Select(AssetDatabase.GUIDToAssetPath))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                if (go == null) continue;
                int score = go.GetComponentsInChildren<Transform>(true).Length;
                if (go.GetComponents<Component>().Any(c => c != null && c.GetType().Name == "VRCAvatarDescriptor")) score += 1000000;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = go;
                    path = p;
                }
            }
            return best;
        }

        private static void Ack(string id, string state, string message, float fraction = -1f, bool wait = false)
        {
            Send("/unity/ack", "{\"id\":" + Quote(id) + ",\"state\":" + Quote(state) + ",\"message\":" + Quote(message ?? "") +
                               ",\"fraction\":" + fraction.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "}", wait);
        }

        // ------------------------------------------------------------------ persistence and helpers

        private static void Save()
        {
            SessionState.SetString(SessSaved, JsonUtility.ToJson(_saved));
        }

        private static void AddLog(string line)
        {
            _saved.log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + line);
            while (_saved.log.Count > 40) _saved.log.RemoveAt(0);
            Save();
            Changed?.Invoke();
        }

        private static string HistoryFile => Path.Combine(ProjectPath, "Library", "KawaiiStudio", "link-history.json");

        private static void LoadHistory()
        {
            try
            {
                _history = File.Exists(HistoryFile) ? JsonUtility.FromJson<TransferList>(File.ReadAllText(HistoryFile)) : null;
            }
            catch
            {
                _history = null;
            }
            if (_history == null) _history = new TransferList();
        }

        private static void Remember(Transfer t)
        {
            if (_history == null) LoadHistory();
            _history.items.Insert(0, t);
            while (_history.items.Count > 12) _history.items.RemoveAt(_history.items.Count - 1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile));
                File.WriteAllText(HistoryFile, JsonUtility.ToJson(_history, true));
            }
            catch
            {
            }
        }

        private static T Parse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch { return null; }
        }

        private static string ErrorOf(string body)
        {
            Match m = Regex.Match(body ?? "", "\"error\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : (string.IsNullOrEmpty(body) ? "no answer" : body);
        }

        private static string MessageOf(string body)
        {
            Match m = Regex.Match(body ?? "", "\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
            return m.Success ? Regex.Unescape(m.Groups[1].Value) : "";
        }

        private static string Describe(Exception e)
        {
            var we = e as WebException;
            if (we != null && (we.Status == WebExceptionStatus.ConnectFailure || we.Status == WebExceptionStatus.ConnectionClosed))
                return "Kawaii Studio is not running. Start the app, the link follows on its own.";
            return "Kawaii Studio could not be reached: " + e.Message;
        }

        private static bool ProcessAlive(int pid)
        {
            if (pid <= 0) return true;
            try
            {
                using (Process p = Process.GetProcessById(pid)) return !p.HasExited;
            }
            catch
            {
                return false;
            }
        }

        private static string Quote(string s)
        {
            if (s == null) return "\"\"";
            var b = new StringBuilder(s.Length + 2);
            b.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                        else b.Append(c);
                        break;
                }
            }
            b.Append('"');
            return b.ToString();
        }

        private static bool SameVolume(string a, string b) =>
            string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

        private static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from)) File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true);
            foreach (string d in Directory.GetDirectories(from)) CopyTree(d, Path.Combine(to, Path.GetFileName(d)));
        }

        // Windows lets the app hand its foreground right to this process (AllowSetForegroundWindow) when it sends.
        private static void BringUnityForward()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor) return;
            try
            {
                IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                if (hwnd == IntPtr.Zero) return;
                if (IsIconic(hwnd)) ShowWindow(hwnd, 9);   // SW_RESTORE
                SetForegroundWindow(hwnd);
            }
            catch
            {
            }
        }

        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    }
}
