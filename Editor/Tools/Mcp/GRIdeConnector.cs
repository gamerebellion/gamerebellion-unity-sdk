using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Registers both MCP servers with Claude Code through its CLI: gr-mcp at user
    /// scope (Claude Code signs in to it itself, through /mcp), and the local Unity
    /// MCP at local scope for the repository the Unity project lives in. Nothing is
    /// written into files the developer commits. Its status comes from Claude Code
    /// itself (claude mcp get), so it is right however the servers were registered.
    /// Codex, Cursor, VS Code and Gemini CLI are set up through their own user-level
    /// config files (GRAgentConfigs), and their status is what those files say.
    ///
    /// The CLI is the one on PATH, or else the copy the Claude desktop app bundles
    /// without putting on PATH. Both write the same ~/.claude.json, so a server
    /// registered either way shows up in the terminal and in the desktop app.
    /// </summary>
    internal sealed class GRIdeConnector : IGRIdeConnector
    {
        private const int CommandTimeoutMs = 20000;

        public IReadOnlyList<GRIdeClient> Clients => GRAgentClients.Detected();

        /// <summary>One server as `claude mcp get` describes it; Listed is false when Claude Code doesn't have it.</summary>
        private readonly struct Registration
        {
            public Registration(string output)
            {
                Status = Field(output, "Status");
                Url = Field(output, "URL");
                Command = Field(output, "Command");
                Args = Field(output, "Args");
            }

            public bool Listed => Status != null;
            public string Status { get; }
            public string Url { get; }
            public string Command { get; }
            public string Args { get; }

            /// <summary>"✔ Connected" answers; anything about auth needs a sign-in; the rest ("✗ Failed to connect") doesn't answer.</summary>
            public GRAgentState Health =>
                Status.IndexOf("auth", StringComparison.OrdinalIgnoreCase) >= 0 ? GRAgentState.NeedsSignIn
                : Status.IndexOf("Connected", StringComparison.Ordinal) >= 0
                  && Status.IndexOf("fail", StringComparison.OrdinalIgnoreCase) < 0
                  && Status.IndexOf("not", StringComparison.OrdinalIgnoreCase) < 0 ? GRAgentState.Ready
                : GRAgentState.Problem;

            private static string Field(string output, string name)
            {
                var line = output.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.StartsWith(name + ":", StringComparison.Ordinal));
                return line?.Substring(name.Length + 1).Trim();
            }
        }

        public async Task<GRAgentStatus> GetStatusAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken)
        {
            if (client != GRIdeClient.ClaudeCode)
            {
                var config = GRAgentClients.ConfigFor(client);
                return await Task.Run(() => config.Status(client, endpoints), cancellationToken);
            }

            var workingDirectory = RepositoryRoot(endpoints.ProjectPath) ?? endpoints.ProjectPath;
            var cli = await Task.Run(() => FindClaudeCli(workingDirectory), cancellationToken);
            if (cli == null)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, "No Claude Code CLI found, on your PATH or in the Claude desktop app.");
            }

            // Both at once: each one contacts its server, which takes a second or so.
            var hostedTask = Task.Run(() => Describe(cli, endpoints.HostedServerName, workingDirectory), cancellationToken);
            var localTask = Task.Run(() => Describe(cli, GRMcpEndpoints.LocalServerName, workingDirectory), cancellationToken);
            var hosted = await hostedTask;
            var local = await localTask;

            var hostedName = endpoints.HostedServerName;
            var localName = GRMcpEndpoints.LocalServerName;
            if (!hosted.Listed && !local.Listed)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, "Not set up for this project yet. Connect registers both servers.");
            }

            if (!hosted.Listed)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{hostedName} isn't registered. Reconnect adds it.");
            }

            if (!string.Equals(hosted.Url?.TrimEnd('/'), endpoints.HostedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{hostedName} points at {hosted.Url}, not {endpoints.HostedUrl}. Reconnect fixes it.");
            }

            if (!local.Listed)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} isn't registered for {workingDirectory}. Reconnect adds it.");
            }

            if (local.Args == null || local.Args.IndexOf(endpoints.ProjectPath, StringComparison.Ordinal) < 0)
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} serves another Unity project. Reconnect points it at this one.");
            }

            // For example the relay's old name, gr-unity-mcp, from before 2026-11-06.
            if (!GRMcpConfigFile.SamePath(local.Command, endpoints.RelayPath))
            {
                return new GRAgentStatus(GRAgentState.NotSetUp, $"{localName} starts {local.Command ?? "another program"}, not the GameRebellion relay. Reconnect updates it.");
            }

            if (local.Health != GRAgentState.Ready)
            {
                return new GRAgentStatus(GRAgentState.Problem, $"Claude Code can't start {localName} ({local.Status}). Reconnect reinstalls it.");
            }

            switch (hosted.Health)
            {
                case GRAgentState.NeedsSignIn:
                    return new GRAgentStatus(GRAgentState.NeedsSignIn,
                        $"Claude Code's {hostedName} server isn't signed in. In Claude Code, run /mcp, pick {hostedName} and choose Authenticate. " +
                        "A GameRebellion connector in the Claude app doesn't count here: it's a second server with its own sign-in, " +
                        $"so authenticate {hostedName} even if the connector says connected.");
                case GRAgentState.Problem:
                    return new GRAgentStatus(GRAgentState.Problem, $"Claude Code can't reach {hostedName} ({hosted.Status}).");
                default:
                    return new GRAgentStatus(GRAgentState.Ready, "Ready: both servers are set up for this project, and you're signed in to gr-mcp.");
            }
        }

        /// <summary>Blocking: runs the CLI, which contacts the server for its status.</summary>
        private static Registration Describe(string cli, string name, string workingDirectory)
        {
            var result = GRShell.RunProgram(cli, new[] { "mcp", "get", name }, workingDirectory, CommandTimeoutMs);
            return new Registration(result.Output + "\n" + result.Error);
        }

        public bool IsSimulated(GRIdeClient client) => false;

        public async Task<string> ConnectAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken)
        {
            GRRelayInstaller.Install();
            if (client != GRIdeClient.ClaudeCode)
            {
                var config = GRAgentClients.ConfigFor(client);
                var name = GRAgentClients.Name(client);
                var changed = await Task.Run(() => config.Write(endpoints), cancellationToken);
                return changed
                    ? $"Set up {endpoints.HostedServerName} and {GRMcpEndpoints.LocalServerName} in {config.DisplayPath} (the old file is next to it, .gamerebellion.bak). " +
                      $"Start a new {name} session to pick them up; it asks you to sign in to gr-mcp the first time."
                    : $"{name} already has both servers in {config.DisplayPath}.";
            }

            // Claude Code's local scope belongs to the folder a session starts in,
            // which is usually the repository root rather than the Unity project.
            var workingDirectory = RepositoryRoot(endpoints.ProjectPath) ?? endpoints.ProjectPath;

            var cli = await Task.Run(() => FindClaudeCli(workingDirectory), cancellationToken);
            if (cli == null)
            {
                EditorGUIUtility.systemCopyBuffer = endpoints.GenericConfig();
                return "No Claude Code CLI found, on your PATH or in the Claude desktop app, so nothing was registered. " +
                       $"A .mcp.json with both servers is on the clipboard: save it as {Path.Combine(workingDirectory, ".mcp.json")}, " +
                       "or install the CLI and click Connect again.";
            }

            // gr-mcp: an entry with the right URL may already be signed in, so it is left
            // alone. One pointing elsewhere is replaced: its sign-in is for another server.
            var existing = await Task.Run(() => Describe(cli, endpoints.HostedServerName, workingDirectory), cancellationToken);
            var sameUrl = existing.Listed && string.Equals(existing.Url?.TrimEnd('/'), endpoints.HostedUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            if (existing.Listed && !sameUrl)
            {
                await Task.Run(() => GRShell.RunProgram(cli, new[] { "mcp", "remove", endpoints.HostedServerName, "-s", "user" }, workingDirectory, CommandTimeoutMs), cancellationToken);
            }

            if (!sameUrl)
            {
                var hosted = await Task.Run(() => GRShell.RunProgram(cli, endpoints.ClaudeHostedAddArguments(), workingDirectory, CommandTimeoutMs), cancellationToken);
                if (hosted.ExitCode != 0)
                {
                    throw new InvalidOperationException((hosted.Error + hosted.Output).Trim());
                }
            }

            // The local server: re-registering replaces an older entry, e.g. one pointing at another project.
            await Task.Run(() => GRShell.RunProgram(cli, new[] { "mcp", "remove", "--scope", "local", GRMcpEndpoints.LocalServerName }, workingDirectory, CommandTimeoutMs), cancellationToken);
            var add = await Task.Run(() => GRShell.RunProgram(cli, endpoints.ClaudeLocalAddArguments(), workingDirectory, CommandTimeoutMs), cancellationToken);
            if (add.ExitCode != 0)
            {
                throw new InvalidOperationException((add.Error + add.Output).Trim());
            }

            return $"Registered {endpoints.HostedServerName} and {GRMcpEndpoints.LocalServerName} with Claude Code for {workingDirectory}. " +
                   $"Start a new Claude Code session there, run /mcp, pick {endpoints.HostedServerName} (not the Claude app's GameRebellion connector) and choose Authenticate.";
        }

        /// <summary>
        /// "claude" when it's on the login shell's PATH; otherwise the full path of the
        /// newest copy the Claude desktop app keeps under Application Support; null if neither.
        /// Either way, a program for <see cref="GRShell.RunProgram"/>. Blocking: runs a shell.
        /// </summary>
        private static string FindClaudeCli(string workingDirectory)
        {
            if (GRShell.Run("command -v claude", workingDirectory, CommandTimeoutMs).ExitCode == 0)
            {
                return "claude";
            }

            return DesktopAppCli();
        }

        /// <summary>
        /// The desktop app's copy: claude-code/&lt;version&gt;/&lt;build&gt;/claude.app/Contents/MacOS/claude
        /// under ~/Library/Application Support/Claude. macOS only; an app-internal layout,
        /// so anything unexpected just means "not found".
        /// </summary>
        private static string DesktopAppCli()
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Claude", "claude-code");
            if (!Directory.Exists(root))
            {
                return null;
            }

            try
            {
                return Directory.GetDirectories(root)
                    .Select(versionFolder => (Folder: versionFolder, Version: Version.TryParse(Path.GetFileName(versionFolder), out var version) ? version : null))
                    .Where(entry => entry.Version != null)
                    .OrderByDescending(entry => entry.Version)
                    .SelectMany(entry => Directory.GetDirectories(entry.Folder))
                    .Select(build => Path.Combine(build, "claude.app", "Contents", "MacOS", "claude"))
                    .FirstOrDefault(File.Exists);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string RepositoryRoot(string path)
        {
            for (var directory = new DirectoryInfo(path); directory != null; directory = directory.Parent)
            {
                var git = Path.Combine(directory.FullName, ".git");
                if (Directory.Exists(git) || File.Exists(git))
                {
                    return directory.FullName;
                }
            }

            return null;
        }
    }
}
