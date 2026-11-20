using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3A.Editor;

namespace KawaiiStudio.VRC.AntiRip
{
    /// <summary>
    /// The VRChat SDK's verdict on a PROTECTED upload, sent to the Kawaii Studio app's KS ANTI-RIP log (3.12.2).
    ///
    /// Owner, 2026-11-18: "y'a aucun log de communication Unity dans la section log de mon app ... et mon Unity a pas
    /// réussi à upload l'avatar, pourquoi je sais pas". The app saw its own part of the upload (the plan, the key) and
    /// nothing after it, and Unity's console only said "Avatar validation failed": the SDK writes its actual reasons
    /// as separate errors, after the fact. This listens to the SDK's builder events and, for an upload KS Anti-Rip
    /// protected, sends the app what the SDK concluded -- built and uploaded, or stopped, with the SDK's own error
    /// lines from that build.
    ///
    /// Nothing here can change a build: it only listens and reports, and a report that cannot reach the app is
    /// dropped (<see cref="KSAntiRipClient.Report"/>).
    /// </summary>
    [InitializeOnLoad]
    public static class KSAntiRipSdkReport
    {
        static bool _watching;        // a protected build is in progress: its outcome goes to the app
        static readonly List<string> Errors = new List<string>();

        static KSAntiRipSdkReport()
        {
            VRCSdkControlPanel.OnSdkPanelEnable += (_, __) => Attach();
            Application.logMessageReceived += OnLog;
            Attach();   // the panel may already be open after a script reload
        }

        /// <summary>Called by the build hook when it protects an avatar: from here, the SDK's verdict is reported.</summary>
        public static void Watch()
        {
            Done();
            _watching = true;
        }

        static void Attach()
        {
            try
            {
                // TryGetBuilder logs a red "Cannot get builder, SDK window is not open" when it is closed (seen on the
                // bench at every script reload): only ask while the panel is there. OnSdkPanelEnable covers the rest.
                if (VRCSdkControlPanel.window == null) return;
                if (!VRCSdkControlPanel.TryGetBuilder<IVRCSdkAvatarBuilderApi>(out IVRCSdkAvatarBuilderApi builder) || builder == null) return;
                builder.OnSdkBuildError -= OnBuildError;
                builder.OnSdkBuildError += OnBuildError;
                // 3.17: the protected build's real sizes, measured before the SDK checks them (KSAntiRipSizes)
                builder.OnSdkBuildSuccess -= OnBuildSuccess;
                builder.OnSdkBuildSuccess += OnBuildSuccess;
                builder.OnSdkUploadSuccess -= OnUploadSuccess;
                builder.OnSdkUploadSuccess += OnUploadSuccess;
                builder.OnSdkUploadError -= OnUploadError;
                builder.OnSdkUploadError += OnUploadError;
            }
            catch
            {
                // an SDK without the builder API: no report, nothing else changes
            }
        }

        /// <summary>
        /// The SDK's reasons: it logs them as separate errors AFTER its build error ("Encountered the following
        /// validation issues during build:", then one Debug.LogError per issue -- Editor.log, 2026-11-18 00:23). Only
        /// errors from that moment on, and only LogError: exceptions an unrelated editor script throws while Unity
        /// repaints are not the SDK's reasons and would bury them.
        /// </summary>
        static void OnLog(string condition, string stackTrace, LogType type)
        {
            if (!_watching || !_errorSeen || type != LogType.Error) return;
            string line = FirstLine(condition);
            if (line.Length == 0 || line.StartsWith("[KS ANTI-RIP]", StringComparison.Ordinal)) return;   // ours: already reported
            if (line == "Avatar validation failed" || line == "Failed to build avatar!" || line == _error ||
                line.StartsWith("Encountered the following validation issues", StringComparison.Ordinal)) return;
            if (Errors.Count < 8 && !Errors.Contains(line)) Errors.Add(line);
            Flush();   // sent as soon as Unity is free again
        }

        static void OnBuildSuccess(object sender, string bundlePath)
        {
            if (!_watching) return;
            KSAntiRipSizes.AfterBuild(bundlePath);
        }

        static bool _errorSeen;
        static string _error = "";

        static void OnBuildError(object sender, string error)
        {
            if (!_watching) return;
            _errorSeen = true;
            _error = FirstLine(error);
            Flush();
        }

        static void Flush()
        {
            EditorApplication.delayCall -= Send;
            EditorApplication.delayCall += Send;
        }

        /// <summary>
        /// Sends the verdict once, then only reasons that came in after it: the SDK raises its build error twice for
        /// one failed build (when the validation fails, and again when the build ends), and its reasons are logged
        /// in between.
        /// </summary>
        static void Send()
        {
            if (!_errorSeen) return;
            var fresh = Errors.FindAll(d => !SentDetails.Contains(d));
            if (!_headSent)
            {
                KSAntiRipClient.Report("the VRChat SDK stopped the upload: " + (_error.Length > 0 ? _error : "no reason given"), fresh);
                _headSent = true;
            }
            else if (fresh.Count > 0)
            {
                KSAntiRipClient.Report("the VRChat SDK's reasons:", fresh);
            }
            foreach (string d in fresh) SentDetails.Add(d);
        }

        static bool _headSent;
        static readonly HashSet<string> SentDetails = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Called by the build hook for an upload it does not protect: nothing of it is reported.</summary>
        public static void Forget() => Done();

        static void OnUploadSuccess(object sender, string id)
        {
            if (!_watching) return;
            KSAntiRipClient.Report("VRChat accepted the protected upload" + (string.IsNullOrEmpty(id) ? "" : " (" + id + ")"));
            // 3.15: once VRChat has the upload, add the recovery-blacklist lines to the avatar's description (idempotent,
            // never throws, reports to the app log). The panel wrote its description before the bundle, so this is the
            // last write and it sticks.
            KSAntiRipDescription.Apply(id);
            Done();
        }

        static void OnUploadError(object sender, string error)
        {
            if (!_watching) return;
            KSAntiRipClient.Report("the upload to VRChat failed: " + FirstLine(error));
            Done();
        }

        static void Done()
        {
            _watching = false;
            _errorSeen = false;
            _headSent = false;
            _error = "";
            Errors.Clear();
            SentDetails.Clear();
        }

        static string FirstLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOfAny(new[] { '\r', '\n' });
            string line = (cut >= 0 ? text.Substring(0, cut) : text).Trim();
            return line.Length <= 300 ? line : line.Substring(0, 299) + "…";
        }
    }
}
