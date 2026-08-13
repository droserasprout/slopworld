namespace SlopWorld
{
    // Each view owns one ephemeral pager session for files or git diffs; a new selection replaces
    // it, and leaving that view or pane closes it without affecting the other view.
    public class Pager
    {
        string _session;
        string _project;      // which project the persistent session serves
        string _filePath;     // the file named by the persistent viewer
        int _operation;

        // Who is showing, or null. Read rather than acted on - the two views use it to tell
        // "click the row that is already open" from "click a different one".
        public string Session => _session;

        public bool Alive
        {
            get
            {
                var info = _session == null ? null : SessionHub.Instance.Get(_session);
                return info != null && info.Alive;
            }
        }

        // The env vars for a persistent `less` that pipes every file through `highlight`.
        // `%s` is less's own placeholder for the filename, expanded on every `:e`.
        static string LessEnv
        {
            get
            {
                string highlighter = (SessionHub.Instance.Config.Highlighter ?? "").Trim();
                if (highlighter.Length == 0) return "LESS=-R";
                if (!highlighter.Contains("%s")) highlighter += " %s";
                return "LESSOPEN=" + Quote("|" + highlighter) + " LESS=-R";
            }
        }

        static string App(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        static string Executable(string value, string fallback)
        {
            string command = App(value, fallback);
            int end = command.IndexOfAny(new[] { ' ', '\t' });
            return end < 0 ? command : command.Substring(0, end);
        }

        // Git feeds its diff to the pager on stdin, so it needs the configured command
        // without file/line placeholders or the normal file argument.
        public static string PipePager =>
            App(SessionHub.Instance.Config.Pager, "less")
                .Replace("{file}", "").Replace("{line}", "").Trim();

        public static bool IsPagerCommand(string command)
        {
            string exe = Executable(SessionHub.Instance.Config.Pager, "less");
            return command == exe || command.StartsWith(exe + " ") ||
                (command.StartsWith("env ") && command.Contains(" " + exe + " "));
        }

        public static bool IsEditorCommand(string command)
        {
            string exe = Executable(SessionHub.Instance.Config.Editor, "micro");
            return command == exe || command.StartsWith(exe + " ");
        }

        public static string FileCommand(string value, string fallback, string file, int line = 0)
        {
            string template = App(value, fallback);
            bool hasFile = template.Contains("{file}");
            bool hasLine = template.Contains("{line}");
            string command = template
                .Replace("{file}", Quote(file))
                .Replace("{line}", (line < 1 ? 1 : line).ToString());
            if (!hasFile)
            {
                if (line > 0 && !hasLine) command += " +" + (line < 1 ? 1 : line);
                command += " -- " + Quote(file);
            }
            return command;
        }

        public static string PagerCommand(string file, int line = 0) =>
            "env " + LessEnv + " " +
            FileCommand(SessionHub.Instance.Config.Pager, "less", file, line);

        public static string EditorCommand(string file) =>
            FileCommand(SessionHub.Instance.Config.Editor, "micro", file);

        // Open a file in the persistent pager. Reuses the existing tmux session only when it
        // is still alive and already showing this file. A different file gets a fresh session
        // so the sidebar's one-line title follows the file instead of keeping the old name.
        public void ViewFile(string project, string filePath, string label)
        {
            bool host = string.IsNullOrEmpty(project);
            if (!host && SessionHub.Instance.Project(project) == null)
            {
                Release();
                SlopWidgets.Fail($"project '{project}' has gone");
                return;
            }

            // Reuse the existing session if it is alive, on the same project, and already
            // showing this file.
            if (_session != null && _project == project && _filePath == filePath && Alive)
            {
                // The pane is all a reopen promises. Sending a less command here would need
                // another quoting language for paths with spaces or apostrophes, and it would
                // also flash the file again before the caller can read it.
                TerminalWindow.Open(_session);
                return;
            }

            // First time, or project changed, or session died: create a new one.
            int operation = ++_operation;
            string oldSession = _session;
            _session = null;
            _project = null;
            _filePath = null;

            string cmd = PagerCommand(filePath);
            SessionHub.Instance.Run(project, cmd, label,
                session =>
                {
                    if (operation != _operation)
                    {
                        StopIf(session);
                        return;
                    }
                    _session = session;
                    _project = project;
                    _filePath = filePath;
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    if (operation != _operation) return;
                    _session = null;
                    _project = null;
                    _filePath = null;
                    StopIf(oldSession);
                    SlopWidgets.Fail(msg);
                }, host: host, temp: host);
        }

        // Search results need the same tracked reader, but positioned before its first draw.
        // It deliberately starts a fresh pager: less's `:e` has no atomic "open at line"
        // form, and flashing the previous file before a second key arrives is worse than the
        // small process cost of a result click.
        public void ViewFileAt(string project, string filePath, int line, string label)
        {
            Open(project, PagerCommand(filePath, line), label);
        }

        // A temporary agent running one command in the project's own sandbox, which is what
        // makes the pager see the working tree the way the agents working on it do. Nothing
        // is typed into it: the command is the errand.
        //
        // Whatever was open is replaced, and its tmux session - and the pane showing it - goes
        // with it: one at a time is the whole arrangement.
        public void Open(string project, string command, string label)
        {
            // The project may have been renamed or deleted since the listing that put the row
            // on screen; the daemon would refuse either way, but the reason is clearer here.
            if (SessionHub.Instance.Project(project) == null)
            {
                Release();
                SlopWidgets.Fail($"project '{project}' has gone");
                return;
            }

            // Start the new session *before* killing the old one so the terminal pane always
            // has something to show — no blink of the game map between the two.
            int operation = ++_operation;
            string oldSession = _session;
            _session = null;
            _project = null;
            _filePath = null;

            SessionHub.Instance.Run(project, command, label,
                session =>
                {
                    if (operation != _operation)
                    {
                        StopIf(session);
                        return;
                    }
                    _session = session;
                    // Don't set _project — this is a one-off command, not the persistent
                    // pager, so the next ViewFile will create its own session.
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    if (operation != _operation) return;
                    _session = null;
                    _filePath = null;
                    StopIf(oldSession);
                    SlopWidgets.Fail(msg);
                });
        }

        // Bring the open one back, for a reader who clicked the row that is already showing.
        // False where there is nothing to bring back, which is the caller's cue to open one.
        public bool Reopen()
        {
            if (!Alive) return false;
            TerminalWindow.Open(_session);
            return true;
        }

        // A session nobody is looking at any more: the sidebar left the view, the terminal it
        // was shown in closed, or something else was selected. Stopping the ephemeral agent is
        // what closes the process; the pane over it noticing the session is gone is what closes
        // itself.
        public void Release()
        {
            ++_operation;
            string s = _session;
            _session = null;
            _project = null;
            _filePath = null;
            if (s == null) return;
            var info = SessionHub.Instance.Get(s);
            if (info != null && info.Alive) SessionHub.Instance.Stop(s);
        }

        // The terminal's own close, for the session it was showing. The pane is the pager's
        // only home, so closing it is the same focus change as leaving the view.
        public void CloseIf(string session)
        {
            if (session != null && session == _session) Release();
        }

        // Stop a session if it's still alive, swallowing any error.
        static void StopIf(string session)
        {
            if (session == null) return;
            var info = SessionHub.Instance.Get(session);
            if (info != null && info.Alive) SessionHub.Instance.Stop(session);
        }

        // The daemon splits a command line into an argv the way a shell would, so a path with
        // a space in it is two arguments unless it says otherwise. Both views build command
        // lines out of paths they were handed, so the quoting lives here.
        public static string Quote(string s) => "'" + (s ?? "").Replace("'", "'\\''") + "'";
    }
}
