using System;
using System.Collections.Generic;

namespace SlopWorld
{
    public enum SandboxEditorRowKind
    {
        Warning,
        Title,
        Status,
        Copy,
        Field,
        Area,
        Rule,
        Heading,
        Note,
        Checkbox,
        Actions,
    }

    // The sandbox editor owns its row vocabulary because its scroll body is made from the
    // same rows it paints. Measurement only places rows; Paint is called later by the page.
    public sealed class SandboxEditorLayout
    {
        public sealed class Row
        {
            public readonly SandboxEditorRowKind Kind;
            public readonly float Height;
            public readonly bool Visible;
            public readonly Action<UiLayoutRect> Paint;
            public UiLayoutRect Bounds { get; internal set; }

            public Row(SandboxEditorRowKind kind, float height, bool visible,
                       Action<UiLayoutRect> paint)
            {
                Kind = kind;
                Height = Math.Max(0f, height);
                Visible = visible;
                Paint = paint;
            }
        }

        readonly List<Row> _rows;

        public IList<Row> Rows => _rows.AsReadOnly();
        public float ContentHeight { get; }

        SandboxEditorLayout(List<Row> rows, float contentHeight)
        {
            _rows = rows;
            ContentHeight = contentHeight;
        }

        public static SandboxEditorLayout Measure(float width, IEnumerable<Row> rows)
        {
            var placed = new List<Row>();
            float safeWidth = Math.Max(0f, width);
            float y = 0f;
            foreach (var row in rows ?? Array.Empty<Row>())
            {
                if (row == null) continue;
                row.Bounds = new UiLayoutRect(0f, y, safeWidth,
                    row.Visible ? row.Height : 0f);
                if (row.Visible) y += row.Height;
                placed.Add(row);
            }
            return new SandboxEditorLayout(placed, y);
        }

        public static bool OptionalVisible(bool editable, string text) =>
            editable || !string.IsNullOrWhiteSpace(text);

        // Callers supply text measurement so this geometry stays testable without Unity's
        // font stack. The empty sample matches UiText's wrapped-label measurement contract.
        public static float WrappedAreaHeight(float width, string text, float minimum,
                                              float fieldPadX, float fieldPadY,
                                              Func<string, float, float> measureText)
        {
            float textWidth = width - Math.Max(0f, fieldPadX) * 2f;
            string sample = string.IsNullOrEmpty(text) ? " " : text;
            float measured = Math.Max(0f, measureText(sample, textWidth));
            return Math.Max(Math.Max(0f, minimum), measured + Math.Max(0f, fieldPadY) * 4f);
        }
    }
}
