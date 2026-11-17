// Kawaii Studio Link window -- pairs this project with the Kawaii Studio app and shows what the
// link carries. The work itself (pairing, the poll, the imports) is KawaiiStudioLink, which keeps
// running with this window closed.
//
// IMGUI + a background thread: KawaiiStudioLink's socket thread grows the log and the history, and flips the
// connection state, at any moment -- including BETWEEN the Layout and the Repaint pass of one OnGUI. When it does,
// a section builds one control count on Layout and a different one on Repaint, and IMGUI aborts with
// "Getting control N's position in a group with only N controls". So every volatile value the window reads is
// captured ONCE, on the Layout pass, into fields, and all the sections draw from those fields -- the two passes
// then always build the same controls.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace KawaiiStudio
{
    public class KawaiiStudioLinkWindow : EditorWindow
    {
        private const string VERSION = KawaiiStudioLinkVersion.Current;

        private Vector2 _scroll;

        // what Test in Kawaii Studio would build, recomputed when the selection or the scene changes (a world scan is not free)
        private KawaiiStudioTestBuild.TargetKind _testKind;
        private GameObject _testAvatar;
        private string _testLabel = "";
        private bool _testRunning;

        // the link state the background thread can change under us, frozen on the Layout pass (see the file header)
        private bool _captured;
        private KawaiiStudioLink.LinkState _vState;
        private KawaiiStudioLink.AppStatus _vStatus;
        private bool _vEnabled, _vImporting, _vIsLinked;
        private string _vDetail = "", _vProject = "", _vImportingName = "", _vImportingWait = "";
        private int _vPort;
        private string[] _vLog = Array.Empty<string>();
        private KawaiiStudioLink.Transfer[] _vRecent = Array.Empty<KawaiiStudioLink.Transfer>();

        [MenuItem("Kawaii Studio/Kawaii Studio Link", priority = 1)]
        public static void ShowWindow()
        {
            var window = GetWindow<KawaiiStudioLinkWindow>("Kawaii Studio Link");
            window.minSize = new Vector2(440, 560);
            window.Show();
        }

        private void OnEnable()
        {
            KawaiiStudioLink.Changed += Repaint;
            ResolveTest();
        }

        private void OnSelectionChange()
        {
            ResolveTest();
            Repaint();
        }

        private void OnHierarchyChange() => ResolveTest();

        private void OnFocus() => ResolveTest();

        private void ResolveTest()
        {
            try { _testKind = KawaiiStudioTestBuild.Resolve(out _testAvatar, out _testLabel); }
            catch { _testKind = KawaiiStudioTestBuild.TargetKind.None; }
        }

        private void OnDisable() => KawaiiStudioLink.Changed -= Repaint;

        // the import step counter and the waiting reason move between polls
        private void OnInspectorUpdate()
        {
            if (KawaiiStudioLink.Importing) Repaint();
        }

        // Freeze everything the background thread can change, once per repaint, on the Layout pass. The Repaint pass
        // (and any input event in the same cycle) then reads the identical values the Layout pass laid out.
        private void Capture()
        {
            if (_captured && Event.current != null && Event.current.type != EventType.Layout) return;
            _captured = true;
            _vState = KawaiiStudioLink.State;
            _vStatus = KawaiiStudioLink.Status;
            _vEnabled = KawaiiStudioLink.Enabled;
            _vDetail = KawaiiStudioLink.Detail ?? "";
            _vProject = KawaiiStudioLink.Project ?? "";
            _vPort = KawaiiStudioLink.Port;
            _vImporting = KawaiiStudioLink.Importing;
            _vImportingName = KawaiiStudioLink.ImportingName ?? "";
            _vImportingWait = KawaiiStudioLink.ImportingWaitReason ?? "";
            _vIsLinked = KawaiiStudioLink.IsLinked;
            // The lists are mutated on the socket thread; a torn copy just shows stale text for one frame, so keep
            // the last good snapshot rather than risk a half-copied array.
            try { _vLog = KawaiiStudioLink.LogLines.ToArray(); } catch { }
            try { _vRecent = KawaiiStudioLink.History.ToArray(); } catch { }
        }

        private void OnGUI()
        {
            Capture();
            KawaiiStudioLinkGUI.Initialize();
            KawaiiStudioLinkGUI.DrawWindowBackground(position);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            KawaiiStudioLinkGUI.DrawBanner(
                "KAWAII STUDIO LINK",
                "This project and the Kawaii Studio app, linked",
                VERSION,
                KawaiiStudioLinkPaths.Logo,
                KawaiiStudioLinkPaths.Banner);   // the app's own galaxy, so the window looks like Kawaii Studio

            KawaiiStudioLinkGUI.DrawSection("Connection", KawaiiStudioLinkGUI.AccentPurple, DrawConnection);
            KawaiiStudioLinkGUI.DrawSection("Account", KawaiiStudioLinkGUI.AccentPink, DrawAccount);
            KawaiiStudioLinkGUI.DrawSection("Send to Unity", KawaiiStudioLinkGUI.AccentCyan, DrawSend);
            KawaiiStudioLinkGUI.DrawSection("Test in Kawaii Studio", KawaiiStudioLinkGUI.AccentBlue, DrawTest);
            KawaiiStudioLinkGUI.DrawSection("Recent", KawaiiStudioLinkGUI.AccentGreen, DrawRecent);
            KawaiiStudioLinkGUI.DrawSection("Log", KawaiiStudioLinkGUI.AccentOrange, DrawLog);

            KawaiiStudioLinkGUI.DrawFooter();
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------------ sections

        private void DrawConnection()
        {
            var state = _vState;
            var status = _vStatus;
            string label;
            Color color;
            switch (state)
            {
                case KawaiiStudioLink.LinkState.Linked:
                    label = "Linked";
                    color = KawaiiStudioLinkGUI.SuccessColor;
                    break;
                case KawaiiStudioLink.LinkState.NoApp:
                    label = "Waiting for Kawaii Studio";
                    color = KawaiiStudioLinkGUI.WarningColor;
                    break;
                case KawaiiStudioLink.LinkState.Failed:
                    label = "Refused";
                    color = KawaiiStudioLinkGUI.ErrorColor;
                    break;
                default:
                    label = _vEnabled ? "Connecting" : "Not connected";
                    color = KawaiiStudioLinkGUI.MutedTextColor;
                    break;
            }

            KawaiiStudioLinkGUI.KeyValueRow("Status", label, color);
            KawaiiStudioLinkGUI.KeyValueRow("Project", _vProject);
            if (state == KawaiiStudioLink.LinkState.Linked && status != null)
            {
                KawaiiStudioLinkGUI.KeyValueRow("App", (string.IsNullOrEmpty(status.app) ? "Kawaii Studio" : status.app) + " v" + status.version);
                KawaiiStudioLinkGUI.KeyValueRow("Bridge", "127.0.0.1:" + _vPort);
            }
            if (!string.IsNullOrEmpty(_vDetail))
                KawaiiStudioLinkGUI.Banner(_vDetail,
                    state == KawaiiStudioLink.LinkState.Failed ? KawaiiStudioLinkGUI.MessageKind.Error : KawaiiStudioLinkGUI.MessageKind.Warning);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (!_vEnabled)
            {
                if (KawaiiStudioLinkGUI.PrimaryButton("Connect")) KawaiiStudioLink.Connect();
            }
            else if (KawaiiStudioLinkGUI.SecondaryButton("Disconnect"))
            {
                KawaiiStudioLink.Disconnect();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.Label(_vEnabled
                ? "This project links itself whenever it is open and Kawaii Studio runs. Nothing leaves this PC: the link is local."
                : "Disconnected: this project stays apart from Kawaii Studio until you click Connect.",
                KawaiiStudioLinkGUI.InfoLabelStyle);
        }

        private void DrawAccount()
        {
            var status = _vStatus;
            if (_vState != KawaiiStudioLink.LinkState.Linked || status == null)
            {
                GUILayout.Label("Shown once the link is up.", KawaiiStudioLinkGUI.InfoLabelStyle);
                return;
            }
            if (!status.signedIn)
            {
                KawaiiStudioLinkGUI.Banner("Nobody is signed in to Kawaii Studio. Sign in there to use the Scene Editor.", KawaiiStudioLinkGUI.MessageKind.Warning);
                return;
            }
            KawaiiStudioLinkGUI.KeyValueRow("Signed in", status.user);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Level", KawaiiStudioLinkGUI.LabelStyle, GUILayout.Width(110));
            KawaiiStudioLinkGUI.Badge(status.levelName + (status.admin ? "  (admin)" : ""), KawaiiStudioLinkGUI.AccentPink);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (status.gates != null)
            {
                GUILayout.Space(4);
                foreach (var gate in status.gates)
                {
                    KawaiiStudioLinkGUI.KeyValueRow(gate.name, gate.ok ? "Included in your level" : "Needs " + gate.need,
                        gate.ok ? KawaiiStudioLinkGUI.SuccessColor : KawaiiStudioLinkGUI.MutedTextColor);
                }
            }
            GUILayout.Space(2);
            GUILayout.Label("The server decides what opens. This window only shows what the app reports.", KawaiiStudioLinkGUI.InfoLabelStyle);
        }

        private void DrawSend()
        {
            var status = _vStatus;
            GUILayout.Label("In the Kawaii Studio Scene Editor, click Send to Unity. The export lands in " +
                            KawaiiStudioLinkPaths.Exported + " and is selected here.", KawaiiStudioLinkGUI.InfoLabelStyle);
            GUILayout.Space(4);

            if (_vImporting)
            {
                string wait = _vImportingWait;
                KawaiiStudioLinkGUI.Banner("Importing " + _vImportingName +
                                       (string.IsNullOrEmpty(wait) ? "." : ". Waiting for Unity to finish " + wait + "."),
                    KawaiiStudioLinkGUI.MessageKind.Info);
            }
            else if (_vState == KawaiiStudioLink.LinkState.Linked && status != null)
            {
                if (status.target)
                    KawaiiStudioLinkGUI.Banner("Send to Unity reaches this project.", KawaiiStudioLinkGUI.MessageKind.Success);
                else
                    KawaiiStudioLinkGUI.Banner("Another linked project was used last. Click in this Unity to make it the target.", KawaiiStudioLinkGUI.MessageKind.Info);
                if (!status.editorReady)
                    GUILayout.Label("The Scene Editor opens from the app's Scene Editor page.", KawaiiStudioLinkGUI.InfoLabelStyle);
            }

            bool front = KawaiiStudioLinkGUI.DrawToggle("Bring Unity to the front when an export arrives", KawaiiStudioLink.BringToFront);
            if (front != KawaiiStudioLink.BringToFront) KawaiiStudioLink.BringToFront = front;
        }

        private void DrawTest()
        {
            var status = _vStatus;
            GUILayout.Label("Builds the selected avatar, or the open world scene, and opens it in the Kawaii Studio Scene Editor. " +
                            "An avatar starts in Play mode, a world on the Photon sim.", KawaiiStudioLinkGUI.InfoLabelStyle);
            GUILayout.Space(4);
            switch (_testKind)
            {
                case KawaiiStudioTestBuild.TargetKind.Avatar:
                    KawaiiStudioLinkGUI.KeyValueRow("Avatar", _testLabel);
                    break;
                case KawaiiStudioTestBuild.TargetKind.World:
                    KawaiiStudioLinkGUI.KeyValueRow("World", _testLabel);
                    break;
                default:
                    KawaiiStudioLinkGUI.Banner("Select an avatar (a GameObject with a VRC Avatar Descriptor), or open a VRChat world scene.", KawaiiStudioLinkGUI.MessageKind.Info);
                    break;
            }
            if (_vIsLinked && status != null && !status.testInApp)
                KawaiiStudioLinkGUI.Banner("This Kawaii Studio cannot open bundles from Unity yet. Update the app.", KawaiiStudioLinkGUI.MessageKind.Warning);

            bool play = KawaiiStudioLinkGUI.DrawToggle("Start playing as soon as it opens", KawaiiStudioTestBuild.PlayOnOpen);
            if (play != KawaiiStudioTestBuild.PlayOnOpen) KawaiiStudioTestBuild.PlayOnOpen = play;

            GUILayout.Space(4);
            bool ready = _vIsLinked && _testKind != KawaiiStudioTestBuild.TargetKind.None && !_testRunning;
            using (new EditorGUI.DisabledScope(!ready))
            {
                if (KawaiiStudioLinkGUI.PrimaryButton(_testRunning ? "Building" : "Test in Kawaii Studio"))
                {
                    _testRunning = true;
                    // after this frame: the build blocks the editor, the button should not stay pressed meanwhile
                    EditorApplication.delayCall += () =>
                        KawaiiStudioTestBuild.Run(_testKind, _testAvatar, _testLabel, KawaiiStudioTestBuild.PlayOnOpen, (ok, message) =>
                        {
                            _testRunning = false;
                            ShowNotification(new GUIContent(ok ? "Sent to Kawaii Studio" : "Not opened"));
                            Repaint();
                        });
                }
            }
            if (!_vIsLinked)
                GUILayout.Label("Connect first: the build goes to the linked app.", KawaiiStudioLinkGUI.InfoLabelStyle);
        }

        private void DrawRecent()
        {
            var items = _vRecent;
            if (items.Length == 0)
            {
                GUILayout.Label("Nothing received yet.", KawaiiStudioLinkGUI.InfoLabelStyle);
                return;
            }
            foreach (var t in items.Take(8))
            {
                if (t == null) continue;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.BeginVertical();
                GUILayout.Label(t.name + "  (" + t.type + ")", KawaiiStudioLinkGUI.H3);
                GUILayout.Label(t.when + "  " + (t.ok ? t.message : "Failed: " + t.message), KawaiiStudioLinkGUI.InfoLabelStyle);
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                bool exists = t.ok && !string.IsNullOrEmpty(t.asset) && (File.Exists(t.asset) || AssetDatabase.IsValidFolder(t.asset));
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (KawaiiStudioLinkGUI.SecondaryButton("Select", GUILayout.Width(70)))
                    {
                        var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(t.asset);
                        Selection.activeObject = obj;
                        EditorGUIUtility.PingObject(obj);
                    }
                    if (t.type == "world" && t.asset != null && t.asset.EndsWith(".unity") &&
                        KawaiiStudioLinkGUI.SecondaryButton("Open", GUILayout.Width(70)) &&
                        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    {
                        EditorSceneManager.OpenScene(t.asset);
                    }
                }
                EditorGUILayout.EndHorizontal();
                KawaiiStudioLinkGUI.Separator();
            }
        }

        private void DrawLog()
        {
            var lines = _vLog;
            if (lines.Length == 0)
            {
                GUILayout.Label("Empty.", KawaiiStudioLinkGUI.InfoLabelStyle);
                return;
            }
            foreach (string line in lines.Skip(Math.Max(0, lines.Length - 10)))
                GUILayout.Label(line, KawaiiStudioLinkGUI.InfoLabelStyle);
        }
    }
}
