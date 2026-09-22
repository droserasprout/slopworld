using System.Linq;

namespace SlopWorld.Tests
{
    static class PagerTabsTests
    {
        public static void RoutedReadersReopenAndPinWithoutStartingProcesses()
        {
            SessionHub.Instance = new SessionHub();
            var store = SessionHub.Instance.SessionStore;
            var tabs = new PagerTabs();
            AssertEx.False(tabs.ContainsSession("missing"), "empty collection has no reader");
            AssertEx.False(tabs.ReopenSession("missing"), "unknown reader cannot reopen");
            AssertEx.False(tabs.Lock("missing"), "unknown reader cannot pin");
            AssertEx.False(tabs.LockPreview("p", "/missing"), "unknown file cannot pin");
            AssertEx.Equal(0, tabs.All.Count(), "empty preview is not a routed tab");

            tabs.Preview.ViewFile("p", "/one", "one");
            store.Complete("one");
            AssertEx.True(tabs.ContainsSession("one"), "live preview is routed");
            AssertEx.True(tabs.ReopenSession("one"), "preview reopens by session");
            AssertEx.True(tabs.LockPreview("p", "/one"), "file identity pins preview");
            tabs.ForPreview().ViewFile("p", "/two", "two");
            store.Complete("two");
            AssertEx.True(tabs.ContainsSession("one"), "moved pinned reader stays routed");
            AssertEx.True(tabs.Lock("one"), "pinning an already pinned reader succeeds");
            AssertEx.True(tabs.LockPreview("p", "/one"), "pinned reader remains addressable by file");
            AssertEx.True(tabs.ReopenSession("one"), "pinned reader reopens by session");
            AssertEx.Equal("one", TerminalWindow.Current, "reopening focuses requested reader");
            AssertEx.Equal(2, tabs.All.Count(), "both readers appear once");
            AssertEx.Equal(2, store.Starts, "navigation never starts another process");
            AssertEx.False(tabs.ReopenSession("missing"), "missing session does not match pinned reader");
            AssertEx.False(tabs.Lock("missing"), "missing pin does not affect existing tabs");
            AssertEx.False(tabs.LockPreview("other", "/one"), "project scopes file identity");
        }

        public static void PreviewReleaseAndPanelClosePreservePinnedReaders()
        {
            SessionHub.Instance = new SessionHub();
            var store = SessionHub.Instance.SessionStore;
            var tabs = new PagerTabs();
            tabs.Preview.ViewFile("p", "/pinned", "pinned");
            store.Complete("pinned");
            tabs.Lock("pinned");
            tabs.ReleasePreview();
            AssertEx.True(tabs.ContainsSession("pinned"), "release preserves a pinned preview slot");
            tabs.ForPreview().ViewFile("p", "/temporary", "temporary");
            store.Complete("temporary");
            tabs.CloseIf("pinned");
            AssertEx.Equal(0, store.Stops, "panel close preserves pin");
            tabs.CloseIf("temporary");
            AssertEx.False(tabs.ContainsSession("temporary"), "panel close releases ordinary preview");
            AssertEx.True(tabs.ContainsSession("pinned"), "panel close keeps other readers");
            tabs.ForPreview().ViewFile("p", "/next", "next");
            store.Complete("next");
            tabs.ReleasePreview();
            AssertEx.False(tabs.ContainsSession("next"), "refresh releases ordinary preview");
            AssertEx.Equal(2, store.Stops, "each ordinary preview stopped once");
            SessionHub.Instance.Get("pinned").Alive = false;
            AssertEx.Equal(0, tabs.All.Count(), "dead pinned readers retire from routed tabs");
            AssertEx.Equal<string>(null, tabs.FilePath("pinned"), "retired reader loses path lookup");
            AssertEx.False(tabs.CloseTab("pinned"), "retired reader cannot be dismissed again");
        }
    }
}
