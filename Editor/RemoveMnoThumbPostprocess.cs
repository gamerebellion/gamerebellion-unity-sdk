#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace GameRebellionSdk.Editor
{
    public static class RemoveMnoThumbPostprocess
    {
        private const string Flag = "-mno-thumb";

        [PostProcessBuild(2000)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS)
            {
                return;
            }

            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            if (!File.Exists(projectPath))
            {
                Debug.LogWarning("[GRiOS] PBX project not found, skipping -mno-thumb cleanup.");
                return;
            }

            var usedFallback = false;

            try
            {
                var project = new PBXProject();
                project.ReadFromFile(projectPath);

                var targetGuids = new HashSet<string>();

                try
                {
                    var mainGuid = project.GetUnityMainTargetGuid();
                    if (!string.IsNullOrEmpty(mainGuid))
                    {
                        targetGuids.Add(mainGuid);
                    }
                }
                catch { }

                try
                {
                    var frameworkGuid = project.GetUnityFrameworkTargetGuid();
                    if (!string.IsNullOrEmpty(frameworkGuid))
                    {
                        targetGuids.Add(frameworkGuid);
                    }
                }
                catch { }

                try
                {
                    var legacyGuid = project.TargetGuidByName("Unity-iPhone");
                    if (!string.IsNullOrEmpty(legacyGuid))
                    {
                        targetGuids.Add(legacyGuid);
                    }
                }
                catch { }

                foreach (var guid in targetGuids)
                {
                    RemoveBuildFlag(project, guid, "OTHER_CFLAGS", Flag);
                    RemoveBuildFlag(project, guid, "OTHER_CPLUSPLUSFLAGS", Flag);
                }

                project.WriteToFile(projectPath);
            }
            catch (Exception)
            {
                usedFallback = true;
            }

            if (usedFallback)
            {
                try
                {
                    var text = File.ReadAllText(projectPath);
                    if (text.Contains(Flag))
                    {
                        text = text.Replace(Flag, string.Empty);
                        File.WriteAllText(projectPath, text);
                    }
                }
                catch (Exception)
                {
                    Debug.LogWarning("[GRiOS] Failed to remove -mno-thumb from Xcode project.");
                    return;
                }

                Debug.Log("[GRiOS] Removed -mno-thumb from Xcode project (fallback text replace).");
                return;
            }

            Debug.Log("[GRiOS] Removed -mno-thumb from Xcode project.");
        }

        private static void RemoveBuildFlag(PBXProject project, string targetGuid, string propertyName, string flag)
        {
            var current = project.GetBuildPropertyForAnyConfig(targetGuid, propertyName);
            if (string.IsNullOrEmpty(current) || !current.Contains(flag))
            {
                return;
            }

            var updated = current.Replace(flag, string.Empty);
            project.SetBuildProperty(targetGuid, propertyName, updated);
        }
    }
}
#endif
