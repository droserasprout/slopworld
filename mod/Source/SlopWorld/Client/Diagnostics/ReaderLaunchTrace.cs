using System;
using System.Collections.Generic;
using System.Globalization;

namespace SlopWorld
{
    // Main-thread launch observations. Durations stay on the client clock; the session
    // handle correlates these records with daemon startup, without logging commands.
    internal sealed class ReaderLaunchTrace
    {
        static long _nextId;
        static readonly Dictionary<string, ReaderLaunchTrace> Pending = new();
        readonly Func<long> _now;
        readonly Action<string> _write;
        readonly long _id = ++_nextId;
        readonly long _started;
        long _previous;
        bool _received;
        bool _repainted;
        bool _pagerScreen;
        string _session = "pending";

        internal ReaderLaunchTrace(Func<long> now, Action<string> write, long? requestedAt = null)
        {
            _now = now;
            _write = write;
            _started = _previous = requestedAt ?? now();
        }

        internal static ReaderLaunchTrace Begin(
            string intent,
            string scope,
            string key,
            long requestedAt
        )
        {
            if (!PerfTrace.Enabled || intent != "diff")
                return null;
            var trace = new ReaderLaunchTrace(
                TerminalLatency.Now,
                TerminalLatency.Record,
                requestedAt == 0 ? null : requestedAt
            );
            trace.Mark(
                "run-request-"
                    + scope
                    + (string.IsNullOrEmpty(key) ? "" : " key=" + Uri.EscapeDataString(key))
            );
            return trace;
        }

        internal void Mark(string stage)
        {
            long now = _now();
            _write(
                $"[SlopWorld] reader-launch id={_id} session={_session} stage={stage}"
                    + " elapsed_ms="
                    + ((now - _started) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)
                    + " step_ms="
                    + ((now - _previous) / 1000.0).ToString("0.###", CultureInfo.InvariantCulture)
            );
            _previous = now;
        }

        internal void Attach(string session)
        {
            // Hidden/closed readers may never repaint. Bound retention and report those
            // censored samples instead of treating them as fast completed launches.
            foreach (string name in new List<string>(Pending.Keys))
                if (_now() - Pending[name]._started > 30_000_000 || Pending.Count >= 64)
                    Cancel(name, "expired");
            _session = session;
            Pending[session] = this;
            Mark("run-reply");
        }

        internal static void Received(string session, bool alternateScreen)
        {
            if (!Pending.TryGetValue(session, out var trace))
                return;
            if (!trace._received)
            {
                trace._received = true;
                trace.Mark("first-screen");
            }
            if (alternateScreen && !trace._pagerScreen)
            {
                trace._pagerScreen = true;
                trace.Mark("pager-screen");
            }
        }

        internal static void Subscribed(string session)
        {
            if (Pending.TryGetValue(session, out var trace))
                trace.Mark("subscribed");
        }

        internal static void Drawn(string session, bool alternateScreen)
        {
            if (session == null || !Pending.TryGetValue(session, out var trace) || !trace._received)
                return;
            if (!trace._repainted)
            {
                trace._repainted = true;
                trace.Mark("first-repaint");
            }
            if (alternateScreen && trace._pagerScreen)
            {
                trace.Mark("pager-repaint");
                Pending.Remove(session);
            }
        }

        internal static void Cancel(string session, string reason)
        {
            if (session == null || !Pending.TryGetValue(session, out var trace))
                return;
            trace.Mark(reason);
            Pending.Remove(session);
        }

        internal static void CancelAll(string reason)
        {
            foreach (string session in new List<string>(Pending.Keys))
                Cancel(session, reason);
        }
    }
}
