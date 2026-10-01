using System;
using System.Collections.Generic;
using System.Threading;

namespace SlopWorld
{
    internal enum IncomingEnqueueResult
    {
        Accepted,
        Closed,
        Oversized,
    }

    // A bounded queue for socket events.
    // New live screens can replace queued live screens before the main thread receives them.
    // Replies, history, and control events wait for space without losing events.
    internal sealed class IncomingMessageQueue
    {
        // Limit the queue to 256 messages and 16 MiB of encoded payload data.
        // Reject any single message above 8 MiB before adding it to the queue.
        internal const int MaxMessages = 256;
        internal const int MaxBytes = 16 * 1024 * 1024;
        internal const int MaxMessageBytes = 8 * 1024 * 1024;

        sealed class Entry
        {
            public readonly ReceivedEvent Text;
            public readonly string LiveName;
            public readonly int Bytes;

            public Entry(ReceivedEvent text, string liveName, int bytes)
            {
                Text = text;
                LiveName = liveName;
                Bytes = bytes;
            }
        }

        readonly object _gate = new object();
        readonly LinkedList<Entry> _queue = new LinkedList<Entry>();
        readonly Dictionary<string, LinkedListNode<Entry>> _latestLive =
            new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        int _bytes;
        bool _closed;

        public int Count
        {
            get { lock (_gate) return _queue.Count; }
        }

        public int Bytes
        {
            get { lock (_gate) return _bytes; }
        }

        public IncomingEnqueueResult Enqueue(byte[] payload)
        {
            if (payload == null) return IncomingEnqueueResult.Oversized;

            int bytes = payload.Length;
            if (bytes > MaxMessageBytes) return IncomingEnqueueResult.Oversized;

            // Validate and classify before acquiring the queue lock. Decode retained live frames on dispatch.
            // Ownership of the payload transfers to the queue. Callers must not mutate it.
            // Ambiguous or malformed messages never replace queued messages.
            var text = new ReceivedEvent(payload, deferLive: true);
            var liveName = text.LiveName;
            lock (_gate)
            {
                while (!_closed)
                {
                    // Remove the old live frame before testing capacity. Appending the new one
                    // keeps every non-live event in order, including replies between frames.
                    if (liveName != null && _latestLive.TryGetValue(liveName, out var old))
                        Remove(old);

                    if (_queue.Count < MaxMessages && _bytes + bytes <= MaxBytes)
                    {
                        var node = _queue.AddLast(new Entry(text, liveName, bytes));
                        _bytes += bytes;
                        if (liveName != null) _latestLive[liveName] = node;
                        Monitor.PulseAll(_gate);
                        return IncomingEnqueueResult.Accepted;
                    }

                    // Wait for queue space without discarding events.
                    // Disposal and connection failure wake this wait so a blocked producer can exit.
                    Monitor.Wait(_gate);
                }
            }
            return IncomingEnqueueResult.Closed;
        }

        public bool TryDequeue(out ReceivedEvent text)
        {
            return TryDequeue(out text, out _);
        }

        internal bool TryDequeue(out ReceivedEvent text, out string liveName)
        {
            lock (_gate)
            {
                if (_queue.First == null)
                {
                    text = null;
                    liveName = null;
                    return false;
                }

                var node = _queue.First;
                text = node.Value.Text;
                liveName = node.Value.LiveName;
                Remove(node);
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                // Discard queued events after disconnection to release their payload references.
                // Wake producers that are waiting for queue space.
                _queue.Clear();
                _latestLive.Clear();
                _bytes = 0;
                Monitor.PulseAll(_gate);
            }
        }

        void Remove(LinkedListNode<Entry> node)
        {
            _queue.Remove(node);
            _bytes -= node.Value.Bytes;
            if (node.Value.LiveName != null &&
                _latestLive.TryGetValue(node.Value.LiveName, out var current) &&
                ReferenceEquals(current, node))
                _latestLive.Remove(node.Value.LiveName);
        }
    }
}
