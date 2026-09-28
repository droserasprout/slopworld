using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalSpriteSequencesTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("sprite joining preserves daemon geometry and boundaries", SpriteJoining);
        }

        static void SpriteJoining()
        {
            var catalog = new TextSpriteCatalog(new[] { "ab", "abc" });
            var runs = new List<SgrRun>
            {
                new SgrRun { Text = "a", Col = 0, CellWidth = 2 },
                new SgrRun { Text = "b", Col = 2, CellWidth = 1 },
                new SgrRun { Text = "c", Col = 3, CellWidth = 2 },
                new SgrRun { Text = "x", Col = 5, CellWidth = 1 },
            };
            TerminalSpriteSequences.Join(runs, catalog);
            AssertEx.Equal(2, runs.Count, "longest complete sprite key wins");
            AssertEx.Equal("abc", runs[0].Text, "joined text");
            AssertEx.True(runs[0].IsCluster, "joined sprite is a display cluster");
            AssertEx.Equal(5, runs[0].Columns, "joining preserves daemon columns");
            AssertEx.Equal(5, runs[1].Col, "following text stays in place");
            AssertEx.Equal("abcx", TerminalColumns.Slice(TerminalColumns.Cells(runs), 0, 5),
                "joining preserves copied text");

            var incomplete = new List<SgrRun>
            {
                new SgrRun { Text = "a", Col = 0, CellWidth = 1 },
                new SgrRun { Text = "b", Col = 1, CellWidth = 1 },
                new SgrRun { Text = "c", Col = 2, CellWidth = 1 },
                new SgrRun { Text = "x", Col = 3, CellWidth = 1 },
            };
            TerminalSpriteSequences.Join(incomplete, new TextSpriteCatalog(new[] { "ab", "abcd" }));
            AssertEx.Equal(3, incomplete.Count, "incomplete longer key keeps trailing runs");
            AssertEx.Equal("ab", incomplete[0].Text, "retain the last complete key");
            AssertEx.Equal("c", incomplete[1].Text, "unmatched suffix stays separate");

            foreach (var second in new[]
            {
                new SgrRun { Text = "b", Col = 2, CellWidth = 1 },
                new SgrRun { Text = "b", Col = 1, CellWidth = 1, Fg = Color.white },
                new SgrRun { Text = "b", Col = 1, CellWidth = 1, Url = "https://example.com" },
            })
            {
                var separate = new List<SgrRun>
                {
                    new SgrRun { Text = "a", Col = 0, CellWidth = 1 }, second,
                };
                TerminalSpriteSequences.Join(separate, catalog);
                AssertEx.Equal(2, separate.Count, "gaps, colors and links prevent joining");
            }
        }
    }
}
