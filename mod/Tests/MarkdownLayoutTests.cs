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
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
            engine.Reflow(blocks.ToList(), width);
            return engine;
        }

        static string Copy(TextLayout text)
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, text, 0, 0);
            return MarkdownSelectionText.CopyRange(lines, new Vector2Int(0, 0),
                new Vector2Int(lines.Last().Text.Length, lines.Count - 1)) + (lines.Last().Source.CopySuffix ?? "");
        }

        public static void LayoutGenerationChangesOnlyWhenPlacementsChange()
        {
            var engine = Flow(200, Paragraph(new InlineRun { Text = "one" }));
            int generation = engine.Generation;
            var blocks = new List<MarkdownBlock> { Paragraph(new InlineRun { Text = "one" }) };
            engine.Reflow(blocks, 200);
            AssertEx.Equal(generation, engine.Generation, "same-width draw keeps visibility index");
            engine.Invalidate();
            engine.Reflow(blocks, 200);
            AssertEx.True(engine.Generation > generation, "invalidation at same width replaces index");
            generation = engine.Generation;
            engine.Clear();
            AssertEx.True(engine.Generation > generation, "document clear retires old index");
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
                ? picture.X + picture.Width + UiTheme.GapS <= text.X + text.Text.Lines[0].OffsetX
                : text.X + text.Text.Lines[0].Width + UiTheme.GapS <= picture.X, "text and image do not overlap");
            AssertEx.Equal("caption", Copy(text.Text), "caption remains selectable");
            AssertEx.True(engine.Height >= Math.Max(text.Y + text.Height, picture.Y + picture.Height), "document contains taller sibling");
        }

        public static void StandaloneFloatAllowsFollowingParagraphsAndListsBesideIt()
        {
            foreach (string alignment in new[] { "left", "right" })
            {
                var engine = PreviewFlow("<img src='shot.png' width='80' height='200' align='" + alignment + "'>\n\nFirst paragraph.\n\nSecond paragraph.\n\n- item one\n- item two", 400);
                var picture = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
                var texts = engine.Placements.Where(p => p.Kind == PlacementKind.Text).ToArray();
                AssertEx.Equal(picture.Y, texts[0].Y, "first paragraph starts beside standalone float");
                AssertEx.True(texts[1].Y < picture.Y + picture.Height, "next paragraph continues beside float");
                foreach (var text in texts)
                    foreach (var line in text.Text.Lines)
                        if (text.Y + line.Offset < picture.Y + picture.Height)
                            AssertEx.True(alignment == "right"
                                ? text.X + line.OffsetX + line.Width + UiTheme.GapS <= picture.X
                                : text.X + line.OffsetX >= picture.X + picture.Width + UiTheme.GapS,
                                "paragraph and list text clears image");
                AssertEx.True(engine.Height >= picture.Y + picture.Height, "float contributes to document height");
            }
        }

        public static void TextRestoresFullWidthBelowFloatWithoutChangingCopy()
        {
            string prose = string.Join(" ", Enumerable.Repeat("word", 60));
            foreach (string alignment in new[] { "left", "right" })
            {
                var engine = PreviewFlow("<img src='shot.png' width='100' height='50' align='" + alignment + "'>\n\n" + prose, 300);
                var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
                var picture = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
                var beside = text.Text.Lines.Where(l => text.Y + l.Offset < picture.Y + picture.Height).ToArray();
                var below = text.Text.Lines.Where(l => text.Y + l.Offset >= picture.Y + picture.Height).ToArray();
                AssertEx.True(beside.Length > 0 && below.Length > 0, "paragraph crosses float bottom");
                AssertEx.True(below.Any(l => l.Width > beside.Max(b => b.Width)), "lines regain full available width");
                AssertEx.True(below.All(l => l.OffsetX == 0f), "left float inset ends at bottom");
                AssertEx.Equal(prose, Copy(text.Text), "copy does not introduce newlines at width changes");
                var selected = new List<SelectionLine>();
                MarkdownSelectionText.CollectText(selected, text.Text, text.X, text.Y);
                AssertEx.Equal(text.X + beside[0].OffsetX, selected[0].X, "selection follows line inset");
            }
        }

        public static void ListRestoresFullWidthBelowFloatWithoutChangingCopy()
        {
            string prose = string.Join(" ", Enumerable.Repeat("word", 100));
            foreach (string alignment in new[] { "left", "right" })
            {
                var engine = PreviewFlow("<img src='shot.png' width='100' height='50' align='" + alignment + "'>\n\n- " + prose, 300);
                var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
                var picture = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
                var bullet = engine.Placements.Single(p => p.Kind == PlacementKind.Bullet);
                var beside = text.Text.Lines.Where(l => text.Y + l.Offset < picture.Y + picture.Height).ToArray();
                var below = text.Text.Lines.Where(l => text.Y + l.Offset >= picture.Y + picture.Height).ToArray();
                AssertEx.True(beside.Length > 0 && below.Length > 0, "list item crosses float bottom");
                AssertEx.Equal(bullet.X + bullet.Width, text.X + beside[0].OffsetX,
                    "first line reserves marker gutter beside float");
                AssertEx.Equal(UiTheme.GapM + bullet.Width, text.X,
                    "base item bounds retain only marker gutter");
                AssertEx.True(below.Any(l => l.Width > beside.Max(b => b.Width)),
                    "list lines regain full available width");
                AssertEx.True(below.All(l => l.OffsetX == 0f), "float inset ends at bottom");
                AssertEx.Equal(prose, Copy(text.Text), "copy retains complete item text across width changes");
            }
        }

        public static void HtmlAlignmentUsesSharedTextAndSelectionGeometry()
        {
            var engine = PreviewFlow("<p align='center'><img src='logo.png' width='32' height='32'></p>\n\n<h1 align='center'>SlopWorld</h1>\n\n<p align='right'>Body</p>", 400);
            var logo = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            AssertEx.Equal((400f - 32f) / 2f, logo.X, "paragraph centers its image");
            var texts = engine.Placements.Where(p => p.Kind == PlacementKind.Text).ToArray();
            AssertEx.True(texts[0].Y >= logo.Y + logo.Height, "centered image reserves block height");
            AssertEx.True(texts[0].Heading, "HTML heading uses heading style");
            AssertEx.Equal((texts[0].Width - texts[0].Text.Lines[0].Width) / 2f,
                texts[0].Text.Lines[0].OffsetX, "heading centers within block");
            AssertEx.Equal(texts[1].Width - texts[1].Text.Lines[0].Width,
                texts[1].Text.Lines[0].OffsetX, "paragraph aligns right");
        }

        public static void FloatCaptionsRetainParagraphAlignmentAndSelectionGeometry()
        {
            foreach (string imageAlign in new[] { "left", "right" })
                foreach (string textAlign in new[] { "center", "right" })
                {
                    var engine = PreviewFlow("<p align='" + textAlign + "'><img src='shot.png' width='40' height='60' align='" + imageAlign + "'>caption</p>", 400);
                    var image = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
                    var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
                    var line = text.Text.Lines.Single();
                    float inset = imageAlign == "left" ? image.Width + UiTheme.GapS : 0f;
                    float spare = text.Width - image.Width - UiTheme.GapS - line.Width;
                    float expected = inset + (textAlign == "center" ? spare / 2f : spare);
                    AssertEx.Equal(expected, line.OffsetX, "caption alignment within float bounds");
                    var selected = new List<SelectionLine>();
                    MarkdownSelectionText.CollectText(selected, text.Text, text.X, text.Y);
                    AssertEx.Equal(text.X + expected, selected.Single().X, "selection follows caption alignment");
                    AssertEx.Equal("caption", Copy(text.Text), "aligned caption stays selectable");
                }
        }

        public static void ImageOnlyParagraphRetainsExplicitBreaksAndLiteralCodeWhitespace()
        {
            foreach (string alignment in new[] { "", "left", "right" })
            {
                var engine = PreviewFlow("<p><br><img src='shot.png' width='40' height='60' align='" + alignment + "'><br></p>", 400);
                var text = engine.Placements.Single(p => p.Kind == PlacementKind.Text);
                AssertEx.Equal(3, text.Text.Lines.Count, "breaks surround image line");
                AssertEx.Equal("\n\n", Copy(text.Text), "explicit breaks survive copying");
            }
            var literal = PreviewFlow("<p><code>  </code><img src='shot.png' width='40' height='60'></p>", 400);
            AssertEx.Equal("  ", Copy(literal.Placements.Single(p => p.Kind == PlacementKind.Text).Text),
                "literal code spaces are retained beside image");
        }

        public static void FloatClearsSlabsAndDoesNotEscapeNestedContainers()
        {
            var engine = PreviewFlow("<img src='shot.png' width='80' height='200' align='right'>\n\n```\ncode\n```", 400);
            var image = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            var code = engine.Placements.Single(p => p.Kind == PlacementKind.Code);
            AssertEx.True(code.Y >= image.Y + image.Height, "code slab clears float");
            engine = PreviewFlow("> <img src='shot.png' width='80' height='200' align='right'>\n>\n> quoted\n\nafter", 400);
            image = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            var after = engine.Placements.Last(p => p.Kind == PlacementKind.Text);
            AssertEx.True(after.Y >= image.Y + image.Height, "quote contains its float");
            AssertEx.Equal(0f, after.Text.Lines[0].OffsetX, "float does not leak into following paragraph");
            var localImage = new InlineRun { IsImage = true, ImageWidth = 80, ImageHeight = 200, ImageAlign = "right" };
            engine = Flow(400, new MarkdownBlock
            {
                Kind = BlockKind.List,
                Children = new List<MarkdownBlock>
                {
                    new MarkdownBlock { Kind = BlockKind.Item, Children = new List<MarkdownBlock>
                    {
                        Paragraph(localImage), Paragraph(new InlineRun { Text = "caption" }),
                    } },
                    new MarkdownBlock { Kind = BlockKind.Item, Children = new List<MarkdownBlock>
                    {
                        Paragraph(new InlineRun { Text = "next item" }),
                    } },
                },
            });
            image = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
            var caption = engine.Placements.First(p => p.Kind == PlacementKind.Text);
            after = engine.Placements.Last(p => p.Kind == PlacementKind.Text);
            AssertEx.Equal(image.Y, caption.Y, "caption flows beside item image");
            AssertEx.True(after.Y >= image.Y + image.Height, "list item contains its float");
            AssertEx.Equal(0f, after.Text.Lines[0].OffsetX, "float does not leak into next list item");
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
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
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
        public static void OrderedMarkerGutter()
        {
            AssertEx.Equal(32f, MarkdownTableGeometry.MarkerGutter(100, 24),
                "wide ordered marker gets marker width plus spacing");
            AssertEx.Equal(20f, MarkdownTableGeometry.MarkerGutter(20, 24),
                "marker gutter never exceeds the available list width");
        }

        public static void TableGeometry()
        {
            var widths = MarkdownTableGeometry.AllocateColumns(100,
                new[] { 20f, 20f }, new[] { 20f, 100f });
            AssertEx.Equal(20f, widths[0], "short numeric column stays near its need");
            AssertEx.Equal(80f, widths[1], "prose column receives remaining viewport width");
            AssertEx.Equal(100f, widths[0] + widths[1], "column widths fill the viewport");

            var table = PreviewParser().Parse("| n | prose |\n| ---: | :---: |\n| 1 | text |")[0];
            AssertEx.Equal(TableAlignment.Right, table.ColumnAlignments[0],
                "right table alignment is retained");
            AssertEx.Equal(TableAlignment.Center, table.ColumnAlignments[1],
                "center table alignment is retained");
            AssertEx.Equal(0f, MarkdownTableGeometry.Padding(1f),
                "narrow table cells remove padding before overflowing");
        }

        public static void NestedListGeometry()
        {
            var engine = PreviewFlow("100. outer\n     - nested\n101. next");
            float lastBottom = 0f;
            float firstX = -1f;
            foreach (var placement in engine.Placements)
            {
                if (placement.Kind != PlacementKind.Text) continue;
                AssertEx.True(placement.Y >= lastBottom, "nested text never overlaps a sibling");
                lastBottom = placement.Y + placement.Height;
                if (firstX < 0f) firstX = placement.X;
            }
            foreach (var placement in engine.Placements)
                if (placement.Kind == PlacementKind.Bullet && placement.X == UiTheme.GapM)
                    AssertEx.True(placement.X + placement.Text.Width < firstX,
                        "three-digit marker leaves a gap before content");
            var tight = PreviewFlow("- one\n- two");
            var loose = PreviewFlow("- one\n\n- two");
            AssertEx.True(loose.Height > tight.Height, "loose list changes actual block spacing");
        }

        public static void TerminalTypography()
        {
            var oldStyle = TerminalFont.Style;
            int oldRevision = TerminalFont.Rev;
            try
            {
                var blocks = PreviewParser().Parse("```\nWWWW\n```");
                var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
                engine.Reflow(blocks, 300);
                var before = engine.Placements[0];
                TerminalFont.Style = new GUIStyle(oldStyle) { fontSize = 24 };
                TerminalFont.Rev++;
                engine.Reflow(blocks, 300);
                AssertEx.True(engine.Placements[0].Text.Width > before.Text.Width,
                    "same-width reflow sees a terminal font change without UI metric changes");
                var settled = engine.Placements[0];
                engine.Reflow(blocks, 300);
                AssertEx.True(ReferenceEquals(settled, engine.Placements[0]),
                    "unchanged frames retain the settled geometry");
            }
            finally { TerminalFont.Style = oldStyle; TerminalFont.Rev = oldRevision; }
        }

        public static void NarrowTableBounds()
        {
            var engine = PreviewFlow("| a | b |\n| ---: | :---: |\n| `W` | W |", 40);
            var placement = engine.Placements[0];
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectTable(lines, placement);
            foreach (var line in lines)
                AssertEx.True(line.X + line.Width <= placement.X + placement.Width,
                    "selection is clipped to the table even when one glyph cannot fit");
            AssertEx.Equal("a\nb\nW\nW", PreviewCopy(lines),
                "clipping preserves complete source text for copying");
            foreach (var row in placement.Table.Rows)
                foreach (var cell in row.Cells)
                    AssertEx.True(cell.Lines.Count > 0, "narrow cells retain source lines");
        }

        public static void ListMarkerFollowsFirstChildBelowOuterFloat()
        {
            foreach (string alignment in new[] { "left", "right" })
            {
                var engine = PreviewFlow("<img src='shot.png' width='80' height='200' align='" + alignment + "'>\n\n- ```\n  code\n  ```", 400);
                var bullet = engine.Placements.Single(p => p.Kind == PlacementKind.Bullet);
                var code = engine.Placements.Single(p => p.Kind == PlacementKind.Code);
                var image = engine.Placements.Single(p => p.Kind == PlacementKind.Image);
                AssertEx.True(code.Y >= image.Y + image.Height, "code clears outer float");
                AssertEx.Equal(code.Y, bullet.Y, "marker follows first child below outer float");
                AssertEx.Equal(code.X, bullet.X + bullet.Width, "marker regains normal gutter below float");
            }
        }

        public static void HtmlWhitespaceCollapsesAcrossStyleBoundaries()
        {
            var engine = PreviewFlow("<p>one <b> <i> two</i></b> three</p>", 400);
            AssertEx.Equal("one two three", Copy(engine.Placements.Single(p => p.Kind == PlacementKind.Text).Text),
                "HTML spaces collapse across nested inline elements");
            engine = PreviewFlow("<p>one <code> two </code> three<br>four</p>", 400);
            AssertEx.Equal("one  two  three\nfour", Copy(engine.Placements.Single(p => p.Kind == PlacementKind.Text).Text),
                "code whitespace and explicit breaks survive normalization");
        }

        static MarkdownDocumentParser PreviewParser()
        {
            var paths = new MarkdownPathResolver("demo", "/work/demo/docs/readme.md",
                () => "/work/demo");
            return new MarkdownDocumentParser(paths);
        }

        static MarkdownLayoutEngine PreviewFlow(string source, float width = 300f)
        {
            var engine = new MarkdownLayoutEngine(new MarkdownResourceStore(new MarkdownPathResolver("", "")));
            engine.Reflow(PreviewParser().Parse(source), width);
            return engine;
        }

        static TextLayout PreviewWrap(string text, float width, bool code = false) =>
            new MarkdownTextLayout(new StyleSet(), (run, available) => new ImageMetrics(1, 1))
                .Wrap(new List<InlineRun> { new InlineRun { Text = text, Code = code } }, width, 0);

        static string PreviewCopy(List<SelectionLine> lines) => MarkdownSelectionText.CopyRange(lines,
            new Vector2Int(0, 0), new Vector2Int(lines[lines.Count - 1].Text.Length, lines.Count - 1)) + (lines[lines.Count - 1].Source.CopySuffix ?? "");

    }
}
