using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class FuzzyTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("matches case-insensitive subsequences",
                          MatchesCaseInsensitiveSubsequences);
            yield return ("matches terms in any order and deduplicates hits",
                          MatchesTermsInAnyOrderAndDeduplicatesHits);
            yield return ("handles empty and missing queries", HandlesEmptyAndMissingQueries);
            yield return ("highlights matched runs", HighlightsMatchedRuns);
            yield return ("prefers word heads to mid-word matches", PrefersWordHeadsToMidWordMatches);
        }

        static void MatchesCaseInsensitiveSubsequences()
        {
            bool matched = Fuzzy.Match("SlopWorld", "sw", out int score, out var hits);

            AssertEx.True(matched, "subsequence should match");
            AssertEx.Sequence(new[] { 0, 4 }, hits, "subsequence hit positions");
            AssertEx.Equal(61, score, "subsequence score");
        }

        static void MatchesTermsInAnyOrderAndDeduplicatesHits()
        {
            bool first = Fuzzy.Match("SlopWorld", "world slop", out int firstScore,
                                     out var firstHits);
            bool second = Fuzzy.Match("SlopWorld", "slop world", out int secondScore,
                                      out var secondHits);

            AssertEx.True(first && second, "all query terms should match");
            AssertEx.Equal(firstScore, secondScore, "term order score");
            AssertEx.Sequence(firstHits, secondHits, "term order hits");
            AssertEx.Sequence(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, firstHits,
                              "deduplicated sorted hits");
        }

        static void HandlesEmptyAndMissingQueries()
        {
            bool empty = Fuzzy.Match("Slop", " \t ", out int emptyScore, out var emptyHits);
            bool missing = Fuzzy.Match("Slop", "sx", out _, out var missingHits);
            bool emptyText = Fuzzy.Match("", "", out _, out _);

            AssertEx.True(empty, "empty query should match nonempty text");
            AssertEx.Equal(0, emptyScore, "empty query score");
            AssertEx.Equal(0, emptyHits?.Count ?? 0, "empty query hits");
            AssertEx.False(missing, "missing term should fail");
            AssertEx.True(missingHits == null, "failed match hits");
            AssertEx.False(emptyText, "empty text should not match");
        }

        static void HighlightsMatchedRuns()
        {
            Fuzzy.Match("SlopWorld", "sw", out _, out List<int> hits);
            string highlighted = Fuzzy.Highlight("SlopWorld", hits);

            AssertEx.Equal("<color=#7FC8FF>S</color>lop<color=#7FC8FF>W</color>orld",
                           highlighted, "separated highlight runs");
            AssertEx.Equal("plain", Fuzzy.Highlight("plain", null), "null highlights");
            AssertEx.Equal("plain", Fuzzy.Highlight("plain", new List<int>()),
                           "empty highlights");
        }

        static void PrefersWordHeadsToMidWordMatches()
        {
            Fuzzy.Match("SlopWorld", "sw", out int wordHeadScore);
            Fuzzy.Match("somewhere", "sw", out int midWordScore);

            AssertEx.True(wordHeadScore > midWordScore,
                          "word-head match should outrank mid-word match");
        }
    }
}
