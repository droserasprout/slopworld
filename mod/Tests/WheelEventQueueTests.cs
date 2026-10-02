using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class WheelEventQueueTests
    {
        [SetUp]
        public void Clear() { Event.ClearEvents(); Time.frameCount++; }

        [TearDown]
        public void Cleanup() => Event.ClearEvents();

        [Test]
        public void CompactsWheelRunsWithoutMovingKeys()
        {
            var current = Wheel(2f);
            for (int i = 0; i < 12; i++) Event.EnqueueTestEvent(Wheel(1f));
            Event.EnqueueTestEvent(new Event { type = EventType.KeyDown });
            for (int i = 0; i < 12; i++) Event.EnqueueTestEvent(Wheel(1f));

            WheelEventQueue.Compact(current);

            Assert.That(WheelEventQueue.UsesLogicalDelta(current), Is.True);
            Assert.That(current.delta.y, Is.EqualTo(14f));
            Assert.That(Event.GetEventCount(), Is.EqualTo(2));
            var next = new Event();
            Assert.That(Event.PopEvent(next), Is.True);
            Assert.That(next.type, Is.EqualTo(EventType.KeyDown));
            Assert.That(Event.PopEvent(next), Is.True);
            WheelEventQueue.Compact(next);
            Assert.That(next.delta.y, Is.EqualTo(12f));
            Assert.That(Event.GetEventCount(), Is.Zero);
        }

        [Test]
        public void DifferentPointerAndModifierKeepSeparateTargets()
        {
            var current = Wheel(1f);
            for (int i = 0; i < 24; i++)
                Event.EnqueueTestEvent(new Event
                {
                    type = EventType.ScrollWheel,
                    mousePosition = new Vector2(40f, 10f),
                    delta = new Vector2(0f, 1f),
                    modifiers = i < 12 ? EventModifiers.None : EventModifiers.Shift
                });

            WheelEventQueue.Compact(current);

            Assert.That(current.delta.y, Is.EqualTo(1f));
            Assert.That(Event.GetEventCount(), Is.EqualTo(2));
            var next = new Event();
            Event.PopEvent(next);
            WheelEventQueue.Compact(next);
            Assert.That(next.delta.y, Is.EqualTo(12f));
            Assert.That(next.modifiers, Is.EqualTo(EventModifiers.None));
            Event.PopEvent(next);
            WheelEventQueue.Compact(next);
            Assert.That(next.delta.y, Is.EqualTo(12f));
            Assert.That(next.modifiers, Is.EqualTo(EventModifiers.Shift));
        }

        [Test]
        public void SmallQueueRemainsUntouched()
        {
            var current = Wheel(2f);
            Event.EnqueueTestEvent(Wheel(1f));
            WheelEventQueue.Compact(current);
            Assert.That(current.delta.y, Is.EqualTo(2f));
            Assert.That(Event.GetEventCount(), Is.EqualTo(1));
        }

        [Test]
        public void LayoutCompactsAllRunsBeforeInputAndKeepsReversals()
        {
            for (int i = 0; i < 10000; i++) Event.EnqueueTestEvent(Wheel(1f));
            for (int i = 0; i < 10000; i++) Event.EnqueueTestEvent(Wheel(-1f));
            Event.EnqueueTestEvent(new Event { type = EventType.MouseDown, button = 1 });
            for (int i = 0; i < 10000; i++) Event.EnqueueTestEvent(Wheel(1f));
            WheelEventQueue.Compact(new Event { type = EventType.Layout });
            Assert.That(Event.GetEventCount(), Is.EqualTo(4));
            var next = new Event();
            Event.PopEvent(next);
            Assert.That(next.delta.y, Is.EqualTo(10000f));
            Assert.That(WheelEventQueue.UsesLogicalDelta(next), Is.True);
            Event.PopEvent(next);
            Assert.That(next.delta.y, Is.EqualTo(-10000f));
            Event.PopEvent(next);
            Assert.That(next.type, Is.EqualTo(EventType.MouseDown));
            Assert.That(next.button, Is.EqualTo(1));
            Event.PopEvent(next);
            Assert.That(next.delta.y, Is.EqualTo(10000f));
        }

        [Test]
        public void UnmergeableStormIsScannedOncePerFrame()
        {
            const int count = 10000;
            for (int i = 0; i < count; i++)
            {
                var e = Wheel(1f);
                e.mousePosition = new Vector2(i, 10f);
                Event.EnqueueTestEvent(e);
            }
            var next = new Event { type = EventType.Layout };
            WheelEventQueue.Compact(next);
            Assert.That(Event.GetEventCount(), Is.EqualTo(count));
            for (int i = 0; i < count; i++)
            {
                Assert.That(Event.PopEvent(next), Is.True);
                Assert.That(next.mousePosition.x, Is.EqualTo(i));
                WheelEventQueue.Compact(next);
            }
            Assert.That(Event.PopCalls, Is.EqualTo(count * 2));
            Time.frameCount++;
            Assert.That(WheelEventQueue.UsesLogicalDelta(next), Is.False);
        }

        [Test]
        public void DifferentDisplaysKeepSeparateWheelRunsAndOrder()
        {
            for (int display = 0; display < 3; display++)
                for (int i = 0; i < 12; i++)
                {
                    var wheel = Wheel(display + 1);
                    wheel.displayIndex = display;
                    Event.EnqueueTestEvent(wheel);
                }
            WheelEventQueue.Compact(new Event { type = EventType.Layout });
            Assert.That(Event.GetEventCount(), Is.EqualTo(3));
            var next = new Event();
            for (int display = 0; display < 3; display++)
            {
                Assert.That(Event.PopEvent(next), Is.True);
                Assert.That(next.displayIndex, Is.EqualTo(display));
                Assert.That(next.delta.y, Is.EqualTo(12f * (display + 1)));
            }
        }

        static Event Wheel(float y) => new Event
        {
            type = EventType.ScrollWheel,
            mousePosition = new Vector2(10f, 10f),
            delta = new Vector2(0f, y)
        };
    }
}
