using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LoadingStreamSessionTests
    {
        [Test]
        public void StartupQueueGapsAndAutoResumePreserveTextAndFullScreen()
        {
            var session = new LoadingStreamSession();
            Assert.That(session.Begin(true), Is.True);
            Assert.That(session.DrawFullScreen(false), Is.True);
            // No current event during a scene switch must not reset streaming text.
            Assert.That(session.Begin(false), Is.False);
            // The entry menu queued auto-resume during its draw.
            session.InteractiveFrame(eventsPending: true);
            Assert.That(session.Begin(true), Is.False);
            // A synchronous event now requests a bubble, but the full-screen load continues.
            Assert.That(session.DrawFullScreen(true), Is.True);
            Assert.That(session.Begin(false), Is.False);
            Assert.That(session.Begin(true), Is.False);
        }

        [Test]
        public void RealInteractiveFrameEndsLoadAndAllowsNormalGameWindows()
        {
            var session = new LoadingStreamSession();
            session.Begin(true);
            session.DrawFullScreen(false);
            session.InteractiveFrame(eventsPending: false);
            Assert.That(session.Begin(false), Is.False);
            Assert.That(session.Begin(true), Is.True);
            Assert.That(session.DrawFullScreen(true), Is.False);
            session.InteractiveFrame(eventsPending: false);
            Assert.That(session.Begin(true), Is.True);
            Assert.That(session.DrawFullScreen(false), Is.True);
        }
    }
}
