using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace SlopWorld
{
    // Registration may happen on the loader thread. Execution belongs exclusively to the
    // game thread; weak queue entries must not retain definitions across a data reload.
    internal sealed class DeferredStartupActions<T>
        where T : class
    {
        sealed class Entry
        {
            internal Action Action;
            internal bool Running;
        }

        readonly object gate = new object();
        readonly ConditionalWeakTable<T, Entry> entries = new ConditionalWeakTable<T, Entry>();
        readonly Queue<WeakReference<T>> queue = new();

        internal void Schedule(T owner, Action action)
        {
            lock (gate)
            {
                var entry = new Entry { Action = action };
                entries.Remove(owner);
                entries.Add(owner, entry);
                // The queued entry must not hold the callback, which usually captures owner.
                queue.Enqueue(new WeakReference<T>(owner));
            }
        }

        internal void Ensure(T owner)
        {
            Entry entry;
            Action action;
            lock (gate)
            {
                if (!entries.TryGetValue(owner, out entry) || entry.Running)
                    return;
                entry.Running = true;
                action = entry.Action;
            }
            try
            {
                action();
            }
            finally
            {
                lock (gate)
                {
                    // A callback can schedule its replacement. Never erase that newer work.
                    if (
                        entries.TryGetValue(owner, out var current)
                        && ReferenceEquals(current, entry)
                    )
                        entries.Remove(owner);
                }
            }
        }

        internal bool WarmOne()
        {
            T owner = null;
            lock (gate)
            {
                while (queue.Count > 0)
                {
                    var queued = queue.Dequeue();
                    if (
                        queued.TryGetTarget(out owner)
                        && entries.TryGetValue(owner, out var entry)
                        && !entry.Running
                    )
                        break;
                    owner = null;
                }
            }
            if (owner == null)
                return false;
            Ensure(owner);
            return true;
        }
    }
}
