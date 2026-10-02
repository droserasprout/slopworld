using System;

namespace SlopWorld
{
    // Request state owns waiting/error/retry bookkeeping. The request delegate performs the
    // endpoint-specific work, so rendering never starts a network call. Callers retain
    // action errors separately so load completion cannot erase a newer mutation failure.
    public sealed class AsyncLoadState<T>
    {
        readonly OperationGate _operations = new OperationGate();

        // Value retains the last success after failure; HasValue marks its availability.
        public T Value { get; private set; }
        public bool HasValue { get; private set; }
        public bool Loading { get; private set; }
        public string Error { get; private set; }

        public void Load(Action<Action<T>, Action<string>> request,
                         Action<T> loaded = null)
        {
            long generation = _operations.Begin();
            Loading = true;
            Error = null;
            request(value =>
            {
                if (!_operations.IsCurrent(generation)) return;
                Value = value;
                HasValue = true;
                Loading = false;
                Error = null;
                loaded?.Invoke(value);
            }, error =>
            {
                if (!_operations.IsCurrent(generation)) return;
                Loading = false;
                HasValue = false;
                Error = error;
            });
        }

        public void Invalidate()
        {
            _operations.Invalidate();
            Loading = false;
        }
    }
}
