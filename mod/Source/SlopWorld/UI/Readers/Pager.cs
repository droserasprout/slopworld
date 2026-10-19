namespace SlopWorld
{
    // Owns one committed reader and at most one pending replacement. PagerTabs owns
    // preview/pin slots; ReaderSessionVisibility owns the sidebar publication barrier.
    public class Pager : IPreviewTab
    {
        readonly System.Func<System.Action> _prepareShow;
        ReaderSession _active;
        ReaderReplacement _pending;
        int _operation;
        bool _locked;

        public Pager(System.Func<System.Action> prepareShow = null) => _prepareShow = prepareShow;

        internal static ReaderSessionVisibility SessionVisibility =>
            ReaderSessionVisibility.For(SessionHub.Instance);

        internal int Operation => _operation;
        internal bool Opening => _pending?.Kind == ReaderReplacementKind.Open;
        ReaderSource RequestedSource => Opening ? _pending.Source : _active?.Source;

        public string Session => _active?.Session;

        // Pending source identity permits deletion to cancel a first open. ReaderProbe
        // also captures Operation, so a reply for the old source cannot cancel a replacement.
        public string FilePath => RequestedSource?.Path;
        public bool Locked => _locked;
        public bool Alive => Session != null && SessionHub.Instance.Get(Session)?.Alive == true;

        public void AttachRestored(SessionInfo info)
        {
            if (info == null || !info.Alive || string.IsNullOrEmpty(info.Intent))
                return;
            ++_operation;
            _pending = null;
            _locked = info.ReaderPinned;
            string sourceCommand =
                (info.Intent == "view" || info.Intent == "search")
                && !string.IsNullOrEmpty(info.ReaderPath)
                    ? PagerCommand(info.ReaderPath, info.ReaderLine)
                    : null;
            Publish(
                new ReaderSession(
                    info.Name,
                    new ReaderSource(
                        string.IsNullOrEmpty(info.ReaderScope) ? info.Project : info.ReaderScope,
                        info.ReaderKey,
                        info.ReaderPath,
                        info.Label,
                        info.Cmd,
                        sourceCommand,
                        info.Intent,
                        info.ReaderLine,
                        info.Intent == "view"
                    ),
                    null
                )
            );
        }

        public bool Owns(string project, string key) =>
            (Opening || Alive) && RequestedSource?.Matches(project, key) == true;

        public bool OwnsSource(string project, string path) =>
            RequestedSource?.SourceCommand != null && Owns(project, path);

        public bool Pending(string project, string key) => Opening && Owns(project, key);

        public bool Matches(string project, string key) => !Opening && Alive && Owns(project, key);

        public void Invalidate()
        {
            _locked = false;
            Release();
        }

        public bool LockPreview(string project, string key)
        {
            if (RequestedSource?.Matches(project, key) != true)
                return false;
            _locked = true;
            PublishPin();
            return true;
        }

        public bool Lock()
        {
            if (!Alive)
                return false;
            _locked = true;
            PublishPin();
            return true;
        }

        void PublishPin()
        {
            if (_locked && Session != null)
                SessionHub.Instance.SessionStore.SetReaderPinned(Session, true);
        }

        // Git feeds its diff to the pager on stdin, so it needs the configured command
        // without file/line placeholders or the normal file argument.
        public static string PipePager =>
            PagerCommands.PipePager(SessionHub.Instance.Config.EffectivePager);

        public static bool IsPagerCommand(string command) =>
            PagerCommands.IsPagerCommand(SessionHub.Instance.Config.EffectivePager, command);

        public static bool IsEditorCommand(string command) =>
            PagerCommands.IsEditorCommand(SessionHub.Instance.Config.Editor, command);

        public static string FileCommand(
            string value,
            string fallback,
            string file,
            long line = 0
        ) => PagerCommands.FileCommand(value, fallback, file, line);

        public static string PagerCommand(string file, long line = 0)
        {
            var settings = ModEntry.Instance.settings;
            string highlighter = SessionHub.Instance.Config.EffectiveHighlighter;
            string pager = PagerCommands.FilePager(
                SessionHub.Instance.Config.EffectivePager,
                settings.codeLineNumbers
            );
            // Bat reads files itself, so the LESSOPEN highlighter does not style a bat pager.
            if (CodeHighlight.Engine(highlighter) == "bat")
                pager = CodeHighlight.Command(pager, settings);
            return PagerCommands.PagerCommand(
                pager,
                CodeHighlight.Command(highlighter, settings),
                file,
                line
            );
        }

        static string DiffCommand(string command)
        {
            var settings = ModEntry.Instance.settings;
            string batTheme =
                CodeHighlight.Engine(SessionHub.Instance.Config.EffectiveHighlighter) == "bat"
                    ? CodeHighlight.Theme(settings, "bat")
                    : null;
            return PagerCommands.DiffCommand(
                command,
                SessionHub.Instance.Config.EffectivePager,
                settings.codeLineNumbers,
                batTheme
            );
        }

        public static string EditorCommand(string file, long line = 0) =>
            PagerCommands.EditorCommand(SessionHub.Instance.Config.Editor, file, line);

        // Reuse is deliberately narrower than a command reader: ordinary file opens
        // promise focus, whereas explicit line/command opens can request a fresh process.
        public void ViewFile(string project, string filePath, string label)
        {
            if (Opening && _pending.Source.ReuseFile && Pending(project, filePath))
                return;
            if (!ValidateProject(project, allowHost: true))
                return;
            if (
                !Opening
                && Alive
                && _active.Source.ReuseFile
                && _active.Source.Matches(project, filePath)
            )
            {
                Show();
                return;
            }
            string command = PagerCommand(filePath);
            BeginReplacement(
                new ReaderSource(
                    project,
                    filePath,
                    filePath,
                    label,
                    command,
                    command,
                    "view",
                    0,
                    true
                ),
                command,
                ReaderReplacementKind.Open
            );
        }

        public void ViewFileAt(
            string project,
            string filePath,
            long line,
            string label,
            string intent = "view"
        ) => Open(project, PagerCommand(filePath, line), label, filePath, filePath, intent, line);

        public void Open(string project, string command, string label) =>
            Open(project, command, label, null);

        public void Open(
            string project,
            string command,
            string label,
            string key,
            string sourcePath = "",
            string intent = "view",
            long line = 0
        )
        {
            if (Opening && Pending(project, key) && _pending.Source.Command == command)
                return;
            if (!ValidateProject(project, allowHost: false))
                return;
            string sourceCommand =
                !string.IsNullOrEmpty(sourcePath)
                && key == sourcePath
                && IsPagerCommand(command)
                && !IsEditorCommand(command)
                    ? command
                    : null;
            var source = new ReaderSource(
                project,
                key,
                sourcePath,
                label,
                command,
                sourceCommand,
                intent,
                line
            );
            BeginReplacement(
                source,
                intent == "diff" ? DiffCommand(command) : command,
                ReaderReplacementKind.Open
            );
        }

        bool ValidateProject(string project, bool allowHost)
        {
            if (
                (allowHost && string.IsNullOrEmpty(project))
                || BrowseScope.ProjectOf(project, SessionHub.Instance.Projects) != null
            )
                return true;
            Release();
            UiLayout.Fail("This reader's project is no longer available.");
            return false;
        }

        // Failed probes retain the committed stamp, allowing the next probe to retry.
        internal void RefreshIfChanged(string stamp)
        {
            if (
                string.IsNullOrEmpty(stamp)
                || _pending != null
                || !Alive
                || _active.Source.SourceCommand == null
                || string.IsNullOrEmpty(_active.Source.Path)
            )
                return;
            if (_active.Stamp == null)
            {
                _active.Stamp = stamp;
                return;
            }
            if (_active.Stamp != stamp)
                BeginReplacement(
                    _active.Source,
                    _active.Source.SourceCommand,
                    ReaderReplacementKind.Refresh,
                    stamp
                );
        }

        internal bool CanRestartForAppearance =>
            Alive
            && _pending == null
            && (
                _active.Source.Intent == "diff"
                    ? !string.IsNullOrEmpty(_active.Source.Command)
                    : _active.Source.SourceCommand != null
                        && !string.IsNullOrEmpty(_active.Source.Path)
            );

        internal void RestartForAppearance(string expectedSession, int expectedOperation)
        {
            if (
                Session != expectedSession
                || _operation != expectedOperation
                || !CanRestartForAppearance
            )
                return;
            var source = _active.Source;
            string command =
                source.Intent == "diff"
                    ? DiffCommand(source.Command)
                    : PagerCommand(source.Path, source.Line);
            if (source.Intent == "view" || source.Intent == "search")
                source = source.WithSourceCommand(command);
            BeginReplacement(source, command, ReaderReplacementKind.Appearance, _active.Stamp);
        }

        // Capture the old owner and display policy before starting asynchronous work.
        void BeginReplacement(
            ReaderSource source,
            string command,
            ReaderReplacementKind kind,
            string stamp = null
        )
        {
            ++_operation;
            var replacement = new ReaderReplacement(
                source,
                _active,
                kind,
                stamp,
                kind == ReaderReplacementKind.Open ? _prepareShow?.Invoke() : null
            );
            _pending = replacement;
            RunReader(
                source.Scope,
                command,
                source.Label,
                session => Commit(replacement, session),
                error => Fail(replacement, error),
                source.LaunchOptions(_locked)
            );
        }

        void Commit(ReaderReplacement replacement, string session)
        {
            if (!ReferenceEquals(_pending, replacement))
            {
                StopIf(session);
                return;
            }
            _pending = null;
            Publish(new ReaderSession(session, replacement.Source, replacement.Stamp));
            PublishPin();
            if (replacement.Kind == ReaderReplacementKind.Open)
                Show(replacement.Shown);
            else
                TerminalWindow.ReplaceReader(replacement.Previous.Session, session);
            StopIf(replacement.Previous?.Session);
        }

        void Fail(ReaderReplacement replacement, string error)
        {
            if (!ReferenceEquals(_pending, replacement))
                return;
            _pending = null;
            // Explicit open failure clears its replaceable pager. Background replacement
            // failures preserve the old reader, its pin and its retry baseline.
            if (replacement.Kind == ReaderReplacementKind.Open)
            {
                Publish(null);
                StopIf(replacement.Previous?.Session);
            }
            if (replacement.Kind != ReaderReplacementKind.Refresh)
                UiLayout.Fail(error);
        }

        void Publish(ReaderSession reader)
        {
            string previous = Session;
            _active = reader;
            if (previous == Session)
                return;
            SessionVisibility.Retire(previous);
            SessionVisibility.Claim(Session);
            RoutedSessionRows.Invalidate();
        }

        // Session snapshots can precede the run callback. Keep the incoming identity
        // private until the callback commits it, including superseded requests that
        // still need to stop a late process. Settlement always invalidates row caches.
        void RunReader(
            string project,
            string command,
            string label,
            System.Action<string> started,
            System.Action<string> failed,
            SessionRunOptions options
        )
        {
            var visibility = SessionVisibility;
            var launch = visibility.Begin(options.Reader.Scope, options.Reader.Key, options.Intent);
            SessionHub.Instance.SessionStore.Run(
                project,
                command,
                label,
                session =>
                {
                    try
                    {
                        started(session);
                    }
                    finally
                    {
                        visibility.End(launch);
                    }
                },
                error =>
                {
                    try
                    {
                        failed(error);
                    }
                    finally
                    {
                        visibility.End(launch);
                    }
                },
                options: options
            );
        }

        public bool Reopen()
        {
            if (!Alive)
                return false;
            Show();
            return true;
        }

        void Show() => Show(_prepareShow?.Invoke());

        void Show(System.Action shown)
        {
            TerminalWindow.Open(Session);
            // Native content retires only after the terminal replaces it.
            shown?.Invoke();
        }

        public void Release()
        {
            if (_locked)
                return;
            ++_operation;
            _pending = null;
            string session = Session;
            Publish(null);
            StopIf(session);
        }

        public void CloseIf(string session)
        {
            if (!_locked && session != null && session == Session)
                Release();
        }

        public bool CloseTab(string session)
        {
            if (session == null || session != Session)
                return false;
            _locked = false;
            Release();
            return true;
        }

        static void StopIf(string session)
        {
            if (session == null)
                return;
            SessionVisibility.Retire(session);
            // Late launch replies may precede their snapshot. Stop by owned identity.
            SessionHub.Instance.SessionStore.Stop(session);
        }

        public static string Quote(string s) => PagerCommands.Quote(s);
    }
}
