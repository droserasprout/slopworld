using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalGeometryRegressionTests
    {
        public static void WordSelectionUsesUnicodeAndWholeEmoji()
        {
            var cells = new[] { "α", "β", "γ", " ", "😀", null, "😁", null, "𐐀", "𐐁" };
            TerminalColumns.WordRange(cells, 1, out int first, out int last);
            Assert.That((first, last), Is.EqualTo((0, 2)));
            TerminalColumns.WordRange(cells, 5, out first, out last);
            Assert.That((first, last), Is.EqualTo((4, 5)));
            TerminalColumns.WordRange(cells, 6, out first, out last);
            Assert.That((first, last), Is.EqualTo((6, 7)));
            TerminalColumns.WordRange(cells, 9, out first, out last);
            Assert.That((first, last), Is.EqualTo((8, 9)));
        }

        public static void LinksUseOccupiedColumnsAcrossRunsAndRows()
        {
            const string url = "https://example.org";
            var buf = new ScreenBuf { Cols = 4, Runs = new[] {
                new List<SgrRun> {
                    new SgrRun { Col = 0, Text = "好", CellWidth = 2, Url = url },
                    new SgrRun { Col = 2, Text = "ab", Url = url } },
                new List<SgrRun> { new SgrRun { Col = 0, Text = "😀", CellWidth = 1, Url = url } }
            } };
            var links = new TerminalLinkService();
            links.Track(buf, new Vector2Int(1, 0));
            Assert.That(links.HoverUrl, Is.EqualTo(url));
            Assert.That(links.HoverSpans.Count, Is.EqualTo(2));
            Assert.That((links.HoverSpans[0].StartColumn, links.HoverSpans[0].EndColumnExclusive), Is.EqualTo((0, 4)));
            Assert.That(links.HoverSpans[1].EndColumnExclusive, Is.EqualTo(1));
            Assert.That(links.Find(buf, new Vector2Int(1, 1)), Is.Null);
            links.ClearHover();
            links.Track(buf, new Vector2Int(2, 0));
            Assert.That(links.HoverSpans[0].StartColumn, Is.EqualTo(0));
        }
    }
}
