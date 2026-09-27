using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // HTTP changes report errors. HTTP replies and socket events both update session state.
    class SessionStore
    {
        const string SessionsPath = WireProtocol.Routes.Sessions;
        const string LibraryPath = WireProtocol.Routes.Library;
        const string RunPath = WireProtocol.Routes.Run;

        public List<SessionInfo> Sessions = new List<SessionInfo>();

        // Session names are the handle used by nearly every UI and simulation caller. Keep
        // the list for ordering and the index for those repeated lookups.
        readonly Dictionary<string, SessionInfo> _byName =
            new Dictionary<string, SessionInfo>(StringComparer.Ordinal);

        readonly Dictionary<string, ScreenBuf> _screens = new Dictionary<string, ScreenBuf>();
        readonly Dictionary<string, Queue<ScreenBuf>> _scrolls =
            new Dictionary<string, Queue<ScreenBuf>>();
        // A sessions event can arrive before the HTTP response that confirms a rename.
        // Track the old name until the response arrives so the terminal does not treat the rename as session termination.
        readonly Dictionary<string, string> _pendingRenames = new Dictionary<string, string>();
        long _sessionsVersion;
        int _refreshSerial;

        public long Version => _sessionsVersion;

        public SessionInfo Get(string name)
        {
            if (name == null) return null;
            return _byName.TryGetValue(name, out var session) ? session : null;
        }

        public bool TryPendingRename(string oldName, out string newName)
        {
            newName = null;
            return !string.IsNullOrEmpty(oldName) && _pendingRenames.TryGetValue(oldName, out newName);
        }

        // The HTTP callback can update the colony before a sessions event replaces the old name.
        // Resolve the alternate name during this transition.
        public bool TryPendingRenameSource(string newName, out string oldName)
        {
            oldName = null;
            if (string.IsNullOrEmpty(newName)) return false;
            foreach (var pair in _pendingRenames)
            {
                if (pair.Value == newName)
                {
                    oldName = pair.Key;
                    return true;
                }
            }
            return false;
        }

        public ScreenBuf Screen(string name) =>
            _screens.TryGetValue(name, out var s) ? s : null;

        public void BeginSubscription(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            // Compare live ScreenBuf values only within one continuous subscription.
            // Discard the previous frame after a subscription gap.
            // Use the first new frame as the baseline so missed redraws do not become local history changes.
            _screens.Remove(name);
            _scrolls.Remove(name);
        }

        public void EndSubscription(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            _screens.Remove(name);
            _scrolls.Remove(name);
        }

        // A reconnect starts a new screen sequence stream.
        // Keep new frames and history replies separate from data received on the previous connection.
        public void ResetConnectionScreens()
        {
            _screens.Clear();
            _scrolls.Clear();
        }

        public bool TryScrollScreen(string name, out ScreenBuf screen)
        {
            screen = null;
            if (!_scrolls.TryGetValue(name, out var pending) || pending.Count == 0)
                return false;
            screen = pending.Dequeue();
            return true;
        }

        // Apply a successful rename locally before requesting a new session list.
        // The terminal can then change its binding without EnsureSession rejecting the new name while the response is pending.
        public void Rename(string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) ||
                oldName == newName) return;

            bool found = false;
            foreach (var session in Sessions)
            {
                if (session.Name != oldName) continue;
                session.Name = newName;
                found = true;
                break;
            }

            if (found) Reindex();
            Move(_screens, oldName, newName);
            Move(_scrolls, oldName, newName);
            if (found) _sessionsVersion++;
        }

        static void Move<T>(Dictionary<string, T> store, string oldName, string newName)
        {
            if (!store.TryGetValue(oldName, out var screen)) return;
            store.Remove(oldName);
            if (!store.ContainsKey(newName)) store[newName] = screen;
        }

        // Replace the list when the socket sends a "sessions" event.
        // Then discard screens for sessions that no longer exist.
        public void ApplySessions(Wire.SessionsReply ev)
        {
            ReplaceSessions(ev);
        }

        void ReplaceSessions(Wire.SessionsReply ev)
        {
            _sessionsVersion++;
            var next = ev.Sessions.Select(SessionInfo.FromWire).ToList();
            foreach (var session in next)
            {
                if (session == null || !_byName.TryGetValue(session.Name, out var previous) ||
                    previous.Alive == session.Alive)
                    continue;

                // A durable name can outlive several processes. Its next live frame belongs
                // to a new emulator, not to the screen/history retained for the old process.
                _screens.Remove(session.Name);
                _scrolls.Remove(session.Name);
            }
            Sessions = next;
            Reindex();
            ForgetScreens();
        }

        void Reindex()
        {
            _byName.Clear();
            foreach (var session in Sessions)
            {
                if (session == null || session.Name == null || _byName.ContainsKey(session.Name))
                    continue;
                // Preserve FirstOrDefault's first-match behavior if malformed input ever
                // contains duplicate names.
                _byName.Add(session.Name, session);
            }
        }

        // Handle the socket's "screen" event.
        // Keep frames for scroll requests separate from the live view.
        public void ApplyScreen(Wire.ScreenView screen)
        {
            string name = screen.Name;
            int off = (int)screen.Off;
            bool historyReply = off > 0 || (long)screen.RequestId > 0;
            if (historyReply)
            {
                // History responses are viewport snapshots.
                // Keep them immutable so TerminalHistory can retain rows and detect overlap.
                // A request for empty history has off=0 but retains its request ID.
                // It must not replace the live frame.
                var history = new ScreenBuf();
                history.FromWire(screen);
                if (!_scrolls.TryGetValue(name, out var pending))
                    _scrolls[name] = pending = new Queue<ScreenBuf>();
                pending.Enqueue(history);
                return;
            }

            var store = _screens;
            if (!store.TryGetValue(name, out var buf))
                store[name] = buf = new ScreenBuf();
            // An old queued frame can arrive after the snapshot from a new subscription.
            // Do not let it replace the new baseline.
            // Otherwise, the next current frame could appear to scroll by one row.
            if (buf.Seq >= 0 && (int)screen.Seq < buf.Seq) return;
            buf.FromWire(screen);
        }

        // Prune against the daemon session list so finished errands do not retain screen buffers.
        void ForgetScreens()
        {
            Prune(_screens);
            Prune(_scrolls);
        }

        void Prune<T>(Dictionary<string, T> store)
        {
            if (store.Count == 0) return;
            List<string> gone = null;
            foreach (var name in store.Keys)
                if (!_byName.ContainsKey(name))
                    (gone ?? (gone = new List<string>())).Add(name);
            if (gone == null) return;
            foreach (var name in gone) store.Remove(name);
        }

        public void Refresh(Action done = null, Action<string> fail = null)
        {
            long version = _sessionsVersion;
            int serial = ++_refreshSerial;
            DaemonClient.Get<Wire.SessionsReply>(SessionsPath, j =>
            {
                // Prefer a WebSocket event over an HTTP snapshot requested before that event.
                // Also prefer later refreshes over earlier ones.
                // Stale responses can hide new sessions or restore stopped sessions to the list.
                if (version == _sessionsVersion && serial == _refreshSerial)
                    ReplaceSessions(j);
                done?.Invoke();
            }, fail);
        }

        // A terminal path is relative to the shell's current directory, not necessarily the
        // project's configured root. Ask tmux at action time so a recent `cd` is respected.
        public void CurrentPath(string name, Action<string> done, Action<string> fail = null) =>
            DaemonClient.Get<Wire.PathResult>($"{SessionsPath}/{HubWire.Esc(name)}/cwd",
                j => done?.Invoke(j.Path), fail);

        // Run a temporary shell or prompt without creating a library item.
        // Refresh Sessions before the callback so the new pane remains visible on the next frame.
        public void Run(string project, string command, string label,
                        Action<string> started, Action<string> fail = null,
                        bool shell = true, string text = "", bool host = false, bool temp = false,
                        string path = "", bool hold = false, string like = "", string agentTemplate = "", string worktree = "",
                        string intent = "", string readerPath = "", string readerKey = "", string readerScope = "", int readerLine = 0,
                        bool readerPinned = false)
        {
            if (BrowseScope.IsKey(project))
            {
                var selected = BrowseScope.ProjectOf(project, SessionHub.Instance.Projects);
                if (selected == null) { fail?.Invoke("The reader's project has gone."); return; }
                worktree = BrowseScope.WorktreeOf(project);
                project = selected.Name;
            }
            var request = new Wire.RunReq
            {
                Worktree = worktree ?? "",
                Project = project ?? "",
                Kind = shell ? "shell" : "prompt",
                Command = command ?? "",
                Path = path ?? "",
                Label = label ?? "",
                Intent = intent ?? "",
                ReaderPath = readerPath ?? "",
                ReaderKey = readerKey ?? "",
                ReaderScope = readerScope ?? "",
                ReaderLine = (uint)System.Math.Max(0, readerLine),
                ReaderPinned = readerPinned,
                Text = text ?? "",
                Host = host,
                Temp = temp,
                Hold = hold,
                Like = like ?? "",
                AgentTemplate = agentTemplate ?? ""
            };
            if (TerminalWindow.TryPanelShape(out int cols, out int rows)) { request.Cols = (uint)cols; request.Rows = (uint)rows; }
            DaemonClient.Post<Wire.SessionResult>(RunPath, request, j => Started(j, started, fail), fail);
        }

        // Host terminal leaves command/label empty: slopd chooses `$SHELL` and returns the
        // generated project-shell session name.
        public void RunHostShell(string project, Action<string> started,
                                 Action<string> fail = null, string worktree = "")
        {
            if (BrowseScope.IsKey(project))
            {
                var selected = BrowseScope.ProjectOf(project, SessionHub.Instance.Projects);
                if (selected == null) { fail?.Invoke("The reader's project has gone."); return; }
                worktree = BrowseScope.WorktreeOf(project);
                project = selected.Name;
            }
            DaemonClient.Post<Wire.SessionResult>(RunPath, new Wire.RunReq { Project = project ?? "", Worktree = worktree ?? "", Kind = "shell", Host = true },
                j => Started(j, started, fail), fail);
        }

        // Refresh the session list before returning the result.
        // Otherwise, a terminal for an unknown session closes on the next frame.
        // Use the same project and temp fields for Ask responses and explicit overrides.
        public void RunLibraryItem(string name, Action<string> started, Action<string> fail = null,
                                string project = null, bool temp = false,
                                List<string> randomTips = null)
        {
            var request = new Wire.RunWhere { Temp = temp, RandomTips = { randomTips ?? Enumerable.Empty<string>() } };
            if (!string.IsNullOrEmpty(project)) request.Project = project;
            DaemonClient.Post<Wire.SessionResult>($"{LibraryPath}/{HubWire.Esc(name)}/run", request,
                j => Started(j, started, fail), fail);
        }

        // The run response supplies the session name.
        // Refresh the list before returning it so the new pane remains visible on the next frame.
        void Started(Wire.SessionResult j, Action<string> started, Action<string> fail)
        {
            string session = j.Session;
            Refresh(() => started?.Invoke(session), fail);
        }

        public void Start(string name, Action<string> fail = null) =>
            DaemonClient.Post($"{SessionsPath}/{HubWire.Esc(name)}/start", null, _ => Refresh(), fail);

        public void Stop(string name, Action<string> fail = null) =>
            DaemonClient.Post($"{SessionsPath}/{HubWire.Esc(name)}/stop", null, _ => Refresh(), fail);

        public void Restart(string name, Action<string> fail = null) =>
            DaemonClient.Post($"{SessionsPath}/{HubWire.Esc(name)}/restart", null, _ => Refresh(), fail);

        public void ResetState(string name, Action<string> fail = null) =>
            DaemonClient.Post($"{SessionsPath}/{HubWire.Esc(name)}/state/reset", null, _ => Refresh(), fail);

        public void SetLabel(string name, string label, Action ok = null, Action<string> fail = null)
        {
            DaemonClient.Put($"{SessionsPath}/{HubWire.Esc(name)}/label",
                new Wire.LabelReq { Label = label ?? "" },
                _ => { Refresh(); ok?.Invoke(); }, fail);
        }

        public void SetReaderPinned(string name, bool pinned) =>
            DaemonClient.Put($"{SessionsPath}/{HubWire.Esc(name)}/reader-pinned",
                new Wire.ReaderPinnedReq { Pinned = pinned }, _ => Refresh());

        public void Remove(string name, Action<string> fail = null) =>
            DaemonClient.Delete($"{SessionsPath}/{HubWire.Esc(name)}", _ => Refresh(), fail);

        // Identify the existing session with origName.
        // During a rename, s contains the new name that the daemon does not yet recognize.
        public void Save(SessionInfo s, bool isNew, string origName, Action ok, Action<string> fail)
        {
            bool renamed = !isNew && origName != s.Name;
            if (renamed) _pendingRenames[origName] = s.Name;

            Action<Wire.Ack> done = _ =>
            {
                if (renamed)
                {
                    Rename(origName, s.Name);
                    _pendingRenames.Remove(origName);
                }
                Refresh();
                ok?.Invoke();
            };
            Action<string> error = message =>
            {
                if (renamed) _pendingRenames.Remove(origName);
                fail?.Invoke(message);
            };
            if (isNew) DaemonClient.Post(SessionsPath, s.ToWire(), done, error);
            else DaemonClient.Put($"{SessionsPath}/{HubWire.Esc(origName)}", s.ToWire(), done, error);
        }
    }
}
