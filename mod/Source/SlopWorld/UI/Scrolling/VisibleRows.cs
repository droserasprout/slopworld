using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Half-open row intervals keep drawing and hit testing on the same viewport boundaries.
    internal static class VisibleRows
    {
        public static bool Intersects(float rowTop, float rowHeight, float top, float height) =>
            rowHeight > 0f && height > 0f && rowTop + rowHeight > top && rowTop < top + height;

        // Ends must be nondecreasing exclusive row ends. Returns Count if none ends after top.
        public static int First(IList<float> ends, float top)
        {
            int lo = 0, hi = ends.Count;
            while (lo < hi)
            {
                int mid = lo + (hi - lo) / 2;
                if (ends[mid] <= top) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        public static void Uniform(int count, float pitch, float top, float height,
                                   out int first, out int end)
        {
            first = end = 0;
            if (count <= 0 || pitch <= 0f || height <= 0f) return;
            first = (int)Math.Min(count, Math.Max(0, Math.Floor(top / pitch)));
            end = (int)Math.Min(count, Math.Max(first, Math.Ceiling((top + height) / pitch)));
        }
    }
}
