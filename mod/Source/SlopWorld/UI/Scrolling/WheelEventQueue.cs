using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SlopWorld
{
    // Compact the native FIFO once per frame, before Root dispatch. Metadata follows
    // the copied events in FIFO order; Unity does not preserve managed Event identity.
    internal static class WheelEventQueue
    {
        const int Threshold = 8;
        internal const int DispatchLimit = 32;
        const int ReportLimit = byte.MaxValue;
        static readonly System.Action<Event> Queue = FindQueue();
        static readonly Event Scratch = new Event();
        static readonly List<Run> Pending = new List<Run>();
        static readonly List<Run> Returning = new List<Run>();
        static readonly Queue<Run> Queued = new Queue<Run>();
        static readonly Queue<Run> Deferred = new Queue<Run>();
        static int _frame = -1;
        static int _dispatched;
        static bool _scanned;
        static Run _active;

        // Shared by chunks of one compatible native burst. Coverage survives deferral:
        // once native motion was spent, its later legacy chunks must remain duplicates.
        sealed class Motion
        {
            public bool Logical;
            public bool Covered;
            public readonly int Frame = Time.frameCount;
        }

        sealed class Run
        {
            public Event Event;
            public int Packets;
            public int VerticalSteps;
            public int DominantSteps;
            public Motion Motion = new Motion();
            public bool Fresh = true;
            public bool Mergeable;

            public Run(Event e, bool mergeable = true)
            {
                Event = new Event(e);
                Mergeable = mergeable;
                if (e.type != EventType.ScrollWheel)
                    return;
                Packets = 1;
                VerticalSteps = Steps(e.delta.y);
                DominantSteps = Steps(Horizontal(e) ? e.delta.x : e.delta.y);
            }

            public bool CanAdd(Run next) =>
                Mergeable
                && next.Mergeable
                && SameWheel(Event, next.Event)
                && VerticalSteps + next.VerticalSteps <= ReportLimit
                && DominantSteps + next.DominantSteps <= ReportLimit;

            public bool CanAdd(Event next) =>
                Mergeable
                && SameWheel(Event, next)
                && VerticalSteps + Steps(next.delta.y) <= ReportLimit
                && DominantSteps + Steps(Horizontal(next) ? next.delta.x : next.delta.y)
                    <= ReportLimit;

            public void Add(Event next)
            {
                Event.delta += next.delta;
                Packets++;
                VerticalSteps += Steps(next.delta.y);
                DominantSteps += Steps(Horizontal(next) ? next.delta.x : next.delta.y);
            }

            public void Add(Run next)
            {
                Event.delta += next.Event.delta;
                Packets += next.Packets;
                VerticalSteps += next.VerticalSteps;
                DominantSteps += next.DominantSteps;
            }
        }

        public static bool RequiresLogicalInput =>
            _active != null
            && (_active.Motion.Logical || _active.Motion.Frame != Time.frameCount)
            && !NativeCovered;
        public static bool NativeCovered => _active?.Motion.Covered == true;
        public static bool HasPendingInput => Queued.Count > 0 || Deferred.Count > 0;
        public static bool UsesAggregatedDelta => _active != null && _active.Packets > 1;
        public static int PacketCount => _active?.Packets ?? 1;

        public static void CoverNative()
        {
            if (_active != null)
                _active.Motion.Covered = true;
        }

        public static void UseLogical()
        {
            if (_active != null)
                _active.Motion.Logical = true;
        }

        public static int ApplicationSteps(Event e, bool reader, bool horizontal = false)
        {
            if (_active != null)
                return reader ? _active.Packets
                    : horizontal ? _active.DominantSteps
                    : _active.VerticalSteps;
            return reader ? 1 : Steps(horizontal ? e.delta.x : e.delta.y);
        }

        static int Steps(float delta) => Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(delta)), 1, 5);

        static bool Horizontal(Event e) => Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y);

        static bool InputEvent(Event e) =>
            e.type != EventType.Layout
            && e.type != EventType.Repaint
            && e.type != EventType.Used
            && e.type != EventType.Ignore;

        // False means this input was retained for a later frame. Root must skip dispatch;
        // using the event alone would still traverse every window, component and control.
        public static bool Compact(Event current, System.Func<Event, bool> canMerge = null)
        {
            _active = null;
            if (current == null)
                return true;
            if (_frame != Time.frameCount)
            {
                _frame = Time.frameCount;
                _dispatched = 0;
                _scanned = false;
            }
            bool input = InputEvent(current);
            bool expected = false;
            Run delivered = input ? Take(current, out expected, canMerge) : null;
            bool hold =
                Queue != null
                && input
                && (
                    _dispatched >= DispatchLimit
                    || ((Deferred.Count > 0 || Queued.Count > 0) && !expected)
                );
            int count = Event.GetEventCount();
            if (!input && count == 0)
                Queued.Clear();
            if (Queue != null && !_scanned && (count >= Threshold || Deferred.Count > 0 || hold))
            {
                _scanned = true;
                long started = PerfTrace.Start();
                Pending.Clear();
                Returning.Clear();
                // Previously emitted events precede the deferred tail. New native events
                // follow it, even if Unity delivers one before this frame's Layout pass.
                if (hold)
                    (expected ? Returning : Pending).Add(delivered);
                bool beforeTransition = !input || current.type == EventType.ScrollWheel;
                for (int i = 0; i < count && Event.PopEvent(Scratch); i++)
                {
                    // A compatible storm allocates one Event per bounded output run,
                    // not one managed/native Event pair per raw touchpad packet.
                    if (
                        Queued.Count == 0
                        && Pending.Count > 0
                        && (canMerge == null || beforeTransition)
                    )
                    {
                        var tail = Pending[Pending.Count - 1];
                        if (tail.CanAdd(Scratch))
                        {
                            tail.Add(Scratch);
                            continue;
                        }
                    }
                    var run = Take(Scratch, out bool old, canMerge);
                    // A key/click may open an unknown native consumer. Its later wheel
                    // packets retain their original deltas until that owner is known.
                    if (canMerge != null && !beforeTransition && !old)
                        run.Mergeable = false;
                    if (Scratch.type != EventType.ScrollWheel)
                        beforeTransition = false;
                    (old ? Returning : Pending).Add(run);
                }
                Queued.Clear();

                _active = input && !hold && delivered.Packets > 0 ? delivered : null;
                // Never rescan the deferred suffix. It is already compacted and may be
                // arbitrarily long; draining 32 runs costs O(32), not O(backlog).
                bool backlog = Deferred.Count > 0 || Returning.Count > 0;
                CompactRuns(backlog);
                if (_active != null)
                    current.delta = _active.Event.delta;
                foreach (var run in Pending)
                {
                    run.Fresh = false;
                    Deferred.Enqueue(run);
                }
                int available = DispatchLimit - _dispatched - (input && !hold ? 1 : 0);
                foreach (var run in Returning)
                {
                    Queue(run.Event);
                    Queued.Enqueue(run);
                    available--;
                }
                Returning.Clear();
                while (available-- > 0 && Deferred.Count > 0)
                {
                    var run = Deferred.Dequeue();
                    Queue(run.Event);
                    Queued.Enqueue(run);
                }
                PerfTrace.End("wheel-queue", started, count, Deferred.Count);
                Pending.Clear();
            }
            else if (hold)
            {
                if (!delivered.Motion.Covered)
                    delivered.Motion.Logical = true;
                delivered.Fresh = false;
                Deferred.Enqueue(delivered);
            }

            if (hold)
            {
                current.Use();
                PerfTrace.Count("root-input-deferred");
                return false;
            }
            _active = delivered?.Packets > 0 ? delivered : null;
            if (input)
            {
                delivered.Fresh = false;
                _dispatched++;
            }
            return true;
        }

        static void CompactRuns(bool backlog)
        {
            Run firstWheel = _active;
            bool ambiguous = backlog;
            foreach (var run in Pending)
            {
                if (run.Packets == 0)
                {
                    // A key/click can change the input owner between wheel runs.
                    ambiguous = true;
                    continue;
                }
                if (firstWheel == null)
                    firstWheel = run;
                else if (!SameWheel(firstWheel.Event, run.Event))
                    ambiguous = true;
            }
            // No native timestamp/context exists for a mixed queue. Mark the retained
            // runs, not the frame: delayed delivery must make the same choice.
            Motion motion = _active?.Fresh == true ? _active.Motion : null;
            if (_active != null && ambiguous && !_active.Motion.Covered)
                _active.Motion.Logical = true;
            int written = 0;
            Run previous = backlog ? null : _active;
            for (int index = 0; index < Pending.Count; index++)
            {
                var run = Pending[index];
                if (run.Packets > 0)
                {
                    if (ambiguous && !run.Motion.Covered)
                        run.Motion.Logical = true;
                    else if (run.Fresh)
                    {
                        if (motion == null)
                            motion = run.Motion;
                        run.Motion = motion;
                    }
                }
                if (
                    previous != null
                    && (
                        ReferenceEquals(previous.Motion, run.Motion)
                        || (
                            previous.Motion.Logical
                            && run.Motion.Logical
                            && !previous.Motion.Covered
                            && !run.Motion.Covered
                        )
                    )
                    && previous.CanAdd(run)
                )
                {
                    previous.Add(run);
                    continue;
                }
                previous = run;
                Pending[written++] = run;
            }
            Pending.RemoveRange(written, Pending.Count - written);
        }

        static Run Take(Event e, out bool expected, System.Func<Event, bool> canMerge)
        {
            expected = false;
            // A different native consumer may pop one of our emitted events. Retire
            // only the skipped prefix; preserve metadata for the surviving suffix.
            int skipped = 0;
            foreach (var run in Queued)
            {
                if (SameEvent(run.Event, e))
                {
                    for (int i = 0; i <= skipped; i++)
                        Queued.Dequeue();
                    expected = true;
                    return run;
                }
                skipped++;
            }
            return new Run(e, e.type == EventType.ScrollWheel && (canMerge == null || canMerge(e)));
        }

        static bool SameEvent(Event a, Event b) =>
            a.type == b.type
            && a.displayIndex == b.displayIndex
            && a.modifiers == b.modifiers
            && a.mousePosition.Equals(b.mousePosition)
            && a.delta.Equals(b.delta)
            && a.keyCode == b.keyCode
            && a.character == b.character
            && a.button == b.button
            && a.clickCount == b.clickCount;

        static bool SameWheel(Event a, Event b) =>
            a.type == EventType.ScrollWheel
            && b.type == EventType.ScrollWheel
            && a.displayIndex == b.displayIndex
            && a.modifiers == b.modifiers
            && a.mousePosition.x == b.mousePosition.x
            && a.mousePosition.y == b.mousePosition.y
            && Horizontal(a) == Horizontal(b)
            && SameDirection(a.delta.x, b.delta.x)
            && SameDirection(a.delta.y, b.delta.y);

        // Reversals must remain separate: edge clamping makes +N,-N differ from zero.
        static bool SameDirection(float a, float b) =>
            (a == 0f && b == 0f) || (a > 0f && b > 0f) || (a < 0f && b < 0f);

        static System.Action<Event> FindQueue()
        {
            var method = typeof(Event).GetMethod(
                "QueueEvent",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
            );
            if (method == null)
                return null;
            try
            {
                return (System.Action<Event>)
                    System.Delegate.CreateDelegate(typeof(System.Action<Event>), method);
            }
            catch
            {
                return e => method.Invoke(null, new object[] { e });
            }
        }
    }
}
