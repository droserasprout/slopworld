using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class DaemonConfigStateTests
    {
        static Wire.ConfigResult Reply(string model) =>
            new Wire.ConfigResult
            {
                Values = new Wire.Config { Daemon = new Wire.Daemon { TitleModel = model } },
                Path = "/config/" + model,
            };

        static DaemonClient.Request Last => DaemonClient.Requests[DaemonClient.Requests.Count - 1];

        public static void SharedReadsRejectReversedRepliesAndOldEndpoint()
        {
            DaemonClient.BaseUrl = "http://config-order-a";
            var store = new DaemonConfigStore();
            store.Refresh();
            var old = Last;
            store.Refresh();
            Last.Reply(Reply("new"));
            old.Reply(Reply("old"));
            Assert.That(store.Value.TitleModel, Is.EqualTo("new"));
            store.Refresh();
            old = Last;
            DaemonClient.BaseUrl = "http://config-order-b";
            old.Reply(Reply("wrong endpoint"));
            Assert.That(store.Value.TitleModel, Is.Not.EqualTo("wrong endpoint"));
            store.Refresh();
            Last.Reply(Reply("endpoint b"));
            DaemonClient.BaseUrl = "http://config-order-a";
            Assert.That(store.Value.TitleModel, Is.Not.EqualTo("endpoint b"));
            old.Reply(Reply("old generation"));
            Assert.That(store.Value.TitleModel, Is.Not.EqualTo("old generation"));
        }

        public static void SettingsAndHubReadsShareSavePublicationOrdering()
        {
            DaemonClient.BaseUrl = "http://config-save-order";
            var hub = SessionHub.Instance = new SessionHub();
            var state = new DaemonConfigState(Guid.NewGuid().ToString());
            hub.ConfigStore.Refresh();
            var hubRead = Last;
            state.Load(false, null);
            Last.Reply(Reply("base"));
            hubRead.Reply(Reply("older hub"));
            Assert.That(hub.Config.TitleModel, Is.EqualTo("base"));
            state.Load(false, null);
            var pageRead = Last;
            state.Config.TitleModel = "saved";
            state.Save(null);
            var save = Last;
            pageRead.Reply(Reply("before save"));
            Assert.That(hub.Config.TitleModel, Is.EqualTo("base"));
            hub.ConfigStore.Refresh();
            var duringSave = Last;
            save.Reply(new Wire.Ack());
            Last.Reply(Reply("saved"));
            duringSave.Reply(Reply("before commit"));
            Assert.That(hub.Config.TitleModel, Is.EqualTo("saved"));
            Assert.That(state.Dirty, Is.False);
        }

        public static void InitialPageLoadSettlesIndependentlyOfNewerHubRefresh()
        {
            foreach (bool backgroundFirst in new[] { true, false })
            {
                DaemonClient.BaseUrl = "http://config-independent-load";
                var hub = SessionHub.Instance = new SessionHub();
                var state = new DaemonConfigState(Guid.NewGuid().ToString());
                int loaded = 0;
                state.Load(false, () => loaded++);
                var pageRead = Last;
                hub.ConfigStore.Refresh();
                var background = Last;
                if (backgroundFirst)
                    background.Reply(Reply("background"));
                pageRead.Reply(Reply("page"));
                if (!backgroundFirst)
                    background.Reply(Reply("background"));
                Assert.That(loaded, Is.EqualTo(1));
                Assert.That(state.Loaded, Is.True);
                Assert.That(state.Config.TitleModel, Is.EqualTo("page"));
                Assert.That(hub.Config.TitleModel, Is.EqualTo("background"));
            }
        }

        public static void PageLoadErrorsSettleAfterBackgroundRefreshButNotAfterSave()
        {
            DaemonClient.BaseUrl = "http://config-independent-error";
            var hub = SessionHub.Instance = new SessionHub();
            var state = new DaemonConfigState(Guid.NewGuid().ToString());
            state.Load(false, null);
            var pageRead = Last;
            hub.ConfigStore.Refresh();
            Last.Reply(Reply("background"));
            pageRead.Fail("page unavailable");
            Assert.That(state.Loaded, Is.False);
            Assert.That(state.Error, Is.EqualTo("page unavailable"));
            Assert.That(hub.Config.TitleModel, Is.EqualTo("background"));
            state.Load(false, null);
            Last.Reply(Reply("base"));
            state.Load(false, null);
            pageRead = Last;
            state.Config.TitleModel = "submitted";
            state.Save(null);
            Last.Reply(new Wire.Ack());
            pageRead.Fail("stale error");
            pageRead.Reply(Reply("stale data"));
            Assert.That(state.Error, Is.Null);
            Assert.That(state.Config.TitleModel, Is.EqualTo("submitted"));
            Assert.That(state.Dirty, Is.False);
        }

    }
}
