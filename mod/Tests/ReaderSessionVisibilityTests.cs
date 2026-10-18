using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class ReaderSessionVisibilityTests
    {
        public static void PreviewReplacementPublishesOneRowAcrossBothSnapshotRaces()
        {
            foreach (bool oldDiff in new[] { false, true })
            foreach (bool newDiff in new[] { false, true })
            {
                SessionHub.Instance = new SessionHub();
                var store = SessionHub.Instance.SessionStore;
                store.DeferStops = true;
                var tabs = new PagerTabs();
                Start(tabs.ForPreview(), oldDiff, "old");
                Publish(store.Requests[0], "old-session");
                store.Complete(store.Requests[0], "old-session", listed: false);
                AssertRows(tabs, "old-session");

                Start(tabs.ForPreview(), newDiff, "new");
                Publish(store.Requests[1], "new-session");
                // WebSocket announces the new process before the launch callback.
                AssertRows(tabs, "old-session");
                AssertEx.Equal(1, tabs.All.Count(), "incoming process is not restored as a tab");

                store.Complete(store.Requests[1], "new-session", listed: false);
                AssertEx.True(SessionHub.Instance.Get("old-session").Alive, "stop remains pending");
                AssertRows(tabs, "new-session");
                AssertEx.Equal(1, tabs.All.Count(), "outgoing process is not restored as a tab");
                AssertEx.False(tabs.IsLocked("new-session"), "replacement stays unpinned");

                SessionHub.Instance.Sessions.Remove("old-session");
                AssertRows(tabs, "new-session");
                AssertEx.Equal(1, store.Stops, "old process stopped once after handoff");
            }
        }

        public static void PinnedRefreshKeepsOtherReadersAndItsSingleRow()
        {
            SessionHub.Instance = new SessionHub();
            var store = SessionHub.Instance.SessionStore;
            store.DeferStops = true;
            var tabs = new PagerTabs();
            var pinned = tabs.ForPreview();
            Start(pinned, false, "pinned");
            Publish(store.Requests[0], "pinned-session");
            store.Complete(store.Requests[0], "pinned-session", listed: false);
            tabs.Lock("pinned-session");
            Start(tabs.ForPreview(), false, "other");
            Publish(store.Requests[1], "other-session");
            store.Complete(store.Requests[1], "other-session", listed: false);

            pinned.RefreshIfChanged("baseline");
            pinned.RefreshIfChanged("changed");
            Publish(store.Requests[2], "refreshed-session");
            AssertRows(tabs, "other-session", "pinned-session");
            store.Complete(store.Requests[2], "refreshed-session", listed: false);
            AssertRows(tabs, "other-session", "refreshed-session");
            AssertEx.True(tabs.IsLocked("refreshed-session"), "refresh retains pin");
            AssertEx.Equal(2, tabs.All.Count(), "no third restored tab");
        }

        public static void LateSupersededLaunchCannotHideOrDuplicateCommittedReader()
        {
            SessionHub.Instance = new SessionHub();
            var store = SessionHub.Instance.SessionStore;
            store.DeferStops = true;
            var tabs = new PagerTabs();
            var pager = tabs.ForPreview();
            Start(pager, false, "same");
            pager.Release();
            Start(pager, false, "same");
            Publish(store.Requests[1], "current-session");
            store.Complete(store.Requests[1], "current-session", listed: false);
            AssertRows(tabs, "current-session");

            Publish(store.Requests[0], "late-session");
            AssertRows(tabs, "current-session");
            store.Complete(store.Requests[0], "late-session", listed: false);
            AssertRows(tabs, "current-session");
            AssertEx.Equal(1, store.Stops, "late launch is stopped");
        }

        public static void FailedRefreshUnblocksRestorationAndKeepsOldReader()
        {
            SessionHub.Instance = new SessionHub();
            var store = SessionHub.Instance.SessionStore;
            var tabs = new PagerTabs();
            var pager = tabs.ForPreview();
            Start(pager, false, "file");
            Publish(store.Requests[0], "old-session");
            store.Complete(store.Requests[0], "old-session", listed: false);
            pager.RefreshIfChanged("baseline");
            pager.RefreshIfChanged("changed");
            var request = store.Requests[1];
            store.Fail(request, "unavailable");
            AssertRows(tabs, "old-session");

            Publish(request, "external-session");
            AssertRows(tabs, "external-session", "old-session");
            AssertEx.Equal(2, tabs.All.Count(), "failed request leaves no pending suppression");
        }

        static void Start(Pager pager, bool diff, string key)
        {
            if (diff)
                pager.Open("p", "git diff", key, "diff:" + key, "", "diff");
            else
                pager.ViewFile("p", "/" + key, key);
        }

        static void Publish(PagerTestStore.LaunchRequest request, string name)
        {
            SessionHub.Instance.Sessions[name] = new SessionInfo
            {
                Name = name,
                Label = request.Label,
                Alive = true,
                Ephemeral = true,
                Intent = request.Options.Intent,
                Project = request.Project,
                ReaderScope = request.Options.Reader.Scope,
                ReaderKey = request.Options.Reader.Key,
                ReaderPath = request.Options.Reader.Path,
                ReaderPinned = request.Options.Reader.Pinned,
            };
        }

        static void AssertRows(PagerTabs tabs, params string[] expected)
        {
            var sessions = SessionHub.Instance.Sessions.Values;
            var visibility = Pager.SessionVisibility;
            visibility.Reconcile(sessions);
            foreach (var info in sessions)
                tabs.AttachRestored(info);
            var rows = new List<SessionInfo>();
            RoutedSessionRows.Rebuild(
                rows,
                sessions,
                info =>
                    visibility.Includes(info)
                    && (info.Intent != "diff" || tabs.IsSession(info.Name)),
                null,
                20f
            );
            AssertEx.Equal(
                string.Join(",", expected.OrderBy(name => name)),
                string.Join(",", rows.Select(row => row.Name).OrderBy(name => name)),
                "only committed reader rows are visible"
            );
        }
    }
}
