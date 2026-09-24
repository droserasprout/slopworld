using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace SlopWorld
{
    // This clock belongs to the client. Daemon timestamps travel separately and
    // only differences within one clock domain may be reported.
    internal static class TerminalLatency
    {
        internal static readonly bool Enabled = Environment.GetEnvironmentVariable("SLOPWORLD_LATENCY") == "1";
        internal static Action<string> Write = _ => { };
        internal static long Now() => (long)(Stopwatch.GetTimestamp() * (1000000.0 / Stopwatch.Frequency));
        // Flush outside the timed frame endpoint. Logging itself must not block SendBinary.
        static readonly Queue<string> Logs = new Queue<string>();
        static int _droppedLogs;
        internal static readonly LatencyTimeline Timeline = new LatencyTimeline(Now, line =>
        {
            if (Logs.Count < 1024) Logs.Enqueue(line);
            else _droppedLogs++;
        });
        internal static void Flush()
        {
            while (Logs.Count > 0) Write(Logs.Dequeue());
            if (_droppedLogs == 0) return;
            Write("[SlopWorld] latency dropped_records=" + _droppedLogs);
            _droppedLogs = 0;
        }
        internal static void Begin(Wire.ClientMessage message)
        {
            if (!Enabled) return;
            if (message.Keys != null) message.Keys.TraceId = Timeline.Begin(message.Keys.Name, "keys");
            else if (message.Paste != null) message.Paste.TraceId = Timeline.Begin(message.Paste.Name, "paste");
            else if (message.Mouse != null) message.Mouse.TraceId = Timeline.Begin(message.Mouse.Name, "mouse");
        }
    }

    // Main-thread owner. Each input request is one sample (a request may contain
    // several characters). Repeated trace metadata lets a superseding live frame
    // complete a sample even if the first captured frame was coalesced away.
    internal sealed class LatencyTimeline
    {
        const int Limit = 128;
        const long TimeoutUs = 10000000;
        readonly Func<long> _now;
        readonly Action<string> _write;
        readonly List<Pending> _ready = new List<Pending>();
        readonly Dictionary<string, Pending> _pending = new Dictionary<string, Pending>();
        sealed class Pending
        {
            internal string Id, Name, Kind;
            internal object ScrollOwner;
            internal string ScrollSource;
            internal long Start, Receive, Dispatch, Draw;
            internal int Frame = -1;
            internal ulong Seq;
            internal Wire.InputTiming Timing;
        }
        internal LatencyTimeline(Func<long> now, Action<string> write) { _now = now; _write = write; }
        internal int Count => _pending.Count;
        internal string Begin(string name, string kind)
        {
            Expire();
            if (_pending.Count >= Limit)
            {
                Pending oldest = null;
                foreach (var p in _pending.Values) if (oldest == null || p.Start < oldest.Start) oldest = p;
                Finish(oldest, "overflow", _now());
            }
            var item = new Pending { Id = Guid.NewGuid().ToString("N"), Name = name, Kind = kind, Start = _now() };
            _pending.Add(item.Id, item);
            _write("[SlopWorld] latency id=" + item.Id + " status=start kind=" + kind);
            return item.Id;
        }
        // Each observed wheel event gets an outcome. An offset replaced before a
        // ready repaint is censored, never completed against an unrelated view.
        internal void Scroll(object owner, long started, bool moved, string source = "wheel")
        {
            if (moved && source != "duplicate") CancelScroll(owner, "superseded");
            string id = Begin("", "history_scroll");
            var p = _pending[id];
            p.Start = started;
            p.ScrollOwner = owner;
            p.ScrollSource = source;
            if (source == "duplicate") Finish(p, "deduplicated", _now());
            else if (!moved) Finish(p, "no_motion", _now());
        }
        internal void CancelScroll(object owner, string status = "cancelled")
        {
            _ready.Clear();
            foreach (var p in _pending.Values)
                if (ReferenceEquals(p.ScrollOwner, owner) && p.Frame < 0) _ready.Add(p);
            foreach (var p in _ready) Finish(p, status, _now());
            _ready.Clear();
        }
        internal void DrawScroll(object owner, bool ready, int frame)
        {
            if (!ready) return;
            foreach (var p in _pending.Values)
            {
                if (!ReferenceEquals(p.ScrollOwner, owner) || p.Frame >= 0) continue;
                p.Draw = _now();
                p.Frame = frame;
            }
        }
        internal void Dispatch(Wire.ScreenView screen, long received)
        {
            if (screen == null || screen.Off != 0 || screen.RequestId != 0) return;
            long at = _now();
            foreach (var timing in screen.InputTimings)
            {
                if (!_pending.TryGetValue(timing.Id, out var p) || p.Name != screen.Name || p.Frame >= 0) continue;
                // Old frames, replayed subscriptions and malformed timestamps cannot
                // overwrite a newer observation. Never compare daemon and client clocks.
                if (p.Timing != null && (p.Timing.RunId != timing.RunId || screen.Seq <= p.Seq)) continue;
                if (received < p.Start || received > at || timing.ReceivedUs == 0 ||
                    timing.TmuxUs < timing.ReceivedUs || timing.FirstCaptureUs < timing.TmuxUs ||
                    timing.CaptureUs < timing.FirstCaptureUs || timing.SendUs < timing.CaptureUs ||
                    timing.FirstSeq > screen.Seq) continue;
                p.Timing = timing.Clone();
                p.Seq = screen.Seq;
                p.Receive = received;
                p.Dispatch = at;
            }
        }
        internal void Draw(string name, ulong seq, IEnumerable<Wire.InputTiming> timings, int frame)
        {
            if (timings == null || _pending.Count == 0) return;
            long at = _now();
            foreach (var timing in timings)
            {
                if (!_pending.TryGetValue(timing.Id, out var p) || p.Name != name || p.Timing == null ||
                    p.Timing.RunId != timing.RunId || p.Seq != seq || p.Frame >= 0) continue;
                p.Draw = at;
                p.Frame = frame;
            }
        }
        // Called after cameras and GUI, before Unity presents. It is deliberately
        // named frame_end, never presented: GPU/compositor/scanout are unobserved.
        internal void EndFrame(int frame)
        {
            if (_pending.Count == 0) return;
            long at = _now();
            _ready.Clear();
            foreach (var p in _pending.Values) if (p.Frame >= 0 && p.Frame <= frame) _ready.Add(p);
            foreach (var p in _ready) Finish(p, p.Frame == frame ? "frame_end" : "missed_frame_end", at);
            _ready.Clear();
        }
        internal void Expire()
        {
            if (_pending.Count == 0) return;
            long at = _now();
            _ready.Clear();
            foreach (var p in _pending.Values) if (at - p.Start >= TimeoutUs) _ready.Add(p);
            foreach (var p in _ready) Finish(p, "timeout", at);
            _ready.Clear();
        }
        internal void Reset()
        {
            foreach (var p in new List<Pending>(_pending.Values)) Finish(p, "disconnected", _now());
        }
        void Finish(Pending p, string status, long at)
        {
            _pending.Remove(p.Id);
            string line = "[SlopWorld] latency id=" + p.Id + " status=" + status + " kind=" + p.Kind +
                " elapsed_us=" + (at - p.Start).ToString(CultureInfo.InvariantCulture) +
                " client_input_us=" + p.Start + " client_end_us=" + at;
            if (p.Timing != null)
            {
                var t = p.Timing;
                line += " run=" + t.RunId + " seq=" + p.Seq + " first_seq=" + t.FirstSeq +
                    " receive_us=" + (p.Receive - p.Start) + " dispatch_us=" + (p.Dispatch - p.Start) +
                    " daemon_received_us=" + t.ReceivedUs + " tmux_us=" + t.TmuxUs +
                    " tmux_done_us=" + t.TmuxDoneUs + " first_capture_us=" + t.FirstCaptureUs +
                    " capture_us=" + t.CaptureUs + " send_us=" + t.SendUs;
            }
            if (p.ScrollSource != null) line += " scroll_source=" + p.ScrollSource;
            if (p.Frame >= 0) line += " draw_us=" + (p.Draw - p.Start) + " frame=" + p.Frame;
            _write(line);
        }
    }
}
