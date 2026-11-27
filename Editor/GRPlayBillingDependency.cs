#if UNITY_ANDROID
using System;
using System.IO;
using System.Linq;

using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Adds Google Play Billing to the generated Gradle project when purchase
    /// auto-tracking is on, and takes it out again when it is off -- so a developer
    /// who leaves the box unticked gets no dependency, no version conflict and no
    /// APK size cost. Removal matters: Unity keeps an appended section across
    /// incremental builds, so without it unticking the box would leave the
    /// dependency behind until a clean build.
    ///
    /// The dependency has to be injected: the observer AAR is consumed as a flat
    /// file with no POM, so nothing its own build declares reaches this project.
    /// Same mechanism Unity IAP uses for its own billing dependency, and no EDM4U
    /// or Dependencies.xml, which only EDM4U reads.
    ///
    /// The floor, as a plain version. Gradle resolves one version of a module --
    /// the highest anyone requests -- so this builds against 8.0.0 on its own and
    /// against Unity IAP's 9.0.0 next to it, and can never downgrade anyone. A
    /// range would buy nothing: conflict resolution ignores its upper bound.
    /// </summary>
    internal sealed class GRPlayBillingDependency : IPostGenerateGradleAndroidProject
    {
        internal const string Dependency = "com.android.billingclient:billing:8.0.0";

        private const string SectionStart =
            "// GameRebellion: Play Billing for purchase auto-tracking. Generated -- do not edit.";
        private const string SectionEnd = "// End of GameRebellion Play Billing.";

        public int callbackOrder => 1;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // path is the unityLibrary module, where the plugin AARs are consumed.
            var buildGradle = Path.Combine(path, "build.gradle");

            try
            {
                if (!File.Exists(buildGradle))
                {
                    Debug.LogWarning(
                        $"[GRC] {buildGradle} not found; Play Billing was not added. Purchase " +
                        "auto-tracking will report GR_IAP_BILLING_MISSING at runtime.");
                    return;
                }

                var content = File.ReadAllText(buildGradle);

                if (!AutoTrackEnabled())
                {
                    var stripped = Remove(content);
                    if (stripped != content) File.WriteAllText(buildGradle, stripped);
                    return;
                }

                var section = SectionStart + "\n" +
                              "dependencies {\n" +
                              "    implementation '" + Dependency + "'\n" +
                              "}\n" +
                              SectionEnd;

                File.WriteAllText(buildGradle, ReplaceOrAppend(content, section));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[GRC] Failed to add Play Billing to the Gradle project: {ex.Message}");
            }
        }

        /// <summary>
        /// Whether the GR_IAP_AUTOTRACK define is set for Android, the define
        /// GRIapDefineSync maintains. Read here rather than through GRIapDefineSync:
        /// this hook ships as source, and the editor code ships compiled, with its
        /// internal names obfuscated.
        /// </summary>
        private static bool AutoTrackEnabled()
        {
            try
            {
                return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Any(d => d.Trim() == "GR_IAP_AUTOTRACK");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Takes the section out, leaving the rest of the file as it was.</summary>
        internal static string Remove(string content)
        {
            var start = content.IndexOf(SectionStart, StringComparison.Ordinal);
            if (start < 0) return content;

            var end = content.IndexOf(SectionEnd, start, StringComparison.Ordinal);
            if (end < 0) return content;

            return content.Substring(0, start).TrimEnd('\n') + "\n" +
                   content.Substring(end + SectionEnd.Length).TrimStart('\n');
        }

        /// <summary>
        /// Replaces an earlier copy of the section rather than stacking another, so
        /// re-running against an exported project stays idempotent.
        /// </summary>
        internal static string ReplaceOrAppend(string content, string section)
        {
            var start = content.IndexOf(SectionStart, StringComparison.Ordinal);
            if (start >= 0)
            {
                var end = content.IndexOf(SectionEnd, start, StringComparison.Ordinal);
                if (end >= 0)
                {
                    return content.Substring(0, start) + section + content.Substring(end + SectionEnd.Length);
                }
            }

            return content.TrimEnd('\n') + "\n\n" + section + "\n";
        }
    }
}
#endif
