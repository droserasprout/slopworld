using NUnit.Framework;
using WindowChange = SlopWorld.LinuxWindowRecovery.WindowChange;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LinuxWindowRecoveryTests
    {
        LinuxWindowRecovery _recovery;

        [SetUp]
        public void Reset()
        {
            _recovery = new LinuxWindowRecovery();
            _recovery.CompleteAttempt(0f, true, false, WindowChange.Applied);
            _recovery.WatchWindow();
        }

        [Test]
        public void WatchStopsAfterQuietSettlingAndCanBeRearmed()
        {
            Assert.That(_recovery.WatchingWindow(10f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(10.9f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(11f, false), Is.False);
            _recovery.WatchWindow();
            Assert.That(_recovery.WatchingWindow(100f, false), Is.True);
        }

        [Test]
        public void LoadingKeepsWatchUntilQuietSettling()
        {
            Assert.That(_recovery.WatchingWindow(10f, true), Is.True);
            Assert.That(_recovery.WatchingWindow(100f, true), Is.True);
            Assert.That(_recovery.WatchingWindow(200f, true), Is.True);
            Assert.That(_recovery.WatchingWindow(300f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(301f, false), Is.False);
        }

        [Test]
        public void NewLoadingWorkRestartsQuietSettling()
        {
            Assert.That(_recovery.WatchingWindow(10f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(10.9f, true), Is.True);
            Assert.That(_recovery.WatchingWindow(11f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(11.9f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(12f, false), Is.False);
        }

        [Test]
        public void LoadingAfterWatchRetiresRearmsRecoveryAndRetainsActiveResize()
        {
            Assert.That(_recovery.WatchingWindow(10f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(11f, false), Is.False);
            _recovery.BeginResize(10f);
            _recovery.CompleteAttempt(199f, true, false, WindowChange.Retry);

            Assert.That(_recovery.WatchingWindow(100f, true), Is.True);
            Assert.That(_recovery.ResyncedAt, Is.EqualTo(-1f));
            Assert.That(_recovery.NextTry, Is.Zero);

            _recovery.BeginResize(100f);
            Assert.That(_recovery.WatchingWindow(100.1f, true), Is.True);
            Assert.That(_recovery.ResyncedAt, Is.EqualTo(100f));
            Assert.That(_recovery.WatchingWindow(110f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(111f, false), Is.False);
            Assert.That(_recovery.WatchingWindow(200f, false), Is.False);
        }

        [Test]
        public void RepeatedTransitionClearsPreviousResizeDeadline()
        {
            _recovery.BeginResize(20f);
            _recovery.WatchWindow();
            Assert.That(_recovery.HasResize, Is.False);
            _recovery.BeginResize(30f);
            _recovery.WatchWindow();
            Assert.That(_recovery.ResyncedAt, Is.EqualTo(-1f));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnsupportedIntegrationCannotBeRearmed(bool loading)
        {
            _recovery.BeginResize(10f);
            _recovery.CompleteAttempt(199f, true, false, WindowChange.Unsupported);
            Assert.That(_recovery.WatchingWindow(100f, loading), Is.False);
            _recovery.WatchWindow();
            Assert.That(_recovery.WatchingWindow(101f, loading), Is.False);
            Assert.That(_recovery.NextTry, Is.EqualTo(200f));
            Assert.That(_recovery.ResyncedAt, Is.EqualTo(10f));
            Assert.That(_recovery.TryBeginAttempt(300f, false, loading, out _), Is.False);
        }

        [Test]
        public void UnsupportedIntegrationSkipsStartup()
        {
            var recovery = new LinuxWindowRecovery();
            recovery.CompleteAttempt(0f, true, false, WindowChange.Unsupported);
            Assert.That(recovery.TryBeginAttempt(100f, true, true, out _), Is.False);
            Assert.That(recovery.ReadySince, Is.EqualTo(-1f));
        }

        [Test]
        public void TimedOutRecoveryRearmsResizeUntilGeometryIsConfirmed()
        {
            _recovery.BeginResize(10f);
            Assert.That(_recovery.ResyncLanded(10.5f, false), Is.True);
            Assert.That(_recovery.AwaitFullscreenRecovery(true, false), Is.EqualTo(WindowChange.Retry));
            Assert.That(_recovery.HasResize, Is.False);

            _recovery.BeginResize(12f);
            Assert.That(_recovery.ResyncLanded(12.5f, false), Is.True);
            Assert.That(_recovery.AwaitFullscreenRecovery(true, false), Is.EqualTo(WindowChange.Retry));
            Assert.That(_recovery.HasResize, Is.False);
            // Correct surface dimensions still require native fullscreen confirmation.
            Assert.That(_recovery.AwaitFullscreenRecovery(true, true), Is.EqualTo(WindowChange.WaitingForResize));
        }

        [Test]
        public void FailedFullscreenRequestRetainsResizeAttemptForRetry()
        {
            _recovery.BeginResize(10f);
            Assert.That(_recovery.AwaitFullscreenRecovery(false, false), Is.EqualTo(WindowChange.Retry));
            Assert.That(_recovery.ResyncedAt, Is.EqualTo(10f));
        }

        [Test]
        public void ResizeTimeoutAllowsNextStageWithoutConfirmingGeometry()
        {
            _recovery.BeginResize(10f);
            Assert.That(_recovery.ResyncLanded(10.1f, false), Is.False);
            Assert.That(_recovery.ResyncLanded(10.5f, false), Is.True);
            Assert.That(_recovery.ResyncLanded(10.1f, true), Is.True);
        }

        [Test]
        public void StartupSettlesBeforeFirstAttemptAndRetryUsesCompletionTime()
        {
            var recovery = new LinuxWindowRecovery();
            Assert.That(recovery.TryBeginAttempt(10f, true, false, out _), Is.False);
            Assert.That(recovery.TryBeginAttempt(11.4f, true, false, out _), Is.False);
            Assert.That(recovery.TryBeginAttempt(11.5f, true, false, out bool watch), Is.True);
            Assert.That(watch, Is.True);
            recovery.CompleteAttempt(12f, true, watch, WindowChange.Retry);
            Assert.That(recovery.Applied, Is.Null);
            Assert.That(recovery.TryBeginAttempt(12.9f, true, false, out _), Is.False);
            Assert.That(recovery.TryBeginAttempt(13f, true, false, out _), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnconfirmedRecoveryRestartsQuietSettling(bool waiting)
        {
            Assert.That(_recovery.WatchingWindow(10f, false), Is.True);
            _recovery.CompleteAttempt(10.9f, true, true,
                waiting ? WindowChange.WaitingForResize : WindowChange.Retry);
            Assert.That(_recovery.WatchingWindow(11f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(11.9f, false), Is.True);
            Assert.That(_recovery.WatchingWindow(12f, false), Is.False);
        }

        [Test]
        public void ConfirmedWatchPollsQuicklyThenLeavesUnchangedPreferenceIdle()
        {
            Assert.That(_recovery.TryBeginAttempt(10f, true, false, out bool watch), Is.True);
            _recovery.CompleteAttempt(10f, true, watch, WindowChange.Applied);
            Assert.That(_recovery.TryBeginAttempt(10.01f, true, false, out _), Is.False);
            Assert.That(_recovery.TryBeginAttempt(10.05f, true, false, out _), Is.True);
            Assert.That(_recovery.TryBeginAttempt(11f, true, false, out _), Is.False);
            Assert.That(_recovery.TryBeginAttempt(11f, false, false, out _), Is.True);
        }

        [Test]
        public void WaitingPollsQuicklyAndPreferenceChangeCanBypassRetryDelay()
        {
            _recovery.CompleteAttempt(10f, true, true, WindowChange.WaitingForResize);
            Assert.That(_recovery.TryBeginAttempt(10.01f, true, true, out _), Is.False);
            Assert.That(_recovery.TryBeginAttempt(10.05f, true, true, out _), Is.True);
            _recovery.CompleteAttempt(11f, true, true, WindowChange.Retry);
            Assert.That(_recovery.TryBeginAttempt(11.1f, false, false, out _), Is.False);
            _recovery.RetryNow();
            Assert.That(_recovery.TryBeginAttempt(11.1f, false, false, out _), Is.True);
        }
    }
}
