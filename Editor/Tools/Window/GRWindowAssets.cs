using System.IO;

using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Finds the window's UXML and USS in the package's Editor/Window folder. The
    /// package lives at Assets/GameRebellion in this repo and in .unitypackage
    /// installs, and at Packages/com.gamerebellion.sdk in UPM installs, so the
    /// folder is found from the package root rather than hard-coded. Not from the
    /// window's own script: published packages ship the editor code as a DLL, whose
    /// MonoScript points at the DLL.
    /// </summary>
    internal sealed class GRWindowAssets
    {
        private GRWindowAssets(string folder)
        {
            Folder = folder;
        }

        /// <summary>The window's folder, e.g. Assets/GameRebellion/Editor/Window.</summary>
        public string Folder { get; }

        /// <summary>The SDK package root, two levels up from the window folder.</summary>
        public string PackageRoot => Path.GetDirectoryName(Path.GetDirectoryName(Folder))?.Replace('\\', '/');

        public static GRWindowAssets Find()
        {
            var root = GRProjectInfo.SdkPackageRoot();
            return new GRWindowAssets(root == null ? string.Empty : $"{root}/Editor/Window");
        }

        public T Load<T>(string relativePath) where T : Object
        {
            return AssetDatabase.LoadAssetAtPath<T>($"{Folder}/{relativePath}");
        }

        public VisualTreeAsset Layout => Load<VisualTreeAsset>("GRSdkWindow.uxml");
        public VisualTreeAsset ChecklistItem => Load<VisualTreeAsset>("Components/GRChecklistItem.uxml");
        public VisualTreeAsset GameRow => Load<VisualTreeAsset>("Components/GRGameRow.uxml");
        public VisualTreeAsset EventRow => Load<VisualTreeAsset>("Components/GREventRow.uxml");
        public VisualTreeAsset AgentRow => Load<VisualTreeAsset>("Components/GRAgentRow.uxml");

        /// <summary>The colour sheet for the current editor skin. Theme is the only styling decision made in code.</summary>
        public StyleSheet Theme => Load<StyleSheet>(EditorGUIUtility.isProSkin ? "Styles/GRThemeDark.uss" : "Styles/GRThemeLight.uss");
    }
}
