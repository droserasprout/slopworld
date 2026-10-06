using UnityEngine;

namespace SlopWorld
{
    // Task cards retain source offsets across wrapping. Shared gestures operate on visual
    // lines; this adapter preserves the original text, including gaps between cards.
    sealed class TaskTextSelection : DocumentSelection<TaskTextSelection.Line>
    {
        internal sealed class Line : DocumentSelectionLine { public int Start, End; }
        string _source = "";
        protected override bool PublishPrimary => false;
        protected override bool CopyOnSelectAll => false;
        protected override bool CanPaste => false;
        protected override bool ExtendRequiresSelection => false;
        protected override float MinimumHighlightWidth => 1f;
        protected override void EnsureEdges(Line line) { }
        public int AnchorOffset => Offset(_selectionStart);
        public int FocusOffset => Offset(_selectionEnd);
        int Offset(Vector2Int point) => _lines.Count == 0 ? 0 :
            _lines[Mathf.Clamp(point.y, 0, _lines.Count - 1)].Start + point.x;

        protected override string CopyRange(Vector2Int start, Vector2Int end)
        {
            int first = Offset(start), last = Offset(end);
            return first < 0 || last > _source.Length || last <= first ? "" : _source.Substring(first, last - first);
        }

        public void Rebuilt(string source, int anchor, int focus)
        {
            _source = source;
            for (int i = 0; i < _lines.Count; i++) _lines[i].LogicalIndex = i;
            Reindex();
            if (anchor > source.Length || focus > source.Length) Clear();
            else RestoreRange(Point(anchor), Point(focus));
        }

        Vector2Int Point(int offset)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (offset <= _lines[i].End) return new Vector2Int(Mathf.Max(0, offset - _lines[i].Start), i);
            return _lines.Count == 0 ? Vector2Int.zero : new Vector2Int(_lines[_lines.Count - 1].Text.Length, _lines.Count - 1);
        }

        protected override int FirstHighlightLine(int selected, float top) => Mathf.Max(selected, FirstVisibleLine(top));

        public int FirstVisibleLine(float top)
        {
            int low = 0;
            int high = _lines.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var line = _lines[middle];
                if (line.Y + line.Height <= top) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        protected override int NearestLine(float x, float y)
        {
            int insertion = FirstVisibleLine(y);
            int first = Mathf.Max(0, insertion - 1);
            int last = Mathf.Min(_lines.Count - 1, insertion);
            int lineIndex = first;
            float bestVertical = float.MaxValue, bestHorizontal = float.MaxValue;
            for (int i = first; i <= last; i++)
            {
                var line = _lines[i];
                float vertical = y < line.Y ? line.Y - y :
                    y > line.Y + line.Height ? y - (line.Y + line.Height) : 0f;
                float left = line.X;
                float right = line.X + line.Width;
                float horizontal = x < left ? left - x : x > right ? x - right : 0f;
                if (vertical < bestVertical || (vertical == bestVertical && horizontal < bestHorizontal))
                {
                    bestVertical = vertical;
                    bestHorizontal = horizontal;
                    lineIndex = i;
                }
            }

            return lineIndex;
        }
    }
}
