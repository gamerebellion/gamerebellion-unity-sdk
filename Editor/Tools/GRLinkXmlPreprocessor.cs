#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GameRebellionSdk.Editor
{
    /// <summary>
    /// Copies the SDK's link.xml to Assets/ before each build.
    ///
    /// Unity does not discover link.xml files inside UPM packages (Packages/).
    /// Without this, the IL2CPP managed code stripping phase may remove SDK
    /// assemblies that are only referenced via reflection or P/Invoke.
    ///
    /// For .unitypackage installs (where the SDK lives under Assets/), the copy
    /// is skipped because Unity can find link.xml directly.
    /// </summary>
    public class GRLinkXmlPreprocessor : IPreprocessBuildWithReport
    {
        private const string PackageName = "com.gamerebellion.sdk";
        private const string LinkFileName = "link.xml";
        private const string AssetsTargetFolder = "Assets/GameRebellion";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var packageLinkXml = Path.Combine("Packages", PackageName, LinkFileName);
            var assetsLinkXml = Path.Combine(AssetsTargetFolder, LinkFileName);

            // If the SDK lives under Assets/ (.unitypackage install), link.xml is
            // already discoverable — nothing to do.
            if (File.Exists(assetsLinkXml))
            {
                return;
            }

            // UPM install: link.xml is inside Packages/ and invisible to the linker.
            if (!File.Exists(packageLinkXml))
            {
                Debug.LogWarning(
                    $"[GameRebellion] link.xml not found at {packageLinkXml}. " +
                    "Managed code stripping may remove SDK assemblies.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(AssetsTargetFolder))
            {
                Directory.CreateDirectory(AssetsTargetFolder);
                AssetDatabase.Refresh();
            }

            AssetDatabase.CopyAsset(packageLinkXml, assetsLinkXml);
            Debug.Log($"[GameRebellion] Copied link.xml to {assetsLinkXml} for IL2CPP stripping protection.");
        }
    }
}
#endif
