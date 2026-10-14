using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class UiScaleDragTests
    {
        [Test]
        public void LostNativeCaptureCommitsRetainedScaleOnceMouseIsUp()
        {
            var drag = new UiScaleDrag();
            Assert.That(drag.Update(1.5f, held: true, released: false, mouseDown: true), Is.Null);
            Assert.That(drag.Update(2f, held: true, released: false, mouseDown: true), Is.Null);
            // Native state can disappear before release, resetting its result to the saved scale.
            Assert.That(drag.Update(1f, held: false, released: false, mouseDown: true), Is.Null);
            Assert.That(drag.Pending, Is.EqualTo(2f));
            Assert.That(
                drag.Update(1f, held: false, released: false, mouseDown: false),
                Is.EqualTo(2f)
            );
            Assert.That(drag.Pending, Is.Null);
            Assert.That(drag.Update(1f, held: false, released: false, mouseDown: false), Is.Null);
        }

        [Test]
        public void ReportedReleaseAppliesFinalPointerSampleAndAllowsAnotherDrag()
        {
            var drag = new UiScaleDrag();
            Assert.That(drag.Update(1.5f, held: true, released: false, mouseDown: true), Is.Null);
            Assert.That(
                drag.Update(1.75f, held: false, released: true, mouseDown: true),
                Is.EqualTo(1.75f)
            );
            Assert.That(drag.Pending, Is.Null);
            Assert.That(drag.Update(2f, held: true, released: false, mouseDown: true), Is.Null);
            Assert.That(
                drag.Update(2.25f, held: false, released: true, mouseDown: false),
                Is.EqualTo(2.25f)
            );
        }

        [Test]
        public void HeldEventRetainsValueEvenBeforePhysicalButtonStateUpdates()
        {
            var drag = new UiScaleDrag();
            Assert.That(drag.Update(1.5f, held: true, released: false, mouseDown: false), Is.Null);
            Assert.That(drag.Pending, Is.EqualTo(1.5f));
        }

        [Test]
        public void IdleReleaseDoesNotChangeScale()
        {
            var drag = new UiScaleDrag();
            Assert.That(drag.Update(3f, held: false, released: true, mouseDown: false), Is.Null);
            Assert.That(drag.Pending, Is.Null);
        }
    }
}
