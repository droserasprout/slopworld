using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// What the daemon last saw a session doing. Down is "the process is not
    /// running" - the colonist it stands for is put on the floor, not killed,
    /// because a downed pawn is one the same process can get back up later.
    /// </summary>
    public enum AgentState { Down, Working, Waiting, Idle }

    /// <summary>
    /// What a session runs. Claude Code is a kind rather than a command string:
    /// knowing it is Claude is what lets the daemon hand it its own state dir.
    /// </summary>
    public enum AgentKind { Claude, Custom }

    /// <summary>
    /// What a shortcut sends its text to: an agent's input field, or a shell's
    /// prompt. The only thing the two kinds disagree about at the far end.
    /// </summary>
    public enum ShortcutKind { Prompt, Shell }

    public class SessionInfo
    {
        public string Name = "";
        /// The project this agent works in. Everything about where it runs and
        /// what it can reach is the project's answer, not the session's.
        public string Project = "";
        /// The project's directory, repeated on the wire so a list of sessions
        /// reads without joining it against anything. Blank when the entry names
        /// a project that has gone.
        public string Dir = "";
        public AgentKind Kind = AgentKind.Claude;
        /// The command as the daemon will actually run it, defaults resolved.
        /// For a custom session it is also what the player typed, which is why
        /// the dialog edits this field directly.
        public string Agent = "";
        public AgentState State = AgentState.Down;
        public bool Alive;
        /// The project's, and read-only here: edit the project to change them.
        public bool Net = true;
        public bool Sandbox = true;
        public bool Autostart;

        /// Nothing in config.toml stands behind this agent: it is a shortcut's
        /// errand, or a tmux session somebody started by hand, and it leaves the
        /// colony when its process exits rather than lying down. There is no
        /// entry to edit or delete, which is what the rows read off this.
        public bool Ephemeral;

        /// The pane's size as the daemon last reported it. Read-only here: the
        /// terminal window measures itself and sends the resize, so there is
        /// nothing to edit and nothing in config.toml to edit it into.
        public int Cols;
        public int Rows;

        /// The terminal window treats a session that is down or gone as one that
        /// has nothing to show it: no pane, no colonist to watch. Checked in one
        /// place because both spell "close" the same way.
        public bool Gone => !Alive;

        public static AgentState ParseState(string s)
        {
            switch (s)
            {
                case "working": return AgentState.Working;
                case "waiting": return AgentState.Waiting;
                case "idle": return AgentState.Idle;
                default: return AgentState.Down;
            }
        }

        public static SessionInfo FromJson(JVal j) => new SessionInfo
        {
            Name = j["name"].AsString(),
            Project = j["project"].AsString(),
            Dir = j["dir"].AsString(),
            Kind = j["kind"].AsString() == "custom" ? AgentKind.Custom : AgentKind.Claude,
            Agent = j["agent"].AsString(),
            State = ParseState(j["state"].AsString()),
            Alive = j["alive"].AsBool(),
            Net = j["net"].AsBool(true),
            Sandbox = j["sandbox"].AsBool(true),
            Autostart = j["autostart"].AsBool(false),
            Ephemeral = j["ephemeral"].AsBool(false),
            Cols = j["cols"].AsInt(0),
            Rows = j["rows"].AsInt(0),
        };

        /// <summary>
        /// The command only rides along for a custom session: a Claude one takes
        /// the daemon's default, and writing back the resolved string would pin
        /// today's default into the file forever.
        /// </summary>
        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"project\":{JVal.Q(Project)}," +
            $"\"kind\":{JVal.Q(Kind == AgentKind.Custom ? "custom" : "claude")}," +
            $"\"command\":{(Kind == AgentKind.Custom && !string.IsNullOrEmpty(Agent) ? JVal.Q(Agent) : "null")}," +
            $"\"autostart\":{JVal.B(Autostart)}}}";
    }

    /// <summary>
    /// A place work happens: a directory plus the sandbox every agent in it
    /// gets.
    /// </summary>
    public class ProjectInfo
    {
        public string Name = "";
        public string Dir = "";
        /// Names out of the daemon's preset table; see <see cref="PresetInfo"/>.
        public List<string> Presets = new List<string>();
        /// Anything the presets do not cover.
        public List<string> RoPaths = new List<string>();
        public List<string> RwPaths = new List<string>();
        public List<string> PassEnv = new List<string>();
        public bool Net = true;
        public bool Sandbox = true;

        public static ProjectInfo FromJson(JVal j) => new ProjectInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            Presets = Strings(j["presets"]),
            RoPaths = Strings(j["ro_paths"]),
            RwPaths = Strings(j["rw_paths"]),
            PassEnv = Strings(j["pass_env"]),
            Net = j["net"].AsBool(true),
            Sandbox = j["sandbox"].AsBool(true),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"dir\":{JVal.Q(Dir)}," +
            $"\"presets\":{Arr(Presets)},\"ro_paths\":{Arr(RoPaths)}," +
            $"\"rw_paths\":{Arr(RwPaths)},\"pass_env\":{Arr(PassEnv)}," +
            $"\"net\":{JVal.B(Net)},\"sandbox\":{JVal.B(Sandbox)}}}";

        public ProjectInfo Copy() => new ProjectInfo
        {
            Name = Name,
            Dir = Dir,
            Presets = new List<string>(Presets),
            RoPaths = new List<string>(RoPaths),
            RwPaths = new List<string>(RwPaths),
            PassEnv = new List<string>(PassEnv),
            Net = Net,
            Sandbox = Sandbox,
        };

        static List<string> Strings(JVal a) => a.Items.Select(i => i.AsString()).ToList();

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";
    }

    /// <summary>
    /// One errand worth repeating: where to run it, what to run there, and what
    /// to type in once it is up.
    ///
    /// The agent it lands is temporary - never written to config.toml, gone the
    /// moment its process exits - so what is saved is the errand and not the
    /// agent. The template is spelled out here rather than pointing at an
    /// existing one: a shortcut that named an agent would stop working the day
    /// that agent was deleted.
    /// </summary>
    public class ShortcutInfo
    {
        public string Name = "";
        public ShortcutKind Kind = ShortcutKind.Prompt;
        /// The project the temporary agent works in - its directory and its
        /// whole sandbox, the same as for any other agent.
        public string Project = "";
        /// The prompt, or the command line. Sent once the pane is ready for it.
        public string Text = "";
        /// Overrides what runs: another agent for a prompt shortcut, another
        /// shell for a shell one. Blank means the daemon's own default.
        public string Command = "";

        public static ShortcutInfo FromJson(JVal j) => new ShortcutInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString() == "shell" ? ShortcutKind.Shell : ShortcutKind.Prompt,
            Project = j["project"].AsString(),
            Text = j["text"].AsString(),
            Command = j["command"].IsNull ? "" : j["command"].AsString(),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(Kind == ShortcutKind.Shell ? "shell" : "prompt")}," +
            $"\"project\":{JVal.Q(Project)},\"text\":{JVal.Q(Text)}," +
            $"\"command\":{(string.IsNullOrEmpty((Command ?? "").Trim()) ? "null" : JVal.Q(Command))}}}";

        public ShortcutInfo Copy() => new ShortcutInfo
        {
            Name = Name,
            Kind = Kind,
            Project = Project,
            Text = Text,
            Command = Command,
        };
    }

    /// <summary>
    /// One named bundle of binds the daemon knows how to apply. Fetched rather
    /// than listed here: a preset the daemon does not have is a checkbox that
    /// saves and then does nothing.
    /// </summary>
    public class PresetInfo
    {
        public string Name = "";
        public string Description = "";
        /// Every path and env var the preset asks for, for the tooltip.
        public List<string> Gives = new List<string>();

        public static PresetInfo FromJson(JVal j)
        {
            var p = new PresetInfo
            {
                Name = j["name"].AsString(),
                Description = j["description"].AsString(),
            };
            foreach (var key in new[] { "ro", "rw", "dev", "env", "setenv" })
                p.Gives.AddRange(j[key].Items.Select(i => i.AsString()));
            return p;
        }
    }

    /// <summary>
    /// One row of the readout as the daemon last saw it - a rate-limit window,
    /// or the extra-usage budget, which is the same shape in money. The reset is
    /// a duration rather than an instant on purpose: a countdown from when we
    /// heard stays honest if the socket dies.
    /// </summary>
    public class UsageWindow
    {
        public string Key = "";
        public string Label = "";
        /// Percent of the window spent, 0-100. Always sent, money row included.
        public float Pct;
        /// What <see cref="Pct"/> counts: "pct" or "usd". A unit this build does
        /// not know reads as a percentage, which is what every window but one is.
        public string Unit = "pct";
        /// Dollars spent, when <see cref="Unit"/> is usd; -1 when the daemon
        /// sent no figure, which leaves the row a percentage.
        public float Amount = -1f;
        /// What <see cref="Amount"/> is out of; -1 if unsaid.
        public float Limit = -1f;
        /// Seconds to the reset as of <see cref="UsageInfo.Heard"/>; -1 if the
        /// daemon did not say.
        public long ResetsIn = -1;

        /// Whether this row is drawn with a `$` rather than a `%`. Both halves
        /// are required: a unit with no figure under it has nothing to spend.
        public bool IsMoney => Unit == "usd" && Amount >= 0f;
    }

    /// <summary>
    /// What is left of the subscription: the colony's one remaining resource.
    /// Ok false means the last poll failed, in which case the windows are the
    /// previous good ones and <see cref="Error"/> says what went wrong - stale
    /// numbers with a reason beat a readout that empties itself.
    /// </summary>
    public class UsageInfo
    {
        public bool Ok;
        public string Error;
        public string Plan = "";
        public List<UsageWindow> Windows = new List<UsageWindow>();

        /// realtimeSinceStartup when this arrived, which is what ages it and what
        /// the countdown runs from.
        public float Heard;

        public bool Any => Windows.Count > 0;

        /// Real seconds since the daemon last spoke about usage.
        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        /// Seconds left on a window now, floored at zero: a window that has run
        /// out reads as due rather than as a negative number.
        public long Remaining(UsageWindow w) =>
            w.ResetsIn < 0 ? -1 : Math.Max(0L, w.ResetsIn - (long)Age);

        public static UsageInfo FromJson(JVal j) => new UsageInfo
        {
            Ok = j["ok"].AsBool(),
            Error = j["error"].IsNull ? null : j["error"].AsString(),
            Plan = j["plan"].AsString(),
            Heard = UnityEngine.Time.realtimeSinceStartup,
            Windows = j["windows"].Items.Select(w => new UsageWindow
            {
                Key = w["key"].AsString(),
                Label = w["label"].AsString(),
                Pct = w["pct"].AsFloat(),
                Unit = w["unit"].AsString("pct"),
                Amount = w["amount"].AsFloat(-1f),
                Limit = w["limit"].AsFloat(-1f),
                ResetsIn = w["resets_in"].IsNull ? -1 : w["resets_in"].AsLong(-1),
            }).ToList(),
        };
    }

    public class ScreenBuf
    {
        public int Seq = -1;
        public int Cols, Rows, Cx, Cy;
        /// Lines scrolled up into scrollback; 0 for a live bottom frame.
        public int Off;
        /// Cursor shape: 0 = block, 1 = underline, 2 = beam.
        public int CursorShape;
        /// Whether the app wants the cursor to blink.
        public bool CursorBlink = true;
        /// The app wants mouse reports (drives Phase 3 wheel/click forwarding).
        public bool AppMouse;
        /// The app wants motion reports too. Without this a drag is ours, and
        /// selecting text in a pane needs no Shift.
        public bool AppDrag;
        /// The app is on the alternate screen (no scrollback of its own).
        public bool AltScreen;
        public string[] Lines = new string[0];

        /// Parsed lazily by the terminal window and thrown away when Seq moves.
        public List<SgrRun>[] Runs;
    }

    /// <summary>
    /// Single source of truth for what the daemon knows. Owns the WebSocket,
    /// reconnects with backoff, and is pumped once per frame on the main thread.
    /// </summary>
    public class SessionHub
    {
        public static readonly SessionHub Instance = new SessionHub();

        public List<SessionInfo> Sessions = new List<SessionInfo>();
        /// The projects, pushed on connect and on any edit, so the dropdown that
        /// picks one can draw without asking first.
        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        /// The errands, pushed on connect and on any edit, for the same reason
        /// the projects are: the window that runs them draws a row per entry.
        public List<ShortcutInfo> Shortcuts = new List<ShortcutInfo>();
        /// The daemon's sandbox preset catalogue. Fetched once per process: it
        /// is compiled into slopd and only moves when slopd does.
        public List<PresetInfo> Presets = new List<PresetInfo>();
        /// Last usage snapshot. Never null: an empty one draws as "no numbers",
        /// which is what a daemon that has not answered yet honestly means.
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

        /// Latest scrollback frame answered for a wheel request, if any.
        public ScreenBuf ScrollScreen(string name) =>
            _scrolls.TryGetValue(name, out var s) ? s : null;

        // ------------------------------------------------------------ lifecycle

        public void Connect()
        {
            Disconnect();
            _ws = new MiniWebSocket();
            Status = "connecting";

            if (_ws.Connect(Settings.Host, Settings.Port, "/ws", Settings.Token))
            {
                Status = "connected";
                _backoff = 1;
                // A reconnect must not silently drop the terminal the player has open.
                foreach (var name in _subs.ToList())
                    _ws.SendText($"{{\"t\":\"sub\",\"name\":{JVal.Q(name)}}}");
            }
            else
            {
                ScheduleRetry(_ws.LastError);
            }
        }

        /// <summary>Drops the dead socket and arms an exponential-backoff reconnect.</summary>
        void ScheduleRetry(string error)
        {
            Status = $"offline: {error}";
            _ws?.Dispose();
            _ws = null;
            _nextRetry = UnityEngine.Time.realtimeSinceStartup + _backoff;
            // Capped low: the usual reason the socket dies is `make install-daemon`
            // restarting slopd, which is over in about two seconds.
            _backoff = Math.Min(_backoff * 2, 5);
        }

        public void Disconnect()
        {
            _ws?.Dispose();
            _ws = null;
            Status = "disconnected";
        }

        /// <summary>Called every frame from the Root.Update patch.</summary>
        public void Update()
        {
            SlopClient.PumpCompletions();

            if (!Settings.AutoConnect) return;

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

        void Handle(JVal ev)
        {
            switch (ev["t"].AsString())
            {
                case "sessions":
                    Sessions = ev["sessions"].Items.Select(SessionInfo.FromJson).ToList();
                    break;

                case "projects":
                    Projects = ev["projects"].Items.Select(ProjectInfo.FromJson).ToList();
                    break;

                case "shortcuts":
                    Shortcuts = ev["shortcuts"].Items.Select(ShortcutInfo.FromJson).ToList();
                    break;

                case "usage":
                    Usage = UsageInfo.FromJson(ev["usage"]);
                    break;

                // The daemon is about to start the game again and wants the
                // colony written first, which only the game can do. Safe here:
                // Update() is the Root.Update patch, so this is the main thread
                // and Shutdown is being called from the same place the menu
                // would call it. The daemon waits for the process to go before
                // it launches, so taking a moment over the save is fine.
                case "quit":
                    Log.Message("[SlopWorld] slopd asked for a restart; saving and quitting");
                    AutoSaver.SaveNow();
                    Root.Shutdown();
                    break;

                case "screen":
                    var s = ev["screen"];
                    string name = s["name"].AsString();
                    int off = s["off"].AsInt(0);
                    // Scrolled frames answer one wheel request; keep them apart so
                    // they never clobber the live view the terminal falls back to.
                    var store = off > 0 ? _scrolls : _screens;
                    if (!store.TryGetValue(name, out var buf))
                        store[name] = buf = new ScreenBuf();

                    buf.Seq = s["seq"].AsInt();
                    buf.Cols = s["cols"].AsInt(80);
                    buf.Rows = s["rows"].AsInt(24);
                    buf.Cx = s["cx"].AsInt();
                    buf.Cy = s["cy"].AsInt();
                    buf.Off = off;
                    buf.CursorShape = s["cursor_shape"].AsInt(0);
                    buf.CursorBlink = s["cursor_blink"].AsBool(true);
                    buf.AppMouse = s["app_mouse"].AsBool(false);
                    buf.AppDrag = s["app_drag"].AsBool(false);
                    buf.AltScreen = s["alt_screen"].AsBool(false);
                    buf.Lines = s["lines"].Items.Select(l => l.AsString()).ToArray();
                    buf.Runs = null; // force a re-parse on next draw
                    break;
            }
        }

        // ------------------------------------------------------------- commands

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
            if (_ws == null || !_ws.Connected) return;
            var arr = string.Join(",", keys.Select(JVal.Q).ToArray());
            _ws.SendText($"{{\"t\":\"keys\",\"name\":{JVal.Q(name)},\"keys\":[{arr}]," +
                         $"\"literal\":{JVal.B(literal)}}}");
        }

        /// <summary>Asks for a one-off capture scrolled `off` lines into scrollback.</summary>
        public void RequestScroll(string name, int off)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"scroll\",\"name\":{JVal.Q(name)},\"off\":{off}}}");
        }

        /// <summary>
        /// Forwards a mouse event in cell coordinates; the daemon encodes it to the
        /// app's current mouse protocol. `action` is press/release/drag/wheelup/
        /// wheeldown, `button` is 0/1/2 = left/middle/right (ignored for the wheel).
        /// </summary>
        public void SendMouse(string name, string action, int button, int col, int row)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"mouse\",\"name\":{JVal.Q(name)},\"action\":{JVal.Q(action)}," +
                         $"\"button\":{button},\"col\":{col},\"row\":{row}}}");
        }

        /// <summary>
        /// Pastes text; the daemon wraps it in bracketed-paste markers when the app
        /// has that mode on, so multi-line pastes don't auto-run or auto-indent.
        /// </summary>
        public void Paste(string name, string text)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"paste\",\"name\":{JVal.Q(name)},\"text\":{JVal.Q(text)}}}");
        }

        public void Resize(string name, int cols, int rows)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"resize\",\"name\":{JVal.Q(name)}," +
                         $"\"cols\":{cols},\"rows\":{rows}}}");
        }

        /// <summary>
        /// Session mutations go over HTTP, not the socket: they rewrite config.toml
        /// on the daemon side and we want the error body back.
        /// </summary>
        public void Refresh() =>
            SlopClient.Get("/api/sessions",
                j => Sessions = j["sessions"].Items.Select(SessionInfo.FromJson).ToList());

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        /// <summary>
        /// Asked for when a window that needs projects opens. The socket pushes
        /// them too, but a window opened while the socket is down still has to
        /// draw something, and this is the road that returns an error body.
        /// </summary>
        public void RefreshProjects(Action<string> fail = null) =>
            SlopClient.Get("/api/projects",
                j => Projects = j["projects"].Items.Select(ProjectInfo.FromJson).ToList(),
                fail);

        public ShortcutInfo Shortcut(string name) =>
            Shortcuts.FirstOrDefault(s => s.Name == name);

        /// <summary>
        /// Asked for when the shortcuts window opens, for the same reason
        /// projects are: a window opened while the socket is down still has to
        /// draw something, and this is the road that returns an error body.
        /// </summary>
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

        /// <summary>
        /// Runs an errand. The daemon answers with the name of the temporary
        /// agent doing it, and the sessions list is fetched again before that
        /// name is handed on: a terminal opened on a session this end has never
        /// heard of closes itself on the next frame.
        /// </summary>
        public void RunShortcut(string name, Action<string> started, Action<string> fail = null) =>
            SlopClient.Post($"/api/shortcuts/{Esc(name)}/run", null,
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

        /// <summary>
        /// A shortcut's name is free-form - it labels a button - so it can carry
        /// spaces and anything else a path segment would object to.
        /// </summary>
        static string Esc(string name) => Uri.EscapeDataString(name ?? "");

        /// <summary>Once per process: the catalogue is compiled into slopd.</summary>
        public void LoadPresets()
        {
            if (Presets.Count > 0) return;
            SlopClient.Get("/api/presets",
                j => Presets = j["presets"].Items.Select(PresetInfo.FromJson).ToList());
        }

        public void SaveProject(ProjectInfo p, bool isNew, string origName,
                                Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { RefreshProjects(); Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/projects", p.ToJson(), done, fail);
            else SlopClient.Put($"/api/projects/{origName}", p.ToJson(), done, fail);
        }

        /// <summary>
        /// The daemon refuses this while agents still work there, and says which
        /// ones - so the message the player sees is the useful half of it.
        /// </summary>
        public void RemoveProject(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/projects/{name}",
                _ => { RefreshProjects(); Refresh(); }, fail);

        public void Start(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/start", null, _ => Refresh(), fail);

        public void Stop(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/stop", null, _ => Refresh(), fail);

        public void Restart(string name, Action<string> fail = null) =>
            SlopClient.Post($"/api/sessions/{name}/restart", null, _ => Refresh(), fail);

        public void Remove(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/sessions/{name}", _ => Refresh(), fail);

        /// <summary>
        /// Writes a session back. `origName` addresses the edit, because the name
        /// in `s` may be a new one the daemon has not heard of yet - that is how a
        /// rename is spelled.
        /// </summary>
        public void Save(SessionInfo s, bool isNew, string origName, Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/sessions", s.ToJson(), done, fail);
            else SlopClient.Put($"/api/sessions/{origName}", s.ToJson(), done, fail);
        }
    }
}
