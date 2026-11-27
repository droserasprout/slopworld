using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Horizontal panning is shared. Each column owns its vertical offset so an
    // empty destination always exposes its insertion line, even beside a long list.
    internal sealed class BoardViewport
    {
        internal const float Header = 40,
            Row = 62,
            Gap = 8;
        internal float Left { get; private set; }
        readonly Dictionary<string, float> _tops = new Dictionary<string, float>();

        internal void Reset()
        {
            Left = 0;
            _tops.Clear();
        }

        internal float Top(Wire.BoardColumn column, float height)
        {
            if (column == null || !_tops.TryGetValue(column.Id, out var top))
                return 0;
            return Math.Clamp(top, 0, VerticalLimit(column, height));
        }

        static float VerticalLimit(Wire.BoardColumn column, float height) =>
            Math.Max(0, Header + column.Cards.Count * Row + Gap - height);

        internal bool Scroll(float dx, float dy, float limit, Wire.BoardColumn column, float height)
        {
            float start = Left;
            Left = Math.Clamp(Left + dx, 0, limit);
            if (column == null || dy == 0)
                return start != Left;
            float old = Top(column, height);
            float next = Math.Clamp(old + dy, 0, VerticalLimit(column, height));
            _tops[column.Id] = next;
            return start != Left || old != next;
        }

        internal int Insertion(Wire.BoardColumn column, float height, float y) =>
            Math.Clamp(
                (int)Math.Floor((y - Header + Top(column, height) + Row / 2) / Row),
                0,
                column.Cards.Count
            );
    }
}
