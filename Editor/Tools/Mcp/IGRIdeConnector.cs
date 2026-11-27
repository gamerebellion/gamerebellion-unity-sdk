using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameRebellionSdk.Unity.Editor
{
    internal enum GRIdeClient
    {
        ClaudeCode,
        Codex,
        Cursor,
        VSCode,
        GeminiCli,
    }

    /// <summary>Whether a coding agent can work on this project, as the agent itself reports it.</summary>
    internal enum GRAgentState
    {
        Checking,

        /// <summary>Both servers registered for this project, and signed in to gr-mcp.</summary>
        Ready,

        /// <summary>Registered, but the agent still has to sign in to gr-mcp.</summary>
        NeedsSignIn,

        /// <summary>A server is missing or points elsewhere; Connect fixes it.</summary>
        NotSetUp,

        /// <summary>Registered, but a server doesn't answer the agent.</summary>
        Problem,
    }

    internal sealed class GRAgentStatus
    {
        public static readonly GRAgentStatus Checking = new GRAgentStatus(GRAgentState.Checking, "Checking…");

        public GRAgentStatus(GRAgentState state, string detail)
        {
            State = state;
            Detail = detail;
        }

        public GRAgentState State { get; }

        /// <summary>One or two sentences for the developer: what's wrong and what to do.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// Where the two MCP servers a coding agent needs are found
    /// (EDITOR_MCP_ARCHITECTURE.md §7): hosted gr-mcp for the sign-in environment (Production),
    /// and the local relay that reaches this project's Editor.
    /// Construct on the main thread: it reads the project path.
    /// </summary>
    internal readonly struct GRMcpEndpoints
    {
        public const string LocalServerName = "gamerebellion-unity";

        public GRMcpEndpoints(GrEnvironment environment)
        {
            HostedUrl = GRAuthConfig.McpUrlFor(environment);
            HostedServerName = HostedServerNameFor(environment);
            RelayPath = GRRelayInstaller.InstalledExe;
            ProjectPath = GRProjectInfo.ProjectPath;
        }

        /// <summary>gr-mcp for the environment the developer signed in to.</summary>
        public string HostedUrl { get; }

        /// <summary>gamerebellion, gamerebellion-staging or gamerebellion-dev, as the gr-mcp setup notes name them.</summary>
        public string HostedServerName { get; }

        /// <summary>The relay's stable install path, shared by every project on this machine.</summary>
        public string RelayPath { get; }

        /// <summary>The Unity project the relay serves, passed as --project.</summary>
        public string ProjectPath { get; }

        public static string HostedServerNameFor(GrEnvironment environment)
        {
            switch (environment)
            {
                case GrEnvironment.Development: return "gamerebellion-dev";
                case GrEnvironment.Staging: return "gamerebellion-staging";
                default: return "gamerebellion";
            }
        }

        /// <summary>
        /// The claude arguments that register gr-mcp for every project. Claude Code signs in
        /// to it itself: the developer runs /mcp and picks Authenticate.
        /// </summary>
        public string[] ClaudeHostedAddArguments() =>
            new[] { "mcp", "add", "--transport", "http", "--scope", "user", HostedServerName, HostedUrl };

        /// <summary>The claude arguments that register the local server for the directory claude runs in.</summary>
        public string[] ClaudeLocalAddArguments() =>
            new[] { "mcp", "add", "--scope", "local", LocalServerName, "--", RelayPath, "--project", ProjectPath };

        /// <summary><see cref="ClaudeHostedAddArguments"/> as a developer would type it.</summary>
        public string ClaudeHostedAddCommand() => GRShell.Display("claude", ClaudeHostedAddArguments());

        /// <summary><see cref="ClaudeLocalAddArguments"/> as a developer would type it, quoted so it is safe to paste.</summary>
        public string ClaudeLocalAddCommand() => GRShell.Display("claude", ClaudeLocalAddArguments());

        /// <summary>What a Connect button sets up for <paramref name="client"/>, as the developer would type it.</summary>
        public string SetupFor(GRIdeClient client)
        {
            switch (client)
            {
                case GRIdeClient.ClaudeCode:
                    return ClaudeHostedAddCommand() + "\n" + ClaudeLocalAddCommand();
                case GRIdeClient.Codex:
                    return "# ~/.codex/config.toml\n" +
                           $"[mcp_servers.{HostedServerName}]\nurl = \"{HostedUrl}\"\n\n" +
                           $"[mcp_servers.{LocalServerName}]\ncommand = \"{Escape(RelayPath)}\"\nargs = [\"--project\", \"{Escape(ProjectPath)}\"]";
                default:
                    return GenericConfig();
            }
        }

        /// <summary>An mcpServers block with both servers: a .mcp.json for Claude Code without its CLI, or any other client.</summary>
        public string GenericConfig() =>
            "{\n" +
            "  \"mcpServers\": {\n" +
            $"    \"{HostedServerName}\": {{ \"type\": \"http\", \"url\": \"{HostedUrl}\" }},\n" +
            $"    \"{LocalServerName}\": {{ \"type\": \"stdio\", \"command\": \"{Escape(RelayPath)}\", \"args\": [\"--project\", \"{Escape(ProjectPath)}\"] }}\n" +
            "  }\n" +
            "}";

        /// <summary>The inside of a JSON or TOML string: quotes, backslashes and control characters escaped.</summary>
        private static string Escape(string value)
        {
            var text = new System.Text.StringBuilder();
            foreach (var c in value)
            {
                if (c == '"' || c == '\\') text.Append('\\').Append(c);
                else if (c < ' ') text.Append("\\u").Append(((int)c).ToString("x4"));
                else text.Append(c);
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Registers the MCP servers with a coding agent, at user or local scope, so
    /// nothing is written into files the developer commits.
    /// </summary>
    internal interface IGRIdeConnector
    {
        /// <summary>The agents to offer: Claude Code, and the others installed on this machine.</summary>
        IReadOnlyList<GRIdeClient> Clients { get; }

        /// <summary>
        /// Asks the client which servers it has for this project and whether they answer.
        /// Takes a second or two: it runs the client's CLI, which contacts each server.
        /// </summary>
        Task<GRAgentStatus> GetStatusAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken);

        /// <summary>True while connecting <paramref name="client"/> is only simulated.</summary>
        bool IsSimulated(GRIdeClient client);

        /// <summary>Sets the client up and returns one line telling the developer what happened.</summary>
        Task<string> ConnectAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken);
    }
}
