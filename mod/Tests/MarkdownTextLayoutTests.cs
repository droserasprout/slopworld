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
                new Vector2Int(lines.Last().Text.Length, lines.Count - 1));
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (bool task in new[] { false, true })
                foreach (bool clippedSpace in new[] { false, true })
                    yield return ($"inline {(task ? "task" : "image")} wraps after {(clippedSpace ? "clipped whitespace" : "full text")}", () => InlineObjectWrap(task, clippedSpace));
            foreach (int length in new[] { 128, 129, 300 })
                yield return ($"long word of {length} characters wraps without losing copy offsets", () => LongWord(length));
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
    }
}
