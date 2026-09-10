namespace SlopWorld
{
    // A token identifies one logical operation. Starting or invalidating an operation makes
    // every callback carrying an older token inert without requiring request cancellation.
    public sealed class OperationGate
    {
        int _generation;

        public int Begin()
        {
            unchecked { return ++_generation; }
        }

        public bool IsCurrent(int generation) => generation != 0 && generation == _generation;

        public void Invalidate()
        {
            unchecked { _generation++; }
        }
    }
}
