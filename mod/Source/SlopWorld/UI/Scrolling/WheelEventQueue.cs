using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SlopWorld
{
    // Merge compatible wheel runs before Root.OnGUI pays for each queued packet.
    internal static class WheelEventQueue
    {
        const int Threshold = 8;
        // QueueEvent is private in Unity. Leave the queue untouched if unavailable.
        static readonly System.Action<Event> Queue = FindQueue();
        static readonly Event Scratch = new Event();
        static readonly List<Event> Pending = new List<Event>();
        static int _compactedFrame = -1;
        static int _logicalFrame = -1;

        public static bool UsesLogicalDelta(Event e) => e != null && _logicalFrame == Time.frameCount;

        public static void Compact(Event current)
        {
            if (current == null || Queue == null || _compactedFrame == Time.frameCount) return;

            int count = Event.GetEventCount();
            if (count < Threshold) return;
            _compactedFrame = Time.frameCount;

            // Scan all runs once per frame; rescanning mixed suffixes is quadratic.
            // Include Layout, which precedes queued input delivery.
            Pending.Clear();
            Event previous = current.type == EventType.ScrollWheel ? current : null;
            for (int i = 0; i < count && Event.PopEvent(Scratch); i++)
            {
                if (previous != null && SameWheel(previous, Scratch))
                {
                    previous.delta += Scratch.delta;
                    _logicalFrame = Time.frameCount;
                    continue;
                }

                // PopEvent reuses Scratch; every queued event needs its own snapshot.
                previous = new Event(Scratch);
                Pending.Add(previous);
            }

            foreach (var e in Pending) Queue(e);
            Pending.Clear();
        }

        static bool SameWheel(Event a, Event b) =>
            a.type == EventType.ScrollWheel && b.type == EventType.ScrollWheel &&
            a.displayIndex == b.displayIndex && a.modifiers == b.modifiers &&
            a.mousePosition.x == b.mousePosition.x &&
            a.mousePosition.y == b.mousePosition.y &&
            SameDirection(a.delta.x, b.delta.x) && SameDirection(a.delta.y, b.delta.y);

        // Reversals must remain separate: clamping at a list edge makes +N,-N
        // observably different from zero movement.
        static bool SameDirection(float a, float b) =>
            (a == 0f && b == 0f) || (a > 0f && b > 0f) || (a < 0f && b < 0f);

        static System.Action<Event> FindQueue()
        {
            var method = typeof(Event).GetMethod("QueueEvent",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null) return null;
            try
            {
                return (System.Action<Event>)System.Delegate.CreateDelegate(
                typeof(System.Action<Event>), method);
            }
            catch { return e => method.Invoke(null, new object[] { e }); }
        }
    }
}
