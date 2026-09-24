using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Mutations during an outstanding read need a later snapshot. Collapse their requests
    // into one follow-up, and defer consumers until that snapshot has arrived.
    sealed class RefreshQueue
    {
        readonly List<Action> _callbacks = new List<Action>();
        public bool Loading { get; private set; }
        public bool Pending { get; private set; }

        public void Reset() { Loading = Pending = false; _callbacks.Clear(); }

        public bool Request(Action refreshed = null)
        {
            if (refreshed != null) _callbacks.Add(refreshed);
            if (Loading) { Pending = true; return false; }
            Loading = true;
            return true;
        }

        // Null requests a follow-up. Success and failure both complete the current read.
        public Action[] Complete()
        {
            Loading = false;
            if (Pending) { Pending = false; return null; }
            var callbacks = _callbacks.ToArray();
            _callbacks.Clear();
            return callbacks;
        }
    }
}
