namespace SlopWorld
{
    // Search result row-model construction.
    public static partial class SearchView
    {
        static void EnsureLayout()
        {
            if (!_layoutDirty) return;

            Layout.Clear();
            bool hasRows = false;
            foreach (var g in Groups)
                if (g.Matches.Count > 0 || g.Error != null || g.Truncated)
                {
                    hasRows = true;
                    break;
                }

            if (Groups.Count == 0 || (!_loading && !hasRows))
                Layout.Add(new LayoutRow
                {
                    Kind = RowKind.Note,
                    Text = _loading ? "Searching…" :
                        string.IsNullOrWhiteSpace(_query) ? "Type a query and press Enter."
                        : "No results.",
                    Color = SlopWidgets.Faint,
                });

            foreach (var group in Groups)
            {
                if (group.Matches.Count == 0 && group.Error == null && !_loading) continue;
                Layout.Add(new LayoutRow { Kind = RowKind.Heading, Group = group });
                if (group.Error != null)
                    Layout.Add(new LayoutRow
                    {
                        Kind = RowKind.Note,
                        Text = group.Error,
                        Color = SlopWidgets.Bad,
                    });
                else
                {
                    string file = null;
                    foreach (var match in group.Matches)
                    {
                        if (match.Path != file)
                        {
                            file = match.Path;
                            Layout.Add(new LayoutRow
                            {
                                Kind = RowKind.File,
                                Text = file,
                            });
                        }
                        Layout.Add(new LayoutRow { Kind = RowKind.Match, Match = match });
                    }
                }
                if (group.Truncated)
                    Layout.Add(new LayoutRow
                    {
                        Kind = RowKind.Note,
                        Text = "… more matches",
                        Color = SlopWidgets.Faint,
                    });
            }

            // A loading row is only needed after project groups have been created; with no
            // projects the empty note above already says what is happening.
            if (_loading && Groups.Count > 0)
                Layout.Add(new LayoutRow
                {
                    Kind = RowKind.Note,
                    Text = "Searching…",
                    Color = SlopWidgets.Faint,
                });

            _contentHeight = Pad * 2f + Layout.Count * RowH;
            _layoutDirty = false;
        }

    }
}
