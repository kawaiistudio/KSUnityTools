using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace KawaiiStudio
{
    /// <summary>
    /// Lets other Kawaii Studio packages answer questions from the app, without the Link having to know they
    /// exist.
    ///
    /// Before this, every tool the app could ask for was a hard-coded case in KawaiiStudioLinkAsk.Run, so a
    /// new feature in KS Unity Tools meant editing, re-versioning and re-publishing the Link package too. The
    /// Link is a separate package with its own release and its own version check, and KS ANTI-RIP was about to
    /// be the second feature to need that.
    ///
    /// Discovery is by convention and reflection, deliberately, so nothing needs a shared assembly and the
    /// Link keeps zero references: any loaded assembly may contain a public static class named `KSLinkTools`
    /// exposing
    ///     public static bool   Knows(string tool)
    ///     public static string Run(string tool, string argsJson)
    /// A provider that throws is reported and skipped, never allowed to take the Link down with it.
    /// </summary>
    internal static class KawaiiStudioLinkPlugins
    {
        const string ClassName = "KSLinkTools";

        struct Provider
        {
            public Type Type;
            public MethodInfo Knows;
            public MethodInfo Run;
        }

        static List<Provider> _providers;

        static List<Provider> Providers()
        {
            if (_providers != null) return _providers;
            _providers = new List<Provider>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Only editor assemblies of ours can hold one; skip the rest so a big project does not pay
                // for a full type scan on every domain reload.
                string name = assembly.GetName().Name ?? "";
                if (name.IndexOf("KawaiiStudio", StringComparison.OrdinalIgnoreCase) < 0) continue;

                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (Type type in types)
                {
                    if (type == null || !type.IsClass || type.Name != ClassName) continue;
                    MethodInfo knows = type.GetMethod("Knows", BindingFlags.Public | BindingFlags.Static,
                                                      null, new[] { typeof(string) }, null);
                    MethodInfo run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                                                    null, new[] { typeof(string), typeof(string) }, null);
                    if (knows == null || run == null) continue;
                    if (knows.ReturnType != typeof(bool) || run.ReturnType != typeof(string)) continue;
                    _providers.Add(new Provider { Type = type, Knows = knows, Run = run });
                }
            }
            return _providers;
        }

        /// <summary>Call after a domain reload, so a newly imported package is seen.</summary>
        internal static void Forget() => _providers = null;

        internal static bool Knows(string tool)
        {
            foreach (Provider p in Providers())
            {
                try
                {
                    if ((bool)p.Knows.Invoke(null, new object[] { tool })) return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Kawaii Studio Link] " + p.Type.FullName + ".Knows failed: " + Unwrap(e).Message);
                }
            }
            return false;
        }

        internal static string Run(string tool, string argsJson)
        {
            foreach (Provider p in Providers())
            {
                bool knows;
                try { knows = (bool)p.Knows.Invoke(null, new object[] { tool }); }
                catch { continue; }
                if (!knows) continue;

                try
                {
                    return (string)p.Run.Invoke(null, new object[] { tool, argsJson }) ?? "";
                }
                catch (Exception e)
                {
                    // The app asked a real question and deserves the real reason, not "unknown tool".
                    throw new InvalidOperationException(
                        p.Type.FullName + " could not answer \"" + tool + "\": " + Unwrap(e).Message);
                }
            }
            throw new ArgumentException("no Kawaii Studio package answers \"" + tool + "\"");
        }

        static Exception Unwrap(Exception e) =>
            e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e;
    }
}
