using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SearchResultTreeTests
    {
        static SearchStore.Group Group(string project, string checkout = "main")
        {
            var group = new SearchStore.Group
            {
                Project = "Shared display name",
                ProjectKey = BrowseScope.Identity(project, ""),
                ScopeKey = BrowseScope.Identity(project, checkout),
                Worktree = checkout,
            };
            group.Matches.Add(
                new SearchStore.Match
                {
                    Project = group.ScopeKey,
                    Path = "src/a.cs",
                    Line = 1,
                }
            );
            group.Matches.Add(
                new SearchStore.Match
                {
                    Project = group.ScopeKey,
                    Path = "src/a.cs",
                    Line = 2,
                }
            );
            return group;
        }

        public static void LoneMainMergesWithProjectAndIgnoresHiddenCheckoutFold()
        {
            var tree = new SearchResultTree();
            var main = Group("p");
            var branch = Group("p", "branch");
            tree.Build(new[] { main, branch }, false, "needle");
            tree.Toggle(main.ScopeKey);

            var rows = tree.Build(new[] { main }, false, "needle");
            var heading = rows.Single(r => r.Kind == SearchResultTree.RowKind.Heading);
            Assert.That(heading.FoldKey, Is.EqualTo(main.ProjectKey));
            Assert.That(heading.Text, Is.EqualTo("Shared display name"));
            Assert.That(heading.MatchCount, Is.EqualTo(2));
            var file = rows.Single(r => r.Kind == SearchResultTree.RowKind.File);
            Assert.That(file.Text, Is.EqualTo("src/a.cs"));
            Assert.That(file.MatchCount, Is.EqualTo(2));
            Assert.That(heading.MainCheckout, Is.True);
            Assert.That(heading.Depth, Is.EqualTo(0));
            Assert.That(
                rows.Single(r => r.Kind == SearchResultTree.RowKind.File).Depth,
                Is.EqualTo(1)
            );
            Assert.That(rows.Count(r => r.Match != null), Is.EqualTo(2));
            Assert.That(rows.Where(r => r.Match != null).All(r => r.Depth == 2), Is.True);

            tree.Toggle(heading.FoldKey);
            Assert.That(tree.Build(new[] { main }, false, "needle").Count, Is.EqualTo(1));
            tree.Reveal(new[] { main }, main.Matches[0]);
            Assert.That(
                tree.Build(new[] { main }, false, "needle").Count(r => r.Match != null),
                Is.EqualTo(2)
            );
        }

        public static void SelectedCheckoutsWithoutMatchesStillPreventMerging()
        {
            var tree = new SearchResultTree();
            var main = Group("p");
            var branch = Group("p", "branch");
            branch.Matches.Clear();
            foreach (bool loading in new[] { true, false })
            {
                var rows = tree.Build(new[] { main, branch }, loading, "needle");
                var project = rows.Single(r => r.FoldKey == main.ProjectKey);
                Assert.That(project.MainCheckout, Is.False);
                Assert.That(project.MatchCount, Is.Null);
                var checkout = rows.Single(r => r.FoldKey == main.ScopeKey);
                Assert.That(checkout.Text, Is.Empty);
                Assert.That(checkout.MatchCount, Is.EqualTo(2));
                Assert.That(checkout.MainCheckout, Is.True);
                Assert.That(checkout.Depth, Is.EqualTo(1));
                Assert.That(
                    rows.Single(r => r.Kind == SearchResultTree.RowKind.File).Depth,
                    Is.EqualTo(2)
                );
                Assert.That(rows.Where(r => r.Match != null).All(r => r.Depth == 3), Is.True);
            }
        }

        public static void LoneNonMainRetainsSeparateCheckoutHeading()
        {
            var tree = new SearchResultTree();
            var branch = Group("p", "branch");
            var rows = tree.Build(new[] { branch }, false, "needle");
            Assert.That(rows.Count(r => r.Kind == SearchResultTree.RowKind.Heading), Is.EqualTo(2));
            var checkout = rows.Single(r => r.FoldKey == branch.ScopeKey);
            Assert.That(checkout.MainCheckout, Is.False);
            Assert.That(checkout.Text, Is.EqualTo("branch"));
            Assert.That(checkout.MatchCount, Is.EqualTo(2));
            tree.Toggle(branch.ScopeKey);
            Assert.That(tree.Build(new[] { branch }, false, "needle").Count, Is.EqualTo(2));
        }

        public static void ProjectCheckoutAndFileFoldsKeepIdenticalPathsIndependent()
        {
            var tree = new SearchResultTree();
            var main = Group("p");
            var branch = Group("p", "branch");
            var other = Group("other");
            var groups = new[] { main, branch, other };
            var rows = tree.Build(groups, false, "needle");
            Assert.That(
                rows.Count(r => r.Kind == SearchResultTree.RowKind.Heading && r.Depth == 0),
                Is.EqualTo(2)
            );
            var file = rows.First(r => r.Kind == SearchResultTree.RowKind.File);
            Assert.That(file.MenuMatch, Is.SameAs(main.Matches[0]));
            tree.Toggle(file.FoldKey);
            rows = tree.Build(groups, false, "needle");
            Assert.That(
                rows.Single(r => r.FoldKey == file.FoldKey).MenuMatch,
                Is.SameAs(main.Matches[0]),
                "folded files retain their captured match for context actions"
            );
            Assert.That(
                rows.Where(r => r.Match != null).Select(r => r.Match.Project),
                Does.Not.Contain(main.ScopeKey)
            );
            Assert.That(rows.Count(r => r.Match != null), Is.EqualTo(4));

            tree.Toggle(branch.ScopeKey);
            rows = tree.Build(groups, false, "needle");
            Assert.That(rows.Count(r => r.Kind == SearchResultTree.RowKind.File), Is.EqualTo(2));
            tree.Toggle(main.ProjectKey);
            rows = tree.Build(groups, false, "needle");
            Assert.That(rows.Count(r => r.Kind == SearchResultTree.RowKind.Heading), Is.EqualTo(2));
            Assert.That(rows.Count(r => r.Match != null), Is.EqualTo(2));
        }

        public static void ReplacementSnapshotsRetainFoldsAndSettledResultsPruneMissingFiles()
        {
            var tree = new SearchResultTree();
            var group = Group("p");
            var file = tree.Build(new[] { group }, false, "first")
                .Single(r => r.Kind == SearchResultTree.RowKind.File);
            tree.Toggle(file.FoldKey);
            tree.Build(Array.Empty<SearchStore.Group>(), true, "replacement");
            tree.Build(Array.Empty<SearchStore.Group>(), false, "");
            var replacement = Group("p");
            var rows = tree.Build(new[] { replacement }, false, "replacement");
            Assert.That(
                rows.Any(r => r.Match != null),
                Is.False,
                "new match objects retain semantic file folds"
            );
            replacement.Matches.Clear();
            tree.Build(new[] { replacement }, false, "replacement");
            Assert.That(
                tree.IsCollapsed(file.FoldKey),
                Is.False,
                "settled results prune obsolete paths"
            );
            rows = tree.Build(new[] { Group("p") }, false, "later");
            Assert.That(rows.Count(r => r.Match != null), Is.EqualTo(2));
        }

        public static void FoldAllRevealAndUnfoldAllPreserveMatches()
        {
            var tree = new SearchResultTree();
            var group = Group("p");
            var groups = new[] { group };
            tree.SetAllFolded(groups, true);
            Assert.That(tree.AllFolded(groups), Is.True);
            Assert.That(tree.Build(groups, false, "needle").Count, Is.EqualTo(1));
            tree.Reveal(groups, group.Matches[1]);
            Assert.That(tree.AllFolded(groups), Is.False);
            Assert.That(
                tree.Build(groups, false, "needle").Count(r => r.Match != null),
                Is.EqualTo(2)
            );
            tree.SetAllFolded(groups, true);
            tree.SetAllFolded(groups, false);
            tree.SetAllFolded(groups, false);
            Assert.That(
                tree.Build(groups, false, "needle").Count(r => r.Match != null),
                Is.EqualTo(2)
            );
            Assert.That(
                group.Matches.Count,
                Is.EqualTo(2),
                "folding does not modify submitted results"
            );
        }

        public static void ErrorTruncationAndLoadingNotesRemainInTheirGroups()
        {
            var tree = new SearchResultTree();
            var failed = Group("failed");
            failed.Matches.Clear();
            failed.Error = "unavailable";
            var capped = Group("capped");
            capped.Truncated = true;
            var groups = new[] { failed, capped };
            var rows = tree.Build(groups, true, "needle");
            Assert.That(
                rows.Any(r => r.Kind == SearchResultTree.RowKind.Error && r.Text == "unavailable"),
                Is.True
            );
            Assert.That(rows.Any(r => r.Text == "More matches are not listed."), Is.True);
            Assert.That(rows.Last().Text, Is.EqualTo("Searching"));
            tree.Toggle(failed.ProjectKey);
            tree.Toggle(capped.ProjectKey);
            rows = tree.Build(groups, false, "needle");
            Assert.That(
                rows.Any(r =>
                    r.Kind == SearchResultTree.RowKind.Error
                    || r.Kind == SearchResultTree.RowKind.Note
                ),
                Is.False
            );
            Assert.That(
                tree.Build(Array.Empty<SearchStore.Group>(), false, "needle").Single().Text,
                Is.EqualTo("No results.")
            );
        }
    }
}
