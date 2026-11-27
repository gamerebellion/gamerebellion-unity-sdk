using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor;
using UnityEngine;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Pretends to register the MCP servers. It runs no command and touches no
    /// config file; it logs what the real connector would do and remembers the
    /// client as connected. For working on the window offline: swap it in in GRServices.
    /// </summary>
    internal sealed class GRMockIdeConnector : IGRIdeConnector
    {
        private const string ConnectedKeyPrefix = "GameRebellion.Mock.Connected.";
        private const int LatencyMs = 400;

        public IReadOnlyList<GRIdeClient> Clients { get; } =
            new[] { GRIdeClient.ClaudeCode, GRIdeClient.Codex, GRIdeClient.Cursor, GRIdeClient.VSCode, GRIdeClient.GeminiCli };

        public Task<GRAgentStatus> GetStatusAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken)
        {
            return Task.FromResult(EditorPrefs.GetBool(ConnectedKeyPrefix + client, false)
                ? new GRAgentStatus(GRAgentState.Ready, "Mock: nothing is registered for real.")
                : new GRAgentStatus(GRAgentState.NotSetUp, "Mock: Connect only pretends to register the servers."));
        }

        public bool IsSimulated(GRIdeClient client) => true;

        public async Task<string> ConnectAsync(GRIdeClient client, GRMcpEndpoints endpoints, CancellationToken cancellationToken)
        {
            await Task.Delay(LatencyMs, cancellationToken);

            Debug.Log($"[GameRebellion] Mock connect for {client}: nothing was changed. A real connect would set up:\n{endpoints.SetupFor(client)}");
            EditorPrefs.SetBool(ConnectedKeyPrefix + client, true);
            return $"{GRWindowModel.ClientName(client)}: mock connect, nothing was changed. The Console shows what a real connect sets up.";
        }
    }
}
