using System.Collections.Generic;

namespace SlopWorld
{
    // Logical order controls copying; positions control hit testing, including table cells.
    class DocumentSelectionLine
    {
        public int LogicalIndex;
        public float X,
            Y,
            Height,
            Width;
        public string Text;
        public readonly List<float> Edges = new List<float>();

        // Null preserves a producer's UTF-16 edges; plain text supplies Unicode element boundaries.
        public int[] Boundaries;

        // Distance to the line rectangle; selection owners choose candidates and ranking.
        public void DistanceTo(float x, float y, out float vertical, out float horizontal)
        {
            vertical =
                y < Y ? Y - y
                : y > Y + Height ? y - (Y + Height)
                : 0f;
            float left = X;
            float right = X + Width;
            horizontal =
                x < left ? left - x
                : x > right ? x - right
                : 0f;
        }
    }
}
