using System.Collections.Generic;

using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The coding agents, one GRAgentRow.uxml each: whether it can work on this
    /// project, and its Connect button (GRMcpSection.uxml). Claude Code is always
    /// listed; the others when they're installed.
    /// </summary>
    internal sealed class GRMcpSection : IGRSection
    {
        private sealed class Row
        {
            public GRIdeClient Client;
            public VisualElement Dot;
            public Label Detail;
            public Button Connect;
        }

        private readonly GRWindowModel _model;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Button _refresh;
        private readonly Label _serversNote;
        private readonly Label _bridgeProblem;
        private readonly Label _message;

        public GRMcpSection(VisualElement root, GRWindowModel model, VisualTreeAsset rowTemplate)
        {
            _model = model;
            _serversNote = root.Q<Label>("servers-note");
            _bridgeProblem = root.Q<Label>("bridge-problem");
            _message = root.Q<Label>("mcp-message");

            _refresh = root.Q<Button>("refresh-agents-button");
            _refresh.clicked += model.RefreshAgents;
            root.Q<Button>("copy-config-button").clicked += model.CopyConfig;

            // The list is fixed for the window's lifetime, so the rows are built once.
            var list = root.Q("agents-list");
            foreach (var client in model.Clients)
            {
                var item = rowTemplate.Instantiate();
                item.Q<Label>("agent-name").text = GRWindowModel.ClientName(client) + (model.IsSimulated(client) ? " (mock)" : string.Empty);
                var row = new Row
                {
                    Client = client,
                    Dot = item.Q("agent-dot"),
                    Detail = item.Q<Label>("agent-detail"),
                    Connect = item.Q<Button>("agent-connect"),
                };
                row.Connect.clicked += () => model.Connect(client);
                _rows.Add(row);
                list.Add(item);
            }
        }

        public void Render()
        {
            foreach (var row in _rows)
            {
                var status = _model.AgentStatus(row.Client);
                GRUi.SetStatus(row.Dot, Dot(status.State));
                row.Detail.text = status.Detail;

                // A client that isn't set up yet is a choice, not a fault: a hollow dot, not a red one.
                var setUp = status.State != GRAgentState.Checking && status.State != GRAgentState.NotSetUp;
                row.Connect.text = _model.ConnectingClient == row.Client ? "Connecting…" : setUp ? "Reconnect" : "Connect";
                row.Connect.SetEnabled(_model.ConnectingClient == null);
            }

            _refresh.text = _model.IsCheckingAgents ? "Checking…" : "Refresh";
            _refresh.SetEnabled(!_model.IsCheckingAgents);
            _serversNote.text = _model.ServersNote;

            var problem = _model.BridgeProblem;
            GRUi.ShowMessage(_bridgeProblem, problem == null ? null : new GRMessage(problem, GRMessageKind.Warn));
            GRUi.ShowMessage(_message, _model.McpMessage);
        }

        private static GRStatus Dot(GRAgentState state)
        {
            switch (state)
            {
                case GRAgentState.Ready: return GRStatus.Ok;
                case GRAgentState.NeedsSignIn: return GRStatus.Warn;
                case GRAgentState.Problem: return GRStatus.Error;
                default: return GRStatus.Idle;
            }
        }
    }
}
