using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalAutolinksTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("autolinks across colors and rows", AutolinksAcrossColorsAndRows);
            yield return ("preserves explicit hyperlink metadata", PreservesExplicitHyperlink);
        }

        public static void CandidateTransitionsPreserveBoundariesAndEscapes()
        {
            var samples = new[]
            {
                "",
                "plain",
                ":",
                "/",
                "//",
                "https:",
                "\x1b[31m/",
                "\x1b]8;;https://hidden\x07",
                "\x1b[7G",
                ":://",
            };
            foreach (string left in samples)
            foreach (string right in samples)
            {
                int state = TerminalAutolinks.CandidateTransitions(left) & 3;
                if (state != 3)
                    state = (TerminalAutolinks.CandidateTransitions(right) >> (state * 2)) & 3;
                if (TerminalAutolinks.MayContainLink(new[] { left, right }))
                    AssertEx.Equal(3, state, "cached transitions cannot reject a candidate");
            }
        }

        public static void CandidateCacheHandlesReplacementAndRemovedLinks()
        {
            var cache = new TerminalRunCache();
            var screen = new ScreenBuf
            {
                Lines = new[] { "plain", "text" },
                Cols = 6,
                Rows = 2,
            };
            cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            var snapshot = screen.Snapshot();
            void Replace(string[] lines)
            {
                screen.Lines = lines;
                screen.Runs = new List<SgrRun>[lines.Length];
                screen.LinksKnown = false;
                cache.Parse(screen, 1, 1, out _, out _);
                screen.RunsRev = 1;
            }
            Replace(new[] { "https:", "//host" });
            AssertEx.Equal(
                "https://host",
                screen.Runs[0][0].Url,
                "changed rows join across boundary"
            );
            Replace(new[] { "plain", "text" });
            AssertEx.True(screen.Runs[0][0].Url == null, "removed link does not survive reuse");
            Replace(new string[] { null, null });
            Replace(new[] { "https:", "//host" });
            AssertEx.Equal(
                "https://host",
                screen.Runs[0][0].Url,
                "null rows preserve incoming delimiter state"
            );
            cache.Parse(snapshot, 1, 1, out _, out _);
            AssertEx.True(snapshot.Runs[0][0].Url == null, "retained snapshot is unaffected");
            screen.Cols = 12;
            Replace(new[] { "https://host" });
            AssertEx.Equal(
                "https://host",
                screen.Runs[0][0].Url,
                "shape changes reset candidate scratch"
            );
        }

        public static void SparseCandidateCacheTracksWrappedDelimiterEdits()
        {
            var cache = new TerminalRunCache();
            var screen = new ScreenBuf
            {
                Lines = new[] { "https:", "plain", "", "" },
                Cols = 6,
                Rows = 4,
            };
            cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            foreach (string text in new[] { "//host", "plain", "//host" })
            {
                screen.Lines = (string[])screen.Lines.Clone();
                screen.Lines[1] = text;
                screen.Runs = (List<SgrRun>[])screen.Runs.Clone();
                screen.Runs[1] = null;
                screen.LinksKnown = false;
                cache.Parse(screen, 1, 1, out _, out _);
                AssertEx.Equal(
                    text == "plain" ? null : "https://host",
                    screen.Runs[0][0].Url,
                    "sparse edits update neighboring wrapped link metadata"
                );
            }
        }

        public static void RowCacheEvictsOldestWithoutDiscardingRecentRows()
        {
            var cache = new TerminalRunCache();
            for (int i = 0; i < 2049; i++)
                cache.Parse(new[] { "row " + i }, 80, 1, 1, out _, out _);
            AssertEx.Equal(2048, cache.Count, "cache stays bounded");
            cache.Parse(new[] { "row 2047" }, 80, 1, 1, out int hits, out _);
            AssertEx.Equal(1, hits, "recent rows survive capacity rollover");
            cache.Parse(new[] { "row 0" }, 80, 1, 1, out _, out int misses);
            AssertEx.Equal(1, misses, "oldest row is evicted");
        }

        public static void LinksAfterColumnOverwrite()
        {
            var cache = new TerminalRunCache();
            var rows = cache.Parse(new[] { "https:xxxxx\x1b[7G//host" }, 20, 1, 1, out _, out _);
            AssertEx.True(
                rows[0].Exists(run => run.Url == "https://host"),
                "candidate filtering preserves links assembled by CHA overwrite"
            );
        }

        static void AutolinksAcrossColorsAndRows()
        {
            AssertEx.False(
                TerminalAutolinks.MayContainLink(new[] { "plain output", "with ANSI \x1b[31mred" }),
                "plain screens bypass URL-grid construction"
            );
            AssertEx.True(
                TerminalAutolinks.MayContainLink(new[] { "https:", "//example.com" }),
                "candidate scan carries across physical rows"
            );
            var colored = Sgr.ParseLine("\x1b[31mhttps://example\x1b[32m.com");
            AssertEx.Equal(2, colored.Count, "URL keeps the two source colors");
            AssertEx.Equal("https://example.com", colored[0].Url, "first URL fragment");
            AssertEx.Equal("https://example.com", colored[1].Url, "second URL fragment");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], colored[0].Fg, "first URL color");
            AssertEx.Equal(TerminalTheme.Current.Ansi[2], colored[1].Fg, "second URL color");

            var rows = Sgr.ParseLines(new[] { "https://example.", "com" }, 16);
            AssertEx.Equal(1, rows[0].Count, "first row stays one link run");
            AssertEx.Equal(1, rows[1].Count, "second row stays one link run");
            AssertEx.Equal("https://example.com", rows[0][0].Url, "cross-row link URL");
            AssertEx.Equal("com", rows[1][0].Text, "cross-row link text");
            AssertEx.Equal("https://example.com", rows[1][0].Url, "cross-row link metadata");

            AssertEx.Equal(
                0,
                Sgr.ParseLines(Array.Empty<string>(), 80).Length,
                "empty screen has no rows"
            );
            var fallback = Sgr.ParseLines(new[] { "https://example.test/path" }, 0)[0];
            AssertEx.Equal(1, fallback.Count, "zero width falls back to linked row width");
            AssertEx.Equal(
                "https://example.test/path",
                fallback[0].Url,
                "fallback width retains the complete URL"
            );

            var cache = new TerminalRunCache();
            var first = cache.Parse(
                new[] { "plain", "\x1b[31mred", "界" },
                16,
                1,
                1,
                out int hits,
                out int misses
            );
            AssertEx.Equal(0, hits, "new rows miss the parsed-row cache");
            AssertEx.Equal(3, misses, "every new row is parsed");
            var second = cache.Parse(new[] { "\x1b[31mred", "界" }, 16, 1, 1, out hits, out misses);
            AssertEx.Equal(2, hits, "ANSI and wide rows are reused at a new anchor");
            AssertEx.True(object.ReferenceEquals(first[1], second[0]), "cached runs are shared");
            cache.Parse(new[] { "plain" }, 16, 2, 1, out hits, out misses);
            AssertEx.Equal(1, misses, "theme changes reparse cached rows");
            cache.Parse(new[] { "plain" }, 16, 2, 2, out hits, out misses);
            AssertEx.Equal(1, misses, "font changes reparse cached rows");
        }

        static void PreservesExplicitHyperlink()
        {
            var runs = Sgr.ParseLine(
                "\x1b]8;;https://named.example\x07https://text.example\x1b]8;;\x07"
            );

            AssertEx.Equal(1, runs.Count, "explicit hyperlink is not split by autolinking");
            AssertEx.Equal("https://text.example", runs[0].Text, "explicit link text");
            AssertEx.Equal(
                "https://named.example",
                runs[0].Url,
                "explicit hyperlink wins over text detection"
            );
        }
    }
}
