using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed class MarkdownRenderer
    {
        readonly MarkdownResourceStore _resources;
        readonly List<LinkHit> _links = new List<LinkHit>();
        float _clipTop;
        float _clipBottom;

        public MarkdownRenderer(MarkdownResourceStore resources)
        {
            _resources = resources;
        }

        public List<LinkHit> Links => _links;

        public void ClearLinks()
        {
            _links.Clear();
        }

        public void Draw(List<Placement> placements, MarkdownSelection selection,
                         float clipTop, float clipBottom)
        {
            _links.Clear();
            _clipTop = clipTop;
            _clipBottom = clipBottom;
            int first = FirstVisiblePlacement(placements, _clipTop);
            for (int i = first; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (placement.Y >= _clipBottom) break;
                DrawPlacementBackground(placement);
            }
            selection.DrawHighlights(_clipTop, _clipBottom);
            for (int i = first; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (placement.Y >= _clipBottom) break;
                DrawPlacementForeground(placement);
            }
            _clipTop = _clipBottom = 0f;
        }

        static int FirstVisiblePlacement(List<Placement> placements, float top)
        {
            int low = 0;
            int high = placements.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var placement = placements[middle];
                if (placement.Y + placement.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        static int FirstVisibleLine(TextLayout text, float y, float top)
        {
            int low = 0;
            int high = text.Lines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var line = text.Lines[middle];
                if (y + line.Offset + line.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        static int FirstVisibleRow(TableLayout table, float y, float top)
        {
            int low = 0;
            int high = table.Rows.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var row = table.Rows[middle];
                if (y + row.Offset + row.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

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
                        TerminalTheme.Current.Bg, UiTheme.Edge);
                    break;

                case PlacementKind.Rule:
                    Slab.Hairline(new Rect(placement.X, placement.Y,
                        placement.Width, placement.Height), UiTheme.Edge);
                    break;

                case PlacementKind.Quote:
                    Slab.Fill(new Rect(placement.X, placement.Y, 3f, placement.Height),
                        UiTheme.Accent);
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
            int first = FirstVisibleLine(text, y, _clipTop);
            for (int i = first; i < text.Lines.Count; i++)
            {
                var line = text.Lines[i];
                float lineY = y + line.Offset;
                if (lineY >= _clipBottom) break;

                float at = x;
                foreach (var piece in line.Pieces)
                {
                    if (piece.Run.Code)
                        Slab.Fill(new Rect(at, lineY, piece.Width, line.Height).ContractedBy(1f),
                            UiTheme.RowBg);
                    at += piece.Width;
                }
            }
        }

        void DrawTableInlineCodeBackgrounds(Placement placement)
        {
            int first = FirstVisibleRow(placement.Table, placement.Y, _clipTop);
            for (int rowIndex = first; rowIndex < placement.Table.Rows.Count; rowIndex++)
            {
                var row = placement.Table.Rows[rowIndex];
                float y = placement.Y + row.Offset;
                if (y >= _clipBottom) break;

                float x = placement.X;
                for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    DrawInlineCodeBackgrounds(row.Cells[cellIndex], x + UiTheme.GapS,
                        y + UiTheme.GapS);
                    x += placement.Table.Widths[cellIndex];
                }
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
                            GUI.color = UiTheme.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(placement.X + UiTheme.GapS,
                                placement.Y + UiTheme.GapS, placement.Width, UiTheme.TinyH),
                                label);
                            GUI.color = Color.white;
                        }
                    }
                    DrawText(placement.Text, placement.X + UiTheme.GapS,
                        placement.Y + UiTheme.GapS +
                        (string.IsNullOrWhiteSpace(placement.Label)
                            ? 0f : UiTheme.TinyH + UiTheme.GapXS), false);
                    break;

                case PlacementKind.Table:
                    DrawTableText(placement);
                    break;
            }
        }

        void DrawText(TextLayout text, float x, float y, bool heading)
        {
            int first = FirstVisibleLine(text, y, _clipTop);
            for (int i = first; i < text.Lines.Count; i++)
            {
                var line = text.Lines[i];
                float lineY = y + line.Offset;
                if (lineY >= _clipBottom) break;

                float at = x;
                foreach (var piece in line.Pieces)
                {
                    var rect = new Rect(at, lineY, piece.Width, line.Height);
                    if (piece.Run.IsImage)
                    {
                        var texture = _resources.ImageFor(piece.Run);
                        if (texture != null)
                        {
                            GUI.color = Color.white;
                            GUI.DrawTexture(new Rect(at, lineY, piece.Width, piece.Height), texture,
                                ScaleMode.ScaleToFit, true);
                        }
                        else
                        {
                            Slab.Box(new Rect(at, lineY, piece.Width, piece.Height),
                                UiTheme.Well, UiTheme.Edge);
                            GUI.color = UiTheme.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(at + UiTheme.GapXS, lineY,
                                Mathf.Max(1f, piece.Width - UiTheme.GapXS * 2f), piece.Height),
                                piece.Run.ImageFailed ? "image unavailable" : "image loading…");
                            GUI.color = Color.white;
                        }
                        at += piece.Width;
                        continue;
                    }
                    if (piece.Run.IsTask)
                    {
                        UiControls.TickBox(rect, piece.Run.TaskChecked);
                        at += piece.Width;
                        continue;
                    }

                    var old = GUI.color;
                    GUI.color = piece.Run.Link != null || piece.Run.LocalLink != null
                        ? UiTheme.Accent
                        : piece.Run.Faint ? UiTheme.Dim
                        : piece.Run.Code && piece.Run.HasColor ? piece.Run.Color
                        : piece.Run.Code ? UiTheme.Lead
                        : heading ? UiTheme.Lead : UiTheme.Name;
                    GUI.Label(rect, piece.Text, piece.Style);
                    if (piece.Run.Link != null || piece.Run.LocalLink != null)
                    {
                        Slab.Hairline(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f),
                            UiTheme.Accent);
                        _links.Add(new LinkHit(rect, piece.Run.Link, piece.Run.LocalLink));
                    }
                    if (piece.Run.Strike)
                        Slab.Hairline(new Rect(rect.x, rect.y + line.Height * .55f,
                            rect.width, 1f), UiTheme.Dim);
                    GUI.color = old;
                    at += piece.Width;
                }
            }
        }

        void DrawImage(Placement placement)
        {
            var texture = _resources.ImageFor(placement.Image);
            var rect = new Rect(placement.X, placement.Y, placement.Width, placement.Height);
            if (texture != null)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true);
                return;
            }

            Slab.Box(rect, UiTheme.Well, UiTheme.Edge);
            GUI.color = UiTheme.Dim;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, placement.Image.ImageFailed ? "image unavailable" : "image loading…");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        void DrawTableBackground(Placement placement)
        {
            int first = FirstVisibleRow(placement.Table, placement.Y, _clipTop);
            for (int rowIndex = first; rowIndex < placement.Table.Rows.Count; rowIndex++)
            {
                var row = placement.Table.Rows[rowIndex];
                float y = placement.Y + row.Offset;
                if (y >= _clipBottom) break;

                float x = placement.X;
                if (row.Header) Slab.Fill(new Rect(x, y, placement.Width, row.Height),
                    UiTheme.RowBg);
                for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    Slab.Hairline(new Rect(x, y, 1f, row.Height), UiTheme.Edge);
                    x += placement.Table.Widths[cellIndex];
                }
                Slab.Hairline(new Rect(placement.X, y + row.Height - 1f,
                    placement.Width, 1f), UiTheme.Edge);
            }
        }

        void DrawTableText(Placement placement)
        {
            int first = FirstVisibleRow(placement.Table, placement.Y, _clipTop);
            for (int rowIndex = first; rowIndex < placement.Table.Rows.Count; rowIndex++)
            {
                var row = placement.Table.Rows[rowIndex];
                float y = placement.Y + row.Offset;
                if (y >= _clipBottom) break;

                float x = placement.X;
                for (int cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
                {
                    DrawText(row.Cells[cellIndex], x + UiTheme.GapS,
                        y + UiTheme.GapS, row.Header);
                    x += placement.Table.Widths[cellIndex];
                }
            }
        }
    }
}
