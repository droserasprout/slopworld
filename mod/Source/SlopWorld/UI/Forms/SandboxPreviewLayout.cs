using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Retains one immutable daemon reply's measured rows. Scrolling only searches
    // row bounds; text preparation belongs to reply/width/typography changes.
    internal sealed class SandboxPreviewLayout
    {
        internal enum Kind
        {
            Title,
            Subtitle,
            Note,
            Heading,
            Value,
            Empty,
        }

        internal readonly struct Row
        {
            public readonly string Text;
            public readonly Kind Style;
            public readonly float Top;
            public readonly float Height;

            public Row(string text, Kind style, float top, float height)
            {
                Text = text;
                Style = style;
                Top = top;
                Height = height;
            }
        }

        readonly List<Row> _rows = new List<Row>();
        readonly List<float> _ends = new List<float>();
        Wire.SettingsPreview _data;
        float _width;
        int _metricsRevision;
        int _fontRevision;
        public IReadOnlyList<Row> Rows => _rows;
        public float Height { get; private set; }

        public void Ensure(
            Wire.SettingsPreview data,
            float width,
            int metricsRevision,
            int fontRevision,
            float headingHeight,
            float gapXS,
            float gapS,
            float gapM,
            Func<string, float, float> measure
        )
        {
            width = Math.Max(1f, width);
            if (
                ReferenceEquals(data, _data)
                && width == _width
                && metricsRevision == _metricsRevision
                && fontRevision == _fontRevision
            )
                return;
            // Publish the key only after successful measurement so failed reflows retry.
            _data = null;
            _rows.Clear();
            _ends.Clear();
            Height = 0f;

            void Add(string text, Kind style, float height)
            {
                _rows.Add(new Row(text, style, Height, height));
                Height += height;
                _ends.Add(Height);
            }

            void Text(string text, Kind style)
            {
                // Split multiline fields so a visible field cannot force native
                // layout and glyph emission for all of its offscreen entries.
                foreach (
                    string line in (text ?? "")
                        .Replace("\r\n", "\n")
                        .Replace('\r', '\n')
                        .Split('\n')
                )
                    Add(line, style, measure(line, width));
            }

            Text(data.Title, Kind.Title);
            Height += gapXS;
            if (!string.IsNullOrEmpty(data.Subtitle))
            {
                Text(data.Subtitle, Kind.Subtitle);
                Height += gapXS;
            }
            if (data.Notes.Count > 0)
            {
                foreach (string note in data.Notes)
                    Text(note, Kind.Note);
                Height += gapS;
            }
            foreach (var field in data.Fields)
            {
                Add(field.Label, Kind.Heading, headingHeight);
                Height += gapXS;
                if (field.Values.Count == 0)
                    Text("(nothing)", Kind.Empty);
                else
                    foreach (string value in field.Values)
                        Text(value, Kind.Value);
                Height += gapM;
            }
            Height += gapM;
            _width = width;
            _metricsRevision = metricsRevision;
            _fontRevision = fontRevision;
            _data = data;
        }

        public int FirstVisible(float top) => VisibleRows.First(_ends, top);
    }
}
