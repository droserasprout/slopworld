namespace SlopWorld
{
    // A layout pass can precede input arrival. Allow one wheel-triggered refresh, but
    // never turn a wheel backlog into an unbounded series of background sample requests.
    internal sealed class ScrollSampleBudget
    {
        int _frame = -1;
        bool _refreshed;

        public bool Take(int frame, bool refresh)
        {
            if (_frame != frame)
            {
                _frame = frame;
                _refreshed = refresh;
                return true;
            }
            if (!refresh || _refreshed) return false;
            _refreshed = true;
            return true;
        }
    }
}
