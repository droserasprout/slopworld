using System;
using System.Collections.Generic;
using System.Text;

namespace SlopWorld
{
    // The socket reader and Unity's main thread run concurrently. Keep the queue's accounting
    // beside its storage so a producer cannot race Count with the byte budget.
    public sealed class BoundedStringQueue
    {
        sealed class Entry
        {
            public string Value;
            public string ReplaceKey;
            public int Bytes;
        }

        readonly object _gate = new object();
        readonly LinkedList<Entry> _items = new LinkedList<Entry>();
        readonly Dictionary<string, LinkedListNode<Entry>> _replaceable =
            new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        readonly int _maxCount;
        readonly int _maxBytes;
        int _bytes;

        public BoundedStringQueue(int maxCount, int maxBytes)
        {
            if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            _maxCount = maxCount;
            _maxBytes = maxBytes;
        }

        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        public int Bytes
        {
            get { lock (_gate) return _bytes; }
        }

        // A replace key makes a queued item a latest-value slot. If the queue is full, stale
        // replaceable items may be evicted to make room; non-replaceable messages are retained.
        public bool TryEnqueue(string value, string replaceKey = null)
        {
            if (value == null) return false;
            int bytes = Encoding.UTF8.GetByteCount(value);
            if (bytes > _maxBytes) return false;

            lock (_gate)
            {
                if (replaceKey != null &&
                    _replaceable.TryGetValue(replaceKey, out var existing))
                {
                    if (_bytes - existing.Value.Bytes + bytes > _maxBytes) return false;
                    _bytes += bytes - existing.Value.Bytes;
                    existing.Value = new Entry
                    {
                        Value = value,
                        ReplaceKey = replaceKey,
                        Bytes = bytes,
                    };
                    return true;
                }

                while ((_items.Count >= _maxCount || _bytes + bytes > _maxBytes) &&
                       TryFindOldestReplaceable(out var stale))
                    Remove(stale);

                if (_items.Count >= _maxCount || _bytes + bytes > _maxBytes)
                    return false;

                var node = _items.AddLast(new Entry
                {
                    Value = value,
                    ReplaceKey = replaceKey,
                    Bytes = bytes,
                });
                if (replaceKey != null) _replaceable.Add(replaceKey, node);
                _bytes += bytes;
                return true;
            }
        }

        public bool TryDequeue(out string value)
        {
            lock (_gate)
            {
                if (_items.First == null)
                {
                    value = null;
                    return false;
                }

                var node = _items.First;
                value = node.Value.Value;
                Remove(node);
                return true;
            }
        }

        public void Clear()
        {
            lock (_gate)
            {
                _items.Clear();
                _replaceable.Clear();
                _bytes = 0;
            }
        }

        bool TryFindOldestReplaceable(out LinkedListNode<Entry> node)
        {
            for (var current = _items.First; current != null; current = current.Next)
            {
                if (current.Value.ReplaceKey != null)
                {
                    node = current;
                    return true;
                }
            }

            node = null;
            return false;
        }

        void Remove(LinkedListNode<Entry> node)
        {
            _items.Remove(node);
            if (node.Value.ReplaceKey != null) _replaceable.Remove(node.Value.ReplaceKey);
            _bytes -= node.Value.Bytes;
        }
    }

    public sealed class BoundedActionQueue
    {
        sealed class Entry
        {
            public Action Action;
            public int Bytes;
        }

        readonly object _gate = new object();
        readonly Queue<Entry> _items = new Queue<Entry>();
        readonly int _maxCount;
        readonly int _maxBytes;
        int _bytes;
        int _reservedCount;
        int _reservedBytes;

        public BoundedActionQueue(int maxCount, int maxBytes)
        {
            if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            _maxCount = maxCount;
            _maxBytes = maxBytes;
        }

        public int Count
        {
            get { lock (_gate) return _items.Count; }
        }

        public int Bytes
        {
            get { lock (_gate) return _bytes; }
        }

        // HTTP reserves the worst-case completion size before starting a request. This keeps
        // a response from racing other producers into a full completion queue.
        public bool TryReserve(int bytes)
        {
            if (bytes <= 0 || bytes > _maxBytes) return false;
            lock (_gate)
            {
                if (_items.Count + _reservedCount >= _maxCount ||
                    _bytes + _reservedBytes > _maxBytes - bytes)
                    return false;
                _reservedCount++;
                _reservedBytes += bytes;
                return true;
            }
        }

        public void ReleaseReservation(int bytes)
        {
            lock (_gate)
            {
                _reservedCount--;
                _reservedBytes -= bytes;
            }
        }

        // Transfers a reservation to an actual callback. actualBytes must be no larger than
        // the reservation; a false result still consumes the reservation.
        public bool EnqueueReserved(Action action, int reservedBytes, int actualBytes)
        {
            if (action == null)
            {
                ReleaseReservation(reservedBytes);
                return true;
            }

            if (actualBytes <= 0) actualBytes = 1;
            lock (_gate)
            {
                _reservedCount--;
                _reservedBytes -= reservedBytes;
                if (actualBytes > reservedBytes || _bytes > _maxBytes - actualBytes)
                    return false;
                _items.Enqueue(new Entry { Action = action, Bytes = actualBytes });
                _bytes += actualBytes;
                return true;
            }
        }

        public bool TryEnqueue(Action action, int bytes)
        {
            if (action == null) return true;
            if (bytes <= 0) bytes = 1;

            lock (_gate)
            {
                if (_items.Count + _reservedCount >= _maxCount ||
                    _bytes + _reservedBytes > _maxBytes - bytes)
                    return false;
                _items.Enqueue(new Entry { Action = action, Bytes = bytes });
                _bytes += bytes;
                return true;
            }
        }

        public bool TryDequeue(out Action action)
        {
            lock (_gate)
            {
                if (_items.Count == 0)
                {
                    action = null;
                    return false;
                }

                var entry = _items.Dequeue();
                _bytes -= entry.Bytes;
                action = entry.Action;
                return true;
            }
        }
    }
}
