using System.Linq;

using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The connected game's SDK settings for this project: debug logs and API key,
    /// with a picker when the game has several active keys
    /// (GRGameSettingsSection.uxml). Hidden until a game is connected. There's no
    /// environment picker: Apply sets the one the key is for, Production.
    /// </summary>
    internal sealed class GRGameSettingsSection : IGRSection
    {
        private readonly GRWindowModel _model;
        private readonly Label _hint;
        private readonly VisualElement _body;
        private readonly Toggle _debugToggle;
        private readonly VisualElement _tokenHost;
        private readonly DropdownField _tokenField;
        private readonly Label _keyValue;
        private readonly Label _keyNote;
        private readonly Label _keyNotice;
        private readonly Button _revealButton;
        private readonly Button _copyButton;
        private readonly Button _applyButton;
        private readonly Label _message;

        public GRGameSettingsSection(VisualElement root, GRWindowModel model)
        {
            _model = model;
            _hint = root.Q<Label>("settings-hint");
            _body = root.Q("settings-body");
            _keyValue = root.Q<Label>("api-key-value");
            _keyNote = root.Q<Label>("key-environment-note");
            _keyNotice = root.Q<Label>("key-notice");
            _message = root.Q<Label>("settings-message");

            _tokenHost = root.Q("token-field-host");
            _tokenField = new DropdownField("SDK key");
            _tokenField.AddToClassList(GRUi.Field);
            _tokenHost.Add(_tokenField);
            _tokenField.RegisterValueChangedCallback(_ =>
            {
                var tokens = model.SdkTokens;
                var index = _tokenField.index;
                if (tokens != null && index >= 0 && index < tokens.Count)
                {
                    model.SelectToken(tokens[index]);
                }
            });

            _debugToggle = root.Q<Toggle>("debug-toggle");
            _debugToggle.RegisterValueChangedCallback(evt => model.SetDebugLogs(evt.newValue));

            _revealButton = root.Q<Button>("reveal-key-button");
            _revealButton.clicked += model.ToggleKeyReveal;
            _copyButton = root.Q<Button>("copy-key-button");
            _copyButton.clicked += model.CopyKey;
            _applyButton = root.Q<Button>("apply-button");
            _applyButton.clicked += model.Apply;
        }

        public void Render()
        {
            var hasGame = _model.SelectedGame != null;
            GRUi.SetHidden(_hint, hasGame);
            GRUi.SetHidden(_body, !hasGame);

            _debugToggle.SetValueWithoutNotify(_model.DebugLogs);

            var tokens = _model.SdkTokens;
            var several = tokens != null && tokens.Count > 1;
            GRUi.SetHidden(_tokenHost, !several);
            if (several)
            {
                // The key's last characters keep two keys with the same label apart.
                _tokenField.choices = tokens.Select(Choice).ToList();
                _tokenField.SetValueWithoutNotify(_model.SelectedToken == null ? string.Empty : Choice(_model.SelectedToken));
            }

            var hasKey = _model.ApiKey != null;
            _keyValue.text = _model.ApiKeyDisplay;
            _revealButton.text = _model.IsKeyRevealed ? "Hide" : "Show";
            _revealButton.SetEnabled(hasKey);
            _copyButton.SetEnabled(hasKey);
            var note = _model.KeyEnvironmentNote;
            GRUi.SetHidden(_keyNote, note == null);
            _keyNote.text = note ?? string.Empty;
            GRUi.ShowMessage(_keyNotice, _model.KeyNotice);
            _applyButton.SetEnabled(hasKey && !_model.IsLoadingGames);
            GRUi.ShowMessage(_message, _model.SettingsMessage);
        }

        private static string Choice(GRSdkToken token) =>
            token.Value.Length <= 4 ? token.Label : $"{token.Label} (••{token.Value.Substring(token.Value.Length - 4)})";
    }
}
