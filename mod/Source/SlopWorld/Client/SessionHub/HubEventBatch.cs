using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SlopWorld
{
    // Reuse main-thread scratch space; live screens coalesce, control/history events do not.
    internal sealed class HubEventBatch
    {
        public const int Limit = 32;
        struct Pending
        {
            public string Text;
            public JVal Value;
        }
        readonly List<Pending> _events = new List<Pending>(Limit);
        readonly Dictionary<string, int> _latest = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Count => _events.Count;
        public JVal this[int index] => _events[index].Value;

        public int Read(ConcurrentQueue<string> incoming, Action<Exception> onError)
        {
            Clear();
            int read = 0;
            while (read < Limit && incoming.TryDequeue(out var text))
            {
                read++;
                Add(text, null, onError);
            }
            Decode(onError);
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
            Decode(onError);
            return read;
        }

        void Add(string text, string name, Action<Exception> onError)
        {
            var pending = new Pending();
            try
            {
                // Resolve ambiguous envelopes now, before choosing winners in arrival order.
                // Only known live screens defer decoding until the batch is collected.
                if (name == null && !HubWire.TryLiveScreenName(text, out name))
                {
                    pending.Value = JVal.Parse(text);
                    if (!LiveName(pending.Value, out name)) name = null;
                }
                else pending.Text = text;
                if (name != null)
                {
                    if (_latest.TryGetValue(name, out var old)) _events[old] = default;
                    _latest[name] = _events.Count;
                }
            }
            catch (Exception e) { onError(e); }
            _events.Add(pending);
        }

        void Decode(Action<Exception> onError)
        {
            for (int i = 0; i < _events.Count; i++)
            {
                string text = _events[i].Text;
                if (text == null) continue;
                try { _events[i] = new Pending { Value = JVal.Parse(text) }; }
                catch (Exception e)
                {
                    _events[i] = default;
                    onError(e);
                }
            }
        }

        public bool ShouldDispatch(int index) => _events[index].Value != null;

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
    }
}
