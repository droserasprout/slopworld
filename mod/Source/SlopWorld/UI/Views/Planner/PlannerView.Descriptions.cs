namespace SlopWorld
{
    public sealed partial class PlannerView
    {
        PlannerDocumentState.DescriptionDraft SelectedDescription =>
            _state.Description(_selected?.Id);

        void EnsureDescription()
        {
            if (IsOpenEndpoint)
                _state.EnsureDescription(_selected?.Id);
        }

        void ReloadDescription()
        {
            var draft = SelectedDescription;
            if (IsOpenEndpoint && Ready && draft != null)
                _state.LoadDescription(_selected.Id, draft);
        }
    }
}
