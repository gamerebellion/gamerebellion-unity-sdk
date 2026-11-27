using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Copies the gr-editor-mcp relay from the package (Editor/McpRelay/&lt;rid&gt;/;
    /// under Editor/, so never in a player build) to ~/.gamerebellion/bin. The package path changes
    /// with every SDK version; the install path doesn't, so IDE configs never go
    /// stale (EDITOR_MCP_ARCHITECTURE.md §7).
    /// </summary>
    internal static class GRRelayInstaller
    {
        private const string ExeBaseName = "gr-editor-mcp";
        private const string ToolsFile = "tools.json";

        private static bool IsWindows => Application.platform == RuntimePlatform.WindowsEditor;

        public static string ExeName => IsWindows ? ExeBaseName + ".exe" : ExeBaseName;

        /// <summary>The .NET runtime identifier the relay is built for on this machine.</summary>
        public static string Rid
        {
            get
            {
                var arm = RuntimeInformation.OSArchitecture == Architecture.Arm64;
                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor: return arm ? "osx-arm64" : "osx-x64";
                    case RuntimePlatform.WindowsEditor: return arm ? "win-arm64" : "win-x64";
                    default: return arm ? "linux-arm64" : "linux-x64";
                }
            }
        }

        public static string InstallFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gamerebellion", "bin");
        public static string InstalledExe => Path.Combine(InstallFolder, ExeName);

        /// <summary>Editor/McpRelay inside the SDK package, as a full path; null if the package can't be found.</summary>
        public static string PackagedFolder
        {
            get
            {
                var root = GRProjectInfo.SdkPackageRoot();
                return root == null ? null : Path.GetFullPath(Path.Combine(root, "Editor", "McpRelay"));
            }
        }

        public static string PackagedExe => PackagedFolder == null ? null : Path.Combine(PackagedFolder, Rid, ExeName);

        public static bool IsPackaged => PackagedExe != null && File.Exists(PackagedExe);

        public static bool IsInstalled => File.Exists(InstalledExe);

        /// <summary>Installs or updates the relay and its tool list. Main thread: it resolves the package path.</summary>
        public static void Install()
        {
            if (!IsPackaged)
            {
                throw new InvalidOperationException($"This GameRebellion SDK package has no MCP relay for {Rid} (expected {PackagedExe}). Reinstall the SDK; in the SDK repo, run tools/gr-editor-mcp/build.sh.");
            }

            Directory.CreateDirectory(InstallFolder);
            CopyIfChanged(PackagedExe, InstalledExe);
            CopyIfChanged(Path.Combine(PackagedFolder, ToolsFile), Path.Combine(InstallFolder, ToolsFile));
            if (!IsWindows)
            {
                GRShell.RunProgram("/bin/chmod", new[] { "755", InstalledExe }, InstallFolder, 5000);
            }
        }

        private static void CopyIfChanged(string from, string to)
        {
            if (File.Exists(to) && File.ReadAllBytes(from).SequenceEqual(File.ReadAllBytes(to)))
            {
                return;
            }

            // Copy beside, then swap: a relay that is running keeps its old file until it exits.
            var temp = to + ".tmp";
            File.Copy(from, temp, true);
            if (File.Exists(to))
            {
                File.Delete(to);
            }

            File.Move(temp, to);
        }
    }
}
