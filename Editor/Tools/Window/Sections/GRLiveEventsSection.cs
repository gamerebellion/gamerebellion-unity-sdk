using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>
    /// Events captured in play mode (GRLiveEventsSection.uxml): a virtualized
    /// ListView of GREventRow.uxml, so a long capture costs nothing to show.
    /// Clicking a row opens the script line that sent the event.
    /// </summary>
    internal sealed class GRLiveEventsSection : IGRSection
    {
        private const float RowHeight = 22f;

        private readonly GRWindowModel _model;
        private readonly Label _summary;
        private readonly Label _empty;
        private readonly Label _hint;
        private readonly ListView _list;
        private int _shownRevision = -1;

        public GRLiveEventsSection(VisualElement root, GRWindowModel model, VisualTreeAsset rowTemplate)
        {
            _model = model;
            _summary = root.Q<Label>("events-summary");
            _empty = root.Q<Label>("events-empty");
            _hint = root.Q<Label>("events-hint");

            _list = root.Q<ListView>("events-list");
            _list.fixedItemHeight = RowHeight;
            _list.selectionType = SelectionType.None;
            _list.makeItem = () =>
            {
                var item = rowTemplate.Instantiate();
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    if (item.userData is int index)
                    {
                        model.OpenEventSite(index);
                    }
                });
                return item;
            };
            _list.bindItem = BindRow;
            _list.itemsSource = model.Events;

            root.Q<Button>("clear-events-button").clicked += model.ClearEvents;
        }

        public void Render()
        {
            var hasEvents = _model.Events.Count > 0;
            GRUi.SetHidden(_empty, hasEvents);
            GRUi.SetHidden(_list, !hasEvents);
            GRUi.SetHidden(_hint, !hasEvents);
            _summary.text = _model.EventsSummary;

            // The whole window redraws on any change; the list only when the capture did.
            if (_model.EventsRevision != _shownRevision)
            {
                _shownRevision = _model.EventsRevision;
                _list.RefreshItems();
                if (hasEvents && _model.IsPlaying)
                {
                    _list.ScrollToItem(_model.Events.Count - 1);
                }
            }
        }

        private void BindRow(VisualElement element, int index)
        {
            var captured = _model.Events[index];
            element.userData = index;
            element.Q("row").EnableInClassList("gr-event-row--invalid", captured.IsInvalid);
            element.Q<Label>("time").text = captured.Time;
            element.Q<Label>("event-name").text = captured.Name;
            element.Q<Label>("payload").text = captured.IsInvalid ? $"{captured.Problem}  {captured.Payload}" : captured.Payload;
            element.Q<Label>("site").text = captured.Site;

            var details = captured.Payload.Length == 0 ? "(no fields set)" : captured.Payload.Replace("  ", "\n");
            element.tooltip = (captured.IsInvalid ? captured.Problem + "\n\n" : string.Empty)
                              + details
                              + (captured.Stack.Length == 0 ? string.Empty : "\n\nSent from:\n" + captured.Stack);
        }
    }
}
