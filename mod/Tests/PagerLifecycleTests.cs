using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Transport/window bindings for testing the real Pager and PagerTabs lifecycle.
    sealed partial class SessionHub
    {
        public static SessionHub Instance = new SessionHub();
        public readonly DaemonConfig Config = new DaemonConfig();
        public readonly PagerTestStore SessionStore = new PagerTestStore();
        public readonly Dictionary<string, SessionInfo> Sessions = new Dictionary<string, SessionInfo>();
        public SessionInfo Get(string name) => Sessions.TryGetValue(name, out var info) ? info : null;
        public List<ProjectInfo> Projects = new List<ProjectInfo> { new ProjectInfo { Name = "p" } };
        public ProjectInfo Project(string name) => new ProjectInfo();
    }

    sealed partial class PagerTestStore
    {
        public readonly Queue<Action<string>> Pending = new Queue<Action<string>>();
        public readonly List<Action<string>> Failures = new List<Action<string>>();
        public int Starts, Stops;
        public bool Host, Temp;
        public string Project;
        public void Run(string project, string command, string label, Action<string> started,
                        Action<string> fail, bool host = false, bool temp = false, string path = "")
        {
            Starts++;
            Host = host;
            Temp = temp;
            Project = project;
            Pending.Enqueue(started);
            Failures.Add(fail);
        }
        public void Complete(string name)
        {
            SessionHub.Instance.Sessions[name] = new SessionInfo { Name = name, Alive = true };
            Pending.Dequeue()(name);
        }
        public void CompleteUnlisted(string name) => Pending.Dequeue()(name);
        public void Stop(string name)
        {
            Stops++;
            if (SessionHub.Instance.Sessions.TryGetValue(name, out var info)) info.Alive = false;
        }
    }

    static class TerminalWindow
    {
        public static string Current;
        public static void Open(string name) { Current = name; }
        public static bool TryPanelShape(out int cols, out int rows)
        {
            cols = rows = 0;
            return false;
        }
    }

    static partial class UiLayout
    {
        public static void Fail(string message) { throw new Exception(message); }
    }
}

