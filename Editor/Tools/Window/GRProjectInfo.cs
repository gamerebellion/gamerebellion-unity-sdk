using System;
using System.IO;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>Facts about this project and the installed SDK, read without asking any server.</summary>
    internal static class GRProjectInfo
    {
        private const string RuntimeAssembly = "GameRebellion.Runtime";

        [Serializable]
        [System.Reflection.Obfuscation(Exclude = true, ApplyToMembers = true)]
        private sealed class PackageManifest
        {
            public string version;
        }

        /// <summary>The Unity project folder: the parent of Assets/.</summary>
        public static string ProjectPath => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// The SDK package root as an asset path: Assets/GameRebellion in this repo and
        /// .unitypackage installs, Packages/com.gamerebellion.sdk in UPM installs. Found
        /// through the runtime assembly definition, so it needs no window or script instance.
        /// </summary>
        public static string SdkPackageRoot()
        {
            var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(RuntimeAssembly);
            return string.IsNullOrEmpty(asmdef) ? null : Path.GetDirectoryName(Path.GetDirectoryName(asmdef))?.Replace('\\', '/');
        }

        /// <summary>The installed SDK version, or null if the package manifest can't be found.</summary>
        public static string SdkVersion()
        {
            var root = SdkPackageRoot();
            if (root == null)
            {
                return null;
            }

            // UPM installs know their own version; an Assets/ copy has to read package.json.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(root);
            if (package != null)
            {
                return package.version;
            }

            var manifest = AssetDatabase.LoadAssetAtPath<TextAsset>($"{root}/package.json");
            return manifest == null ? null : JsonUtility.FromJson<PackageManifest>(manifest.text)?.version;
        }

        /// <summary>The project's GameRebellionSettings asset, or null if there is none.</summary>
        public static GameRebellionSettings FindSettings(out string path)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(GameRebellionSettings)))
            {
                path = AssetDatabase.GUIDToAssetPath(guid);
                var settings = AssetDatabase.LoadAssetAtPath<GameRebellionSettings>(path);
                if (settings != null)
                {
                    return settings;
                }
            }

            path = null;
            return null;
        }
    }
}
