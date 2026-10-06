using System;

namespace SlopWorld
{
    // Publish names and paths before requesting recursive size accounting. One generation
    // owns both stages, so refresh/closure cannot publish an older scan over a newer list.
    public sealed class StorageLoadState<T>
    {
        readonly OperationGate _operations = new OperationGate();

        public T Value { get; private set; }
        public bool Loading { get; private set; }
        public bool SizesAvailable { get; private set; }
        public string Error { get; private set; }

        public void Load(Action<bool, Action<T>, Action<string>> request)
        {
            long generation = _operations.Begin();
            Loading = true;
            SizesAvailable = false;
            Error = null;

            void Failed(string error)
            {
                if (!_operations.IsCurrent(generation)) return;
                Loading = false;
                Error = error;
            }
            void InventoryLoaded(T inventory)
            {
                if (!_operations.IsCurrent(generation)) return;
                Value = inventory;
                request(true, SizesLoaded, Failed);
            }

            void SizesLoaded(T measured)
            {
                if (!_operations.IsCurrent(generation)) return;
                Value = measured;
                SizesAvailable = true;
                Loading = false;
                Error = null;
            }

            request(false, InventoryLoaded, Failed);
        }

        public void Invalidate()
        {
            _operations.Invalidate();
            Loading = false;
        }
    }
}
