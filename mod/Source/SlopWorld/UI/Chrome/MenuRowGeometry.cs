using System;

namespace SlopWorld
{
    // Row starts are shared by hit testing, keyboard reveal and drawing. A menu may have
    // short separator rows, so dividing the offset by a uniform height is not enough.
    internal sealed class MenuRowGeometry
    {
        float[] _tops = { 0f };

        public int Count => _tops.Length - 1;
        public float Height => _tops[Count];

        // Heights must be finite and nonnegative so cumulative row offsets stay ordered.
        public void Build(int count, Func<int, float> height)
        {
            _tops = new float[count + 1];
            for (int i = 0; i < count; i++)
                _tops[i + 1] = _tops[i] + height(i);
        }

        public float Top(int index) => _tops[index];
        public float RowHeight(int index) => _tops[index + 1] - _tops[index];

        // The first row whose end is beyond y. A boundary belongs to the next row.
        public int At(float y)
        {
            if (y < 0f || y >= Height) return -1;
            return FirstEndingAfter(y);
        }

        public void Visible(float top, float height, out int firstVisible, out int endExclusive)
        {
            firstVisible = top + height <= 0f ? 0 : FirstEndingAfter(top);
            endExclusive = FirstStartingAtOrAfter(top + height);
            if (endExclusive < firstVisible) endExclusive = firstVisible;
        }

        int FirstEndingAfter(float y)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_tops[mid + 1] <= y) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        int FirstStartingAtOrAfter(float y)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_tops[mid] < y) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }
    }
}
