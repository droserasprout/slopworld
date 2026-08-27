using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // The session list the daemon reports, the screen buffers it streams, and every mutation
    // that changes what sessions exist. Mutations go over HTTP rather than the socket: they
    // rewrite config.toml, and the error body matters. The list and screens are updated both
    // here and from the socket, so the coordinator hands screen/session events straight in.
    class SessionStore
    {
        public List<SessionInfo> Sessions = new List<SessionInfo>();

        readonly Dictionary<string, ScreenBuf> _screens = new Dictionary<string, ScreenBuf>();
        readonly Dictionary<string, ScreenBuf> _scrolls = new Dictionary<string, ScreenBuf>();
        // The daemon's sessions event can arrive before the HTTP response that confirms a
        // rename. Keep the old name marked until that response settles so the terminal does not
        // mistake the expected gap for an exited session.
        readonly Dictionary<string, string> _pendingRenames = new Dictionary<string, string>();
        long _sessionsVersion;
        int _refreshSerial;

        public SessionInfo Get(string name) => Sessions.FirstOrDefault(s => s.Name == name);

        public bool TryPendingRename(string oldName, out string newName) =>
            _pendingRenames.TryGetValue(oldName, out newName);

        public ScreenBuf Screen(string name) =>
            _screens.TryGetValue(name, out var s) ? s : null;

        public ScreenBuf ScrollScreen(string name) =>
            _scrolls.TryGetValue(name, out var s) ? s : null;

        // Apply the local half of a successful rename before the refresh it starts. The
        // terminal can then change its binding immediately without EnsureSession mistaking
        // the new name for an unknown session while the HTTP snapshot is in flight.
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

            Move(_screens, oldName, newName);
            Move(_scrolls, oldName, newName);
            if (found) _sessionsVersion++;
        }

        static void Move(Dictionary<string, ScreenBuf> store, string oldName, string newName)
        {
            if (!store.TryGetValue(oldName, out var screen)) return;
            store.Remove(oldName);
            if (!store.ContainsKey(newName)) store[newName] = screen;
        }

        // The socket's "sessions" event: replace the list, then drop the screens of sessions
        // that no longer exist.
        public void ApplySessions(JVal ev)
        {
            _sessionsVersion++;
            ReplaceSessions(ev);
        }

        void ReplaceSessions(JVal ev)
        {
            Sessions = ev["sessions"].Items.Select(SessionInfo.FromJson).ToList();
            ForgetScreens();
        }

        // The socket's "screen" event. Scrolled frames answer one wheel request; kept apart
        // from the live view.
        public void ApplyScreen(JVal screen)
        {
            string name = screen["name"].AsString();
            int off = screen["off"].AsInt(0);
            bool historyReply = off > 0 || screen["request_id"].AsLong(0) > 0;
            if (historyReply)
            {
                // History responses are viewport snapshots. Keep each response immutable so
                // TerminalHistory can retain and overlap its rows. A request clamped to empty history has
                // off=0 but keeps its request id, and must not overwrite the streamed live frame.
                var history = new ScreenBuf();
                history.FromJson(screen);
                _scrolls[name] = history;
                return;
            }

            var store = _screens;
            if (!store.TryGetValue(name, out var buf))
                store[name] = buf = new ScreenBuf();
            buf.FromJson(screen);
        }

        // Both stores are keyed by session name and nothing else ever drops from them, so an
        // agent that has been removed - or a temporary errand, which mints a fresh name every
        // time one is run - would leave its last screen behind for as long as the game is up.
        // The session list is the daemon's own answer to "what exists", so it is what prunes.
        void ForgetScreens()
        {
            Prune(_screens);
            Prune(_scrolls);
        }

        void Prune(Dictionary<string, ScreenBuf> store)
        {
            if (store.Count == 0) return;
            List<string> gone = null;
            foreach (var name in store.Keys)
                if (!Sessions.Any(s => s.Name == name))
                    (gone ?? (gone = new List<string>())).Add(name);
            if (gone == null) return;
            foreach (var name in gone) store.Remove(name);
        }

        public void Refresh(Action done = null, Action<string> fail = null)
        {
            long version = _sessionsVersion;
            int serial = ++_refreshSerial;
            SlopClient.Get("/api/sessions", j =>
            {
                // A websocket event is newer than an HTTP snapshot requested before it, and a
                // later refresh supersedes an earlier one. Applying either stale answer can
                // briefly hide a newly created session or resurrect one that just stopped.
                if (version == _sessionsVersion && serial == _refreshSerial)
                    ReplaceSessions(j);
                done?.Invoke();
            }, fail);
        }

        // A terminal path is relative to the shell's current directory, not necessarily the
        // project's configured root. Ask tmux at action time so a recent `cd` is respected.
        public void CurrentPath(string name, Action<string> done, Action<string> fail = null) =>
            SlopClient.Get($"/api/sessions/{HubWire.Esc(name)}/cwd",
                j => done?.Invoke(j["path"].AsString()), fail);

        // Run an ephemeral shell/prompt without creating a shortcut; refresh Sessions before
        // the callback so a newly opened pane is visible next frame.
        public void Run(string project, string command, string label,
                        Action<string> started, Action<string> fail = null,
                        bool shell = true, string text = "", bool host = false, bool temp = false,
                        string path = "", bool hold = false) =>
            SlopClient.Post("/api/run",
                "{" + $"\"project\":{JVal.Q(project ?? "")}," +
                $"\"kind\":{JVal.Q(shell ? "shell" : "prompt")}," +
                $"\"command\":{JVal.Q(command ?? "")}," +
                $"\"path\":{JVal.Q(path ?? "")}," +
                $"\"label\":{JVal.Q(label ?? "")}," +
                $"\"text\":{JVal.Q(text ?? "")}," +
                $"\"host\":{JVal.B(host)}," +
                $"\"temp\":{JVal.B(temp)}," +
                $"\"hold\":{JVal.B(hold)}" + "}",
                j => Started(j, started, fail),
                fail);

        // Host terminal leaves command/label empty: slopd chooses `$SHELL` and returns the
        // generated project-shell session name.
        public void RunHostShell(string project, Action<string> started,
                                 Action<string> fail = null) =>
            Run(project, "", "", started, fail, shell: true, host: true);

        // The sessions list is fetched again before the answer is handed on: a terminal opened
        // on a session this end has never heard of closes itself next frame. `project` and
        // `temp` are the same message whether they answer an `ask` entry or override one.
        public void RunShortcut(string name, Action<string> started, Action<string> fail = null,
                                string project = null, bool temp = false,
                                List<string> randomTips = null) =>
            SlopClient.Post($"/api/shortcuts/{HubWire.Esc(name)}/run",
                "{" + $"\"project\":{(string.IsNullOrEmpty(project) ? "null" : JVal.Q(project))}," +
                $"\"temp\":{JVal.B(temp)}," +
                $"\"random_tips\":{HubWire.Tips(randomTips)}" + "}",
                j => Started(j, started, fail),
                fail);

        // A run's answer names the session; refresh the list before handing it on so the pane
        // it opens is visible next frame.
        void Started(JVal j, Action<string> started, Action<string> fail)
        {
            string session = j["session"].AsString();
            Refresh(() => started?.Invoke(session), fail);
        }

        public void Start(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/start", null, _ => Refresh(), fail);

        public void Stop(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/stop", null, _ => Refresh(), fail);

        public void Restart(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/restart", null, _ => Refresh(), fail);

        public void ResetState(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/state/reset", null, _ => Refresh(), fail);

        public void SetLabel(string name, string label, Action ok = null, Action<string> fail = null)
        {
            SlopClient.Put($"/api/sessions/{HubWire.Esc(name)}/label",
                $"{{\"label\":{JVal.Q(label ?? "")}}}",
                _ => { Refresh(); ok?.Invoke(); }, fail);
        }

        public void Remove(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/sessions/{name}", _ => Refresh(), fail);

        // `origName` addresses the edit: the name in `s` may be a new one the daemon has not
        // heard of, which is how a rename is spelled.
        public void Save(SessionInfo s, bool isNew, string origName, Action ok, Action<string> fail)
        {
            bool renamed = !isNew && origName != s.Name;
            if (renamed) _pendingRenames[origName] = s.Name;

            Action<JVal> done = _ =>
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
            if (isNew) SlopClient.Post("/api/sessions", s.ToJson(), done, error);
            else SlopClient.Put($"/api/sessions/{origName}", s.ToJson(), done, error);
        }
    }
}
