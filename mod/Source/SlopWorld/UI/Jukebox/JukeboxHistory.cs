using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The likes-file reader, kept apart from the history view so the file format can be
    // exercised without Unity. The daemon appends current TOML [[like]] tables. A malformed
    // table is dropped rather than shown as broken.
    public static class JukeboxHistory
    {
        public sealed class Entry
        {
            public string At = "";
            public string Source = "";
            public string Artist = "";
            public string Title = "";
            public string OriginalArtist = "";
            public string OriginalTitle = "";

            // The station's own provenance as one line. Artist is usually empty because ICY
            // supplies a single title string, so fall back to the title alone.
            public string Original => string.IsNullOrEmpty(OriginalArtist)
                ? OriginalTitle : OriginalArtist + " - " + OriginalTitle;

            // "Artist - Title", or the title alone when nothing named the artist. The effective
            // pair, which is what a copy of a row is expected to yield.
            public string Line => string.IsNullOrEmpty(Artist)
                ? Title : Artist + " - " + Title;
        }

        // Newest first: the file appends, and a browsing surface wants the last like on top.
        public static List<Entry> Parse(string text)
        {
            var entries = new List<Entry>();
            var table = new List<string>();
            bool inTable = false;

            foreach (string line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            {
                if (line.Trim() == "[[like]]")
                {
                    FinishTable(entries, table);
                    table.Clear();
                    inTable = true;
                    continue;
                }
                if (inTable)
                {
                    table.Add(line);
                    continue;
                }
            }
            FinishTable(entries, table);
            entries.Reverse();
            return entries;
        }

        static void FinishTable(List<Entry> entries, List<string> lines)
        {
            if (lines.Count == 0) return;
            try
            {
                var values = Toml.ParseFlat(string.Join("\n", lines));
                entries.Add(new Entry
                {
                    At = Value(values, "at"),
                    Source = Value(values, "source"),
                    Artist = Value(values, "artist"),
                    Title = Value(values, "title"),
                    OriginalArtist = Value(values, "original_artist"),
                    OriginalTitle = Value(values, "original_title"),
                });
            }
            catch
            {
                // A malformed table is dropped so one bad entry cannot take the history down.
                // the file stays append-only and the rest still reads.
            }
        }

        static string Value(Dictionary<string, string> values, string key) =>
            values.TryGetValue(key, out var value) ? value : "";
    }
}
