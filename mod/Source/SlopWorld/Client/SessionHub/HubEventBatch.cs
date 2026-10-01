using System;
using System.Collections.Generic;
namespace SlopWorld
{
    internal sealed class HubEventBatch
    {
        public const int Limit = 32;
        readonly List<long> _received = new List<long>(Limit);
        public long ReceivedAt(int index) => _received[index];
        readonly List<Wire.Event> _events = new List<Wire.Event>(Limit);
        readonly Dictionary<string, int> _latest = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Count => _events.Count;
        public Wire.Event this[int index] => _events[index];
        public bool ShouldDispatch(int index) => _events[index] != null;
        public int Read(IncomingMessageQueue incoming, Action<Exception> onError)
        {
            Clear();
            int read = 0;
            while (read < Limit && incoming.TryDequeue(out var entry))
            {
                read++;
                _received.Add(entry.ReceivedAt);
                if (entry.Error != null) { onError(entry.Error); _events.Add(null); continue; }
                if (entry.LiveName != null)
                {
                    // Keep a null slot rather than removing it: event indexes must
                    // stay aligned with receive timestamps for latency dispatch.
                    if (_latest.TryGetValue(entry.LiveName, out var old)) _events[old] = null;
                    _latest[entry.LiveName] = _events.Count;
                }
                _events.Add(entry.Value);
            }
            return read;
        }
        public void Clear() { _events.Clear(); _latest.Clear(); _received.Clear(); }
    }
}
