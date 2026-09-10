using System;

namespace SlopWorld
{
    // Request state owns waiting/error/retry bookkeeping; the request delegate performs the
    // endpoint-specific work, so rendering never starts a network call.
    public sealed class AsyncLoadState<T>
    {
        readonly OperationGate _operations = new OperationGate();

        public T Value { get; private set; }
        public bool HasValue { get; private set; }
        public bool Loading { get; private set; }
        public string Error { get; private set; }

        public void Load(Action<Action<T>, Action<string>> request,
                         Action<T> loaded = null)
        {
            int generation = _operations.Begin();
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

        public void SetError(string error)
        {
            Loading = false;
            Error = error;
        }
    }
}
