using NUnit.Framework;

namespace SlopWorld.Tests
{
    public class CursorAnimationTests
    {
        [Test]
        public void PatSuppressesClicksAndRepeatedPatsWithoutExtendingItsLifetime()
        {
            var animation = new CursorAnimation();
            animation.Pat(1f);
            Assert.That(animation.Step(1.125f), Is.EqualTo(CursorAnimation.Steps));

            animation.Click(1.4f);
            animation.Pat(1.4f);
            Assert.That(animation.Step(1.5f), Is.Zero);
            Assert.That(animation.PatActive, Is.False);
            Assert.That(animation.Step(1.51f), Is.Zero);

            animation.Click(1.6f);
            Assert.That(animation.Step(1.61f), Is.GreaterThan(0));
        }

        [Test]
        public void PatReplacesClickAndReturnsToRestBetweenWaggles()
        {
            var animation = new CursorAnimation();
            animation.Click(1f);
            animation.Pat(1.01f);
            Assert.That(animation.Step(1.01f), Is.Zero);
            Assert.That(animation.Step(1.135f), Is.EqualTo(CursorAnimation.Steps));
            Assert.That(animation.Step(1.26f), Is.Zero);
            Assert.That(animation.PatActive, Is.True);
            Assert.That(animation.Step(1.385f), Is.EqualTo(CursorAnimation.Steps));
            Assert.That(animation.Step(1.51f), Is.Zero);
            Assert.That(animation.PatActive, Is.False);
        }

        [Test]
        public void RepeatedClicksRestartTheShortAnimation()
        {
            var animation = new CursorAnimation();
            animation.Click(1f);
            int clickFrame = animation.Step(1.01f);
            Assert.That(clickFrame, Is.GreaterThan(0));
            animation.Click(1.1f);
            Assert.That(animation.Step(1.2f), Is.EqualTo(clickFrame));
            Assert.That(animation.Step(1.22f), Is.Zero);
        }

        [Test]
        public void RepeatedResetClearsEitherAnimationAndAllowsAnotherPat()
        {
            var animation = new CursorAnimation();
            animation.Click(1f);
            animation.Reset();
            Assert.That(animation.Step(1.01f), Is.Zero);

            animation.Pat(2f);
            animation.Reset();
            animation.Reset();
            Assert.That(animation.PatActive, Is.False);
            Assert.That(animation.Step(2.125f), Is.Zero);

            animation.Pat(3f);
            Assert.That(animation.Step(3.125f), Is.EqualTo(CursorAnimation.Steps));
        }
    }
}
