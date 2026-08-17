using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class JukeboxHistoryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("reads legacy plain and tabbed lines newest first", LegacyLines);
            yield return ("reads a file mixing legacy lines and TOML tables", MixedFile);
            yield return ("reads a recognized like with original metadata", RecognizedLike);
            yield return ("falls back to title when original fields are missing", MissingOriginal);
            yield return ("drops a malformed table without losing the rest", MalformedTable);
            yield return ("returns nothing for an empty file", EmptyFile);
        }

        static void LegacyLines()
        {
            string text = string.Join("\n", new[]
            {
                "old song one",
                "2021-01-02T03:04:05Z\tArtist - Old Song",
                "-",
                "# a comment line",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(2, entries.Count, "the separator and comment are skipped");

            // Newest first: the tabbed line, written last, comes out on top.
            AssertEx.Equal("2021-01-02T03:04:05Z", entries[0].At, "the timestamp is split off");
            AssertEx.Equal("Artist - Old Song", entries[0].Title, "the title keeps the rest");
            AssertEx.Equal("Artist - Old Song", entries[0].OriginalTitle,
                "a legacy line is its own original");
            AssertEx.Equal("", entries[1].At, "a plain line has no timestamp");
            AssertEx.Equal("old song one", entries[1].Title, "the plain title survives");
        }

        static void MixedFile()
        {
            string text = string.Join("\n", new[]
            {
                "legacy title line",
                "[[like]]",
                "at = \"2022-05-05T10:00:00Z\"",
                "source = \"WeFunk\"",
                "artist = \"DJ\"",
                "title = \"Tune\"",
                "original_artist = \"\"",
                "original_title = \"WeFunk Radio - DJ Tune\"",
                "",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(2, entries.Count, "both shapes are read");

            var table = entries[0];
            AssertEx.Equal("WeFunk", table.Source, "the table's source is read");
            AssertEx.Equal("DJ", table.Artist, "the table's artist is read");
            AssertEx.Equal("WeFunk Radio - DJ Tune", table.Original,
                "an empty original_artist leaves the raw title alone");
            AssertEx.Equal("legacy title line", entries[1].Title, "the legacy line is kept");
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

        static void MissingOriginal()
        {
            string text = string.Join("\n", new[]
            {
                "[[like]]",
                "at = \"2023-01-01T00:00:00Z\"",
                "source = \"Radio Paradise\"",
                "artist = \"A\"",
                "title = \"B\"",
                "",
            });

            var entries = JukeboxHistory.Parse(text);
            AssertEx.Equal(1, entries.Count, "the table is read");
            var e = entries[0];
            AssertEx.Equal("", e.OriginalArtist, "a missing original_artist is empty");
            AssertEx.Equal("B", e.OriginalTitle, "a missing original_title falls back to the title");
            AssertEx.Equal("B", e.Original, "the provenance is the title alone");
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
