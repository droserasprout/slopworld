using System;
using System.Linq;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class PalettePolicyTests
    {
        public static void EmptyFilterKeepsRecentsDistinctAndGroupsRemainingCatalogRows()
        {
            var entries = new[] {
                new PaletteSearch.Entry(0, "View first", true, "view.first", "View"),
                new PaletteSearch.Entry(1, "Settings", true, "settings", "Settings"),
                new PaletteSearch.Entry(2, "View second", true, "view.second", "View"),
                new PaletteSearch.Entry(3, "Unavailable", false, "disabled", "Hidden"),
                new PaletteSearch.Entry(4, "View recent", true, "view.recent", "View"),
            };
            var result = PaletteSearch.Commands(entries, "", new[] { "disabled", "missing", "view.recent", "view.recent" });
            Assert.That(result.Hits.Select(hit => hit.Index), Is.EqualTo(new[] { 4, 0, 2, 1 }));
            Assert.That(result.RecentCount, Is.EqualTo(1));
            Assert.That(result.Hits[0].Label, Is.EqualTo("View recent"));
        }

        public static void EqualScoresUseRecencyThenStableCatalogOrder()
        {
            var entries = new[] {
                new PaletteSearch.Entry(0, "Open", true, "old"),
                new PaletteSearch.Entry(1, "Open", true, "untracked-a"),
                new PaletteSearch.Entry(2, "Open", true, "recent"),
                new PaletteSearch.Entry(3, "Open", true, "untracked-b"),
            };
            var result = PaletteSearch.Commands(entries, "open", new[] { "recent", "old" });
            Assert.That(result.Hits.Select(hit => hit.Index), Is.EqualTo(new[] { 2, 0, 1, 3 }));
            Assert.That(result.RecentCount, Is.Zero, "filtered results do not acquire a Recently heading");
        }

        public static void RecencyCannotPromoteAWorseLabelMatch()
        {
            var entries = new[] {
                new PaletteSearch.Entry(0, "Open", true, "best"),
                new PaletteSearch.Entry(1, "Reopen", true, "recent"),
            };
            var result = PaletteSearch.Commands(entries, "open", new[] { "recent" });
            Assert.That(result.Hits.Select(hit => hit.Index), Is.EqualTo(new[] { 0, 1 }));
        }

        public static void HiddenIdMatchesArePenalizedWithoutHighlightingTheLabel()
        {
            var entries = new[] {
                new PaletteSearch.Entry(0, "Unrelated", true, "needle"),
                new PaletteSearch.Entry(1, "Needle", true, "label"),
                new PaletteSearch.Entry(2, "Needle", false, "disabled"),
                new PaletteSearch.Entry(3, "No match", true, "absent"),
            };
            var result = PaletteSearch.Commands(entries, "needle", new[] { "needle" });
            Assert.That(result.Hits.Select(hit => hit.Index), Is.EqualTo(new[] { 1, 0 }));
            Assert.That(result.Hits[0].Label, Does.Contain("<color="));
            Assert.That(result.Hits[1].Label, Is.EqualTo("Unrelated"));
            Fuzzy.Match("needle", "needle", out int score);
            Assert.That(result.Hits[1].Score, Is.EqualTo(score - 80));
        }

        public static void SubmenuSearchHidesDisabledPlaceholdersAndKeepsEqualScoreOrder()
        {
            var entries = new[] {
                new PaletteSearch.Entry(0, "Empty", false),
                new PaletteSearch.Entry(1, "Open", true),
                new PaletteSearch.Entry(2, "Open", true),
            };
            Assert.That(PaletteSearch.Options(entries, "").Select(hit => hit.Index), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(PaletteSearch.Options(entries, "OPEN").Select(hit => hit.Index), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(PaletteSearch.Options(entries, "empty"), Is.Empty);
        }

        public static void NewAvailabilitySnapshotReplacesResultsWithoutChangingRowIdentity()
        {
            var recent = Array.Empty<string>();
            var enabled = new[] { new PaletteSearch.Entry(7, "Open", true, "open") };
            var disabled = new[] { new PaletteSearch.Entry(7, "Open", false, "open") };
            Assert.That(PaletteSearch.Commands(enabled, "open", recent).Hits.Single().Index, Is.EqualTo(7));
            Assert.That(PaletteSearch.Commands(disabled, "open", recent).Hits, Is.Empty);
            Assert.That(PaletteSearch.Commands(enabled, "absent", recent).Hits, Is.Empty);
        }

        public static void HistoryLoadDropsDuplicatesAndObsoleteIdsBeforeApplyingCapacity()
        {
            var history = new PaletteHistory();
            var ids = Enumerable.Range(0, 10).Select(i => "command-" + i).ToArray();
            history.Load("obsolete\r\ncommand-0\ncommand-0\n" + string.Join("\r\n", ids.Skip(1)), ids);
            Assert.That(history.Entries, Is.EqualTo(ids.Take(8)));
            Assert.That(history.Text, Is.EqualTo(string.Join("\n", ids.Take(8))));
            history.Load(null, ids);
            Assert.That(history.Entries, Is.Empty, "reload replaces rather than appends");
        }

        public static void HistoryTrackingMovesExistingCommandsAndEvictsOnlyTheOldest()
        {
            var history = new PaletteHistory();
            var ids = Enumerable.Range(0, 8).Select(i => "command-" + i).ToArray();
            history.Load(string.Join("\n", ids), ids);
            Assert.That(history.Track(null), Is.False);
            Assert.That(history.Track(""), Is.False);
            Assert.That(history.Track("command-0"), Is.False, "repeated newest action needs no settings write");
            Assert.That(history.Track("command-3"), Is.True);
            Assert.That(history.Entries, Is.EqualTo(new[] { "command-3", "command-0", "command-1", "command-2", "command-4", "command-5", "command-6", "command-7" }));
            Assert.That(history.Track("new"), Is.True);
            Assert.That(history.Entries.Count, Is.EqualTo(8));
            Assert.That(history.Entries[0], Is.EqualTo("new"));
            Assert.That(history.Entries, Does.Not.Contain("command-7"));
        }
    }
}
