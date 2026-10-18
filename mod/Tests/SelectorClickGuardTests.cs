using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class SelectorClickGuardTests
    {
        [Test]
        public void RepeatedClicksAlternateOpeningAndClosingWithoutGlobalPressDispatch()
        {
            var guard = new SelectorClickGuard();
            var source = new Vector2(100f, 200f);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                // Only the activation reaches the selector. No window-stack
                // press callback is needed to open again after a blocked press.
                Assert.That(guard.TryBlock(source), Is.False);
                guard.Dismiss(source, cycle * 10);
                Assert.That(guard.TryBlock(source), Is.True);
            }
            Assert.That(guard.TryBlock(source), Is.False);
        }

        [Test]
        public void DismissalBeforeSelectorDrawSurvivesRepeatedPassesOfTheSamePress()
        {
            var guard = new SelectorClickGuard();
            var source = new Vector2(100f, 200f);

            guard.Dismiss(source, 10);
            guard.BeginPress(10);
            guard.BeginPress(10);
            Assert.That(guard.TryBlock(source), Is.True);
            Assert.That(guard.TryBlock(source), Is.False);
        }

        [Test]
        public void LaterPhysicalPressRetiresDismissalWhenDismissalConsumedTheEvent()
        {
            var guard = new SelectorClickGuard();
            var source = new Vector2(100f, 200f);

            guard.BeginPress(10);
            guard.Dismiss(source, 10);
            // Clicking outside or consuming the dismissal press produces no activation.
            guard.BeginPress(20);
            Assert.That(guard.TryBlock(source), Is.False);
        }

        [Test]
        public void SwitchingSelectorsDoesNotConsumeTheDismissedSourcesPress()
        {
            var guard = new SelectorClickGuard();
            var first = new Vector2(100f, 200f);
            var second = new Vector2(100f, 250f);

            guard.Dismiss(first, 10);
            Assert.That(guard.TryBlock(second), Is.False);
            Assert.That(guard.TryBlock(first), Is.True);
        }
    }
}
