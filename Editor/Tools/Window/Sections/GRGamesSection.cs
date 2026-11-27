using System;
using System.Linq;

using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// The account's games, one GRGameRow.uxml each (GRGamesSection.uxml).
    /// Clicking a row connects this project to that game. A game disabled by the
    /// studio's plan shows as disabled and can't be picked. Five rows show; the
    /// rest scroll, and the connected game is scrolled into view. With no studio
    /// or no games, it says so instead.
    /// </summary>
    internal sealed class GRGamesSection : IGRSection
    {
        private const int VisibleGames = 5;

        private static readonly GRMessage NoStudio = new GRMessage(
            "Your account isn't in a studio yet. Create one, or ask to be invited, in the GameRebellion dashboard.",
            GRMessageKind.Warn);

        private static readonly GRMessage NoGames = new GRMessage(
            "This studio has no games yet. Add one in the GameRebellion dashboard.",
            GRMessageKind.Warn);

        private readonly GRWindowModel _model;
        private readonly VisualTreeAsset _rowTemplate;
        private readonly ScrollView _list;
        private readonly IVisualElementScheduledItem _scrollToSelected;
        private readonly Label _loading;
        private readonly Label _hint;
        private readonly Label _empty;
        private readonly Label _message;
        private readonly Label _studio;
        private readonly VisualElement _studioHost;
        private readonly DropdownField _studioField;
        private GRGame _scrolledTo;

        public GRGamesSection(VisualElement root, GRWindowModel model, VisualTreeAsset rowTemplate)
        {
            _model = model;
            _rowTemplate = rowTemplate;
            _list = root.Q<ScrollView>("games-list");

            // Rows are rebuilt on every render and laid out a frame later: retry until they are.
            _scrollToSelected = _list.schedule.Execute(() =>
            {
                if (TryScrollToSelected())
                {
                    _scrollToSelected.Pause();
                }
            }).Every(50);
            _scrollToSelected.Pause();

            _loading = root.Q<Label>("games-loading");
            _hint = root.Q<Label>("games-hint");
            _empty = root.Q<Label>("games-empty");
            _message = root.Q<Label>("games-message");
            _studio = root.Q<Label>("games-studio");

            // A picker when the developer belongs to several studios; a label for one.
            _studioHost = root.Q("studio-field-host");
            _studioField = new DropdownField("Studio");
            _studioField.AddToClassList(GRUi.Field);
            _studioHost.Add(_studioField);
            _studioField.RegisterValueChangedCallback(_ =>
            {
                var index = _studioField.index;
                if (index >= 0 && index < model.Studios.Count)
                {
                    model.SelectStudio(model.Studios[index]);
                }
            });
        }

        public void Render()
        {
            var hasGames = _model.Games.Count > 0;
            GRUi.SetHidden(_loading, !_model.IsLoadingGames);
            GRUi.SetHidden(_hint, !hasGames);
            GRUi.SetHidden(_list, !hasGames);
            GRUi.ShowMessage(_empty,
                hasGames || _model.IsLoadingGames || _model.GamesMessage != null ? null
                : _model.Studios.Count == 0 ? NoStudio
                : NoGames);
            GRUi.ShowMessage(_message, _model.GamesMessage);

            var several = _model.Studios.Count > 1;
            GRUi.SetHidden(_studioHost, !several);
            _studioField.choices = _model.Studios.Select(studio => studio.Name).ToList();
            _studioField.SetValueWithoutNotify(_model.SelectedStudio?.Name ?? string.Empty);
            GRUi.SetHidden(_studio, several || _model.SelectedStudio == null);
            _studio.text = $"Studio: {_model.SelectedStudio?.Name}";

            _list.Clear();
            foreach (var game in _model.Games)
            {
                var item = _rowTemplate.Instantiate();
                item.AddToClassList("gr-games__item");
                if (_list.contentContainer.childCount < VisibleGames)
                {
                    // The list's height comes from these rows, so it's fitted once they're laid out.
                    item.RegisterCallback<GeometryChangedEvent>(_ => FitVisibleGames());
                }

                var row = item.Q("game-row");
                row.EnableInClassList("gr-game-row--selected", game == _model.SelectedGame);
                row.EnableInClassList("gr-game-row--disabled", game.IsDisabled);
                row.tooltip = game.IsDisabled ? "Disabled by your studio's plan, so it has no SDK key. Ask a studio owner to re-enable it."
                    : game == _model.SelectedGame ? "This project is connected to this game."
                    : "Connect this project to this game.";
                item.Q<Label>("game-name").text = game.Name;
                var details = new[] { game.Platform, game.Genre }.Where(detail => !string.IsNullOrEmpty(detail)).ToArray();
                item.Q<Label>("game-meta").text = details.Length > 0 ? string.Join(" · ", details) : $"id {game.Id}";
                row.RegisterCallback<ClickEvent>(_ => _model.SelectGame(game));
                _list.Add(item);
            }

            if (_model.SelectedGame != _scrolledTo)
            {
                _scrolledTo = _model.SelectedGame;
                if (_scrolledTo != null)
                {
                    _scrollToSelected.Resume();
                }
            }
        }

        /// <summary>
        /// Sizes the list to its first <see cref="VisibleGames"/> rows, measured once laid
        /// out. An explicit height, so it doesn't rely on how each Unity version sizes a
        /// ScrollView to its content. True when it changed.
        /// </summary>
        private bool FitVisibleGames()
        {
            var content = _list.contentContainer;
            var rows = Math.Min(content.childCount, VisibleGames);
            var height = rows > 0 ? content[rows - 1].layout.yMax : 0f;
            if (float.IsNaN(height))
            {
                return false;
            }

            var target = new StyleLength((float)Math.Ceiling(height));
            if (_list.style.height == target)
            {
                return false;
            }

            _list.style.height = target;
            return true;
        }

        /// <summary>True when done: scrolled, already in view, or not in this list.</summary>
        private bool TryScrollToSelected()
        {
            var games = _model.Games;
            var index = games.Count - 1;
            while (index >= 0 && games[index] != _scrolledTo)
            {
                index--;
            }

            var content = _list.contentContainer;
            if (index < 0 || index >= content.childCount)
            {
                return true;
            }

            var item = content[index];
            if (float.IsNaN(item.layout.height) || FitVisibleGames())
            {
                return false;
            }

            _list.ScrollTo(item);
            return true;
        }
    }
}
