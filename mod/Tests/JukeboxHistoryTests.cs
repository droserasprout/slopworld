using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class JukeboxHistoryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads a recognized like with original metadata", RecognizedLike);
            yield return ("leaves missing original fields empty", MissingOriginalFieldsStayEmpty);
            yield return ("drops a malformed table without losing the rest", MalformedTable);
            yield return ("returns nothing for an empty file", EmptyFile);
        }

        static void RecognizedLike()
        {
            string text = string.Join("\n", new[]
            {
                "[[like]]",
                "at = \"2024-08-16T12:00:00Z\"",
                "source = \"SlopWorld OST\"",
                "artist = \"Real Artist\"",
                "title = \"Real Song\"",
                "original_artist = \"Terry Fail\"",
                "original_title = \"OST\"",
                "",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(1, entries.Count, "the table is read");
            var e = entries[0];
            AssertEx.Equal("Real Artist - Real Song", e.Line, "the effective line pairs the names");
            AssertEx.Equal("Terry Fail - OST", e.Original,
                "both original fields join into the provenance");
        }

        static void MissingOriginalFieldsStayEmpty()
        {
            string text = string.Join("\n", new[]
            {
                "[[like]]",
                "at = \"2023-01-01T00:00:00Z\"",
                "source = \"Example Radio\"",
                "artist = \"A\"",
                "title = \"B\"",
                "",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(1, entries.Count, "the table is read");
            var e = entries[0];
            AssertEx.Equal("", e.OriginalArtist, "a missing original_artist is empty");
            AssertEx.Equal("", e.OriginalTitle, "a missing original_title stays empty");
            AssertEx.Equal("", e.Original, "the provenance stays empty");
        }

        static void MalformedTable()
        {
            string text = string.Join("\n", new[]
            {
                "[[like]]",
                "at = \"2020-01-01T00:00:00Z\"",
                "source = \"Good\"",
                "title = \"Kept\"",
                "",
                "[[like]]",
                "this line has no key/value separator",
                "",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(1, entries.Count, "the malformed table is dropped, the good one kept");
            AssertEx.Equal("Kept", entries[0].Title, "the surviving entry is intact");
        }

        static void EmptyFile()
        {
            AssertEx.Equal(0, JukeboxHistory.Parse("").Count, "an empty file has no entries");
            AssertEx.Equal(0, JukeboxHistory.Parse("   \n\n  ").Count,
                "whitespace has no entries");
            AssertEx.Equal(0, JukeboxHistory.Parse(null).Count, "null is tolerated");
        }
    }
}
