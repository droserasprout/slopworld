using System.Collections.Generic;
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
        public void OpeningShortcutDiscardsDelayedTextAndRepeatsAcrossFrames()
        {
            var queue = new KeyboardEventQueue();
            queue.Key(KeyCode.T, true, false, 0);
            queue.Drain(); // The map's native key pass opens the terminal.
            queue.SuppressOpeningKey(KeyCode.T);
            queue.Clear(); // Waylith begins another frame before the text callback.
            queue.Text('t', 0);
            queue.Key(KeyCode.T, true, true, 0);
            queue.Text('t', 0);
            queue.Key(KeyCode.T, false, false, 0);
            queue.Text('t', 0);
            Assert.That(Delivered(queue), Is.Empty);

            queue.Key(KeyCode.T, true, false, 0);
            queue.Text('t', 0);
            Assert.That(Delivered(queue).Select(e => e.Character), Is.EqualTo(new[] { '\0', 't' }));
        }

        [Test]
        public void OpeningDuringReplayPreservesFollowingPressInSameBatch()
        {
            var queue = new KeyboardEventQueue();
            queue.Key(KeyCode.T, true, false, 0);
            queue.Text('t', 0);
            queue.Key(KeyCode.T, false, false, 0);
            queue.Key(KeyCode.T, true, false, 0);
            queue.Text('t', 0);
            var batch = queue.Drain();
            queue.Dispatch(batch[0], () => queue.SuppressOpeningKey(KeyCode.T));
            var delivered = new List<KeyboardEventQueue.Entry>();
            foreach (var input in batch.Skip(1))
                queue.Dispatch(input, () => delivered.Add(input));
            Assert.That(delivered.Select(e => e.Character), Is.EqualTo(new[] { '\0', 't' }));
        }

        [Test]
        public void ReboundOpeningKeyDoesNotDiscardAnotherKeysText()
        {
            var queue = new KeyboardEventQueue();
            queue.Key(KeyCode.O, true, false, 0);
            queue.SuppressOpeningKey(KeyCode.O);
            queue.Text('ö', 0);
            queue.Key(KeyCode.A, true, false, 0);
            queue.Text('a', 0);
            Assert.That(Delivered(queue).Select(e => e.Character), Is.EqualTo(new[] { '\0', 'a' }));
        }

        [Test]
        public void FocusResetClearsOpeningStrokeSuppression()
        {
            var queue = new KeyboardEventQueue();
            queue.Key(KeyCode.T, true, false, 0);
            queue.SuppressOpeningKey(KeyCode.T);
            queue.Reset();
            queue.Text('t', 0);
            Assert.That(Delivered(queue).Single().Character, Is.EqualTo('t'));
        }

        static List<KeyboardEventQueue.Entry> Delivered(KeyboardEventQueue queue)
        {
            var delivered = new List<KeyboardEventQueue.Entry>();
            foreach (var input in queue.Drain())
                queue.Dispatch(input, () => delivered.Add(input));
            return delivered;
        }

        [Test]
        public void FocusResetDiscardsUndispatchedInput()
        {
            var queue = new KeyboardEventQueue();
            queue.Text('.', 0);
            queue.Key(KeyCode.Return, true, false, EventModifiers.Shift);
            queue.Reset();
            queue.Text('x', 0);
            Assert.That(queue.Drain().Single().Character, Is.EqualTo('x'));
        }
    }
}
