namespace SlopWorld
{
    // A reader owns one ephemeral pager session. Files and Git share preview/pinned slots.
    // sidebar tab changes preserve them and explicit dismissal releases them.
    public class Pager : IPreviewTab
    {
        string _sessionValue;
        string _session
        {
            get => _sessionValue;
            set
            {
                if (_sessionValue == value) return;
                _sessionValue = value;
                RoutedSessionRows.Invalidate();
            }
        }
        string _project;      // which project the persistent session serves
        string _filePath;     // the file named by the persistent viewer
        string _openProject;  // project for the current one-off command
        string _key;          // path identity for a one-off command, when it has one
        int _operation;
        bool _locked;
        bool _opening;
        string _pendingCommand;
        string _sourceCommand, _sourceLabel, _fileStamp;
        bool _refreshing;
        internal int Operation => _operation;

        // Who is showing, or null. Read rather than acted on - the two views use it to tell
        // "click the row that is already open" from "click a different one".
        public string Session => _session;
        public string FilePath => _pendingCommand == null ? _key : _filePath;

        // A single-click preview is replaceable until its routed header is double-clicked.
        // Locked previews deliberately remain ephemeral daemon sessions. "locked" is a UI
        // lifetime choice, not a request to persist a generated session in config.toml.
        public bool Locked => _locked;

        public bool Alive
        {
            get
            {
                var info = _session == null ? null : SessionHub.Instance.Get(_session);
                return info != null && info.Alive;
            }
        }

        public bool Owns(string project, string key) =>
            (_opening || Alive) && _openProject == project && _key == key;

        public void Invalidate()
        {
            _locked = false;
            Release();
        }

        public bool Matches(string project, string key)
        {
            return !_opening && Alive && _openProject == project && _key == key;
        }

        public bool LockPreview(string project, string key)
        {
            if (_openProject != project || _key != key) return false;
            _locked = true;
            return true;
        }

        public bool Lock()
        {
            if (!Alive) return false;
            _locked = true;
            return true;
        }

        // Git feeds its diff to the pager on stdin, so it needs the configured command
        // without file/line placeholders or the normal file argument.
        public static string PipePager =>
            PagerCommands.PipePager(SessionHub.Instance.Config.Pager);

        public static bool IsPagerCommand(string command) =>
            PagerCommands.IsPagerCommand(SessionHub.Instance.Config.Pager, command);

        public static bool IsEditorCommand(string command) =>
            PagerCommands.IsEditorCommand(SessionHub.Instance.Config.Editor, command);

        public static string FileCommand(string value, string fallback, string file, int line = 0) =>
            PagerCommands.FileCommand(value, fallback, file, line);

        public static string PagerCommand(string file, int line = 0) =>
            PagerCommands.PagerCommand(
                SessionHub.Instance.Config.Pager, SessionHub.Instance.Config.Highlighter, file, line);

        public static string EditorCommand(string file, int line = 0) =>
            PagerCommands.EditorCommand(SessionHub.Instance.Config.Editor, file, line);

        // Open a file in the persistent pager. Reuses the existing tmux session only when it
        // is still alive and already showing this file. A different file gets a fresh session
        // so the sidebar's one-line title follows the file instead of keeping the old name.
        public void ViewFile(string project, string filePath, string label)
        {
            if (_opening && _pendingCommand == null && _openProject == project && _key == filePath) return;
            bool host = string.IsNullOrEmpty(project);
            if (!host && BrowseScope.ProjectOf(project, SessionHub.Instance.Projects) == null)
            {
                Release();
                UiLayout.Fail("This reader's project is no longer available.");
                return;
            }

            // Reuse the existing session if it is alive, on the same project, and already
            // showing this file.
            if (_session != null && _project == project && _filePath == filePath && Alive)
            {
                // The pane is all a reopen promises. Sending a less command here would need another
                // quoting language for paths with spaces or apostrophes. It would also flash the
                // file again before the caller can read it.
                TerminalWindow.Open(_session);
                return;
            }

            // First time, or project changed, or session died: create a new one.
            _fileStamp = null;
            _refreshing = false;
            int operation = ++_operation;
            string oldSession = _session;
            // Keep the old session as the visible one until the replacement is ready. Git's sidebar
            // uses this identity for its routed row. Therefore, clearing it here makes the old row
            // disappear before the new one arrives and causes a layout jump.
            _project = null;
            _filePath = null;
            _openProject = project;
            _key = filePath;
            _pendingCommand = null;
            _opening = true;

            string cmd = PagerCommand(filePath);
            _sourceCommand = cmd;
            _sourceLabel = label;
            SessionHub.Instance.SessionStore.Run(project, cmd, label,
                session =>
                {
                    if (operation != _operation)
                    {
                        StopIf(session);
                        return;
                    }
                    _session = session;
                    _opening = false;
                    _project = project;
                    _filePath = filePath;
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    if (operation != _operation) return;
                    _session = null;
                    _opening = false;
                    _project = null;
                    _filePath = null;
                    StopIf(oldSession);
                    UiLayout.Fail(msg);
                }, host: true, path: filePath);
        }

