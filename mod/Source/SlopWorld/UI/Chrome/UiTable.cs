using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Compact settings table chrome shared by Usage and other daemon-backed pages. Columns
    // with Flexible set consume the space left by fixed-width columns; when the table is too
    // narrow, fixed columns shrink together so every cell still ends at the table edge.
    public static class UiTable
    {
        public sealed class Column
        {
            public readonly string Label;
            public readonly float Width;
            public readonly bool Flexible;
            public readonly TextAnchor Anchor;
            public readonly float LeftPad;

            public Column(string label, float width, bool flexible = false,
                          TextAnchor anchor = TextAnchor.MiddleLeft, float leftPad = 0f)
            {
                Label = label;
                Width = width;
                Flexible = flexible;
                Anchor = anchor;
                LeftPad = leftPad;
            }
        }

        public static float Draw<T>(Rect rect, IList<T> rows, float rowH, IList<Column> columns,
                                    Action<T, Rect, Rect[]> drawRow)
        {
            if (columns == null || columns.Count == 0) return UiTheme.GapS;

            float fixedW = 0f;
            int flexible = 0;
            foreach (var column in columns)
            {
                if (column.Flexible) flexible++;
                else fixedW += Mathf.Max(0f, column.Width);
            }

            float availableW = Mathf.Max(0f, rect.width);
            float scale = fixedW > availableW && fixedW > 0f
                ? availableW / fixedW
                : 1f;
            float remaining = Mathf.Max(0f, availableW - fixedW * scale);
            float flexibleW = flexible > 0 ? remaining / flexible : 0f;

            var widths = new float[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                widths[i] = column.Flexible
                    ? flexibleW
                    : Mathf.Max(0f, column.Width) * scale;
            }

            var header = new Rect(rect.x, rect.y, rect.width, rowH);
            Slab.Fill(header, UiTheme.RowBg);
            float x = rect.x;
            for (int i = 0; i < columns.Count; i++)
            {
                var column = columns[i];
                var cell = new Rect(x, header.y, widths[i], header.height);
                float pad = Mathf.Min(column.LeftPad, cell.width);
                UiText.RowLabel(new Rect(cell.x + pad, cell.y,
                    Mathf.Max(0f, cell.width - pad), cell.height), column.Label, column.Anchor);
                x += widths[i];
            }
            Slab.Hairline(new Rect(rect.x, header.yMax - 1f, rect.width, 1f), UiTheme.Edge);

            float y = header.yMax;
            rows = rows ?? new List<T>();
            foreach (var item in rows)
            {
                var row = new Rect(rect.x, y, rect.width, rowH);
                RowChrome.Hover(row, false, true, RowHoverPolicy.OverlayAware);

                var cells = new Rect[columns.Count];
                x = rect.x;
                for (int i = 0; i < columns.Count; i++)
                {
                    cells[i] = new Rect(x, row.y, widths[i], row.height);
                    x += widths[i];
                }
                drawRow?.Invoke(item, row, cells);

                Slab.Hairline(new Rect(row.x, row.yMax - 1f, row.width, 1f), UiTheme.Edge);
                y = row.yMax;
            }
            return y - rect.y + UiTheme.GapS;
        }
    }
}
