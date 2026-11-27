using UnityEngine.UIElements;

namespace GameRebellionSdk.Unity.Editor
{
    /// <summary>The integration checklist (GRIntegrationSection.uxml), one GRChecklistItem.uxml per check.</summary>
    internal sealed class GRIntegrationSection : IGRSection
    {
        private readonly GRWindowModel _model;
        private readonly VisualTreeAsset _itemTemplate;
        private readonly VisualElement _checklist;

        public GRIntegrationSection(VisualElement root, GRWindowModel model, VisualTreeAsset itemTemplate)
        {
            _model = model;
            _itemTemplate = itemTemplate;
            _checklist = root.Q("checklist");
            root.Q<Button>("refresh-integration-button").clicked += model.RefreshIntegration;
        }

        public void Render()
        {
            _checklist.Clear();
            foreach (var check in _model.Checks)
            {
                var item = _itemTemplate.Instantiate();
                var row = item.Q("item");
                row.EnableInClassList("gr-checklist-item--done", check.State == GRCheckState.Done);
                row.EnableInClassList("gr-checklist-item--partial", check.State == GRCheckState.Partial);
                row.EnableInClassList("gr-checklist-item--todo", check.State == GRCheckState.Todo);
                item.Q<Label>("label").text = check.Label;
                item.Q<Label>("detail").text = check.Detail;
                _checklist.Add(item);
            }
        }
    }
}
