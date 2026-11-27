using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Switches between the sign-in screen and the signed-in view, and runs the
    /// signed-in view's header and tabs: Game, MCP and Live events
    /// (GRSdkWindow.uxml).
    /// </summary>
    internal sealed class GRMainView : IGRSection
    {
        private readonly GRWindowModel _model;
        private readonly VisualElement _signInView;
        private readonly VisualElement _mainView;
        private readonly Label _userEmail;
        private readonly (GRTab Tab, Button Button, VisualElement Page)[] _tabs;

        public GRMainView(VisualElement root, GRWindowModel model)
        {
            _model = model;
            _signInView = root.Q("signin-view");
            _mainView = root.Q("main-view");
            _userEmail = root.Q<Label>("user-email");
            root.Q<Button>("sign-out-button").clicked += model.SignOut;

            _tabs = new[]
            {
                (GRTab.Game, root.Q<Button>("tab-game"), root.Q("page-game")),
                (GRTab.Mcp, root.Q<Button>("tab-mcp"), root.Q("page-mcp")),
                (GRTab.LiveEvents, root.Q<Button>("tab-live-events"), root.Q("page-live-events")),
            };

            foreach (var (tab, button, _) in _tabs)
            {
                button.clicked += () => model.SelectTab(tab);
            }
        }

        public void Render()
        {
            GRUi.SetHidden(_signInView, _model.IsSignedIn);
            GRUi.SetHidden(_mainView, !_model.IsSignedIn);
            _userEmail.text = _model.User?.Email ?? string.Empty;
            var studio = string.IsNullOrEmpty(_model.User?.Studio) ? null : $"Studio {_model.User.Studio}";
            var storage = _model.IsSignedIn && _model.StorageNotice != null ? "Signed in until Unity closes: this machine has no keyring to save it." : null;
            _userEmail.tooltip = studio != null && storage != null ? $"{studio}\n{storage}" : studio ?? storage;

            foreach (var (tab, button, page) in _tabs)
            {
                var selected = tab == _model.SelectedTab;
                button.EnableInClassList("gr-tabs__tab--selected", selected);
                GRUi.SetHidden(page, !selected);
            }
        }
    }
}
