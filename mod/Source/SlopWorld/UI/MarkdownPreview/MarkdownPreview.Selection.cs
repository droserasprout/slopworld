using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Translate Markdown placements and styled pieces into shared document selection.
    sealed class MarkdownSelection : DocumentSelection<SelectionLine>
    {
        StyleSet _styles;
        public void AttachStyles(StyleSet styles) { _styles = styles; }
        protected override string CopyRange(Vector2Int start, Vector2Int end) =>
            MarkdownSelectionText.CopyRange(_lines, start, end);

        public void Rebuild(List<Placement> placements)
        {
            // Layout changes rewrite visual line indices and character edges. A stale range
            // would otherwise select different text after a resize or resource reflow.
            Clear();
            _lines.Clear();
            foreach (var placement in placements)
            {
                switch (placement.Kind)
                {
                    case PlacementKind.Text:
                    case PlacementKind.Bullet:
                        MarkdownSelectionText.CollectText(_lines, placement.Text, placement.X, placement.Y);
                        break;

                    case PlacementKind.Code:
                        MarkdownSelectionText.CollectText(_lines, placement.Text, placement.X + UiTheme.GapS,
                            placement.Y + UiTheme.GapS +
                            (string.IsNullOrWhiteSpace(placement.Label)
                                ? 0f : UiTheme.TinyH + UiTheme.GapXS));
                        break;

                    case PlacementKind.Table:
                        MarkdownSelectionText.CollectTable(_lines, placement);
                        break;
                }
            }
            Reindex();
        }

        protected override void EnsureEdges(SelectionLine line)
        {
            if (line == null || line.Edges.Count == line.Text.Length + 1) return;

            line.Edges.Clear();
            line.Edges.Add(0f);
            float at = 0f;
            foreach (var piece in line.Source.Pieces)
            {
                if (piece.Run.IsImage)
                {
                    at += piece.Width;
                    continue;
                }
                if (piece.Run.IsTask)
                {
                    float markerWidth = 0f;
                    for (int i = 0; i < piece.Text.Length; i++)
                        markerWidth += _styles.MeasureChar(piece.Style, piece.Text[i]);
                    float scale = markerWidth <= 0f ? 0f : piece.Width / markerWidth;
                    for (int i = 0; i < piece.Text.Length; i++)
                    {
                        at += _styles.MeasureChar(piece.Style, piece.Text[i]) * scale;
                        line.Edges.Add(at);
                    }
                    continue;
                }
                at += piece.PaddingLeft;
                for (int i = 0; i < piece.Text.Length; i++)
                {
                    at += _styles.MeasureChar(piece.Style, piece.Text[i]);
                    line.Edges.Add(at);
                }
                at += piece.PaddingRight;
            }
        }

    }
}
