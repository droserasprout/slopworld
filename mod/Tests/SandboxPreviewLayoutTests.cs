using System;
using System.Linq;

namespace SlopWorld.Tests
{
    static class SandboxPreviewLayoutTests
    {
        static void Ensure(
            SandboxPreviewLayout layout,
            Wire.SettingsPreview data,
            Func<string, float, float> measure,
            float width = 300f,
            int metrics = 1,
            int font = 1
        ) => layout.Ensure(data, width, metrics, font, 24f, 4f, 8f, 16f, measure);

        public static void ScrollingLargeFieldDoesNotRemeasureOrVisitOffscreenRows()
        {
            var data = new Wire.SettingsPreview { Title = "Settings" };
            var field = new Wire.PreviewField { Label = "Environment" };
            field.Values.Add(string.Join("\n", Enumerable.Range(0, 10000).Select(i => "KEY=" + i)));
            data.Fields.Add(field);
            var layout = new SandboxPreviewLayout();
            int measures = 0;
            float Measure(string text, float width)
            {
                measures++;
                return 20f;
            }
            Ensure(layout, data, Measure);
            AssertEx.Equal(10001, measures, "each logical text row is measured once");
            for (int frame = 0; frame < 100; frame++)
            {
                Ensure(layout, data, Measure);
                float top = 100000f + frame * 20f;
                int first = layout.FirstVisible(top);
                AssertEx.True(first > 4000, "binary search skips offscreen prefix");
                int drawn = 0;
                for (int i = first; i < layout.Rows.Count && layout.Rows[i].Top < top + 400f; i++)
                    drawn++;
                AssertEx.True(drawn <= 21, "drawing stays bounded by viewport height");
            }
            AssertEx.Equal(10001, measures, "scrolling never remeasures the document");
        }

        public static void ReflowTracksReplyWidthAndTypographyAndRecoversAfterFailure()
        {
            var data = new Wire.SettingsPreview { Title = "title" };
            var layout = new SandboxPreviewLayout();
            int measures = 0;
            float Measure(string text, float width)
            {
                measures++;
                return width / 10f;
            }
            Ensure(layout, data, Measure);
            Ensure(layout, data, Measure, width: 200f);
            AssertEx.Equal(20f, layout.Rows[0].Height, "width reflows wrapped text");
            Ensure(layout, data, Measure, width: 200f, metrics: 2);
            Ensure(layout, data, Measure, width: 200f, metrics: 2, font: 2);
            Ensure(layout, data.Clone(), Measure, width: 200f, metrics: 2, font: 2);
            AssertEx.Equal(5, measures, "replacement reply and metric changes invalidate layout");
            try
            {
                Ensure(layout, data, (_, _) => throw new InvalidOperationException("controlled"));
                throw new Exception("measurement failure was swallowed");
            }
            catch (InvalidOperationException) { }
            Ensure(layout, data, Measure);
            AssertEx.Equal(6, measures, "failed layout does not retain a successful cache key");
            AssertEx.Equal(1, layout.Rows.Count, "retry replaces partial rows");
        }

        public static void MultilineValuesKeepBlankLinesAndEmptyFieldsAndExclusiveEdges()
        {
            var data = new Wire.SettingsPreview { Title = "title", Subtitle = "sub" };
            data.Notes.Add("note");
            var field = new Wire.PreviewField { Label = "values" };
            field.Values.Add("first\r\n\r\nlast\n");
            data.Fields.Add(field);
            data.Fields.Add(new Wire.PreviewField { Label = "empty" });
            var layout = new SandboxPreviewLayout();
            Ensure(layout, data, (_, _) => 20f);
            AssertEx.Equal(
                "first||last|",
                string.Join(
                    "|",
                    layout
                        .Rows.Where(r => r.Style == SandboxPreviewLayout.Kind.Value)
                        .Select(r => r.Text)
                ),
                "logical newlines and blank entries survive"
            );
            AssertEx.Equal(
                "(nothing)",
                layout.Rows[layout.Rows.Count - 1].Text,
                "empty fields retain placeholder"
            );
            AssertEx.Equal(1, layout.FirstVisible(20f), "row ending at viewport top is excluded");
            AssertEx.Equal(
                layout.Rows.Count,
                layout.FirstVisible(layout.Height),
                "scroll beyond last row has no visible content"
            );
        }
    }
}
