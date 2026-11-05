using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class WheelEventQueueTests
    {
        [SetUp]
        public void Clear()
        {
            Drain(_ => { });
            Event.ClearEvents();
            Time.frameCount++;
            Event.current = new Event { type = EventType.Repaint };
            WheelEventQueue.Compact(Event.current);
        }

        [TearDown]
        public void Cleanup() => Clear();

        internal static Event Wheel(float y, float x = 0f, float pointer = 10f) =>
            new Event
            {
                type = EventType.ScrollWheel,
                rawType = EventType.ScrollWheel,
                mousePosition = new Vector2(pointer, 10f),
                delta = new Vector2(x, y),
            };

        internal static void Drain(Action<Event> dispatch)
        {
            int frames = 0;
            do
            {
                Assert.That(++frames, Is.LessThan(5000), "queue must make progress");
                Time.frameCount++;
                Event.current = new Event { type = EventType.Layout };
                Assert.That(WheelEventQueue.Compact(Event.current), Is.True);
                int calls = 0;
                while (Event.PopEvent(Event.current))
                {
                    if (!WheelEventQueue.Compact(Event.current))
                        continue;
                    calls++;
                    dispatch(Event.current);
                }
                Assert.That(calls, Is.LessThanOrEqualTo(WheelEventQueue.DispatchLimit));
            } while (WheelEventQueue.HasPendingInput || Event.GetEventCount() > 0);
        }

        [Test]
        public void CompactsRunsWithoutMovingKeysOrLosingApplicationSteps()
        {
            var current = Wheel(2f);
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(.25f));
            Event.EnqueueTestEvent(
                new Event { type = EventType.KeyDown, keyCode = KeyCode.Return }
            );
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(-9f));
            Assert.That(WheelEventQueue.Compact(current), Is.True);
            Assert.That(current.delta.y, Is.EqualTo(5f));
            Assert.That(WheelEventQueue.ApplicationSteps(current, false), Is.EqualTo(14));
            Assert.That(WheelEventQueue.ApplicationSteps(current, true), Is.EqualTo(13));
            Assert.That(WheelEventQueue.RequiresLogicalInput, Is.True);
            var events = new List<Event>();
            Drain(e =>
            {
                events.Add(new Event(e));
                if (e.type == EventType.ScrollWheel)
                {
                    Assert.That(WheelEventQueue.ApplicationSteps(e, false), Is.EqualTo(60));
                    Assert.That(WheelEventQueue.RequiresLogicalInput, Is.True);
                }
            });
            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events[0].keyCode, Is.EqualTo(KeyCode.Return));
            Assert.That(events[1].delta.y, Is.EqualTo(-108f));
        }

        [Test]
        public void LargeCompatibleStormBoundsDispatchAndEachApplicationWrite()
        {
            const int packets = 36000;
            for (int i = 0; i < packets; i++)
                Event.EnqueueTestEvent(Wheel(.25f));
            Event.CopyCalls = 0;
            int reports = 0,
                readers = 0,
                calls = 0;
            float distance = 0;
            Drain(e =>
            {
                calls++;
                int steps = WheelEventQueue.ApplicationSteps(e, false);
                Assert.That(steps, Is.InRange(1, 255));
                reports += steps;
                readers += WheelEventQueue.ApplicationSteps(e, true);
                distance += e.delta.y;
            });
            Assert.That(reports, Is.EqualTo(packets));
            Assert.That(readers, Is.EqualTo(packets));
            Assert.That(distance, Is.EqualTo(packets * .25f));
            Assert.That(calls, Is.EqualTo((packets + 254) / 255));
            Assert.That(Event.CopyCalls, Is.LessThanOrEqualTo(calls * 2 + 2));
        }

        [Test]
        public void MixedStormPreservesEveryBoundaryAndDoesNotRescanDeferredTail()
        {
            const int count = 10000;
            for (int i = 0; i < count; i++)
            {
                var e = Wheel(i % 2 == 0 ? 1f : -1f, pointer: i);
                e.modifiers = i % 3 == 0 ? EventModifiers.Shift : EventModifiers.None;
                e.displayIndex = i % 2;
                Event.EnqueueTestEvent(e);
            }
            int consumed = 0;
            Drain(e =>
            {
                Assert.That(e.mousePosition.x, Is.EqualTo(consumed));
                Assert.That(e.delta.y, Is.EqualTo(consumed % 2 == 0 ? 1f : -1f));
                Assert.That(e.displayIndex, Is.EqualTo(consumed % 2));
                Assert.That(
                    e.modifiers,
                    Is.EqualTo(consumed % 3 == 0 ? EventModifiers.Shift : EventModifiers.None)
                );
                Assert.That(WheelEventQueue.RequiresLogicalInput, Is.True);
                consumed++;
            });
            Assert.That(consumed, Is.EqualTo(count));
            // One native scan and one dispatch pop per packet, plus one empty pop/frame.
            Assert.That(Event.PopCalls, Is.LessThan(count * 2 + 400));
        }

        [Test]
        public void NewInputCannotOvertakeDeferredWheelOrKeys()
        {
            for (int i = 0; i < 70; i++)
                Event.EnqueueTestEvent(Wheel(1f, pointer: i));
            WheelEventQueue.Compact(new Event { type = EventType.Layout });
            var next = new Event();
            int index = 0;
            while (Event.PopEvent(next))
            {
                Assert.That(WheelEventQueue.Compact(next), Is.True);
                Assert.That(next.mousePosition.x, Is.EqualTo(index++));
            }
            // Native input arrives before Layout on the next frame.
            Time.frameCount++;
            var key = new Event { type = EventType.KeyDown, keyCode = KeyCode.Return };
            Assert.That(WheelEventQueue.Compact(key), Is.False);
            Assert.That(key.type, Is.EqualTo(EventType.Used));
            var seen = new List<Event>();
            Drain(e => seen.Add(new Event(e)));
            for (int i = 0; i < 38; i++)
                Assert.That(seen[i].mousePosition.x, Is.EqualTo(i + 32));
            Assert.That(seen.Count, Is.EqualTo(39));
            Assert.That(seen[38].keyCode, Is.EqualTo(KeyCode.Return));
        }

        [Test]
        public void LateNativeArrivalWaitsBehindAlreadyEmittedAndDeferredInput()
        {
            for (int i = 0; i < 40; i++)
                Event.EnqueueTestEvent(Wheel(1f, pointer: i));
            WheelEventQueue.Compact(new Event { type = EventType.Layout });
            var first = new Event();
            Event.PopEvent(first);
            Assert.That(WheelEventQueue.Compact(first), Is.True);
            Assert.That(first.mousePosition.x, Is.Zero);
            var late = Wheel(1f, pointer: 1000);
            Assert.That(WheelEventQueue.Compact(late), Is.False);
            int next = 1;
            Drain(e =>
            {
                Assert.That(e.mousePosition.x, Is.EqualTo(next < 40 ? next : 1000));
                next++;
            });
            Assert.That(next, Is.EqualTo(41));
        }

        [Test]
        public void MetadataSurvivesFrameChangeAndAnotherConsumerSkippingAPrefix()
        {
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(.25f));
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(-.5f));
            WheelEventQueue.Compact(new Event { type = EventType.Layout });
            var next = new Event();
            Event.PopEvent(next); // A different consumer removes the first run.
            Time.frameCount++;
            Event.PopEvent(next);
            Assert.That(WheelEventQueue.Compact(next), Is.True);
            Assert.That(WheelEventQueue.ApplicationSteps(next, false), Is.EqualTo(12));
            Assert.That(WheelEventQueue.RequiresLogicalInput, Is.True);
            Assert.That(next.delta.y, Is.EqualTo(-6f));
            Event.current = new Event { type = EventType.Repaint };
            WheelEventQueue.Compact(Event.current);
            Assert.That(WheelEventQueue.HasPendingInput, Is.False);
        }

        [Test]
        public void UnknownConsumersAndInputAfterTransitionsKeepIndividualPackets()
        {
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(.25f));
            WheelEventQueue.Compact(new Event { type = EventType.Layout }, _ => false);
            Assert.That(Event.GetEventCount(), Is.EqualTo(12));
            Drain(e => Assert.That(e.delta.y, Is.EqualTo(.25f)));
            Time.frameCount++;
            Event.EnqueueTestEvent(new Event { type = EventType.KeyDown });
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(.25f));
            WheelEventQueue.Compact(new Event { type = EventType.Layout }, _ => true);
            Assert.That(Event.GetEventCount(), Is.EqualTo(13));
            Drain(e =>
            {
                if (e.type == EventType.ScrollWheel)
                    Assert.That(e.delta.y, Is.EqualTo(.25f));
            });
        }

        [Test]
        public void ApplicationDominantAxisAndReaderCountsKeepOriginalPackets()
        {
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(.1f, 3f));
            for (int i = 0; i < 12; i++)
                Event.EnqueueTestEvent(Wheel(3f, .1f));
            int calls = 0;
            Drain(e =>
            {
                bool horizontal = e.delta.x > e.delta.y;
                Assert.That(horizontal, Is.EqualTo(calls == 0));
                Assert.That(WheelEventQueue.ApplicationSteps(e, false, horizontal), Is.EqualTo(36));
                Assert.That(WheelEventQueue.ApplicationSteps(e, true, horizontal), Is.EqualTo(12));
                calls++;
            });
            Assert.That(calls, Is.EqualTo(2));
        }
    }
}
