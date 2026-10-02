namespace SlopWorld
{
    // A token identifies one logical operation. Starting or invalidating an operation makes
    // every callback carrying an older token inert without requiring request cancellation.
    // Callers must serialize access on the UI/main thread. Zero is reserved.
    public sealed class OperationGate
    {
        long _generation;

        public long Begin()
        {
            unchecked { if (++_generation == 0) ++_generation; }
            return _generation;
        }

        public bool IsCurrent(long generation) => generation != 0 && generation == _generation;

        public void Invalidate()
        {
            Begin();
        }
    }
}
