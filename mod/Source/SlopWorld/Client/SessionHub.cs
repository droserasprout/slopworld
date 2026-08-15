using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Verse;

namespace SlopWorld
{
    // Owns the WebSocket, reconnects with backoff, and is pumped once per frame on
    // the main thread.
    public partial class SessionHub
    {
        public static readonly SessionHub Instance = new SessionHub();

        public List<SessionInfo> Sessions = new List<SessionInfo>();
        // Pushed on connect and on any edit, so the dropdown that picks one draws without
        // asking first.
        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<ShortcutInfo> Shortcuts = new List<ShortcutInfo>();
        // Fetched rather than listed here, and fetched again on every dialog that draws
        // them: both tables are TOML files under the daemon's config directory, so a preset
        // can arrive without slopd being rebuilt or restarted.
        public List<PresetInfo> Presets = new List<PresetInfo>();
        public List<CommandInfo> Commands = new List<CommandInfo>();
        // Defaults for transient host applications. This is refreshed on connect and after
        // any settings page saves; the initial object keeps file actions usable before the
        // first daemon response.
        public SlopConfig Config = new SlopConfig();
        // Never null: an empty one draws as "no numbers", which is what a daemon that has not
        // answered yet means.
        public UsageInfo Usage = new UsageInfo();
        public string Status = "disconnected";
        public bool Online => _ws != null && _ws.Connected;

        readonly Dictionary<string, ScreenBuf> _screens = new Dictionary<string, ScreenBuf>();
        readonly Dictionary<string, ScreenBuf> _scrolls = new Dictionary<string, ScreenBuf>();
        readonly HashSet<string> _subs = new HashSet<string>();

        MiniWebSocket _ws;
        float _nextRetry;
        int _backoff = 1;

        public SessionInfo Get(string name) => Sessions.FirstOrDefault(s => s.Name == name);

        public ScreenBuf Screen(string name) =>
            _screens.TryGetValue(name, out var s) ? s : null;

        public ScreenBuf ScrollScreen(string name) =>
            _scrolls.TryGetValue(name, out var s) ? s : null;


        public void Connect()
        {
            Disconnect();
            _ws = new MiniWebSocket();
            Status = "connecting";

            var connection = Settings.Connection;
            if (_ws.Connect(connection.Host, connection.Port, "/ws", connection.Token))
            {
                Status = "connected";
                _backoff = 1;
                RefreshConfig();
                // A reconnect must not silently drop the terminal the player has open.
                foreach (var name in _subs.ToList())
                    _ws.SendText($"{{\"t\":\"sub\",\"name\":{JVal.Q(name)}}}");
            }
            else
            {
                ScheduleRetry(_ws.LastError);
            }
        }

        void ScheduleRetry(string error)
        {
            Status = $"offline: {error}";
            _ws?.Dispose();
            _ws = null;
            _nextRetry = UnityEngine.Time.realtimeSinceStartup + _backoff;
            // Capped low: the usual reason the socket dies is `make install-daemon`, which is
            // over in about two seconds.
            _backoff = Math.Min(_backoff * 2, 5);
        }

        public void Disconnect()
        {
            _ws?.Dispose();
            _ws = null;
            Status = "disconnected";
        }

        // Called every frame from the Root.Update patch.
        public void Update()
        {
            SlopClient.PumpCompletions();

            if (!Settings.AutoConnect)
            {
                // Auto-connect is a live setting: turning it off must also release an
                // already-open socket, not merely stop the next retry.
                if (_ws != null) Disconnect();
                return;
            }

            if (_ws == null || !_ws.Connected)
            {
                if (_ws != null && !_ws.Connected)
                {
                    // The reader thread noticed the socket die.
                    ScheduleRetry(_ws.LastError ?? "closed");
                }
                if (UnityEngine.Time.realtimeSinceStartup >= _nextRetry)
                    Connect();
                return;
            }

            while (_ws.Incoming.TryDequeue(out var text))
            {
                try { Handle(JVal.Parse(text)); }
                catch (Exception e) { Log.Warning($"[SlopWorld] bad event: {e.Message}"); }
            }
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

        public void Subscribe(string name)
        {
            _subs.Add(name);
            _ws?.SendText($"{{\"t\":\"sub\",\"name\":{JVal.Q(name)}}}");
        }

        public void Unsubscribe(string name)
        {
            _subs.Remove(name);
            _ws?.SendText($"{{\"t\":\"unsub\",\"name\":{JVal.Q(name)}}}");
        }

        public void SendKeys(string name, IEnumerable<string> keys, bool literal)
        {
            SendKeys(name, keys, literal, null);
        }

        // `randomTips` fills a waiting breadcrumb's `{{ random_tip }}`, one per mention. Null
        // on all but the Enter of an agent that still has breadcrumbs pending: every other
        // keystroke would be paying to send a dozen strings nothing renders.
        public void SendKeys(string name, IEnumerable<string> keys, bool literal,
                             List<string> randomTips)
        {
            if (_ws == null || !_ws.Connected) return;
            var arr = string.Join(",", keys.Select(JVal.Q).ToArray());
            _ws.SendText($"{{\"t\":\"keys\",\"name\":{JVal.Q(name)},\"keys\":[{arr}]," +
                         $"\"literal\":{JVal.B(literal)},\"random_tips\":{Tips(randomTips)}}}");
        }

        static string Tips(List<string> tips) =>
            tips == null ? "[]" : "[" + string.Join(",", tips.Select(JVal.Q).ToArray()) + "]";

        public void RequestScroll(string name, int off, ulong requestId)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"scroll\",\"name\":{JVal.Q(name)},\"off\":{off}," +
                         $"\"request_id\":{requestId}}}");
        }

