using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownLayoutTests
    {
        static MarkdownBlock Paragraph(params InlineRun[] runs) => new MarkdownBlock
        {
            Kind = BlockKind.Paragraph, Runs = runs.ToList(),
        };

        static MarkdownLayoutEngine Flow(float width, params MarkdownBlock[] blocks)
        {
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore());
            engine.Reflow(blocks.ToList(), width);
            return engine;
        }

        static string Copy(TextLayout text)
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, text, 0, 0);
            return MarkdownSelectionText.CopyRange(lines, new Vector2Int(0, 0),
                new Vector2Int(lines.Last().Text.Length, lines.Count - 1));
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (string alignment in new[] { "left", "center", "right" })
                yield return ("standalone image alignment: " + alignment, () => SingleImage(alignment));
            foreach (string alignment in new[] { "left", "right" })
                yield return ("text flows beside image: " + alignment, () => AlignedImage(alignment));
        }

        static void SingleImage(string alignment)
        {
            var image = new InlineRun { IsImage = true, ImageWidth = 80, ImageHeight = 40, ImageAlign = alignment };
            var engine = Flow(200, Paragraph(new InlineRun { Text = "  " }, image));
            var placement = engine.Placements.Single();
            AssertEx.Equal(PlacementKind.Image, placement.Kind, "whitespace does not prevent standalone image placement");
            float spare = 200 - UiTheme.GapM * 2 - 80;
            float expected = UiTheme.GapM + (alignment == "right" ? spare : alignment == "center" ? spare / 2 : 0);
            AssertEx.Equal(expected, placement.X, "image alignment within content inset");
            AssertEx.Equal(80f, placement.Width, "explicit width");
            AssertEx.Equal(40f, placement.Height, "explicit height");
            AssertEx.True(engine.Height >= placement.Y + placement.Height, "document contains image");
        }

        static void AlignedImage(string alignment)
        {
            var image = new InlineRun { IsImage = true, ImageWidth = 40, ImageHeight = 60, ImageAlign = alignment };
            var engine = Flow(240, Paragraph(image, new InlineRun { Text = "caption" }));
            var picture = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
            AssertEx.Equal(picture.Y, text.Y, "text and image share top edge");
            AssertEx.True(alignment == "left"
                ? picture.X + picture.Width + UiTheme.GapS <= text.X
                : text.X + text.Width + UiTheme.GapS <= picture.X, "text and image do not overlap");
            AssertEx.Equal("caption", Copy(text.Text), "caption remains selectable");
            AssertEx.True(engine.Height >= Math.Max(text.Y + text.Height, picture.Y + picture.Height), "document contains taller sibling");
        }

        public static void ImageSizingPreservesAspectAndFitsAvailableWidth()
        {
            foreach (var image in new[]
            {
                new InlineRun { IsImage = true },
                new InlineRun { IsImage = true, ImageWidth = 160 },
                new InlineRun { IsImage = true, ImageHeight = 90 },
                new InlineRun { IsImage = true, ImageWidth = 320, ImageHeight = 180 },
            })
            {
                var placement = Flow(80 + UiTheme.GapM * 2, Paragraph(image)).Placements.Single();
                AssertEx.Equal(80f, placement.Width, "image fits content width");
                AssertEx.Equal(45f, placement.Height, "fallback 16:9 aspect survives proportional scaling");
            }
        }

        public static void FullWidthAlignedImagePushesTextBelowIt()
        {
            var image = new InlineRun { IsImage = true, ImageWidth = 500, ImageHeight = 250, ImageAlign = "left" };
            var engine = Flow(200, Paragraph(image, new InlineRun { Text = "caption" }));
            var picture = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
            AssertEx.Equal(picture.X, text.X, "stacked caption uses full content width");
            AssertEx.Equal(picture.Y + picture.Height + UiTheme.GapS, text.Y, "caption clears full-width image");
            AssertEx.Equal(picture.Width, text.Width, "caption width is restored");
        }

        public static void MultipleOrUnalignedImagesRemainInline()
        {
            var first = new InlineRun { IsImage = true, ImageWidth = 20, ImageHeight = 10, ImageAlign = "left" };
            var second = new InlineRun { IsImage = true, ImageWidth = 20, ImageHeight = 10, ImageAlign = "right" };
            var placement = Flow(300, Paragraph(first, second)).Placements.Single();
            AssertEx.Equal(PlacementKind.Text, placement.Kind, "multiple images use inline flow");
            AssertEx.Equal(2, placement.Text.Lines.SelectMany(l => l.Pieces).Count(p => p.Run.IsImage), "both images retained");
            first.ImageAlign = "center";
            placement = Flow(300, Paragraph(new InlineRun { Text = "caption " }, first)).Placements.Single();
            AssertEx.Equal(PlacementKind.Text, placement.Kind, "centered image with prose remains inline");
        }

        public static void HighlightingCannotChangeCopiedSource()
        {
            const string source = "red\nnext";
            var block = new MarkdownBlock { Kind = BlockKind.Code, Code = source, Info = "rust", Highlighted = "\u001b[31mred\u001b[0m\r\nnext" };
            var placement = Flow(300, block).Placements.Single();
            AssertEx.Equal(source, Copy(placement.Text), "ANSI highlighting preserves original code");
            AssertEx.True(placement.Text.Lines.SelectMany(l => l.Pieces).Any(p => p.Run.HasColor), "matching highlight colors are retained");
            AssertEx.Equal("rust", placement.Label, "language label retained");
            block.Highlighted = "rewritten output";
            var fallback = Flow(300, block).Placements.Single();
            AssertEx.Equal(source, Copy(fallback.Text), "mismatching highlight output falls back to source");
            AssertEx.False(fallback.Text.Lines.SelectMany(l => l.Pieces).Any(p => p.Run.HasColor), "mismatching colors discarded");
            block.Info = "";
            var unlabelled = Flow(300, block).Placements.Single();
            AssertEx.Equal(UiTheme.TinyH + UiTheme.GapXS, fallback.Height - unlabelled.Height, "language label reserves vertical space");
        }

        public static void QuoteRuleAndItemGeometryPreservesDocumentOrder()
        {
            var engine = Flow(240,
                new MarkdownBlock { Kind = BlockKind.Quote, Children = new List<MarkdownBlock>
                {
                    Paragraph(new InlineRun { Text = "quoted" }), new MarkdownBlock { Kind = BlockKind.Rule },
                } },
                new MarkdownBlock { Kind = BlockKind.Item, Children = new List<MarkdownBlock>
                {
                    Paragraph(new InlineRun { Text = "after" }),
                } });
            var quote = engine.Placements.Single(p => p.Kind == PlacementKind.Quote);
            var texts = engine.Placements.Where(p => p.Kind == PlacementKind.Text).ToArray();
            var rule = engine.Placements.Single(p => p.Kind == PlacementKind.Rule);
            AssertEx.Equal(quote.X + UiTheme.GapM, texts[0].X, "quote text indented");
            AssertEx.True(rule.Y >= texts[0].Y + texts[0].Height, "rule follows quote text");
            AssertEx.True(texts[1].Y >= quote.Y + quote.Height, "next item clears quote");
            AssertEx.True(engine.Placements.Zip(engine.Placements.Skip(1), (a, b) => a.Y <= b.Y).All(v => v), "placements sorted spatially");
            AssertEx.True(engine.Placements.IndexOf(texts[0]) < engine.Placements.IndexOf(quote), "equal-Y placements retain insertion order");
        }

        public static void InvalidationAndClearRebuildAtTheSameWidth()
        {
            var blocks = new List<MarkdownBlock> { Paragraph(new InlineRun { Text = "first" }) };
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore());
            engine.InvalidateTypography();
            engine.Reflow(blocks, 200);
            var first = engine.Placements.Single();
            engine.Reflow(blocks, 200);
            AssertEx.True(ReferenceEquals(first, engine.Placements.Single()), "settled width reuses geometry");
            blocks[0].Runs[0].Text = "replacement";
            engine.Invalidate();
            engine.Reflow(blocks, 200);
            AssertEx.Equal("replacement", Copy(engine.Placements.Single().Text), "document invalidation reflows at same width");
            engine.Clear();
            AssertEx.Equal(0, engine.Placements.Count, "clear removes old placements");
            AssertEx.Equal(0f, engine.Height, "clear removes old height");
            engine.Reflow(blocks, 200);
            AssertEx.Equal("replacement", Copy(engine.Placements.Single().Text), "clear permits reuse at same width");
            engine.Clear();
            engine.Reflow(null, 0);
            AssertEx.Equal(1f, engine.Width, "zero viewport width is bounded");
            AssertEx.Equal(0, engine.Placements.Count, "empty document has no placements");
        }

        public static void RaggedTablesFillMissingCellsAndDefaultAlignment()
        {
            var header = new TableRow { Header = true };
            header.Cells.Add(new List<InlineRun> { new InlineRun { Text = "a" } });
            header.Cells.Add(new List<InlineRun> { new InlineRun { Text = "b" } });
            var row = new TableRow();
            row.Cells.Add(new List<InlineRun> { new InlineRun { Text = "value" } });
            var block = new MarkdownBlock { Kind = BlockKind.Table, Rows = new List<TableRow> { header, row } };
            var table = Flow(300, block).Placements.Single().Table;
            AssertEx.Sequence(new[] { TableAlignment.Left, TableAlignment.Left }, table.Alignments, "unspecified columns align left");
            AssertEx.True(table.Rows[0].Header && !table.Rows[1].Header, "header status retained");
            AssertEx.Equal(2, table.Rows[1].Cells.Count, "missing cells padded");
            AssertEx.Equal("", Copy(table.Rows[1].Cells[1]), "padded cell has no invented text");
            AssertEx.Equal(table.Rows[0].Height, table.Rows[1].Offset, "rows stack without overlap");
            AssertEx.Equal(table.Rows.Sum(r => r.Height), table.Height, "table contains all rows");
            AssertEx.Sequence(new[] { 50f, 50f }, MarkdownTableGeometry.AllocateColumns(100, new[] { 10f, 10f }, new[] { 20f, 20f }), "surplus beyond preferred widths distributes evenly");
        }

        public static void HeadingsReserveLeadingSpaceOnlyAfterContent()
        {
            var heading = new MarkdownBlock { Kind = BlockKind.Heading, Level = 1,
                Runs = new List<InlineRun> { new InlineRun { Text = "Title" } } };
            var engine = Flow(300, heading, new MarkdownBlock { Kind = BlockKind.Raw,
                Runs = new List<InlineRun> { new InlineRun { Text = "<unsupported>", Faint = true } } }, heading);
            var placements = engine.Placements;
            AssertEx.Equal(UiTheme.GapM, placements[0].Y, "first heading uses document inset");
            AssertEx.True(placements[0].Heading && placements[2].Heading, "headings retain heading color");
            AssertEx.False(placements[1].Heading, "raw content remains body text");
            AssertEx.Equal(placements[1].Y + placements[1].Height + UiTheme.GapS + UiTheme.GapM,
                placements[2].Y, "later heading receives additional leading margin");
            AssertEx.Equal("<unsupported>", Copy(placements[1].Text), "raw content remains selectable");
            AssertEx.True(engine.Styles != null, "layout initializes typography");
        }
    }
}
