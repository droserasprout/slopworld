using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class NameToolsTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("preserves digits that are part of a name",
                          PreservesDigitsThatArePartOfAName);
            yield return ("removes a separated numeric copy suffix",
                          RemovesSeparatedNumericCopySuffix);
            yield return ("skips an occupied suggestion",
                          SkipsAnOccupiedSuggestion);
            yield return ("falls back when every suggestion is occupied", AllSuggestionsOccupied);
        }

        static void PreservesDigitsThatArePartOfAName()
        {
            string copy = NameTools.FreeName("word-letter123",
                new[] { "word-letter123" }, "agent");

            AssertEx.Equal("word-letter123-2", copy, "embedded digits stay in the stem");
        }

        static void RemovesSeparatedNumericCopySuffix()
        {
            string copy = NameTools.FreeName("word-letter-123",
                new[] { "word-letter-123" }, "agent");

            AssertEx.Equal("word-letter-2", copy, "generated suffix is replaced");
        }

        static void SkipsAnOccupiedSuggestion()
        {
            string copy = NameTools.FreeName("agent", new[] { "agent", "agent-2" }, "agent");

            AssertEx.Equal("agent-3", copy, "occupied suffix is skipped");
        }

        static void AllSuggestionsOccupied()
        {
            var taken = new List<string>();
            for (int n = 2; n <= 99; n++) taken.Add("agent-" + n);

            AssertEx.Equal("agent", NameTools.FreeName("agent", taken, "fallback"),
                           "original stem is the final fallback");
        }
    }
}