namespace SlopWorld.Tests
{
    static class PagerLifecycleTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("fresh diff replaces a pinned reader without duplicating it", FreshDiff);
            yield return ("deleted readers close pinned and pending sessions", DeletedReaders);
            yield return ("late file metadata cannot close a replacement reader", LateMetadata);
            yield return ("fresh reader collection has no viewer path", FreshPaths);
            yield return ("pending file clicks share one start", Pending);
            yield return ("pinned files reopen without a new preview", Pinned);
            yield return ("shared file and diff preview replacement preserves pinned readers", SharedReaders);
            yield return ("pending diff cannot reopen the previous file", PendingDiff);
            yield return ("late unlisted diff completion is stopped", LateUnlistedCompletion);
            foreach (bool file in new[] { true, false })
            {
                string kind = file ? "file" : "command";
                yield return (kind + " replacement failure retires the old reader and permits retry", () => FailedReplacement(file));
                yield return (kind + " stale failure leaves the replacement alone", () => StaleFailure(file));
                yield return (kind + " pinned pending reader survives release and closes explicitly", () => PinnedPending(file));
                yield return (kind + " close cancels pending replacement", () => ClosePending(file));
                yield return (kind + " dead reader can be replaced", () => DeadReader(file));
            }
        }

        static void Start(Pager pager, bool file, string key)
        {
            if (file) pager.ViewFile("p", key, key);
            else pager.Open("p", "cat " + key, key, key);
        }

        static void FailedReplacement(bool file)
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            Start(pager, file, "/one");
            store.Complete("one");
            Start(pager, file, "/two");
            AssertEx.Equal("one", pager.Session, "old reader remains visible during handoff");
            var error = AssertEx.Throws<Exception>(() => store.Failures[1]("start failed"), "failure reaches UI");
            store.Pending.Dequeue();
            AssertEx.Equal("start failed", error.Message, "original failure is reported");
            AssertEx.Equal<string>(null, pager.Session, "failed handoff clears the reader");
            AssertEx.False(pager.Owns("p", "/two"), "failed request no longer owns the key");
            AssertEx.False(pager.Reopen(), "failed reader cannot reopen");
            AssertEx.Equal(1, store.Stops, "old reader is stopped once");
            Start(pager, file, "/two");
            store.Complete("retry");
            AssertEx.True(pager.Matches("p", "/two"), "same key can retry after failure");
            AssertEx.Equal(3, store.Starts, "retry starts a new reader");
        }

        static void StaleFailure(bool file)
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            Start(pager, file, "/one");
            Start(pager, file, "/two");
            store.Failures[0]("obsolete failure");
            store.Pending.Dequeue();
            AssertEx.True(pager.Owns("p", "/two"), "stale failure preserves pending identity");
            store.Complete("two");
            AssertEx.True(pager.Matches("p", "/two"), "replacement completes normally");
            AssertEx.Equal("two", TerminalWindow.Current, "replacement has focus");
            AssertEx.Equal(0, store.Stops, "stale failure cannot stop the replacement");
        }

        static void PinnedPending(bool file)
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            AssertEx.False(pager.Lock(), "empty reader cannot pin");
            Start(pager, file, "/one");
            AssertEx.False(pager.LockPreview("other", "/one"), "project identity must match");
            AssertEx.False(pager.LockPreview("p", "/other"), "key identity must match");
            AssertEx.True(pager.LockPreview("p", "/one"), "pending reader can pin by identity");
            pager.Release();
            store.Complete("one");
            pager.CloseIf("one");
            AssertEx.True(pager.Alive && pager.Locked, "preview release and panel close preserve pinned reader");
            AssertEx.Equal(0, store.Stops, "pinned reader has not been stopped");
            AssertEx.False(pager.CloseTab(null), "null tab cannot dismiss reader");
            AssertEx.False(pager.CloseTab("other"), "unrelated tab cannot dismiss reader");
            AssertEx.True(pager.CloseTab("one"), "explicit dismissal closes pinned reader");
            AssertEx.False(pager.Locked || pager.Alive, "dismissal clears pin and session");
            pager.Release();
            AssertEx.Equal(1, store.Stops, "repeated release does not stop twice");
        }

        static void ClosePending(bool file)
        {
            SessionHub.Instance = new SessionHub();
            TerminalWindow.Current = null;
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            Start(pager, file, "/one");
            store.Complete("one");
            pager.CloseIf(null);
            pager.CloseIf("other");
            AssertEx.True(pager.Alive, "unrelated panel close leaves reader alive");
            Start(pager, file, "/two");
            SessionHub.Instance.Sessions.Remove("one");
            pager.CloseIf("one");
            AssertEx.Equal(1, store.Stops, "owned reader is stopped even without snapshot metadata");
            store.Complete("late");
            AssertEx.False(SessionHub.Instance.Get("late").Alive, "late completion is stopped");
            AssertEx.Equal<string>(null, pager.Session, "late completion cannot revive closed reader");
            AssertEx.Equal("one", TerminalWindow.Current, "late completion cannot take focus");
            AssertEx.Equal(2, store.Stops, "old and late readers are each stopped once");
        }

        static void DeadReader(bool file)
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            Start(pager, file, "/one");
            store.Complete("dead");
            SessionHub.Instance.Get("dead").Alive = false;
            AssertEx.False(pager.Reopen(), "dead reader cannot reopen");
            AssertEx.False(pager.Lock(), "dead reader cannot pin");
            AssertEx.False(pager.Owns("p", "/one"), "dead reader releases its identity");
            Start(pager, file, "/one");
            store.Complete("replacement");
            AssertEx.Equal(2, store.Starts, "same key starts again after process death");
            AssertEx.True(pager.Matches("p", "/one"), "replacement owns the identity");
            AssertEx.Equal("replacement", TerminalWindow.Current, "replacement takes focus");
            AssertEx.Equal(1, store.Stops, "old session cleanup is requested once");
        }

        static void FreshDiff()
        {
            SessionHub.Instance = new SessionHub();
            var tabs = new PagerTabs();
            var store = SessionHub.Instance.SessionStore;
            tabs.OpenFresh("p", "git diff", "diff-one", "diff:one");
            store.Complete("old");
            tabs.Lock("old");
            tabs.ForPreview().ViewFile("p", "/two", "view-two");
            store.Complete("two");
            tabs.OpenFresh("p", "git diff", "diff-one", "diff:one");
            tabs.OpenFresh("p", "git diff", "diff-one", "diff:one");
            AssertEx.Equal(3, store.Starts, "pending refresh shares its start");
            AssertEx.Equal(0, store.Stops, "old diff survives until handoff");
            store.Complete("fresh");
            AssertEx.True(tabs.IsLocked("fresh"), "pin survives process replacement");
            AssertEx.True(tabs.IsSession("two"), "other preview remains open");
            AssertEx.False(tabs.IsSession("old"), "old snapshot retired");
            AssertEx.Equal(1, store.Stops, "only old diff stopped");
        }

        static void DeletedReaders()
        {
            SessionHub.Instance = new SessionHub();
            var tabs = new PagerTabs();
            var store = SessionHub.Instance.SessionStore;
            tabs.ForPreview().ViewFile("p", "/gone/pinned", "pinned");
            store.Complete("pinned");
            tabs.Lock("pinned");
            tabs.ForPreview().ViewFile("p", "/gone/pending", "pending");
            tabs.Invalidate(tab => tab.FilePath != null && tab.FilePath.StartsWith("/gone/"));
            AssertEx.False(tabs.IsSession("pinned"), "deletion overrides pin");
            store.Complete("late");
            AssertEx.False(SessionHub.Instance.Get("late").Alive, "late handoff is stopped");
            AssertEx.Equal(2, store.Stops, "both deleted readers stopped");
            tabs.ForPreview().ViewFile("p", "/kept", "kept");
            store.Complete("kept");
            tabs.Invalidate(tab => tab.FilePath == "/gone");
            AssertEx.True(tabs.IsSession("kept"), "unrelated reader survives");
        }

        static void LateMetadata()
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            pager.ViewFile("p", "/one", "one");
            store.Complete("one");
            var probe = new ReaderProbe(pager);
            pager.ViewFile("p", "/two", "two");
            probe.Apply(false);
            store.Complete("two");
            AssertEx.True(pager.Alive, "old path cannot cancel a pending replacement");
            pager.ViewFile("p", "/one", "one");
            store.Complete("new-one");
            probe.Apply(false);
            AssertEx.True(pager.Alive, "old session cannot close a reopened path");
            var current = new ReaderProbe(pager);
            current.Apply(true);
            AssertEx.True(pager.Alive, "existing file stays open");
            pager.Lock();
            current.Apply(false);
            AssertEx.False(pager.Alive, "confirmed deletion closes the current pinned reader");
        }

        static void FreshPaths()
        {
            var tabs = new PagerTabs();
            AssertEx.Equal<string>(null, tabs.FilePath("restored-viewer"), "unknown viewer before first preview");
            AssertEx.Equal<string>(null, tabs.FilePath(null), "no active session before first preview");
        }

        static void PendingDiff()
        {
            SessionHub.Instance = new SessionHub();
            var tabs = new PagerTabs();
            var store = SessionHub.Instance.SessionStore;
            tabs.ForPreview().ViewFile("p", "/one", "view-one");
            store.Complete("file");
            tabs.ForPreview().Open("p", "git diff", "diff-one", "diff:one");
            AssertEx.False(tabs.Reopen("p", "diff:one"), "old source is not the pending diff");
            tabs.ForPreview().Open("p", "git diff", "diff-one", "diff:one");
            AssertEx.Equal(2, store.Starts, "pending diff clicks share the request");
            AssertEx.True(store.Host, "diff runs on host");
            AssertEx.False(store.Temp, "diff retains project cwd");
            AssertEx.Equal("p", store.Project, "diff project");
            store.Complete("diff");
            AssertEx.True(tabs.Reopen("p", "diff:one"), "completed diff can reopen");
            AssertEx.Equal("diff", TerminalWindow.Current, "correct reader is active");
            AssertEx.Equal(1, store.Stops, "source retired after diff handoff");
        }

        static void LateUnlistedCompletion()
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            pager.Open("p", "git diff", "diff-one", "diff:one");
            pager.Release();
            store.CompleteUnlisted("late-diff");
            AssertEx.Equal(1, store.Stops, "superseded run is stopped without a session snapshot row");
        }

        static void SharedReaders()
        {
            SessionHub.Instance = new SessionHub();
            int nativeReleases = 0;
            var tabs = new PagerTabs(() => nativeReleases++);
            var store = SessionHub.Instance.SessionStore;
            tabs.ForPreview().ViewFile("p", "/one", "view-one");
            store.Complete("file");
            tabs.Lock("file");
            tabs.ForPreview().Open("p", "git diff", "diff-one", "diff:one");
            store.Complete("diff");
            AssertEx.True(tabs.Reopen("p", "/one"), "source remains independently addressable");
            AssertEx.True(tabs.Reopen("p", "diff:one"), "diff has a separate identity");
            AssertEx.Equal(2, nativeReleases, "reopening does not replace a native preview");
            tabs.ForPreview().ViewFile("p", "/two", "view-two");
            store.Complete("two");
            AssertEx.False(tabs.IsSession("diff"), "file replaces the unpinned diff");
            AssertEx.True(tabs.IsSession("file"), "pinned source survives replacement");
            AssertEx.Equal(1, store.Stops, "only the shared preview was stopped");
            tabs.CloseTab("file");
            AssertEx.True(tabs.IsSession("two"), "closing pinned reader keeps the preview");
        }

        static void Pending()
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            var store = SessionHub.Instance.SessionStore;
            pager.ViewFile("p", "/one", "view-one");
            pager.ViewFile("p", "/one", "view-one");
            AssertEx.Equal(1, store.Starts, "duplicate click while starting");
            AssertEx.True(store.Host, "file viewer runs on host");
            AssertEx.False(store.Temp, "viewer retains project cwd");
            AssertEx.Equal("p", store.Project, "viewer project");
            store.Complete("one");
            pager.ViewFile("p", "/two", "view-two");
            AssertEx.False(pager.Matches("p", "/two"), "old session is not the pending file");
            pager.ViewFile("p", "/two", "view-two");
            AssertEx.Equal(2, store.Starts, "replacement click is coalesced");
            store.Complete("two");
            pager.ViewFile("p", "/two", "view-two");
            AssertEx.Equal(2, store.Starts, "live file is reused");
            AssertEx.Equal(1, store.Stops, "only replaced session stopped");
        }

        static void Pinned()
        {
            SessionHub.Instance = new SessionHub();
            var tabs = new PagerTabs();
            var store = SessionHub.Instance.SessionStore;
            tabs.ForPreview().ViewFile("p", "/one", "view-one");
            store.Complete("one");
            tabs.Lock("one");
            tabs.ForPreview().ViewFile("p", "/two", "view-two");
            store.Complete("two");
            AssertEx.True(tabs.Reopen("p", "/one"), "find pinned file independently of selection");
            AssertEx.Equal("one", TerminalWindow.Current, "focus pinned session");
            AssertEx.Equal(2, store.Starts, "no duplicate session");
            AssertEx.Equal(0, store.Stops, "both readers remain alive");
            AssertEx.Equal("/one", tabs.FilePath("one"), "pinned session retains original path");
            AssertEx.Equal("/two", tabs.FilePath("two"), "preview has its own path");
        }
    }
}
