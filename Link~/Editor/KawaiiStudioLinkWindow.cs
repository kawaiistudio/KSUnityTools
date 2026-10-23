// Kawaii Studio Link window -- pairs this project with the Kawaii Studio app and shows what the
// link carries. The work itself (pairing, the poll, the imports) is KawaiiStudioLink, which keeps
// running with this window closed.
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

        private void OnGUI()
        {
            KawaiiStudioLinkGUI.Initialize();
            KawaiiStudioLinkGUI.DrawWindowBackground(position);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            KawaiiStudioLinkGUI.DrawBanner(
                "KAWAII STUDIO LINK",
                "This project and the Kawaii Studio app, linked",
                VERSION,
                KawaiiStudioLinkPaths.Logo,
                null);

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
            var state = KawaiiStudioLink.State;
            var status = KawaiiStudioLink.Status;
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
                    label = KawaiiStudioLink.Enabled ? "Connecting" : "Not connected";
                    color = KawaiiStudioLinkGUI.MutedTextColor;
                    break;
            }

            KawaiiStudioLinkGUI.KeyValueRow("Status", label, color);
            KawaiiStudioLinkGUI.KeyValueRow("Project", KawaiiStudioLink.Project);
            if (state == KawaiiStudioLink.LinkState.Linked && status != null)
            {
                KawaiiStudioLinkGUI.KeyValueRow("App", (string.IsNullOrEmpty(status.app) ? "Kawaii Studio" : status.app) + " v" + status.version);
                KawaiiStudioLinkGUI.KeyValueRow("Bridge", "127.0.0.1:" + KawaiiStudioLink.Port);
            }
            if (!string.IsNullOrEmpty(KawaiiStudioLink.Detail))
                KawaiiStudioLinkGUI.Banner(KawaiiStudioLink.Detail,
                    state == KawaiiStudioLink.LinkState.Failed ? KawaiiStudioLinkGUI.MessageKind.Error : KawaiiStudioLinkGUI.MessageKind.Warning);

            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (!KawaiiStudioLink.Enabled)
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
            GUILayout.Label(KawaiiStudioLink.Enabled
                ? "This project connects again on its own each time it opens."
                : "Connect once and this project links itself each time it opens. Nothing leaves this PC: the link is local.",
                KawaiiStudioLinkGUI.InfoLabelStyle);
        }

        private void DrawAccount()
        {
            var status = KawaiiStudioLink.Status;
            if (KawaiiStudioLink.State != KawaiiStudioLink.LinkState.Linked || status == null)
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
            var status = KawaiiStudioLink.Status;
            GUILayout.Label("In the Kawaii Studio Scene Editor, click Send to Unity. The export lands in " +
                            KawaiiStudioLinkPaths.Exported + " and is selected here.", KawaiiStudioLinkGUI.InfoLabelStyle);
            GUILayout.Space(4);

            if (KawaiiStudioLink.Importing)
            {
                string wait = KawaiiStudioLink.ImportingWaitReason;
                KawaiiStudioLinkGUI.Banner("Importing " + KawaiiStudioLink.ImportingName +
                                       (string.IsNullOrEmpty(wait) ? "." : ". Waiting for Unity to finish " + wait + "."),
                    KawaiiStudioLinkGUI.MessageKind.Info);
            }
            else if (KawaiiStudioLink.State == KawaiiStudioLink.LinkState.Linked && status != null)
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
            var status = KawaiiStudioLink.Status;
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
            if (KawaiiStudioLink.IsLinked && status != null && !status.testInApp)
                KawaiiStudioLinkGUI.Banner("This Kawaii Studio cannot open bundles from Unity yet. Update the app.", KawaiiStudioLinkGUI.MessageKind.Warning);

            bool play = KawaiiStudioLinkGUI.DrawToggle("Start playing as soon as it opens", KawaiiStudioTestBuild.PlayOnOpen);
            if (play != KawaiiStudioTestBuild.PlayOnOpen) KawaiiStudioTestBuild.PlayOnOpen = play;

            GUILayout.Space(4);
            bool ready = KawaiiStudioLink.IsLinked && _testKind != KawaiiStudioTestBuild.TargetKind.None && !_testRunning;
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
            if (!KawaiiStudioLink.IsLinked)
                GUILayout.Label("Connect first: the build goes to the linked app.", KawaiiStudioLinkGUI.InfoLabelStyle);
        }

        private void DrawRecent()
        {
            var items = KawaiiStudioLink.History;
            if (items.Count == 0)
            {
                GUILayout.Label("Nothing received yet.", KawaiiStudioLinkGUI.InfoLabelStyle);
                return;
            }
            foreach (var t in items.Take(8))
            {
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
                        var obj = AssetDatabase.LoadAssetAtPath<Object>(t.asset);
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
            var lines = KawaiiStudioLink.LogLines;
            if (lines.Count == 0)
            {
                GUILayout.Label("Empty.", KawaiiStudioLinkGUI.InfoLabelStyle);
                return;
            }
            foreach (string line in lines.Skip(System.Math.Max(0, lines.Count - 10)))
                GUILayout.Label(line, KawaiiStudioLinkGUI.InfoLabelStyle);
        }
    }
}
