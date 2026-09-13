using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SlopWorld
{
    // Reuse main-thread scratch space; live screens coalesce, control/history events do not.
    internal sealed class HubEventBatch
    {
        public const int Limit = 32;
        readonly List<JVal> _events = new List<JVal>(Limit);
        readonly Dictionary<string, int> _latest = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Count => _events.Count;
        public JVal this[int index] => _events[index];

        public int Read(ConcurrentQueue<string> incoming, Action<Exception> onError)
        {
            Clear();
            int read = 0;
            while (read < Limit && incoming.TryDequeue(out var text))
            {
                read++;
                Add(text, null, onError);
            }
            return read;
        }

        public int Read(IncomingMessageQueue incoming, Action<Exception> onError)
        {
            Clear();
            int read = 0;
            while (read < Limit && incoming.TryDequeue(out var text, out var liveName))
            {
                read++;
                Add(text, liveName, onError);
            }
            return read;
        }

        void Add(string text, string knownLiveName, Action<Exception> onError)
        {
            try
            {
                var ev = JVal.Parse(text);
                if (knownLiveName != null)
                    _latest[knownLiveName] = _events.Count;
                else if (LiveName(ev, out var name))
                    _latest[name] = _events.Count;
                _events.Add(ev);
            }
            catch (Exception e) { onError(e); }
        }

        public bool ShouldDispatch(int index) =>
            !LiveName(_events[index], out var name) || _latest[name] == index;

        public void Clear()
        {
            _events.Clear();
            _latest.Clear();
        }

        static bool LiveName(JVal ev, out string name)
        {
            name = null;
            if (!string.Equals(ev["t"].AsString(), WireContract.Events.Screen, StringComparison.Ordinal)) return false;
            var screen = ev["screen"];
            if (screen["off"].AsInt(0) != 0 || screen["request_id"].AsLong(0) != 0) return false;
            name = screen["name"].AsString(null);
            return !string.IsNullOrEmpty(name);
        }

        // The bounded transport uses the same conservative classification before enqueueing.
        // Parsing failures stay in the queue and are reported by Add on the main thread.
        internal static bool TryLiveScreenName(string text, out string name)
        {
            try { return LiveName(JVal.Parse(text), out name); }
            catch
            {
                name = null;
                return false;
            }
        }
    }
}
