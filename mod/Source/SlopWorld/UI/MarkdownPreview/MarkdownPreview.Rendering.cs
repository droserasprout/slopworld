using UnityEngine;
using Verse;

namespace SlopWorld
{
    // MarkdownPreview rendering, selection collection, and hit testing.
    public sealed partial class MarkdownPreview
    {
        void DrawPlacementBackground(Placement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Text:
                case PlacementKind.Bullet:
                    DrawInlineCodeBackgrounds(placement.Text, placement.X, placement.Y);
                    break;

                case PlacementKind.Code:
                    Slab.Box(new Rect(placement.X, placement.Y, placement.Width, placement.Height),
                        SlopWidgets.Well, SlopWidgets.Edge);
                    DrawInlineCodeBackgrounds(placement.Text, placement.X + SlopWidgets.GapS,
                        placement.Y + SlopWidgets.GapS +
                        (string.IsNullOrWhiteSpace(placement.Label)
                            ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS));
                    break;

                case PlacementKind.Rule:
                    Slab.Hairline(new Rect(placement.X, placement.Y,
                        placement.Width, placement.Height), SlopWidgets.Edge);
                    break;

                case PlacementKind.Quote:
                    Slab.Fill(new Rect(placement.X, placement.Y, 3f, placement.Height),
                        SlopWidgets.Accent);
                    break;

                case PlacementKind.Table:
                    DrawTableBackground(placement);
                    DrawTableInlineCodeBackgrounds(placement);
                    break;
            }
        }

        void DrawInlineCodeBackgrounds(TextLayout text, float x, float y)
        {
            if (text == null) return;
            foreach (var line in text.Lines)
            {
                float at = x;
                foreach (var piece in line.Pieces)
                {
                    if (piece.Run.Code)
                        Slab.Fill(new Rect(at, y, piece.Width, line.Height).ContractedBy(1f),
                            SlopWidgets.RowBg);
                    at += piece.Width;
                }
                y += line.Height;
            }
        }

