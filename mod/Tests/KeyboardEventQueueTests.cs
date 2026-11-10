using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class KeyboardEventQueueTests
    {
        [Test]
        public void LowFrameRateBatchPreservesTextEnterAndFollowingDotOrder()
        {
            var queue = new KeyboardEventQueue();
            queue.Text('a', 0);
            queue.Text('.', 0);
            queue.Key(KeyCode.Return, true, false, 0);
            queue.Key(KeyCode.Return, false, false, 0);
            queue.Text('b', EventModifiers.Shift);
            queue.Key(KeyCode.Return, true, false, EventModifiers.Shift);
            queue.Text('.', 0);
            var batch = queue.Drain();
            Assert.That(
                batch.Select(e => e.Character),
                Is.EqualTo(new[] { 'a', '.', '\0', '\0', 'b', '\0', '.' })
            );
            Assert.That(
                batch[2].Modifiers,
                Is.EqualTo((EventModifiers)0),
                "later Shift must not turn an earlier Enter into a newline"
            );
            Assert.That(batch[5].Modifiers, Is.EqualTo(EventModifiers.Shift));
            Assert.That(batch[6].Modifiers, Is.EqualTo((EventModifiers)0));
            Assert.That(batch[3].Type, Is.EqualTo(EventType.KeyUp));
            Assert.That(queue.Drain(), Is.Empty, "dispatch cannot replay the same frame");
        }

        [Test]
        public void RepeatsAndSurrogatePairsAreNotCollapsedByFrameBatching()
        {
            var queue = new KeyboardEventQueue();
            foreach (char c in "🦀")
                queue.Text(c, 0);
            queue.Key(KeyCode.Backspace, true, false, 0);
            queue.Key(KeyCode.Backspace, true, true, 0);
            queue.Key(KeyCode.Backspace, true, true, 0);
            var batch = queue.Drain();
            Assert.That(
                new string(batch.Take(2).Select(e => e.Character).ToArray()),
                Is.EqualTo("🦀")
            );
            Assert.That(batch.Length, Is.EqualTo(5));
            Assert.That(
                batch.Skip(2).Select(e => e.Repeat),
                Is.EqualTo(new[] { false, true, true })
            );
        }

        [Test]
        public void FocusResetDiscardsUndispatchedInput()
        {
            var queue = new KeyboardEventQueue();
            queue.Text('.', 0);
            queue.Key(KeyCode.Return, true, false, EventModifiers.Shift);
            queue.Clear();
            queue.Text('x', 0);
            Assert.That(queue.Drain().Single().Character, Is.EqualTo('x'));
        }
    }
}
