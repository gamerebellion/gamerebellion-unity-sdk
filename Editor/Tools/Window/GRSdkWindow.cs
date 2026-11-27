using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Window > GameRebellion (EDITOR_MCP_ARCHITECTURE.md §5). Signed out, it shows
    /// the branded sign-in screen. Signed in, it has three tabs: Game (the account's
    /// games, integration state and game settings), MCP and Live events.
    ///
    /// UXML holds the structure, USS every visual property, and this class plus
    /// the section controllers only behaviour. Nothing here writes a style.
    /// </summary>
    internal sealed class GRSdkWindow : EditorWindow
    {
        // USS has no media queries; below this width the root gets gr-root--narrow.
        private const float NarrowWidth = 420f;

        // The empty slots in GRSdkWindow.uxml and the file that fills each one. Composed
        // here rather than with <ui:Template>, whose references are fixed at import
        // time and break if a template is imported after the file that uses it.
        private static readonly (string Slot, string File)[] Parts =
        {
            ("signin-view", "Views/GRSignInView.uxml"),
            ("games-section", "Sections/GRGamesSection.uxml"),
            ("integration-section", "Sections/GRIntegrationSection.uxml"),
            ("game-settings-section", "Sections/GRGameSettingsSection.uxml"),
            ("mcp-section", "Sections/GRMcpSection.uxml"),
            ("live-events-section", "Sections/GRLiveEventsSection.uxml"),
        };

        private GRWindowModel _model;

        [MenuItem("Window/GameRebellion", priority = 2000)]
        private static void Open()
        {
            var window = GetWindow<GRSdkWindow>();
            window.titleContent = new GUIContent("GameRebellion");
            window.minSize = new Vector2(320f, 360f);
        }

        // Runs again after every domain reload, so the UI is always rebuilt from the model.
        private void CreateGUI()
        {
            if (GRLegacySources.Found)
            {
                GRLegacySources.ShowIn(rootVisualElement);
                return;
            }

            var assets = GRWindowAssets.Find();
            var model = new GRWindowModel(GRServices.Auth, GRServices.Api, GRServices.Ide, GRServices.UsesMocks);

            VisualElement root;
            string missing;
            try
            {
                root = Build(rootVisualElement, model, assets, out missing);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                root = null;
                missing = null;
            }

            if (root == null)
            {
                model.Dispose();
                rootVisualElement.Clear();
                rootVisualElement.Add(new Label(missing != null
                    ? $"GameRebellion: {missing} is missing. Reimport the GameRebellion package."
                    : "GameRebellion: the window failed to build. See the Console for details."));
                return;
            }

            root.RegisterCallback<GeometryChangedEvent>(evt => root.EnableInClassList(GRUi.Narrow, evt.newRect.width < NarrowWidth));
            _model = model;
        }

        /// <summary>
        /// Clones the layout and its parts into <paramref name="host"/>, wires every
        /// view to <paramref name="model"/> and renders it. Returns the .gr-root
        /// element, or null with the first missing file in <paramref name="missing"/>.
        /// Separate from CreateGUI so the views can be driven without a window.
        /// </summary>
        internal static VisualElement Build(VisualElement host, GRWindowModel model, GRWindowAssets assets, out string missing)
        {
            var layout = assets.Layout;
            var theme = assets.Theme;
            var checklistItem = assets.ChecklistItem;
            var gameRow = assets.GameRow;
            var eventRow = assets.EventRow;
            var agentRow = assets.AgentRow;
            missing = layout == null ? "GRSdkWindow.uxml"
                : theme == null ? "the theme style sheet"
                : checklistItem == null ? "Components/GRChecklistItem.uxml"
                : gameRow == null ? "Components/GRGameRow.uxml"
                : eventRow == null ? "Components/GREventRow.uxml"
                : agentRow == null ? "Components/GRAgentRow.uxml"
                : null;
            if (missing != null)
            {
                return null;
            }

            host.styleSheets.Add(theme);
            layout.CloneTree(host);
            var root = host.Q(className: "gr-root");

            foreach (var (slot, file) in Parts)
            {
                var part = assets.Load<VisualTreeAsset>(file);
                if (part == null)
                {
                    missing = file;
                    return null;
                }

                part.CloneTree(root.Q(slot));
            }

            GRUi.SetHidden(root.Q("mock-badge"), !model.UsesMocks);
            root.Query(className: GRUi.MockOnly).ForEach(element => GRUi.SetHidden(element, !model.UsesMocks));
            root.Query(className: "gr-mock-auth-only").ForEach(element => GRUi.SetHidden(element, !model.IsAuthSimulated));

            IGRSection[] views =
            {
                new GRMainView(root, model),
                new GRSignInView(root.Q("signin-view"), model),
                new GRGamesSection(root.Q("games-section"), model, gameRow),
                new GRIntegrationSection(root.Q("integration-section"), model, checklistItem),
                new GRGameSettingsSection(root.Q("game-settings-section"), model),
                new GRMcpSection(root.Q("mcp-section"), model, agentRow),
                new GRLiveEventsSection(root.Q("live-events-section"), model, eventRow),
            };

            void RenderAll()
            {
                foreach (var view in views)
                {
                    view.Render();
                }
            }

            model.Changed += RenderAll;
            RenderAll();
            return root;
        }

        // Coming back from the agent's terminal (after /mcp, say) re-checks its status.
        private void OnFocus()
        {
            _model?.OnWindowFocused();
        }

        private void OnDisable()
        {
            _model?.Dispose();
            _model = null;
        }
    }
}
