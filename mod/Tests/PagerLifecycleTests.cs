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
        public string Project, Command;
        public void Run(string project, string command, string label, Action<string> started,
                        Action<string> fail, SessionRunOptions options = null)
        {
            options = options ?? new SessionRunOptions();
            Starts++;
            Host = options.Host;
            Temp = options.Temp;
            Project = project;
            Command = command;
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
        public void SetReaderPinned(string name, bool pinned) { }
    }

    static class TerminalWindow
    {
        public static string Current;
        public static void Open(string name) { Current = name; }
        public static void ReplaceReader(string oldName, string newName)
        {
            if (Current == oldName) Current = newName;
        }
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
            yield return ("reader settings restart preserves pin and background focus", AppearanceRestart);
            yield return ("reader settings restart ignores stale confirmation", AppearanceStale);
            yield return ("failed reader settings restart keeps old process", AppearanceFailure);
            yield return ("restored diff restart uses current pager", AppearanceDiff);
            yield return ("bat file pager uses the selected bat highlighter theme", BatFileTheme);
            yield return ("changed file refresh keeps its pinned tab and active pane", RefreshActive);
            yield return ("background refresh does not steal terminal focus", RefreshBackground);
            yield return ("failed refresh retains the reader and retries", RefreshFailure);
            yield return ("stale refresh completion cannot replace a newer preview", RefreshSuperseded);
            yield return ("diffs and editor commands do not auto refresh", RefreshExclusions);
            yield return ("file-at-line previews refresh with the same command", RefreshAtLine);
            yield return ("line-targeted source opens reuse pending and pinned readers", ReuseFileAtLine);
            yield return ("fresh diff replaces a pinned reader without duplicating it", FreshDiff);
            yield return ("deleted readers close pinned and pending sessions", DeletedReaders);
            yield return ("late file metadata cannot close a replacement reader", LateMetadata);
            yield return ("fresh reader collection has no viewer path", FreshPaths);
            yield return ("restored reader keeps path and pin without starting a process", RestoredReader);
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

        static void AppearanceRestart()
        {
            var pager = WatchedFile();
            var store = SessionHub.Instance.SessionStore;
            pager.Lock();
            TerminalWindow.Current = "settings-backing-session";
            SessionHub.Instance.Config.Pager = "less -N";
            pager.RestartForAppearance(pager.Session, pager.Operation);
            AssertEx.True(store.Command.Contains("less -N"), "current pager command is rebuilt");
            AssertEx.Equal("original", pager.Session, "old session retained until ready");
            AssertEx.Equal(0, store.Stops, "old process stays alive during restart");
            store.Complete("restyled");
            AssertEx.Equal("restyled", pager.Session, "same tab receives replacement");
            AssertEx.True(pager.Locked, "pin retained");
            AssertEx.Equal("settings-backing-session", TerminalWindow.Current, "settings focus retained");
            AssertEx.Equal(1, store.Stops, "old process stopped after replacement is ready");
        }

        static void AppearanceStale()
        {
            var pager = WatchedFile();
            string session = pager.Session;
            int operation = pager.Operation;
            pager.ViewFile("p", "/other", "other");
            var store = SessionHub.Instance.SessionStore;
            int starts = store.Starts;
            pager.RestartForAppearance(session, operation);
            AssertEx.Equal(starts, store.Starts, "approval cannot restart a different reader");
        }

        static void AppearanceFailure()
        {
            var pager = WatchedFile();
            var store = SessionHub.Instance.SessionStore;
            pager.RestartForAppearance(pager.Session, pager.Operation);
            AssertEx.Throws<Exception>(() => store.Failures[1]("cannot restart"), "failure reported");
            store.Pending.Dequeue();
            AssertEx.Equal("original", pager.Session, "failed replacement keeps original");
            AssertEx.Equal(0, store.Stops, "original process survives");
            AssertEx.True(pager.CanRestartForAppearance, "retry remains possible");
        }

        static void AppearanceDiff()
        {
            string oldTheme = ModEntry.Instance.settings.codeBatTheme;
            ModEntry.Instance.settings.codeBatTheme = "Monokai Extended";
            try
            {
                SessionHub.Instance = new SessionHub();
                var info = new SessionInfo { Name = "diff", Alive = true, Intent = "diff",
                    ReaderScope = "p", ReaderKey = "diff:file", ReaderPinned = true,
                    Cmd = PagerCommands.DiffCommand("git diff HEAD", "less") };
                SessionHub.Instance.Sessions[info.Name] = info;
                var pager = new Pager();
                pager.AttachRestored(info);
                SessionHub.Instance.Config.Pager = "less -N";
                SessionHub.Instance.Config.Highlighter = "bat --color=always --style=plain --paging=never";
                pager.RestartForAppearance(pager.Session, pager.Operation);
                AssertEx.Equal("env BAT_THEME='Monokai Extended' DELTA_PAGER='less -N -+N -S --shift=1 --wheel-lines=1' git -c delta.line-numbers=true -c delta.minus-style='syntax auto' diff HEAD",
                    SessionHub.Instance.SessionStore.Command, "recovered diff uses the selected bat theme");
                SessionHub.Instance.SessionStore.Complete("new-diff");
                AssertEx.True(pager.Locked, "restored diff pin retained");
            }
            finally { ModEntry.Instance.settings.codeBatTheme = oldTheme; }
        }

        static void BatFileTheme()
        {
            string oldTheme = ModEntry.Instance.settings.codeBatTheme;
            ModEntry.Instance.settings.codeBatTheme = "ansi";
            try
            {
                SessionHub.Instance = new SessionHub();
                SessionHub.Instance.Config.Pager = "bat --paging=always";
                SessionHub.Instance.Config.Highlighter = "bat --color=always --style=plain --paging=never";
                string command = Pager.PagerCommand("/sample.rs");
                AssertEx.True(command.Contains("bat --paging=always --style=numbers --decorations=always --wrap=never --pager 'less -RS --shift=1 --wheel-lines=1' --theme='ansi' -- '/sample.rs'"),
                    "file pager gets the selected bat theme before its file argument");
                SessionHub.Instance.Config.Highlighter = "highlight --out-format=xterm256";
                AssertEx.False(Pager.PagerCommand("/sample.rs").Contains("--theme='ansi'"),
                    "other highlighters do not apply an old bat theme to files");
            }
            finally { ModEntry.Instance.settings.codeBatTheme = oldTheme; }
        }

        static Pager WatchedFile()
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            pager.ViewFile("p", "/file", "file");
            SessionHub.Instance.SessionStore.Complete("original");
            new ReaderProbe(pager).Apply(true, "first");
            return pager;
        }

        static void RestoredReader()
        {
            SessionHub.Instance = new SessionHub();
            var info = new SessionInfo
            {
                Name = "tab-recovered", Alive = true, Intent = "view", Label = "file.rs",
                ReaderPath = "/repo/file.rs", ReaderKey = "/repo/file.rs",
                ReaderScope = "p", ReaderPinned = true
            };
            SessionHub.Instance.Sessions[info.Name] = info;
            var tabs = new PagerTabs();
            tabs.AttachRestored(info);
            AssertEx.True(tabs.IsSession(info.Name), "restored reader is routed");
            AssertEx.True(tabs.IsLocked(info.Name), "restored pin survives");
            AssertEx.Equal("/repo/file.rs", tabs.FilePath(info.Name), "source path survives");
            AssertEx.True(tabs.Reopen("p", "/repo/file.rs"), "path lookup reuses reader");
            AssertEx.Equal(0, SessionHub.Instance.SessionStore.Starts, "recovery does not launch");
            tabs.ReleasePreview();
            AssertEx.True(tabs.IsSession(info.Name), "preview release preserves the pin");
        }

        static void RefreshActive()
        {
            var pager = WatchedFile();
            var store = SessionHub.Instance.SessionStore;
            pager.Lock();
            new ReaderProbe(pager).Apply(true, "first");
            new ReaderProbe(pager).Apply(true, "");
            AssertEx.Equal(1, store.Starts, "same or unavailable stamp does not restart");
            var stale = new ReaderProbe(pager);
            stale.Apply(true, "second");
            new ReaderProbe(pager).Apply(true, "third");
            AssertEx.Equal(2, store.Starts, "one refresh at a time");
            AssertEx.Equal("original", pager.Session, "old reader survives until handoff");
            store.Complete("refreshed");
            AssertEx.Equal("refreshed", TerminalWindow.Current, "visible pane follows replacement");
            AssertEx.True(pager.Locked, "pin survives refresh");
            AssertEx.False(SessionHub.Instance.Get("original").Alive, "old process retired");
            stale.Apply(false);
            AssertEx.True(pager.Alive, "stale metadata cannot close refreshed reader");
            new ReaderProbe(pager).Apply(true, "third");
            AssertEx.Equal(3, store.Starts, "edits during refresh are detected next time");
        }

        static void RefreshBackground()
        {
            var pager = WatchedFile();
            new ReaderProbe(pager).Apply(true, "second");
            TerminalWindow.Open("another-session");
            SessionHub.Instance.SessionStore.Complete("refreshed");
            AssertEx.Equal("another-session", TerminalWindow.Current, "focus is unchanged");
            AssertEx.Equal("refreshed", pager.Session, "hidden reader is still updated");
        }

        static void RefreshFailure()
        {
            var pager = WatchedFile();
            var store = SessionHub.Instance.SessionStore;
            new ReaderProbe(pager).Apply(true, "second");
            store.Pending.Dequeue();
            store.Failures[1]("temporary failure");
            AssertEx.Equal("original", pager.Session, "failure keeps original reader");
            AssertEx.True(pager.Alive, "failure does not stop the preview");
            new ReaderProbe(pager).Apply(true, "second");
            AssertEx.Equal(3, store.Starts, "unchanged failed stamp is retried");
            store.Complete("retry");
            AssertEx.Equal("retry", pager.Session, "retry handoff succeeds");
        }

        static void RefreshSuperseded()
        {
            var pager = WatchedFile();
            var store = SessionHub.Instance.SessionStore;
            new ReaderProbe(pager).Apply(true, "second");
            pager.ViewFile("p", "/other", "other");
            store.Complete("obsolete-refresh");
            AssertEx.False(SessionHub.Instance.Get("obsolete-refresh").Alive, "late result stopped");
            store.Complete("other");
            AssertEx.Equal("other", pager.Session, "new preview wins");
            new ReaderProbe(pager).Apply(true, "other-first");
            AssertEx.Equal(3, store.Starts, "replacement starts with its own baseline");

            new ReaderProbe(pager).Apply(true, "other-second");
            pager.Invalidate();
            store.Complete("closed-refresh");
            AssertEx.False(SessionHub.Instance.Get("closed-refresh").Alive, "closed reader stays closed");
        }

        static void RefreshExclusions()
        {
            foreach (bool editor in new[] { true, false })
            {
                SessionHub.Instance = new SessionHub();
                var pager = new Pager();
                string command = editor ? Pager.EditorCommand("/file") : "git diff | less";
                pager.Open("p", command, "file", editor ? "/file" : "diff:/file", "/file");
                var store = SessionHub.Instance.SessionStore;
                store.Complete("reader");
                new ReaderProbe(pager).Apply(true, "first");
                new ReaderProbe(pager).Apply(true, "second");
                AssertEx.Equal(1, store.Starts, "only source-file pagers restart");
            }
        }

        static void RefreshAtLine()
        {
            SessionHub.Instance = new SessionHub();
            var pager = new Pager();
            pager.ViewFileAt("p", "/file", 42, "file");
            var store = SessionHub.Instance.SessionStore;
            store.Complete("reader");
            string command = store.Command;
            new ReaderProbe(pager).Apply(true, "first");
            new ReaderProbe(pager).Apply(true, "second");
            AssertEx.Equal(2, store.Starts, "line-targeted file refreshes");
            AssertEx.Equal(command, store.Command, "original line and command retained");
        }

        static void ReuseFileAtLine()
        {
            SessionHub.Instance = new SessionHub();
            var tabs = new PagerTabs();
            var store = SessionHub.Instance.SessionStore;
            tabs.ForPreview().ViewFileAt("p", "/file", 42, "file");
            AssertEx.True(tabs.ReuseFile("p", "/file"), "pending source is already owned");
            AssertEx.Equal(1, store.Starts, "pending source does not start twice");
            store.Complete("file");
            tabs.Lock("file");
            tabs.ForPreview().ViewFile("p", "/other", "other");
            store.Complete("other");
            AssertEx.True(tabs.ReuseFile("p", "/file"), "pinned source reopens by path");
            AssertEx.Equal("file", TerminalWindow.Current, "existing line-targeted reader receives focus");
            AssertEx.Equal(2, store.Starts, "reopening does not create a duplicate reader");
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
