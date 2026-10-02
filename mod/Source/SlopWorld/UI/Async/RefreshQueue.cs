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

        // Invalidate the owner's in-flight operation before resetting this queue.
        public void Reset() { Loading = Pending = false; _callbacks.Clear(); }

        public bool Request(Action refreshed = null)
        {
            if (refreshed != null) _callbacks.Add(refreshed);
            if (Loading) { Pending = true; return false; }
            Loading = true;
            return true;
        }

        internal readonly struct Completion
        {
            public readonly bool NeedsFollowUp;
            public readonly Action[] Callbacks;
            public Completion(bool needsFollowUp, Action[] callbacks)
            { NeedsFollowUp = needsFollowUp; Callbacks = callbacks; }
        }

        // Success and failure both complete the current read.
        public Completion Complete()
        {
            Loading = false;
            if (Pending) { Pending = false; return new Completion(true, Array.Empty<Action>()); }
            var callbacks = _callbacks.ToArray();
            _callbacks.Clear();
            return new Completion(false, callbacks);
        }
    }
}
