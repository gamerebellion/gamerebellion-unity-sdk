using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Editor scripts left behind by an SDK version that shipped the editor code as
    /// source. Published packages ship it compiled (GameRebellion.EditorTools), and
    /// importing a .unitypackage never deletes files, so an upgrade keeps the old
    /// scripts. They still compile, into GameRebellion.Editor next to the three
    /// platform hooks, and start a second bridge, window and set of build hooks.
    /// While they are there, this assembly starts none of its own and says what to
    /// delete. UPM replaces the whole package and never gets here.
    /// </summary>
    [InitializeOnLoad]
    internal static class GRLegacySources
    {
        private const string LegacyAssembly = "GameRebellion.Editor";

        // The platform hooks stay in GameRebellion.Editor, as source, on purpose.
        private static readonly HashSet<string> Hooks = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GRIapXcodePostProcess.cs",
            "GRPlayBillingDependency.cs",
            "RemoveMnoThumbPostprocess.cs",
        };

        // Every editor script the SDK has shipped is named GR*.cs or IGR*.cs.
        private static readonly Regex SdkScript = new Regex(@"^I?GR\w*\.cs$", RegexOptions.IgnoreCase);

        private static string[] _files;

        static GRLegacySources()
        {
            if (Found)
            {
                Debug.LogWarning(Message);
            }
        }

        /// <summary>True while old editor scripts are in the project. Checked once per domain load.</summary>
        public static bool Found => Files.Length > 0;

        /// <summary>The old scripts, as project paths.</summary>
        public static string[] Files => _files ??= FindFiles();

        private static IEnumerable<string> Folders =>
            Files.Select(f => Path.GetDirectoryName(f)?.Replace('\\', '/')).Where(f => !string.IsNullOrEmpty(f)).Distinct();

        private static string Message =>
            $"GameRebellion: this project still has {Files.Length} editor scripts from an earlier SDK version, in " +
            $"{string.Join(", ", Folders)}. This version ships them compiled, so its window, MCP bridge and build " +
            "hooks stay off until the old scripts are deleted. Delete them from Window > GameRebellion, or by hand: " +
            "the GR*.cs and IGR*.cs files in those folders, except GRIapXcodePostProcess.cs and " +
            "GRPlayBillingDependency.cs. Keep the .uxml, .uss and image files.";

        private static string[] FindFiles()
        {
            try
            {
                var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                    .FirstOrDefault(a => a.name == LegacyAssembly);
                if (assembly == null)
                {
                    return Array.Empty<string>();
                }

                return assembly.sourceFiles
                    .Where(f => SdkScript.IsMatch(Path.GetFileName(f)) && !Hooks.Contains(Path.GetFileName(f)))
                    .Select(f => f.Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return Array.Empty<string>();
            }
        }

        /// <summary>Fills the window with the explanation and a button that deletes the old scripts.</summary>
        public static void ShowIn(VisualElement root)
        {
            root.Clear();
            root.Add(new HelpBox(Message, HelpBoxMessageType.Warning));
            root.Add(new Button(Delete) { text = $"Move the {Files.Length} old scripts to the Trash" });
        }

        private static void Delete()
        {
            var files = Files;
            if (files.Length == 0 || !EditorUtility.DisplayDialog(
                    "Delete old GameRebellion editor scripts",
                    $"Move these {files.Length} scripts from an earlier SDK version to the Trash?\n\n{string.Join("\n", files)}",
                    "Move to Trash",
                    "Cancel"))
            {
                return;
            }

            var failed = new List<string>();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var file in files)
                {
                    if (!AssetDatabase.MoveAssetToTrash(file))
                    {
                        failed.Add(file);
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            // Folders the scripts leave empty (Api, Auth, Mcp/Bridge, Mcp), deepest first.
            foreach (var folder in Folders.OrderByDescending(f => f.Length))
            {
                if (AssetDatabase.IsValidFolder(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
                {
                    AssetDatabase.MoveAssetToTrash(folder);
                }
            }

            if (failed.Count > 0)
            {
                Debug.LogWarning($"GameRebellion: couldn't move these to the Trash; delete them by hand: {string.Join(", ", failed)}");
            }

            AssetDatabase.Refresh();
        }
    }
}
