using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class TerminalGeometryRegressionTests
    {
        public static void EdgeDragKeepsPenultimateLineSeparateFromPrompt()
        {
            var screen = new ScreenBuf
            {
                Rows = 4,
                Lines = new[] { "➜ cat", "test", "test", "➜ prompt" },
            };
            var history = new TerminalHistory();
            foreach (int row in new[] { 1, 2 })
            {
                int endpoint = TerminalPanelGeometry.SelectionEdgeRow(row, screen.Rows);
                Assert.That(endpoint, Is.EqualTo(row));
                Assert.That(history.SelectionText(screen, 0, row, 3, endpoint), Is.EqualTo("test"));
                Assert.That(history.SelectionText(screen, 3, row, 0, endpoint), Is.EqualTo("test"));
            }
        }

        public static void EdgeDragClampsOutsideRowsAndPreservesInteriorRows()
        {
            Assert.That(TerminalPanelGeometry.SelectionEdgeRow(-1, 4), Is.EqualTo(0));
            Assert.That(TerminalPanelGeometry.SelectionEdgeRow(4, 4), Is.EqualTo(3));
            Assert.That(TerminalPanelGeometry.SelectionEdgeRow(1, 4), Is.EqualTo(1));
            Assert.That(TerminalPanelGeometry.SelectionEdgeRow(2, 4), Is.EqualTo(2));
            Assert.That(TerminalPanelGeometry.SelectionEdgeRow(0, 1), Is.EqualTo(0));
        }

        public static void RejectsNonFiniteGeometry()
        {
            foreach (
                float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity }
            )
            {
                Assert.That(
                    TerminalPanelGeometry.TryMeasure(
                        new UiLayoutRect(0, 0, invalid, 100),
                        10,
                        10,
                        out _,
                        out _
                    ),
                    Is.False
                );
                Assert.That(
                    TerminalPanelGeometry.TryMeasure(
                        new UiLayoutRect(0, 0, 100, invalid),
                        10,
                        10,
                        out _,
                        out _
                    ),
                    Is.False
                );
                Assert.That(
                    TerminalPanelGeometry.TryMeasure(
                        new UiLayoutRect(0, 0, 100, 100),
                        invalid,
                        10,
                        out _,
                        out _
                    ),
                    Is.False
                );
                Assert.That(
                    TerminalPanelGeometry.TryMeasure(
                        new UiLayoutRect(0, 0, 100, 100),
                        10,
                        invalid,
                        out _,
                        out _
                    ),
                    Is.False
                );
            }
        }

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
            var buf = new ScreenBuf
            {
                Cols = 4,
                Runs = new[]
                {
                    new List<SgrRun>
                    {
                        new SgrRun
                        {
                            Col = 0,
                            Text = "好",
                            CellWidth = 2,
                            Url = url,
                        },
                        new SgrRun
                        {
                            Col = 2,
                            Text = "ab",
                            Url = url,
                        },
                    },
                    new List<SgrRun>
                    {
                        new SgrRun
                        {
                            Col = 0,
                            Text = "😀",
                            CellWidth = 1,
                            Url = url,
                        },
                    },
                },
            };
            var links = new TerminalLinkService();
            links.Track(buf, new Vector2Int(1, 0));
            Assert.That(links.HoverUrl, Is.EqualTo(url));
            Assert.That(links.HoverSpans.Count, Is.EqualTo(2));
            Assert.That(
                (links.HoverSpans[0].StartColumn, links.HoverSpans[0].EndColumnExclusive),
                Is.EqualTo((0, 4))
            );
            Assert.That(links.HoverSpans[1].EndColumnExclusive, Is.EqualTo(1));
            Assert.That(links.Find(buf, new Vector2Int(1, 1)), Is.Null);
            links.ClearHover();
            links.Track(buf, new Vector2Int(2, 0));
            Assert.That(links.HoverSpans[0].StartColumn, Is.EqualTo(0));
        }
    }
}
