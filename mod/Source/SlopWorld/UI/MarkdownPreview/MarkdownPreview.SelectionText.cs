using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SlopWorld
{
    static class MarkdownSelectionText
    {
        static bool Before(Vector2Int a, Vector2Int b) =>
            a.y < b.y || (a.y == b.y && a.x < b.x);

        public static string CopyRange(List<SelectionLine> lines, Vector2Int start,
                                       Vector2Int end)
        {
            if (lines == null || lines.Count == 0) return "";
            var a = start;
            var b = end;
            if (Before(b, a))
            {
                var temp = a;
                a = b;
                b = temp;
            }
            int first = Mathf.Clamp(a.y, 0, lines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, lines.Count - 1);
            var output = new StringBuilder();
            for (int i = first; i <= last; i++)
            {
                string text = lines[i].Text ?? "";
                int charStart = i == a.y ? a.x : 0;
                int charEnd = i == b.y ? b.x : text.Length;
                charStart = Mathf.Clamp(charStart, 0, text.Length);
                charEnd = Mathf.Clamp(charEnd, charStart, text.Length);
                if (charEnd > charStart)
                    output.Append(text.Substring(charStart, charEnd - charStart));
                if (i < last)
                {
                    output.Append(lines[i].Source.CopySuffix);
                    if (lines[i].CopyBreakAfter) output.Append('\n');
                }
            }
            return output.ToString();
        }

        public static void CollectText(List<SelectionLine> target, TextLayout text, float x, float y)
        {
            if (text == null) return;
            for (int i = 0; i < text.Lines.Count; i++)
                CollectTextLine(target, text.Lines[i], x, y, i + 1 == text.Lines.Count);
        }

        static void CollectTextLine(List<SelectionLine> target, TextLine line, float x, float y,
                                    bool copyBreakAfter)
        {
            var output = new SelectionLine
            {
                LogicalIndex = target.Count,
                X = x,
                Y = y + line.Offset,
                Height = line.Height,
                Width = line.Width,
                Text = "",
                Source = line,
                CopyBreakAfter = copyBreakAfter || line.BreakAfter == TextBreakKind.Source,
            };
            var chars = new StringBuilder();
            foreach (var piece in line.Pieces)
            {
                if (piece.Run.IsImage) continue;
                chars.Append(piece.Text);
            }
            output.Text = chars.ToString();
            target.Add(output);
        }

        public static void CollectTable(List<SelectionLine> target, Placement placement)
        {
            // Copy each cell in full; hit testing uses its own spatial order.
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    TextLayout text = row.Cells[i];
                    float cellWidth = placement.Table.Widths[i];
                    float padding = MarkdownTableGeometry.Padding(placement.Table.Widths[i]);
                    float innerWidth = MarkdownTableGeometry.InnerWidth(cellWidth);
                    TableAlignment alignment = placement.Table.Alignments != null &&
                        i < placement.Table.Alignments.Length
                        ? placement.Table.Alignments[i] : TableAlignment.Left;
                    for (int lineIndex = 0; lineIndex < text.Lines.Count; lineIndex++)
                    {
                        var line = text.Lines[lineIndex];
                        float lineX = MarkdownTableGeometry.AlignX(x + padding, innerWidth,
                            line.Width, alignment);
                        CollectTextLine(target, line, lineX,
                            placement.Y + row.Offset + padding,
                            lineIndex + 1 == text.Lines.Count);
                        target[target.Count - 1].Width = Mathf.Min(line.Width,
                            Mathf.Max(0f, x + cellWidth - lineX));
                    }
                    x += cellWidth;
                }
            }
        }
    }
}
