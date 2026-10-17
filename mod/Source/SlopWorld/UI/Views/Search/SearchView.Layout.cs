namespace SlopWorld
{
    // Search result row-model construction.
    public static partial class SearchView
    {
        static void EnsureLayout()
        {
            int layoutRevision = WorkspaceLayout.Revision;
            if (!_layoutDirty && _layoutRevision == layoutRevision)
                return;

            Layout.Clear();
            Layout.AddRange(ResultTree.Build(Groups, _loading, Store.Submitted?.Query ?? _query));

            _contentHeight = Pad * 2f + Layout.Count * RowH;
            _layoutDirty = false;
            _layoutRevision = layoutRevision;
        }
    }
}