        void DrawTableInlineCodeBackgrounds(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    DrawInlineCodeBackgrounds(row.Cells[i], x + SlopWidgets.GapS,
                        y + SlopWidgets.GapS);
                    x += placement.Table.Widths[i];
                }
                y += row.Height;
            }
        }

        void DrawPlacementForeground(Placement placement)
        {
            switch (placement.Kind)
            {
                case PlacementKind.Text:
                case PlacementKind.Bullet:
                    DrawText(placement.Text, placement.X, placement.Y, placement.Heading);
                    break;

                case PlacementKind.Image:
                    DrawImage(placement);
                    break;

                case PlacementKind.Code:
                    if (!string.IsNullOrEmpty(placement.Label))
                    {
                        var label = placement.Label.Trim();
                        if (label.Length > 0)
                        {
                            GUI.color = SlopWidgets.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(placement.X + SlopWidgets.GapS,
                                placement.Y + SlopWidgets.GapS, placement.Width, SlopWidgets.TinyH),
                                label);
                            GUI.color = Color.white;
                        }
                    }
                    DrawText(placement.Text, placement.X + SlopWidgets.GapS,
                        placement.Y + SlopWidgets.GapS +
                        (string.IsNullOrWhiteSpace(placement.Label)
                            ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS), false);
                    break;

                case PlacementKind.Table:
                    DrawTableText(placement);
                    break;
            }
        }

        void CollectSelection()
        {
            _selectionLines.Clear();
            foreach (var placement in _placements)
            {
                switch (placement.Kind)
                {
                    case PlacementKind.Text:
                    case PlacementKind.Bullet:
                        CollectText(placement.Text, placement.X, placement.Y);
                        break;

                    case PlacementKind.Code:
                        CollectText(placement.Text, placement.X + SlopWidgets.GapS,
                            placement.Y + SlopWidgets.GapS +
                            (string.IsNullOrWhiteSpace(placement.Label)
                                ? 0f : SlopWidgets.TinyH + SlopWidgets.GapXS));
                        break;

                    case PlacementKind.Table:
                        CollectTableSelection(placement);
                        break;
                }
            }
        }

        void CollectText(TextLayout text, float x, float y)
        {
            if (text == null) return;

            foreach (var line in text.Lines)
            {
                CollectTextLine(line, x, y);
                y += line.Height;
            }
        }

        void CollectTextLine(TextLine line, float x, float y)
        {
            var output = new SelectionLine
            {
                X = x,
                Y = y,
                Height = line.Height,
                Text = "",
            };
            output.Edges.Add(0f);
            float at = x;
            var chars = new System.Text.StringBuilder();
            foreach (var piece in line.Pieces)
            {
                if (piece.Run.IsImage)
                {
                    at += piece.Width;
                    continue;
                }
                for (int i = 0; i < piece.Text.Length; i++)
                {
                    chars.Append(piece.Text[i]);
                    at += Measure(piece.Style, piece.Text[i].ToString());
                    output.Edges.Add(at - x);
                }
            }
            output.Text = chars.ToString();
            _selectionLines.Add(output);
        }

        void CollectTableSelection(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                int lines = 0;
                foreach (var cell in row.Cells) lines = Mathf.Max(lines, cell.Lines.Count);
                for (int lineIndex = 0; lineIndex < lines; lineIndex++)
                {
                    float x = placement.X;
                    for (int i = 0; i < row.Cells.Count; i++)
                    {
                        var cell = row.Cells[i];
                        if (lineIndex < cell.Lines.Count)
                        {
                            float lineY = y + SlopWidgets.GapS;
                            for (int j = 0; j < lineIndex; j++)
                                lineY += cell.Lines[j].Height;
                            CollectTextLine(cell.Lines[lineIndex], x + SlopWidgets.GapS, lineY);
                        }
                        x += placement.Table.Widths[i];
                    }
                }
                y += row.Height;
            }
        }

        void DrawSelectionHighlights()
        {
            if (!_hasSel || _selectionLines.Count == 0) return;
            OrderedSelection(out var a, out var b);

            int first = Mathf.Clamp(a.y, 0, _selectionLines.Count - 1);
            int last = Mathf.Clamp(b.y, 0, _selectionLines.Count - 1);
            for (int i = first; i <= last; i++)
            {
                var line = _selectionLines[i];
                int start = i == a.y ? a.x : 0;
                int end = i == b.y ? b.x : line.Text.Length;
                start = Mathf.Clamp(start, 0, line.Text.Length);
                end = Mathf.Clamp(end, start, line.Text.Length);
                if (end <= start) continue;

                float left = line.X + line.Edges[start];
                float right = line.X + line.Edges[end];
                Slab.Fill(new Rect(left, line.Y, right - left, line.Height), SlopWidgets.Sel);
            }
        }

        void DrawText(TextLayout text, float x, float y, bool heading)
        {
            foreach (var line in text.Lines)
            {
                float at = x;
                foreach (var piece in line.Pieces)
                {
                    var rect = new Rect(at, y, piece.Width, line.Height);
                    if (piece.Run.IsImage)
                    {
                        var texture = ImageFor(piece.Run);
                        if (texture != null)
                        {
                            GUI.color = Color.white;
                            GUI.DrawTexture(new Rect(at, y, piece.Width, piece.Height), texture,
                                ScaleMode.ScaleToFit, true);
                        }
                        else
                        {
                            Slab.Box(new Rect(at, y, piece.Width, piece.Height),
                                SlopWidgets.Well, SlopWidgets.Edge);
                            GUI.color = SlopWidgets.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(at + SlopWidgets.GapXS, y,
                                Mathf.Max(1f, piece.Width - SlopWidgets.GapXS * 2f), piece.Height),
                                piece.Run.ImageFailed ? "image unavailable" : "image loading…");
                            GUI.color = Color.white;
                        }
                        at += piece.Width;
                        continue;
                    }

                    var old = GUI.color;
                    GUI.color = piece.Run.Link != null || piece.Run.LocalLink != null
                        ? SlopWidgets.Accent
                        : piece.Run.Faint ? SlopWidgets.Dim
                        : piece.Run.Code ? SlopWidgets.Lead
                        : heading ? SlopWidgets.Lead : SlopWidgets.Name;
                    GUI.Label(rect, piece.Text, piece.Style);
                    if (piece.Run.Link != null || piece.Run.LocalLink != null)
                    {
                        Slab.Hairline(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                            SlopWidgets.Accent);
                        _links.Add(new LinkHit(rect, piece.Run.Link, piece.Run.LocalLink));
                    }
                    if (piece.Run.Strike)
                        Slab.Hairline(new Rect(rect.x, rect.y + line.Height * .55f,
                            rect.width, 1f), SlopWidgets.Dim);
                    GUI.color = old;
                    at += piece.Width;
                }
                y += line.Height;
            }
        }

        void DrawImage(Placement placement)
        {
            var texture = ImageFor(placement.Image);
            var rect = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
            if (texture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                return;
            }

            Slab.Box(rect, SlopWidgets.Well, SlopWidgets.Edge);
            GUI.color = SlopWidgets.Dim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, placement.Image.ImageFailed ? "image unavailable" : "image loading…");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        void DrawTableBackground(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                if (row.Header) Slab.Fill(new Rect(x, y, placement.Width, row.Height), SlopWidgets.RowBg);
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    Slab.Hairline(new Rect(x, y, 1f, row.Height), SlopWidgets.Edge);
                    x += placement.Table.Widths[i];
                }
                Slab.Hairline(new Rect(placement.X, y + row.Height - 1f,
                    placement.Width, 1f), SlopWidgets.Edge);
                y += row.Height;
            }
        }

        void DrawTableText(Placement placement)
        {
            float y = placement.Y;
            foreach (var row in placement.Table.Rows)
            {
                float x = placement.X;
                for (int i = 0; i < row.Cells.Count; i++)
                {
                    DrawText(row.Cells[i], x + SlopWidgets.GapS, y + SlopWidgets.GapS,
                        row.Header);
                    x += placement.Table.Widths[i];
                }
                y += row.Height;
            }
        }

    }
}
