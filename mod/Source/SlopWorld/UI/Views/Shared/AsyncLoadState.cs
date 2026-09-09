using System;

namespace SlopWorld
{
    // Request state owns waiting/error/retry bookkeeping; the request delegate performs the
    // endpoint-specific work, so rendering never starts a network call.
    public sealed class AsyncLoadState<T>
    {
        public T Value { get; private set; }
        public bool HasValue { get; private set; }
        public bool Loading { get; private set; }
        public string Error { get; private set; }

        public void Load(Action<Action<T>, Action<string>> request,
                         Action<T> loaded = null)
        {
            Loading = true;
            Error = null;
            request(value =>
            {
                Value = value;
                HasValue = true;
                Loading = false;
                Error = null;
                loaded?.Invoke(value);
            }, error =>
            {
                Loading = false;
                HasValue = false;
                Error = error;
            });
        }

        public void SetError(string error)
        {
            Loading = false;
            Error = error;
        }
    }
}
