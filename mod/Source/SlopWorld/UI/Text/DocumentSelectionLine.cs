using System.Collections.Generic;

namespace SlopWorld
{
    // Logical order controls copying; positions control hit testing, including table cells.
    class DocumentSelectionLine
    {
        public int LogicalIndex;
        public float X, Y, Height, Width;
        public string Text;
        public readonly List<float> Edges = new List<float>();
        // Null preserves a producer's UTF-16 edges; plain text supplies Unicode element boundaries.
        public int[] Boundaries;
    }
}
