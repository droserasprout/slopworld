using System;
using System.Reflection;
using NUnit.Framework;
using Verse;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class LoadFailureRecoveryTests
    {
        [SetUp]
        public void Reset()
        {
            typeof(LoadFailureRecovery).GetField("<Pending>k__BackingField",
                BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, false);
            typeof(LoadFailureRecovery).GetField("_notice",
                BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
            Find.WindowStack = new WindowStack();
            LongEventHandler.AnyEventNowOrWaiting = false;
            GenScene.InEntryScene = false;
            GenScene.MenuTransitions = 0;
            Scribe.Stops = 0;
            Scribe.mode = LoadSaveMode.LoadingVars;
            QuickStart.Queued = 0;
        }

        static bool Prefix(Type patch, params object[] args) => (bool)patch
            .GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);

        [Test]
        public void FailedLoadSuppressesNativeHandlerAndStopsScribeBeforeSceneCleanup()
        {
            Assert.That(Prefix(typeof(Patch_LoadFailureRecovery), new System.Xml.XmlException("truncated save")), Is.False);
            Assert.That(LoadFailureRecovery.Pending, Is.True);
            Assert.That(Scribe.Stops, Is.EqualTo(1));
            Assert.That(GenScene.MenuTransitions, Is.EqualTo(1));
            Assert.That(Prefix(typeof(Patch_LoadFailureMenu)), Is.False);
            Assert.That(QuickStart.Queued, Is.Zero);
            LoadFailureRecovery.Begin(new Exception("repeated failure"));
            Assert.That(GenScene.MenuTransitions, Is.EqualTo(1));
        }

        [Test]
        public void NoticeWaitsForIdleEntrySceneAndWindowStack()
        {
            LoadFailureRecovery.Begin(new Exception("load failed"));
            LoadFailureRecovery.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.Zero);
            GenScene.InEntryScene = true;
            LongEventHandler.AnyEventNowOrWaiting = true;
            LoadFailureRecovery.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.Zero);
            LongEventHandler.AnyEventNowOrWaiting = false;
            Find.WindowStack = null;
            Assert.DoesNotThrow(LoadFailureRecovery.ShowPendingNotice);
            Find.WindowStack = new WindowStack();
            LoadFailureRecovery.ShowPendingNotice();
            LoadFailureRecovery.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
            Assert.That(((Window)Find.WindowStack[0]).closeOnCancel, Is.False);
        }

        [Test]
        public void NoticeStartsOneColonyDirectlyAndReleasesMenuSuppression()
        {
            LoadFailureRecovery.Begin(new Exception("load failed"));
            GenScene.InEntryScene = true;
            LoadFailureRecovery.ShowPendingNotice();
            var notice = (AlertDialog.Notice)Find.WindowStack[0];
            Assert.That(notice.Primary, Is.EqualTo("New colony"));
            notice.Action();
            notice.Action();
            Assert.That(QuickStart.Queued, Is.EqualTo(1));
            Assert.That(GenScene.MenuTransitions, Is.EqualTo(1));
            Assert.That(LoadFailureRecovery.Pending, Is.False);
            Assert.That(Prefix(typeof(Patch_LoadFailureMenu)), Is.True);
        }

        [Test]
        public void LostNoticeIsDeliveredAgainWithoutStartingAColony()
        {
            LoadFailureRecovery.Begin(new Exception("load failed"));
            GenScene.InEntryScene = true;
            LoadFailureRecovery.ShowPendingNotice();
            Find.WindowStack = new WindowStack();
            LoadFailureRecovery.ShowPendingNotice();
            Assert.That(Find.WindowStack.Count, Is.EqualTo(1));
            Assert.That(QuickStart.Queued, Is.Zero);
        }

        [TearDown]
        public void CleanUp()
        {
            Reset();
            Scribe.mode = LoadSaveMode.Inactive;
        }
    }
}

namespace Verse
{
    public static class GameAndMapInitExceptionHandlers
    {
        public static void ErrorWhileLoadingGame(Exception e) { }
    }

    public static class GenScene
    {
        public static bool InEntryScene;
        public static int MenuTransitions;
        public static void GoToMainMenu()
        {
            Assert.That(SlopWorld.LoadFailureRecovery.Pending, Is.True);
            Assert.That(Scribe.mode, Is.EqualTo(LoadSaveMode.Inactive));
            MenuTransitions++;
        }
    }
}

namespace RimWorld
{
    public static class MainMenuDrawer { public static void MainMenuOnGUI() { } }
}

namespace SlopWorld
{
    public static class QuickStart
    {
        public static int Queued;
        public static void Queue() { Queued++; }
    }
}