        // `action` is press/release/drag/wheelup/wheeldown, `button` is 0/1/2 =
        // left/middle/right and ignored for the wheel. `count` repeats the report
        // that many times in one tmux write, so a wheel notch does not spawn one
        // process per scrolled line.
        public void SendMouse(string name, string action, int button, int col, int row,
                              int count = 1)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"mouse\",\"name\":{JVal.Q(name)},\"action\":{JVal.Q(action)}," +
                         $"\"button\":{button},\"col\":{col},\"row\":{row}," +
                         $"\"count\":{count}}}");
        }

        // The daemon wraps it in bracketed-paste markers when the app has that mode on.
        public void Paste(string name, string text)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"paste\",\"name\":{JVal.Q(name)},\"text\":{JVal.Q(text)}}}");
        }

        public void PasteBreadcrumb(string name, string breadcrumb, List<string> randomTips)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"breadcrumb\",\"name\":{JVal.Q(name)}," +
                         $"\"breadcrumb\":{JVal.Q(breadcrumb)}," +
                         $"\"random_tips\":{Tips(randomTips)}}}");
        }

        // The jukebox. A station is an id plus its catalog stream key; a file is the absolute
        // path of one of the mod's OST files or its containing directory. All three nulls stop it, and leaving selection
        // out altogether is the volume moving on its own - which must not restart a stream.
        // URLs never leave the selection message. See Sim/Radio.cs.
        public void SendAudio(string station, string stream, string file, float volume)
        {
            if (_ws == null || !_ws.Connected) return;
            string selection;
            if (file != null)
                selection = $"{{\"file\":{JVal.Q(file)}}}";
            else if (station != null && stream != null)
                selection = $"{{\"station\":{JVal.Q(station)},\"stream\":{JVal.Q(stream)}}}";
            else
                selection = "null";
            _ws.SendText($"{{\"t\":\"audio\",\"selection\":{selection}," +
                         $"\"volume\":{Num(volume)}}}");
        }

        public void SendVolume(float volume)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"audio\",\"volume\":{Num(volume)}}}");
        }

        // Invariant, and short: a comma for a decimal point is not JSON, and the daemon
        // has no use for the last four digits of a slider.
        static string Num(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        public void Resize(string name, int cols, int rows)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"resize\",\"name\":{JVal.Q(name)}," +
                         $"\"cols\":{cols},\"rows\":{rows}}}");
        }

        // Mutations go over HTTP rather than the socket: they rewrite config.toml, and the
        // error body matters.
        public void Refresh() =>
            SlopClient.Get("/api/sessions",
                j => Sessions = j["sessions"].Items.Select(SessionInfo.FromJson).ToList());

        public void RefreshConfig(Action<string> fail = null) =>
            SlopClient.Get("/api/config",
                j => Config = SlopConfig.FromJson(j["values"]), fail);

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // The socket pushes these too, but a window opened while it is down still has to draw
        // something, and this road returns an error body.
        public void RefreshProjects(Action<string> fail = null) =>
            SlopClient.Get("/api/projects",
                j => Projects = j["projects"].Items.Select(ProjectInfo.FromJson).ToList(),
                fail);

        public ShortcutInfo Shortcut(string name) =>
            Shortcuts.FirstOrDefault(s => s.Name == name);

        public void RefreshShortcuts(Action<string> fail = null) =>
            SlopClient.Get("/api/shortcuts",
                j => Shortcuts = j["shortcuts"].Items.Select(ShortcutInfo.FromJson).ToList(),
                fail);

        public void SaveShortcut(ShortcutInfo s, bool isNew, string origName,
                                 Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { RefreshShortcuts(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/shortcuts", s.ToJson(), done, fail);
            else SlopClient.Put($"/api/shortcuts/{Esc(origName)}", s.ToJson(), done, fail);
        }

        public void RemoveShortcut(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/shortcuts/{Esc(name)}",
                _ => RefreshShortcuts(), fail);

        // The sessions list is fetched again before the answer is handed on: a terminal opened
        // on a session this end has never heard of closes itself next frame. `project` and
        // `temp` are the same message whether they answer an `ask` entry or override one.
        public void RunShortcut(string name, Action<string> started, Action<string> fail = null,
                                string project = null, bool temp = false,
                                List<string> randomTips = null) =>
            SlopClient.Post($"/api/shortcuts/{Esc(name)}/run",
                "{" + $"\"project\":{(string.IsNullOrEmpty(project) ? "null" : JVal.Q(project))}," +
                $"\"temp\":{JVal.B(temp)}," +
                $"\"random_tips\":{Tips(randomTips)}" + "}",
                j =>
                {
                    string session = j["session"].AsString();
                    SlopClient.Get("/api/sessions", list =>
                    {
                        Sessions = list["sessions"].Items.Select(SessionInfo.FromJson).ToList();
                        started?.Invoke(session);
                    }, fail);
                },
                fail);

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
                j =>
                {
                    string session = j["session"].AsString();
                    SlopClient.Get("/api/sessions", list =>
                    {
                        Sessions = list["sessions"].Items.Select(SessionInfo.FromJson).ToList();
                        started?.Invoke(session);
                    }, fail);
                },
                fail);

        // Host terminal leaves command/label empty: slopd chooses `$SHELL` and returns the
        // generated project-shell session name.
        public void RunHostShell(string project, Action<string> started,
                                 Action<string> fail = null) =>
            Run(project, "", "", started, fail, shell: true, host: true);

        // A shortcut's name is free-form, so it can carry anything a path segment objects to.
        static string Esc(string name) => Uri.EscapeDataString(name ?? "");

        // The old lists stay up until the answer lands, so a dialog opened with the socket
        // down draws what it knew rather than nothing.
        public void LoadPresets(Action ok = null, Action<string> fail = null)
        {
            SlopClient.Get("/api/presets", j =>
            {
                Presets = j["presets"].Items.Select(PresetInfo.FromJson).ToList();
                Commands = j["commands"].Items.Select(CommandInfo.FromJson).ToList();
                ok?.Invoke();
            }, fail);
        }

        public void CopyPreset(string kind, string name, string newName,
                               Action ok, Action<string> fail) =>
            SlopClient.Post($"/api/presets/{kind}/{Uri.EscapeDataString(name)}/copy",
                $"{{\"name\":{JVal.Q(newName ?? "")}}}", _ =>
                {
                    LoadPresets(ok, fail);
                }, fail);

        public void SavePreset(PresetInfo p, Action ok, Action<string> fail) =>
            SlopClient.Put($"/api/presets/sandbox/{Uri.EscapeDataString(p.Name)}", p.ToJson(),
                _ => LoadPresets(ok, fail), fail);

        public void RemovePreset(string kind, string name, Action ok, Action<string> fail) =>
            SlopClient.Delete($"/api/presets/{kind}/{Uri.EscapeDataString(name)}",
                _ => LoadPresets(ok, fail), fail);

        public void SaveCommand(CommandInfo c, Action ok, Action<string> fail) =>
            SlopClient.Put($"/api/presets/command/{Uri.EscapeDataString(c.Name)}", c.ToJson(),
                _ => LoadPresets(ok, fail), fail);

        public CommandInfo Command(string name) =>
            string.IsNullOrEmpty(name) ? null : Commands.FirstOrDefault(c => c.Name == name);

        public void SaveProject(ProjectInfo p, bool isNew, string origName,
                                Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { RefreshProjects(); Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/projects", p.ToJson(), done, fail);
            else SlopClient.Put($"/api/projects/{origName}", p.ToJson(), done, fail);
        }

        // The daemon refuses this while agents still work there, and says which ones.
        public void RemoveProject(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/projects/{name}",
                _ => { RefreshProjects(); Refresh(); }, fail);

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
            SlopClient.Put($"/api/sessions/{Esc(name)}/label",
                $"{{\"label\":{JVal.Q(label ?? "")}}}",
                _ => { Refresh(); ok?.Invoke(); }, fail);
        }

        public void Remove(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/sessions/{name}", _ => Refresh(), fail);

        // `origName` addresses the edit: the name in `s` may be a new one the daemon has not
        // heard of, which is how a rename is spelled.
        public void Save(SessionInfo s, bool isNew, string origName, Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/sessions", s.ToJson(), done, fail);
            else SlopClient.Put($"/api/sessions/{origName}", s.ToJson(), done, fail);
        }
    }
}
