using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Owns Search's visible result hierarchy and semantic fold state. SearchStore owns
    // captured result identities; SearchView owns geometry, input and reader lifetime.
    internal sealed class SearchResultTree
    {
        readonly ContentTreeState Folds = new ContentTreeState();

        internal enum RowKind
        {
            Heading,
            File,
            Match,
            Note,
            Error,
        }

        internal sealed class Row
        {
            internal RowKind Kind;
            internal string Text;
            internal string FoldKey;
            internal int Depth;
            internal SearchStore.Match Match;
            internal SearchStore.Match MenuMatch;
        }

        internal bool IsCollapsed(string key) => Folds.IsCollapsed(key);

        internal void Toggle(string key) => Folds.ToggleCollapsed(key);

        internal bool AllFolded(IEnumerable<SearchStore.Group> groups) =>
            Folds.AllFolded(groups.Where(HasResults).Select(g => g.ProjectKey).Distinct());

        internal void SetAllFolded(IEnumerable<SearchStore.Group> groups, bool folded) =>
            Folds.SetAllFolded(Keys(groups), folded);

        static string FileKey(SearchStore.Group group, string path) => group.ScopeKey + "\n" + path;

        static IEnumerable<string> Keys(IEnumerable<SearchStore.Group> groups)
        {
            foreach (var group in groups)
            {
                yield return group.ProjectKey;
                yield return group.ScopeKey;
                foreach (var path in group.Matches.Select(m => m.Path).Distinct())
                    yield return FileKey(group, path);
            }
        }

        static bool HasResults(SearchStore.Group group) =>
            group.Matches.Count > 0 || group.Error != null || group.Truncated;

        internal void Reveal(IEnumerable<SearchStore.Group> groups, SearchStore.Match match)
        {
            foreach (var group in groups)
                if (group.Matches.Contains(match))
                {
                    Folds.SetCollapsed(group.ProjectKey, false);
                    Folds.SetCollapsed(group.ScopeKey, false);
                    Folds.SetCollapsed(FileKey(group, match.Path), false);
                    return;
                }
        }

        internal List<Row> Build(
            IReadOnlyList<SearchStore.Group> groups,
            bool loading,
            string query
        )
        {
            // A replacement search publishes an empty snapshot before replies arrive.
            // Prune only a settled, non-empty query so those snapshots retain fold choices.
            if (!loading && !string.IsNullOrWhiteSpace(query))
                Folds.SyncGroups(Keys(groups));

            var rows = new List<Row>();
            if (groups.Count == 0 || (!loading && !groups.Any(HasResults)))
                rows.Add(
                    new Row
                    {
                        Kind = RowKind.Note,
                        Text =
                            loading ? "Searching"
                            : string.IsNullOrWhiteSpace(query) ? "Type a query and press Enter."
                            : "No results.",
                    }
                );

            foreach (
                var project in groups
                    .Where(g => loading || HasResults(g))
                    .GroupBy(g => g.ProjectKey)
            )
            {
                rows.Add(
                    new Row
                    {
                        Kind = RowKind.Heading,
                        Text = project.First().Project,
                        FoldKey = project.Key,
                    }
                );
                if (IsCollapsed(project.Key))
                    continue;
                foreach (var group in project)
                {
                    rows.Add(
                        new Row
                        {
                            Kind = RowKind.Heading,
                            Text = $"{group.Worktree}  {group.Matches.Count}",
                            FoldKey = group.ScopeKey,
                            Depth = 1,
                        }
                    );
                    if (IsCollapsed(group.ScopeKey))
                        continue;
                    if (group.Error != null)
                        rows.Add(new Row { Kind = RowKind.Error, Text = group.Error });
                    else
                        foreach (var file in group.Matches.GroupBy(m => m.Path))
                        {
                            var key = FileKey(group, file.Key);
                            rows.Add(
                                new Row
                                {
                                    Kind = RowKind.File,
                                    Text = $"{file.Key}  {file.Count()}",
                                    FoldKey = key,
                                    Depth = 2,
                                    MenuMatch = file.First(),
                                }
                            );
                            if (!IsCollapsed(key))
                                foreach (var match in file)
                                    rows.Add(new Row { Kind = RowKind.Match, Match = match });
                        }
                    if (group.Truncated)
                        rows.Add(
                            new Row { Kind = RowKind.Note, Text = "More matches are not listed." }
                        );
                }
            }
            if (loading && groups.Count > 0)
                rows.Add(new Row { Kind = RowKind.Note, Text = "Searching" });
            return rows;
        }
    }
}
