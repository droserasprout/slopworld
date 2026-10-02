using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    sealed class MarkdownRenderer
    {
        readonly MarkdownResourceStore _resources;
        readonly List<LinkHit> _links = new List<LinkHit>();
        readonly List<float> _prefixBottoms = new List<float>();
        int _layoutGeneration = -1;
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

        public void Draw(List<Placement> placements, int generation, MarkdownSelection selection,
                         float clipTop, float clipBottom)
        {
            _links.Clear();
            if (_layoutGeneration != generation)
            {
                BuildVisibilityIndex(placements);
                _layoutGeneration = generation;
            }
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

        void BuildVisibilityIndex(List<Placement> placements)
        {
            _prefixBottoms.Clear();
            float farthest = float.MinValue;
            foreach (var placement in placements ?? new List<Placement>())
            {
                farthest = Mathf.Max(farthest, placement.Y + placement.Height);
                _prefixBottoms.Add(farthest);
            }
        }

        int FirstVisiblePlacement(List<Placement> placements, float top)
        {
            int low = 0;
            int high = placements.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (_prefixBottoms[middle] <= top) low = middle + 1;
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

        void DrawInlineCodeBackgrounds(TextLayout text, float x, float y,
                                       float availableWidth = -1f,
                                       TableAlignment alignment = TableAlignment.Left)
        {
            if (text == null) return;
            int first = FirstVisibleLine(text, y, _clipTop);
            for (int i = first; i < text.Lines.Count; i++)
            {
                var line = text.Lines[i];
                float lineY = y + line.Offset;
                if (lineY >= _clipBottom) break;

                float at = availableWidth > 0f
                    ? MarkdownTableGeometry.AlignX(x, availableWidth, line.Width, alignment)
                    : x;
                foreach (var piece in line.Pieces)
                {
                    if (piece.Run.InlineCode)
                        Slab.Fill(new Rect(at, lineY + piece.OffsetY, piece.Width,
                                piece.Height).ContractedBy(1f),
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
                    DrawCell(placement.Table, row, cellIndex, x, y, true);
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
                            ? 0f : UiTheme.TinyH + UiTheme.GapXS), false, true);
                    break;

                case PlacementKind.Table:
                    DrawTableText(placement);
                    break;
            }
        }

        void DrawText(TextLayout text, float x, float y, bool heading,
                      bool codeBlock = false, float availableWidth = -1f,
                      TableAlignment alignment = TableAlignment.Left)
        {
            int first = FirstVisibleLine(text, y, _clipTop);
            for (int i = first; i < text.Lines.Count; i++)
            {
                var line = text.Lines[i];
                float lineY = y + line.Offset;
                if (lineY >= _clipBottom) break;

                float lineX = availableWidth > 0f
                    ? MarkdownTableGeometry.AlignX(x, availableWidth, line.Width, alignment)
                    : x;
                if (codeBlock && line.Continuation)
                    Slab.VHairline(new Rect(lineX - UiTheme.GapXS * .5f, lineY + 2f,
                        1f, Mathf.Max(1f, line.Height - 4f)), UiTheme.Edge);

                float at = lineX;
                foreach (var piece in line.Pieces)
                {
                    float pieceY = lineY + piece.OffsetY;
                    if (piece.Run.IsImage)
                    {
                        AddLink(new Rect(at, pieceY, piece.Width, piece.Height), piece.Run);
                        var texture = _resources.ImageFor(piece.Run);
                        if (texture != null)
                        {
                            GUI.color = Color.white;
                            GUI.DrawTexture(new Rect(at, pieceY, piece.Width, piece.Height), texture,
                                ScaleMode.ScaleToFit, true);
                        }
                        else
                        {
                            Slab.Box(new Rect(at, pieceY, piece.Width, piece.Height),
                                UiTheme.Well, UiTheme.Edge);
                            GUI.color = UiTheme.Dim;
                            Text.Font = GameFont.Tiny;
                            Widgets.Label(new Rect(at + UiTheme.GapXS, pieceY,
                                Mathf.Max(1f, piece.Width - UiTheme.GapXS * 2f), piece.Height),
                                piece.Run.ImageFailed ? "image unavailable" : "Loading image");
                            GUI.color = Color.white;
                        }
                        at += piece.Width;
                        continue;
                    }
                    if (piece.Run.IsTask)
                    {
                        UiControls.TickBox(new Rect(at, pieceY, piece.Width, piece.Height),
                            piece.Run.TaskChecked);
                        at += piece.Width;
                        continue;
                    }

                    float textX = at + piece.PaddingLeft;
                    float textWidth = Mathf.Max(1f,
                        piece.Width - piece.PaddingLeft - piece.PaddingRight);
                    var rect = new Rect(textX, pieceY, textWidth, piece.Height);
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
                        var linkRect = new Rect(at, pieceY, piece.Width, piece.Height);
                        Slab.Hairline(new Rect(linkRect.x, linkRect.yMax - 1f,
                                linkRect.width, 1f),
                            UiTheme.Accent);
                        _links.Add(new LinkHit(linkRect, piece.Run.Link, piece.Run.LocalLink));
                    }
                    if (piece.Run.Strike)
                        Slab.Hairline(new Rect(rect.x, rect.y + piece.Baseline * .65f,
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
            AddLink(rect, placement.Image);
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
            Widgets.Label(rect, placement.Image.ImageFailed ? "image unavailable" : "Loading image");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        void AddLink(Rect rect, InlineRun run)
        {
            if (run == null || (run.Link == null && run.LocalLink == null)) return;
            _links.Add(new LinkHit(rect, run.Link, run.LocalLink));
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

        void DrawCell(TableLayout table, TableRowLayout row, int index, float x, float y,
                      bool background)
        {
            float width = table.Widths[index];
            float padding = MarkdownTableGeometry.Padding(width);
            float top = _clipTop, bottom = _clipBottom;
            int firstLink = _links.Count;
            // A viewport narrower than one glyph cannot wrap further. Clip the whole cell
            // so glyphs, chips, images and task controls cannot paint over another column.
            GUI.BeginGroup(new Rect(x, y, width, row.Height));
            try
            {
                _clipTop -= y;
                _clipBottom -= y;
                if (background)
                    DrawInlineCodeBackgrounds(row.Cells[index], padding, padding,
                        MarkdownTableGeometry.InnerWidth(width), table.Alignments[index]);
                else
                    DrawText(row.Cells[index], padding, padding, row.Header, false,
                        MarkdownTableGeometry.InnerWidth(width), table.Alignments[index]);
            }
            finally
            {
                GUI.EndGroup();
                _clipTop = top;
                _clipBottom = bottom;
            }
            for (int i = _links.Count - 1; i >= firstLink; i--)
            {
                var link = _links[i];
                var rect = link.Rect;
                float left = Mathf.Clamp(rect.x, 0f, width);
                float right = Mathf.Clamp(rect.xMax, 0f, width);
                if (right <= left) _links.RemoveAt(i);
                else _links[i] = new LinkHit(new Rect(x + left, y + rect.y,
                    right - left, rect.height), link.Url, link.LocalPath);
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
                    DrawCell(placement.Table, row, cellIndex, x, y, false);
                    x += placement.Table.Widths[cellIndex];
                }
            }
        }
    }
}
