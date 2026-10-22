using System.Reflection;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class QuitInterceptorTests
    {
        [SetUp]
        public void Reset()
        {
            var phase = typeof(QuitInterceptor).GetField(
                "_phase",
                BindingFlags.Static | BindingFlags.NonPublic
            );
            phase.SetValue(null, System.Enum.ToObject(phase.FieldType, 0));
            typeof(QuitInterceptor)
                .GetField("_notice", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, null);
            UnityEngine.Application.wantsToQuit = null;
            SaveCoordinator.Programmatic = false;
            Verse.Root.Shutdowns = 0;
            Verse.Find.WindowStack = new Verse.WindowStack();
            Settings.S.askOnExit = true;
            Settings.S.stopSessionsOnExit = false;
            SessionHub.Instance = new SessionHub();
            QuitInterceptor.Register();
        }

        [Test]
        public void RepeatedExternalCloseWaitsForOneDeferredShutdown()
        {
            Settings.S.askOnExit = false;
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.False);
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.False);
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            QuitInterceptor.Check();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.True);
        }

        [Test]
        public void ProgrammaticShutdownDoesNotQueueAnotherShutdown()
        {
            SaveCoordinator.Programmatic = true;
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.True);
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
        }

        static AlertDialog.Notice Notice => (AlertDialog.Notice)Verse.Find.WindowStack[0];

        static void Accept()
        {
            var notice = Notice;
            Verse.Find.WindowStack.Remove(notice);
            notice.Action();
        }

        [Test]
        public void GmAndExternalCloseShareOneConfirmationAndCancelCanBeRetried()
        {
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            var cancelled = Notice;
            cancelled.CheckboxChanged(true);
            Assert.That(cancelled.Title, Is.EqualTo("Quit SlopWorld?"));
            Assert.That(UnityEngine.Application.wantsToQuit(), Is.False);
            QuitInterceptor.Check();
            Assert.That(Verse.Find.WindowStack.Count, Is.EqualTo(1));
            Verse.Find.WindowStack.Remove(cancelled);
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            cancelled.Action();
            QuitInterceptor.Check();
            Assert.That(Settings.S.askOnExit, Is.True, "cancelled choice must not disable Ask");
            Assert.That(
                Verse.Root.Shutdowns,
                Is.Zero,
                "stale confirmation cannot accept new attempt"
            );
            Accept();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void ConfirmationListsOnlyActiveSessionsWhenStoppingIsEnabled()
        {
            var hub = SessionHub.Instance;
            hub.Sessions["z-agent"] = new SessionInfo { Name = "z-agent", Alive = true };
            hub.Sessions["a-shell"] = new SessionInfo
            {
                Name = "a-shell",
                Alive = true,
                Host = true,
            };
            hub.Sessions["stopped"] = new SessionInfo { Name = "stopped" };
            Settings.S.stopSessionsOnExit = true;
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            Assert.That(
                Notice.Text,
                Does.Contain("Active sessions will stop (2):\n• a-shell\n• z-agent")
            );
            Assert.That(Notice.Text, Does.Not.Contain("stopped"));
            Assert.That(hub.SessionStore.ExitStops, Is.Empty, "listing does not stop anything");
        }

        [Test]
        public void ConfirmationOmitsSessionListWhenSessionsWillKeepRunning()
        {
            SessionHub.Instance.Sessions["agent"] = new SessionInfo
            {
                Name = "agent",
                Alive = true,
            };
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            Assert.That(Notice.Text, Does.Contain("Active sessions will keep running."));
            Assert.That(Notice.Text, Does.Not.Contain("• agent"));
        }

        [Test]
        public void ConfirmationExplainsWhenThereAreNoActiveSessions()
        {
            Settings.S.stopSessionsOnExit = true;
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            Assert.That(Notice.Text, Does.Contain("No active sessions to stop."));
        }

        [Test]
        public void DontAskAgainPersistsBeforeShutdownAndSkipsNextConfirmation()
        {
            string previousProfile = Verse.GenFilePaths.SaveDataFolderPath;
            string profile = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "slop-quit-" + System.Guid.NewGuid()
            );
            try
            {
                Verse.GenFilePaths.SaveDataFolderPath = profile;
                QuitInterceptor.Request();
                QuitInterceptor.Check();
                Assert.That(Notice.CheckboxLabel, Is.EqualTo("Don't ask again"));
                Notice.CheckboxChanged(true);
                Assert.That(
                    Settings.S.askOnExit,
                    Is.True,
                    "choice applies only when Quit is confirmed"
                );
                Accept();
                Assert.That(Settings.S.askOnExit, Is.False);
                Assert.That(
                    ProfileSettings.Load().askOnExit,
                    Is.False,
                    "saved before the next update"
                );
                QuitInterceptor.Check();
                Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));

                Reset();
                Settings.S.askOnExit = ProfileSettings.Load().askOnExit;
                QuitInterceptor.Request();
                QuitInterceptor.Check();
                Assert.That(Verse.Find.WindowStack.Count, Is.Zero);
                Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
            }
            finally
            {
                Verse.GenFilePaths.SaveDataFolderPath = previousProfile;
                if (System.IO.Directory.Exists(profile))
                    System.IO.Directory.Delete(profile, true);
            }
        }

        [Test]
        public void UncheckingDontAskAgainKeepsAskEnabled()
        {
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            Notice.CheckboxChanged(true);
            Notice.CheckboxChanged(false);
            Accept();
            Assert.That(Settings.S.askOnExit, Is.True);
        }

        [Test]
        public void SessionStoppingRefreshesAndWaitsForEveryAcknowledgement()
        {
            Settings.S.stopSessionsOnExit = true;
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            var hub = SessionHub.Instance;
            hub.Sessions["agent"] = new SessionInfo { Name = "agent", Alive = true };
            hub.Sessions["shell"] = new SessionInfo
            {
                Name = "shell",
                Alive = true,
                Host = true,
            };
            hub.Sessions["stopped"] = new SessionInfo { Name = "stopped" };
            Accept();
            QuitInterceptor.Check();
            Assert.That(hub.SessionStore.ExitStops.Count, Is.Zero, "await authoritative refresh");
            hub.SessionStore.Refreshed();
            Assert.That(hub.SessionStore.ExitStops.Count, Is.EqualTo(2));
            hub.SessionStore.ExitStops[0].Done();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            hub.SessionStore.ExitStops[1].Done();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void StopFailureCanRetryAndLateCallbacksCannotFinishTheRetry()
        {
            Settings.S.askOnExit = false;
            Settings.S.stopSessionsOnExit = true;
            var hub = SessionHub.Instance;
            hub.Sessions["agent"] = new SessionInfo { Name = "agent", Alive = true };
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            hub.SessionStore.Refreshed();
            var failed = hub.SessionStore.ExitStops[0];
            failed.Fail("stop rejected");
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            Assert.That(Notice.Text, Does.Contain("stop rejected"));
            Accept();
            failed.Done();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            hub.SessionStore.Refreshed();
            hub.SessionStore.ExitStops[1].Done();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ConnectionChangeBeforeShutdownRequiresQuitAnyway(bool empty, bool offline)
        {
            Settings.S.askOnExit = false;
            Settings.S.stopSessionsOnExit = true;
            var hub = SessionHub.Instance;
            if (!empty)
                hub.Sessions["agent"] = new SessionInfo { Name = "agent", Alive = true };
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            hub.SessionStore.Refreshed();
            if (!empty)
                hub.SessionStore.ExitStops[0].Done();

            // Model the hub update between pumping HTTP completions and checking quit.
            if (offline)
                hub.Online = false;
            else
                hub.ConnectionGeneration++;
            QuitInterceptor.Check();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            Assert.That(Notice.Text, Does.Contain("connection changed"));
            var notice = Notice;
            Verse.Find.WindowStack.Remove(notice);
            notice.SecondaryAction();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void OfflineDaemonRequiresExplicitQuitAnyway()
        {
            Settings.S.askOnExit = false;
            Settings.S.stopSessionsOnExit = true;
            SessionHub.Instance.Online = false;
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            Notice.SecondaryAction();
            QuitInterceptor.Check();
            Assert.That(Verse.Root.Shutdowns, Is.EqualTo(1));
        }

        [Test]
        public void ReconnectDuringRefreshDoesNotStopSessionsOnNewConnection()
        {
            Settings.S.askOnExit = false;
            Settings.S.stopSessionsOnExit = true;
            QuitInterceptor.Request();
            QuitInterceptor.Check();
            SessionHub.Instance.ConnectionGeneration++;
            SessionHub.Instance.SessionStore.Refreshed();
            QuitInterceptor.Check();
            Assert.That(SessionHub.Instance.SessionStore.ExitStops.Count, Is.Zero);
            Assert.That(Verse.Root.Shutdowns, Is.Zero);
            Assert.That(Notice.Text, Does.Contain("connection changed"));
        }

        [TearDown]
        public void CleanUp()
        {
            Settings.S.askOnExit = true;
            Settings.S.stopSessionsOnExit = false;
        }
    }
}

namespace UnityEngine
{
    public static partial class Application
    {
        public static System.Func<bool> wantsToQuit;
    }
}

namespace Verse
{
    public static class Root
    {
        public static int Shutdowns;

        public static void Update() { }

        public static void Shutdown()
        {
            Shutdowns++;
            SlopWorld.SaveCoordinator.Programmatic = true;
            NUnit.Framework.Assert.That(
                UnityEngine.Application.wantsToQuit(),
                NUnit.Framework.Is.True
            );
        }
    }
}

namespace SlopWorld
{
    sealed partial class SessionHub
    {
        public int ConnectionGeneration;
    }

    sealed partial class PagerTestStore
    {
        public System.Collections.Generic.IEnumerable<SessionInfo> Sessions =>
            SessionHub.Instance.Sessions.Values;
        public readonly System.Collections.Generic.List<(
            string Name,
            System.Action<string> Fail,
            System.Action Done
        )> ExitStops = new();

        public void Stop(string name, System.Action<string> fail, System.Action done) =>
            ExitStops.Add((name, fail, done));
    }

    public static class SaveCoordinator
    {
        public static bool Programmatic;

        public static bool ConsumeProgrammaticShutdown()
        {
            bool result = Programmatic;
            Programmatic = false;
            return result;
        }
    }
}
