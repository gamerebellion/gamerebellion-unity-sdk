#if UNITY_IOS
using System.IO;

using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Teaches the generated Xcode project how to build the StoreKit observer.
    ///
    /// The observer is Swift, because StoreKit 2 is Swift-only -- StoreKit 1 has
    /// been deprecated since iOS 18 with Apple's own note reading "No longer
    /// supported". Unity will add a .swift file under Plugins/iOS to the compile
    /// sources on its own, but it does not set the Swift build settings, and
    /// without them the project fails to build with "Cannot find 'Transaction' in
    /// scope" or a missing-swift-runtime link error. Hence this pass.
    ///
    /// Runs only when GR_IAP_AUTOTRACK is set, so a project with auto-tracking off
    /// gets an Xcode project untouched by any of it.
    /// </summary>
    internal static class GRIapXcodePostProcess
    {
        /// <summary>
        /// After Unity's own post-processing. The generated project must already
        /// exist and already contain the plugin sources before this edits it.
        /// </summary>
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

#if !GR_IAP_AUTOTRACK
            return;
#else
            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning("[GRC] No Xcode project found; skipped StoreKit observer setup.");
                return;
            }

            var project = new PBXProject();
            project.ReadFromString(File.ReadAllText(projectPath));

            // The plugin sources live in UnityFramework, so that is where the Swift
            // settings belong. The Swift runtime, on the other hand, is embedded by
            // the app, which is a different target -- getting these two the wrong
            // way round produces a build that links and then crashes at launch.
            var frameworkGuid = project.GetUnityFrameworkTargetGuid();
            var appGuid = project.GetUnityMainTargetGuid();

            foreach (var guid in new[] { frameworkGuid, appGuid })
            {
                project.SetBuildProperty(guid, "SWIFT_VERSION", "5.0");
                project.SetBuildProperty(guid, "CLANG_ENABLE_MODULES", "YES");
                project.AddBuildProperty(guid, "LD_RUNPATH_SEARCH_PATHS",
                    "@executable_path/Frameworks");
            }

            project.AddBuildProperty(frameworkGuid, "LD_RUNPATH_SEARCH_PATHS",
                "@loader_path/Frameworks");

            // Embeds libswiftCore and friends. Required for any deployment target
            // below iOS 12.2; harmless above it, and cheaper than explaining the
            // launch crash to whoever still ships iOS 12.
            project.SetBuildProperty(appGuid, "ALWAYS_EMBED_SWIFT_STANDARD_LIBRARIES", "YES");

            // The plugin's .meta declares StoreKit as a framework dependency, which
            // covers the normal path. Adding it here too costs nothing and keeps the
            // build working if the importer settings are ever lost in a merge.
            project.AddFrameworkToProject(frameworkGuid, "StoreKit.framework", false);

            File.WriteAllText(projectPath, project.WriteToString());

            Debug.Log("[GRC] Configured the Xcode project for the StoreKit purchase observer.");
#endif
        }
    }
}
#endif
