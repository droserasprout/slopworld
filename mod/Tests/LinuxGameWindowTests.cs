using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LinuxGameWindowTests
    {
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        static void Set(string name, object value) =>
            typeof(LinuxGameWindow).GetField(name, PrivateStatic).SetValue(null, value);

        static T Call<T>(string name, params object[] args) =>
            (T)typeof(LinuxGameWindow).GetMethod(name, PrivateStatic).Invoke(null, args);

        [SetUp]
        public void Reset()
        {
            NextPlanet.Pending = false;
            Verse.LongEventHandler.AnyEventNowOrWaiting = false;
            Set("_applied", true);
            Display.main.systemWidth = Screen.width = 1920;
            Display.main.systemHeight = Screen.height = 1080;
            LinuxGameWindow.WatchWindow();
        }

        [TearDown]
        public void ClearLoading()
        {
            NextPlanet.Pending = false;
            Verse.LongEventHandler.AnyEventNowOrWaiting = false;
            Screen.width = 1920;
            Screen.height = 1080;
        }

        [Test]
        public void WatchStopsAfterQuietSettlingAndCanBeRearmed()
        {
            Assert.That(Call<bool>("WatchingWindow", 10f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 10.9f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 11f), Is.False);
            LinuxGameWindow.WatchWindow();
            Assert.That(Call<bool>("WatchingWindow", 100f), Is.True);
        }

        [Test]
        public void PendingLandingHandsWatchToQueuedGenerationBeforeSettling()
        {
            NextPlanet.Pending = true;
            Assert.That(Call<bool>("WatchingWindow", 10f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 100f), Is.True);
            NextPlanet.Pending = false;
            Verse.LongEventHandler.AnyEventNowOrWaiting = true;
            Assert.That(Call<bool>("WatchingWindow", 200f), Is.True);
            Verse.LongEventHandler.AnyEventNowOrWaiting = false;
            Assert.That(Call<bool>("WatchingWindow", 300f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 301f), Is.False);
        }

        [Test]
        public void NewLoadingWorkRestartsQuietSettling()
        {
            Assert.That(Call<bool>("WatchingWindow", 10f), Is.True);
            Verse.LongEventHandler.AnyEventNowOrWaiting = true;
            Assert.That(Call<bool>("WatchingWindow", 10.9f), Is.True);
            Verse.LongEventHandler.AnyEventNowOrWaiting = false;
            Assert.That(Call<bool>("WatchingWindow", 11f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 11.9f), Is.True);
            Assert.That(Call<bool>("WatchingWindow", 12f), Is.False);
        }

        [Test]
        public void RepeatedTransitionClearsPreviousResizeDeadline()
        {
            Set("_resyncedAt", 20f);
            LinuxGameWindow.WatchWindow();
            Assert.That(typeof(LinuxGameWindow).GetField("_resyncedAt", PrivateStatic).GetValue(null),
                Is.EqualTo(-1f));
        }

        [Test]
        public void TimedOutRecoveryRearmsResizeUntilGeometryIsConfirmed()
        {
            Screen.height = 1000;
            Set("_resyncedAt", 10f);
            Assert.That(Call<bool>("ResyncLanded", 10.5f), Is.True);
            Assert.That(Call<object>("AwaitFullscreenRecovery", true).ToString(), Is.EqualTo("Retry"));
            Assert.That(typeof(LinuxGameWindow).GetField("_resyncedAt", PrivateStatic).GetValue(null),
                Is.EqualTo(-1f));

            // A second unsuccessful resize must also permit another surface request.
            Set("_resyncedAt", 12f);
            Assert.That(Call<bool>("ResyncLanded", 12.5f), Is.True);
            Assert.That(Call<object>("AwaitFullscreenRecovery", true).ToString(), Is.EqualTo("Retry"));
            Assert.That(typeof(LinuxGameWindow).GetField("_resyncedAt", PrivateStatic).GetValue(null),
                Is.EqualTo(-1f));

            Screen.height = 1080;
            // Correct surface dimensions still require native fullscreen confirmation.
            Assert.That(Call<object>("AwaitFullscreenRecovery", true).ToString(),
                Is.EqualTo("WaitingForResize"));
        }

        [Test]
        public void FailedFullscreenRequestRetainsResizeAttemptForRetry()
        {
            Set("_resyncedAt", 10f);
            Assert.That(Call<object>("AwaitFullscreenRecovery", false).ToString(), Is.EqualTo("Retry"));
            Assert.That(typeof(LinuxGameWindow).GetField("_resyncedAt", PrivateStatic).GetValue(null),
                Is.EqualTo(10f));
        }

        [Test]
        public void ResizeTimeoutAllowsNextStageButDoesNotConfirmSurfaceGeometry()
        {
            Screen.height = 1000;
            Set("_resyncedAt", 10f);
            Assert.That(Call<bool>("ResyncLanded", 10.1f), Is.False);
            Assert.That(Call<bool>("ResyncLanded", 10.5f), Is.True);
            Assert.That(Call<bool>("SurfaceSized"), Is.False);
            Screen.height = 1080;
            Assert.That(Call<bool>("SurfaceSized"), Is.True);
            Display.main.systemHeight = 0;
            Assert.That(Call<bool>("SurfaceSized"), Is.False);
            Display.main.systemHeight = 1080;
        }
    }
}
