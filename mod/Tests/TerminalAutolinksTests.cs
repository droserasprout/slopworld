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

        public static void LinksAfterColumnOverwrite()
        {
            var cache = new TerminalRunCache();
            var rows = cache.Parse(new[] { "https:xxxxx\x1b[7G//host" }, 20, 1, 1,
                out _, out _);
            AssertEx.True(rows[0].Exists(run => run.Url == "https://host"),
                "candidate filtering preserves links assembled by CHA overwrite");
        }

        static void AutolinksAcrossColorsAndRows()
        {
            AssertEx.False(TerminalAutolinks.MayContainLink(new[] { "plain output", "with ANSI \x1b[31mred" }),
                           "plain screens bypass URL-grid construction");
            AssertEx.True(TerminalAutolinks.MayContainLink(new[] { "https:", "//example.com" }),
                          "candidate scan carries across physical rows");
            var colored = Sgr.ParseLine(
                "\x1b[31mhttps://example\x1b[32m.com");
            AssertEx.Equal(2, colored.Count, "URL keeps the two source colors");
            AssertEx.Equal("https://example.com", colored[0].Url, "first URL fragment");
            AssertEx.Equal("https://example.com", colored[1].Url, "second URL fragment");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], colored[0].Fg,
                           "first URL color");
            AssertEx.Equal(TerminalTheme.Current.Ansi[2], colored[1].Fg,
                           "second URL color");

            var rows = Sgr.ParseLines(new[] { "https://example.", "com" }, 16);
            AssertEx.Equal(1, rows[0].Count, "first row stays one link run");
            AssertEx.Equal(1, rows[1].Count, "second row stays one link run");
            AssertEx.Equal("https://example.com", rows[0][0].Url,
                           "cross-row link URL");
            AssertEx.Equal("com", rows[1][0].Text, "cross-row link text");
            AssertEx.Equal("https://example.com", rows[1][0].Url,
                           "cross-row link metadata");

            AssertEx.Equal(0, Sgr.ParseLines(Array.Empty<string>(), 80).Length,
                           "empty screen has no rows");
            AssertEx.Equal(1, Sgr.ParseLines(new[] { "plain" }, 0)[0].Count,
                           "zero width falls back to row width");

            var cache = new TerminalRunCache();
            var first = cache.Parse(new[] { "plain", "\x1b[31mred", "界" }, 16, 1, 1,
                                    out int hits, out int misses);
            AssertEx.Equal(0, hits, "new rows miss the parsed-row cache");
            AssertEx.Equal(3, misses, "every new row is parsed");
            var second = cache.Parse(new[] { "\x1b[31mred", "界" }, 16, 1, 1,
                                     out hits, out misses);
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
                "\x1b]8;;https://named.example\x07https://text.example\x1b]8;;\x07");

            AssertEx.Equal(1, runs.Count, "explicit hyperlink is not split by autolinking");
            AssertEx.Equal("https://text.example", runs[0].Text, "explicit link text");
            AssertEx.Equal("https://named.example", runs[0].Url,
                           "explicit hyperlink wins over text detection");
        }
    }
}
