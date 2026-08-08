namespace SlopWorld
{
    // One ephemeral agent, running one pager, shown in a pane over the sidebar - and the
    // sidebar's grip on it, so the next thing clicked can replace it and the focus leaving
    // can close it.
    //
    // Both bodies that read something want exactly this: the files view opens `less` on a
    // file, the git view opens `git diff` on a change, and each has at most one open at a
    // time. What differs is the command; what does not is that leaving the view, closing the
    // pane, or picking something else all mean the same thing to the session behind it.
    //
    // An instance rather than a static: two views, two pagers, and the git view's diff is not
    // closed by the files view being left.
    public class Pager
    {
        string _session;
        string _project;      // which project the persistent session serves

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
        const string LessEnv =
            "LESSOPEN='|highlight --out-format=xterm256 %s' LESS=-R";

        // Open a file in the persistent pager. Reuses the existing tmux session if it is
        // still alive and serving the same project — sends `:e <path>` via keys instead of
        // spawning a whole new sandbox. For a different project, or a dead session, falls
        // back to creating a fresh one.
        public void ViewFile(string project, string filePath, string label)
        {
            if (SessionHub.Instance.Project(project) == null)
            {
                Release();
                SlopWidgets.Fail($"project '{project}' has gone");
                return;
            }

            // Reuse the existing session if it's alive and on the same project.
            if (_session != null && _project == project && Alive)
            {
                SessionHub.Instance.SendKeys(
                    _session, new[] { ":e " + filePath, "Enter" }, true);
                TerminalWindow.Open(_session);
                return;
            }

            // First time, or project changed, or session died: create a new one.
            string oldSession = _session;
            _session = null;
            _project = null;

            string cmd = "env " + LessEnv + " less " + Quote(filePath);
            SessionHub.Instance.Run(project, cmd, label,
                session =>
                {
                    _session = session;
                    _project = project;
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    _session = null;
                    _project = null;
                    StopIf(oldSession);
                    SlopWidgets.Fail(msg);
                });
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
            string oldSession = _session;
            _session = null;
            _project = null;

            SessionHub.Instance.Run(project, command, label,
                session =>
                {
                    _session = session;
                    // Don't set _project — this is a one-off command, not the persistent
                    // pager, so the next ViewFile will create its own session.
                    TerminalWindow.Open(session);
                    StopIf(oldSession);
                },
                msg =>
                {
                    _session = null;
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
            string s = _session;
            _session = null;
            _project = null;
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
