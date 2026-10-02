using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class MarkdownTextLayoutTests
    {
        static MarkdownTextLayout Layout(StyleSet styles = null) => new MarkdownTextLayout(
            styles ?? new StyleSet(), (run, available) => new ImageMetrics(run.ImageWidth, run.ImageHeight));

        static string Copy(TextLayout layout)
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, layout, 0, 0);
            return MarkdownSelectionText.CopyRange(lines, new Vector2Int(0, 0),
                new Vector2Int(lines.Last().Text.Length, lines.Count - 1)) + (lines.Last().Source.CopySuffix ?? "");
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (bool task in new[] { false, true })
                foreach (bool clippedSpace in new[] { false, true })
                    yield return ($"inline {(task ? "task" : "image")} wraps after {(clippedSpace ? "clipped whitespace" : "full text")}", () => InlineObjectWrap(task, clippedSpace));
            foreach (int length in new[] { 128, 129, 300 })
                yield return ($"long word of {length} characters wraps without losing copy offsets", () => LongWord(length));
        }

        public static void NarrowCodeAndProseKeepUnicodeTextElementsAndCopyLosslessly()
        {
            const string source = "😀e\u0301𝄞界";
            foreach (bool code in new[] { false, true })
            {
                var text = code ? source + "\n" + source : source;
                var wrapped = Layout().Wrap(new List<InlineRun> { new InlineRun { Text = text, Code = code } }, 1f, 0);
                Assert.That(Copy(wrapped), Is.EqualTo(text));
                var elements = wrapped.Lines.SelectMany(line => line.Pieces).Select(piece => piece.Text).ToArray();
                Assert.That(elements, Is.EqualTo(code
                    ? new[] { "😀", "e\u0301", "𝄞", "界", "😀", "e\u0301", "𝄞", "界" }
                    : new[] { "😀", "e\u0301", "𝄞", "界" }));
            }
            string longWord = string.Concat(Enumerable.Repeat(source, 40));
            var longLayout = Layout().Wrap(new List<InlineRun> { new InlineRun { Text = longWord } }, 15f, 0);
            Assert.That(Copy(longLayout), Is.EqualTo(longWord), "long-word measurement and wrapping agree on UTF-16 identity");
        }

        static void LongWord(int length)
        {
            var styles = new StyleSet();
            var layout = Layout(styles);
            string source = string.Concat(Enumerable.Repeat("Wi", length / 2)) + (length % 2 == 0 ? "" : "W");
            var runs = new List<InlineRun> { new InlineRun { Text = source } };
            float measured = styles.Normal.CalcSize(new GUIContent(source)).x;
            var metrics = layout.Measure(runs, 0);
            Assert.That(metrics.MinimumWidth, Is.EqualTo(measured));
            Assert.That(metrics.PreferredWidth, Is.EqualTo(measured));
            var wrapped = layout.Wrap(runs, 48f, 0);
            Assert.That(wrapped.Lines.Count, Is.GreaterThan(1));
            Assert.That(Copy(wrapped), Is.EqualTo(source));
            int offset = 0;
            foreach (var line in wrapped.Lines)
            {
                Assert.That(line.Width, Is.LessThanOrEqualTo(48f));
                Assert.That(line.Pieces.Count, Is.EqualTo(1), "adjacent characters share one run piece");
                Assert.That(line.Pieces[0].TextBuilder, Is.Null, "builders are finalized for rendering");
                Assert.That(line.LogicalOffset, Is.EqualTo(offset));
                Assert.That(line.LogicalLength, Is.EqualTo(line.Pieces[0].Text.Length));
                offset += line.LogicalLength;
            }
            Assert.That(offset, Is.EqualTo(source.Length));
        }

        static void InlineObjectWrap(bool task, bool clippedSpace)
        {
            var styles = new StyleSet();
            float textWidth = styles.Normal.CalcSize(new GUIContent("a")).x;
            var item = new InlineRun { IsTask = task, IsImage = !task, Text = task ? "[x]" : "",
                ImageWidth = 40, ImageHeight = 35 };
            var runs = new List<InlineRun>
            {
                new InlineRun { Text = clippedSpace ? "a   " : "a" }, item,
            };
            var wrapped = Layout(styles).Wrap(runs, textWidth + 0.5f, 0);
            Assert.That(wrapped.Lines.Count, Is.EqualTo(2));
            Assert.That(wrapped.Lines[0].BreakAfter, Is.EqualTo(TextBreakKind.SoftWrap));
            Assert.That(wrapped.Lines[0].CopySuffix ?? "", Is.EqualTo(clippedSpace ? "   " : ""));
            var objectLine = wrapped.Lines[1];
            Assert.That(objectLine.Pieces.Single().Run, Is.SameAs(item));
            Assert.That(objectLine.Width, Is.EqualTo(task ? UiControls.TickColW : 40));
            Assert.That(objectLine.LogicalOffset, Is.EqualTo(clippedSpace ? 4 : 1));
            Assert.That(objectLine.LogicalLength, Is.EqualTo(task ? 3 : 0));
            Assert.That(wrapped.Height, Is.EqualTo(wrapped.Lines.Sum(l => l.Height)));
            Assert.That(objectLine.Offset, Is.EqualTo(wrapped.Lines[0].Height));
        }

        public static void IntrinsicWidthsIncludeImagesTasksAndInlineCodePadding()
        {
            var layout = Layout();
            var image = new InlineRun { IsImage = true, ImageWidth = 80, ImageHeight = 40 };
            var task = new InlineRun { IsTask = true, Text = "[ ]" };
            var metrics = layout.Measure(new List<InlineRun> { null, image, task }, 0);
            Assert.That(metrics.MinimumWidth, Is.EqualTo(Math.Max(80, UiControls.TickColW)));
            Assert.That(metrics.PreferredWidth, Is.EqualTo(80 + UiControls.TickColW));
            var styles = new StyleSet();
            var code = new InlineRun { Code = true, InlineCode = true, Text = "a\r\nb" };
            metrics = Layout(styles).Measure(new List<InlineRun> { code }, 0);
            float glyph = styles.For(code, 0).CalcSize(new GUIContent("a")).x;
            Assert.That(metrics.MinimumWidth, Is.EqualTo(glyph + 2 * UiTheme.GapXS), "code can shrink to one padded glyph");
        }

        public static void EmptyAndNullRunsHaveStableEmptyGeometry()
        {
            foreach (var runs in new List<InlineRun>[] { null, new List<InlineRun>(), new List<InlineRun> { null, new InlineRun() } })
            {
                var layout = Layout();
                var metrics = layout.Measure(runs, 0);
                Assert.That(metrics.MinimumWidth, Is.EqualTo(1f));
                var wrapped = layout.Wrap(runs, 100, 0);
                Assert.That(wrapped.Lines.Count, Is.EqualTo(1));
                Assert.That(wrapped.Lines[0].Pieces, Is.Empty);
                Assert.That(wrapped.Height, Is.GreaterThan(0f));
                Assert.That(wrapped.Width, Is.Zero);
                Assert.That(Copy(wrapped), Is.Empty);
            }
        }

        public static void ExplicitNewlinesAfterLongWordsPreserveSourceOffsets()
        {
            string source = new string('a', 150) + "\n\nlast\n";
            var wrapped = Layout().Wrap(new List<InlineRun> { new InlineRun { Text = source } }, 48, 0);
            Assert.That(Copy(wrapped), Is.EqualTo(source));
            Assert.That(wrapped.Lines.Count(l => l.BreakAfter == TextBreakKind.Source), Is.EqualTo(3));
            Assert.That(wrapped.Lines.Last().LogicalOffset, Is.EqualTo(source.Length));
            Assert.That(wrapped.Lines.Last().LogicalLength, Is.Zero);
        }
        public static void CodeContinuations()
        {
            var layout = PreviewWrap("abcdef", 3f, true);
            AssertEx.True(layout.Lines.Count >= 2, "narrow code wraps into visual lines");
            AssertEx.True(layout.Lines[1].Continuation,
                "soft-wrapped code line is visually marked as a continuation");
            AssertEx.Equal("abcdef", PreviewCopyCodeLines(layout),
                "continuation treatment does not change copied source");
        }

        public static void InlineCodePadding()
        {
            var run = new InlineRun { Text = "x", Code = true, InlineCode = true };
            var layout = new MarkdownTextLayout(new StyleSet(), (item, available) =>
                new ImageMetrics(1, 1)).Wrap(new List<InlineRun> { run }, 100, 0);
            var piece = layout.Lines[0].Pieces[0];
            AssertEx.Equal(UiTheme.GapXS, piece.PaddingLeft, "inline chip has left padding");
            AssertEx.Equal(UiTheme.GapXS, piece.PaddingRight, "inline chip has right padding");
            AssertEx.Equal(8f + UiTheme.GapXS * 2f, piece.Width,
                "inline chip width includes both paddings");
        }

        public static void TypographyInvalidation()
        {
            var styles = new StyleSet();
            var oldNormal = styles.Normal;
            var layout = new MarkdownTextLayout(styles, (run, available) =>
                new ImageMetrics(1, 1));
            layout.InvalidateMetrics();
            AssertEx.True(!ReferenceEquals(oldNormal, styles.Normal),
                "metrics invalidation replaces cached styles");
        }

        public static void HeadingCodeFont()
        {
            var styles = new StyleSet();
            for (int heading = 1; heading <= 6; heading++)
            {
                var prose = styles.For(new InlineRun(), heading);
                var code = styles.For(new InlineRun { Code = true }, heading);
                AssertEx.True(ReferenceEquals(code.font, TerminalFont.Style.font),
                    "heading code retains terminal face");
                int size = prose.fontSize > 0 ? prose.fontSize : prose.font.fontSize;
                AssertEx.Equal(size, code.fontSize, "code follows heading size even for baked fonts");
                AssertEx.Equal(styles.MeasureChar(code, 'W'), styles.MeasureChar(code, 'i'),
                    "code glyph advances stay monospaced");
            }
            AssertEx.True(styles.H1.fontSize > styles.H2.fontSize &&
                styles.H2.fontSize > styles.H3.font.fontSize,
                "zero-size baked styles still get distinct heading sizes");
        }

        public static void MixedBaselines()
        {
            var styles = new StyleSet();
            var layout = new MarkdownTextLayout(styles, (run, width) => new ImageMetrics(20, 40))
                .Wrap(new List<InlineRun>
                {
                    new InlineRun { Text = "Wi" },
                    new InlineRun { Text = "code", Code = true, InlineCode = true },
                    new InlineRun { IsImage = true },
                }, 300, 0);
            var line = layout.Lines[0];
            AssertEx.True(styles.MeasureChar(styles.Normal, 'W') > styles.MeasureChar(styles.Normal, 'i'),
                "test exercises unequal proportional advances");
            var prose = line.Pieces[0];
            var code = line.Pieces[1];
            AssertEx.Equal(prose.OffsetY + StyleSet.Baseline(prose.Style),
                code.OffsetY + StyleSet.Baseline(code.Style), "font ascents share a baseline");
            foreach (var piece in line.Pieces)
                AssertEx.True(piece.OffsetY + piece.Height <= line.Height,
                    "line bounds contain shifted images and font descenders");
            AssertEx.True(line.Height > 40f, "descenders extend below the inline image baseline");
        }

        static TextLayout PreviewWrap(string text, float width, bool code = false) =>
            new MarkdownTextLayout(new StyleSet(), (run, available) => new ImageMetrics(1, 1))
                .Wrap(new List<InlineRun> { new InlineRun { Text = text, Code = code } }, width, 0);

        static string PreviewCopy(List<SelectionLine> lines) => MarkdownSelectionText.CopyRange(lines,
            new Vector2Int(0, 0), new Vector2Int(lines[lines.Count - 1].Text.Length, lines.Count - 1)) + (lines[lines.Count - 1].Source.CopySuffix ?? "");

        static string PreviewCopyCodeLines(TextLayout layout)
        {
            var lines = new List<SelectionLine>();
            MarkdownSelectionText.CollectText(lines, layout, 0, 0);
            return PreviewCopy(lines);
        }

    }
}
