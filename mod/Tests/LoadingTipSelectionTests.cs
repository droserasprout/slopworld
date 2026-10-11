using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LoadingTipSelectionTests
    {
        [Test]
        public void SelectionFillsRemainingCellsIncludingQuoteSeparator()
        {
            var selection = new LoadingTipSelection();
            var pool = new List<string> { "Too short.", "Ninechars continues here.", "Unbreakablylong first word." };
            Assert.That(selection.Pick(pool, 40, 30, new Random(1)), Is.EqualTo(pool[1]));
        }

        [Test]
        public void PrefixRankingStopsAtWordAndParagraphBoundaries()
        {
            Assert.That(LoadingTipSelection.PrefixGap("One two three", 9), Is.EqualTo(2));
            Assert.That(LoadingTipSelection.PrefixGap("One\ntwo three", 9), Is.EqualTo(6));
            Assert.That(LoadingTipSelection.PrefixGap("  One   two", 7), Is.Zero);
            Assert.That(LoadingTipSelection.PrefixGap("Longword", 3), Is.EqualTo(3));
        }

        [Test]
        public void FullAndOverfullRowsRankAgainstANewLine()
        {
            var pool = new List<string> { "Short.", "Exactlyten carries on." };
            foreach (int occupied in new[] { 0, 10, 15 })
                Assert.That(new LoadingTipSelection().Pick(pool, 10, occupied, new Random(1)), Is.EqualTo(pool[1]));
        }

        [Test]
        public void EqualFitsCanBothBePicked()
        {
            var pool = new List<string> { "One", "Two" };
            var seen = new HashSet<string>();
            var random = new Random(1);
            for (int i = 0; i < 40; i++)
                seen.Add(new LoadingTipSelection().Pick(pool, 3, 0, random));
            Assert.That(seen, Is.EquivalentTo(pool));
        }

        [Test]
        public void RecentQuotesAreSkippedAndSmallPoolsRecover()
        {
            var selection = new LoadingTipSelection();
            var pool = new List<string> { "Ninechars", "Short" };
            var random = new Random(1);
            Assert.That(selection.Pick(pool, 40, 30, random), Is.EqualTo(pool[0]));
            Assert.That(selection.Pick(pool, 40, 30, random), Is.EqualTo(pool[1]));
            Assert.That(selection.Pick(pool, 40, 30, random), Is.EqualTo(pool[0]));
            selection.Clear();
            Assert.That(selection.Pick(pool, 40, 30, random), Is.EqualTo(pool[0]));
            Assert.That(selection.Pick(new List<string>(), 40, 30, random), Is.EqualTo("Loading."));
        }

        [Test]
        public void HistoryKeepsTheLastTwelveQuotes()
        {
            var selection = new LoadingTipSelection();
            var pool = new List<string>();
            for (int i = 0; i < 13; i++) pool.Add(i.ToString("D2", CultureInfo.InvariantCulture));
            var random = new Random(1);
            var seen = new HashSet<string>();
            string first = selection.Pick(pool, 2, 0, random);
            seen.Add(first);
            for (int i = 1; i < 13; i++)
                Assert.That(seen.Add(selection.Pick(pool, 2, 0, random)), Is.True);
            Assert.That(selection.Pick(pool, 2, 0, random), Is.EqualTo(first));
        }
    }
}