        // Start a fresh pager at the requested line. Less's `:e` cannot open at a line atomically.
        public void ViewFileAt(string project, string filePath, int line, string label)
        {
            Open(project, PagerCommand(filePath, line), label, filePath, filePath);
        }

        // Run the pager on the host in the project directory. Replacement closes its tmux session.
        public void Open(string project, string command, string label) =>
            Open(project, command, label, null);

        // Open a one-off command and retain a caller-supplied identity so a click on a pinned
        // routed header can focus that exact diff instead of creating a second tab.
        public void Open(string project, string command, string label, string key, string sourcePath = "")
        {
            if (_opening && _openProject == project && _key == key &&
                _pendingCommand == command) return;
            // The project may have been renamed or deleted since the listing that put the row
            // on screen. The daemon would refuse either way, but the reason is clearer here.
            if (BrowseScope.ProjectOf(project, SessionHub.Instance.Projects) == null)
            {
                Release();
                UiLayout.Fail("This reader's project is no longer available.");
                return;
            }

            // Start the new session *before* killing the old one so the terminal pane always
            // has something to show — no blink of the game map between the two.
            _fileStamp = null;
            _refreshing = false;
            int operation = ++_operation;
            string oldSession = _session;
            // Keep the old session as the visible one until the replacement is ready. See the
            // matching handoff in ViewFile above.
            _project = null;
            _filePath = null;
            _openProject = project;
            _key = key;
            _opening = true;
            _pendingCommand = command;
            // Only file pagers are refreshed. Diff commands and editors own their own behavior.
            _sourceCommand = !string.IsNullOrEmpty(sourcePath) && key == sourcePath &&
                IsPagerCommand(command) && !IsEditorCommand(command) ? command : null;
            _sourceLabel = label;

            SessionHub.Instance.SessionStore.Run(project, command, label,
                session =>
                {
                    if (operation != _operation)
                    {
                        StopIf(session);
                        return;
                    }
                    _session = session;
                    _opening = false;
                    _filePath = string.IsNullOrEmpty(sourcePath) ? null : sourcePath;
                    // Don't set _project — this is a one-off command, not the persistent
                    // pager, so the next ViewFile will create its own session.
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    if (operation != _operation) return;
                    _session = null;
                    _opening = false;
                    _filePath = null;
                    StopIf(oldSession);
                    UiLayout.Fail(msg);
                }, host: true, path: sourcePath);
        }

        // The first successful probe establishes a baseline. Missing stamps support older daemons.
        internal void RefreshIfChanged(string stamp)
        {
            if (string.IsNullOrEmpty(stamp) || _sourceCommand == null || _opening ||
                _refreshing || !Alive || string.IsNullOrEmpty(FilePath)) return;
            if (_fileStamp == null) { _fileStamp = stamp; return; }
            if (_fileStamp == stamp) return;

            int operation = ++_operation;
            string oldSession = _session;
            _refreshing = true;
            SessionHub.Instance.SessionStore.Run(_openProject, _sourceCommand, _sourceLabel,
                session =>
                {
                    if (operation != _operation) { StopIf(session); return; }
                    _refreshing = false;
                    _fileStamp = stamp;
                    _session = session;
                    // Rebind only the pane that still shows this reader; never open or focus a window.
                    TerminalWindow.ReplaceReader(oldSession, session);
                    StopIf(oldSession);
                },
                _ =>
                {
                    if (operation != _operation) return;
                    // Keep the old reader and stamp so a later probe retries the refresh.
                    _refreshing = false;
                }, host: true, path: FilePath);
        }

        // Bring the open one back, for a reader who clicked the row that is already showing.
        // False where there is nothing to bring back, which is the caller's cue to open one.
        public bool Reopen()
        {
            if (!Alive) return false;
            TerminalWindow.Open(_session);
            return true;
        }

        // Stopping the owned session closes its process. The pane reacts to session removal.
        public void Release()
        {
            if (_locked) return;
            ++_operation;
            _refreshing = false;
            _fileStamp = null;
            _sourceCommand = null;
            _opening = false;
            string s = _session;
            _session = null;
            _project = null;
            _filePath = null;
            _openProject = null;
            _key = null;
            if (s == null) return;
            // The session snapshot can lose this name while a replacement or a reconnect is
            // settling. This pager owns the session, so stop it by name rather than making
            // cleanup depend on the snapshot still containing it.
            SessionHub.Instance.SessionStore.Stop(s);
        }

        // The terminal's own close, for the session it was showing. The pane is the pager's
        // only home, so closing it is the same focus change as leaving the view.
        public void CloseIf(string session)
        {
            if (!_locked && session != null && session == _session) Release();
        }

        // Explicit tab dismissal also releases pinned previews and cancels pending handoffs.
        public bool CloseTab(string session)
        {
            if (session == null || session != _session) return false;
            _locked = false;
            Release();
            return true;
        }

        // Stop a session if it's still alive, swallowing any error.
        static void StopIf(string session)
        {
            if (session == null) return;
            // A superseded run is exactly the case where the latest session snapshot may not
            // contain the returned name. Let the daemon answer an already-gone session. Do not
            // turn a missing local row into an orphaned tmux process.
            SessionHub.Instance.SessionStore.Stop(session);
        }

        public static string Quote(string s) => PagerCommands.Quote(s);
    }
}
