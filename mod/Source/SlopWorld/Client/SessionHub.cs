using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Down is "the process is not running": the colonist is put on the floor, not
    // killed, because the same process can get it back up.
    public enum AgentState { Down, Working, Waiting, Idle }

    // Claude is a kind rather than a command string: knowing it is Claude is what
    // lets the daemon hand it its own state dir.
    public enum AgentKind { Claude, Custom }

    // The only thing the two kinds disagree about at the far end: an agent's input
    // field, or a shell's prompt.
    public enum ShortcutKind { Prompt, Shell }

    // Temp is a fresh scratch directory per run; Ask is decided at the button.
    public enum ShortcutLink { Project, Temp, Ask }

    public class SessionInfo
    {
        public string Name = "";
        // Everything about where it runs and what it can reach is the project's answer.
        public string Project = "";
        // Repeated on the wire so a list of sessions reads without joining it against
        // anything. Blank when the entry names a project that has gone.
        public string Dir = "";
        public AgentKind Kind = AgentKind.Claude;
        // Defaults resolved. For a custom session it is also what the player typed, which
        // is why the dialog edits this field directly.
        public string Agent = "";
        public AgentState State = AgentState.Down;
        public bool Alive;
        // The project's, and read-only here: edit the project to change them.
        public bool Net = true;
        public bool Sandbox = true;
        public bool Autostart;

        // A shortcut's errand, or a tmux session somebody started by hand: it leaves the
        // colony when its process exits. There is no entry to edit or delete.
        public bool Ephemeral;

        // Read-only here: the terminal window measures itself and sends the resize.
        public int Cols;
        public int Rows;

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

        // The command only rides along for a custom session: writing back a Claude one's
        // resolved string would pin today's default into the file forever.
        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"project\":{JVal.Q(Project)}," +
            $"\"kind\":{JVal.Q(Kind == AgentKind.Custom ? "custom" : "claude")}," +
            $"\"command\":{(Kind == AgentKind.Custom && !string.IsNullOrEmpty(Agent) ? JVal.Q(Agent) : "null")}," +
            $"\"autostart\":{JVal.B(Autostart)}}}";
    }

    public class ProjectInfo
    {
        public string Name = "";
        public string Dir = "";
        // Scratch ground: the daemon coins TempRoot/name and makes it when the first
        // agent starts there. It is /tmp that is temporary, not the entry.
        public bool Temp;
        public List<string> Presets = new List<string>();
        public List<string> RoPaths = new List<string>();
        public List<string> RwPaths = new List<string>();
        public List<string> PassEnv = new List<string>();
        public bool Net = true;
        public bool Sandbox = true;

        // The daemon coins the path and is the only thing that writes it; this is so the
        // dialog can show what a name is about to become before anything is saved.
        public const string TempRoot = "/tmp/slopworld";

        // The same rule as the daemon's `slug`.
        public static string TempDir(string name)
        {
            var slug = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim())
            {
                if (char.IsWhiteSpace(c) || c == ':' || c == '.' || c == '/')
                {
                    if (slug.Length > 0 && slug[slug.Length - 1] != '-') slug.Append('-');
                }
                else slug.Append(c);
            }
            return TempRoot + "/" + slug.ToString().Trim('-');
        }

        public static ProjectInfo FromJson(JVal j) => new ProjectInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            Temp = j["temp"].AsBool(false),
            Presets = Strings(j["presets"]),
            RoPaths = Strings(j["ro_paths"]),
            RwPaths = Strings(j["rw_paths"]),
            PassEnv = Strings(j["pass_env"]),
            Net = j["net"].AsBool(true),
            Sandbox = j["sandbox"].AsBool(true),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"dir\":{JVal.Q(Dir)},\"temp\":{JVal.B(Temp)}," +
            $"\"presets\":{Arr(Presets)},\"ro_paths\":{Arr(RoPaths)}," +
            $"\"rw_paths\":{Arr(RwPaths)},\"pass_env\":{Arr(PassEnv)}," +
            $"\"net\":{JVal.B(Net)},\"sandbox\":{JVal.B(Sandbox)}}}";

        public ProjectInfo Copy() => new ProjectInfo
        {
            Name = Name,
            Dir = Dir,
            Temp = Temp,
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

    // The agent it lands is temporary - never written to config.toml - so what is
    // saved is the errand and not the agent. The template is spelled out rather than
    // pointing at an existing agent, which would stop working the day it was deleted.
    public class ShortcutInfo
    {
        public string Name = "";
        public ShortcutKind Kind = ShortcutKind.Prompt;
        public ShortcutLink Link = ShortcutLink.Project;
        // Where it runs when Link is Project, the sandbox a fresh scratch project copies
        // when it is Temp, and unread when it is Ask.
        public string Project = "";
        // The prompt, or the command line. Sent once the pane is ready for it.
        public string Text = "";
        // Blank means the daemon's own default.
        public string Command = "";

        public static ShortcutInfo FromJson(JVal j) => new ShortcutInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString() == "shell" ? ShortcutKind.Shell : ShortcutKind.Prompt,
            Link = ParseLink(j["link"].AsString()),
            Project = j["project"].AsString(),
            Text = j["text"].AsString(),
            Command = j["command"].IsNull ? "" : j["command"].AsString(),
        };

        // A link this build has never heard of reads as Project, the same way an unknown
        // state reads as Down: a version skew has to stay survivable.
        public static ShortcutLink ParseLink(string s)
        {
            switch (s)
            {
                case "temp": return ShortcutLink.Temp;
                case "ask": return ShortcutLink.Ask;
                default: return ShortcutLink.Project;
            }
        }

        public static string LinkName(ShortcutLink l) =>
            l == ShortcutLink.Temp ? "temp" : l == ShortcutLink.Ask ? "ask" : "project";

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(Kind == ShortcutKind.Shell ? "shell" : "prompt")}," +
            $"\"link\":{JVal.Q(LinkName(Link))}," +
            $"\"project\":{JVal.Q(Project)},\"text\":{JVal.Q(Text)}," +
            $"\"command\":{(string.IsNullOrEmpty((Command ?? "").Trim()) ? "null" : JVal.Q(Command))}}}";

        public ShortcutInfo Copy() => new ShortcutInfo
        {
            Name = Name,
            Kind = Kind,
            Link = Link,
            Project = Project,
            Text = Text,
            Command = Command,
        };
    }

    // Fetched rather than listed here: a preset the daemon does not have is a
    // checkbox that saves and then does nothing.
    public class PresetInfo
    {
        public string Name = "";
        public string Description = "";
        // Kept apart rather than in one bag, because the project dialog groups what a
        // sandbox is handed the same way it is edited: read-only, read-write, env. A
        // device node is grouped with the read-only binds - it is bound rather than
        // passed, and which flag bwrap gets for it is not this screen's business.
        public List<string> Ro = new List<string>();
        public List<string> Rw = new List<string>();
        public List<string> Env = new List<string>();

        // Every path and env var the preset asks for, for the tooltip.
        public List<string> Gives =>
            Ro.Concat(Rw).Concat(Env).ToList();

        public static PresetInfo FromJson(JVal j)
        {
            var p = new PresetInfo
            {
                Name = j["name"].AsString(),
                Description = j["description"].AsString(),
            };
            foreach (var key in new[] { "ro", "dev" })
                p.Ro.AddRange(j[key].Items.Select(i => i.AsString()));
            p.Rw.AddRange(j["rw"].Items.Select(i => i.AsString()));
            foreach (var key in new[] { "env", "setenv" })
                p.Env.AddRange(j[key].Items.Select(i => i.AsString()));
            return p;
        }
    }

    // A rate-limit window, or the extra-usage budget, which is the same shape in
    // money. The reset is a duration rather than an instant, so a countdown from when
    // we heard stays honest if the socket dies.
    public class UsageWindow
    {
        public string Key = "";
        public string Label = "";
        // Percent of the window spent, 0-100. Always sent, money row included.
        public float Pct;
        // A unit this build does not know reads as a percentage.
        public string Unit = "pct";
        // -1 when the daemon sent no figure, which leaves the row a percentage.
        public float Amount = -1f;
        // What Amount is out of; -1 if unsaid.
        public float Limit = -1f;
        // Seconds to the reset as of Heard; -1 if the daemon did not say.
        public long ResetsIn = -1;

        // Both halves are required: a unit with no figure under it has nothing to spend.
        public bool IsMoney => Unit == "usd" && Amount >= 0f;
    }

    // Ok false means the last poll failed, in which case the windows are the previous
    // good ones and Error says what went wrong.
    public class UsageInfo
    {
        public bool Ok;
        public string Error;
        public string Plan = "";
        public List<UsageWindow> Windows = new List<UsageWindow>();

        // realtimeSinceStartup when this arrived, which is what ages it and what the
        // countdown runs from.
        public float Heard;

        public bool Any => Windows.Count > 0;

        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        // Floored at zero: a window that has run out reads as due rather than as a
        // negative number.
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
        // Lines scrolled up into scrollback; 0 for a live bottom frame.
        public int Off;
        // 0 = block, 1 = underline, 2 = beam.
        public int CursorShape;
        public bool CursorBlink = true;
        public bool AppMouse;
        // Without this a drag is ours, and selecting text in a pane needs no Shift.
        public bool AppDrag;
        // The app is on the alternate screen (no scrollback of its own).
        public bool AltScreen;
        // What the app calls itself (OSC 0/2); empty until it says.
        public string Title = "";
        public string[] Lines = new string[0];

        // Parsed lazily by the terminal window and thrown away when Seq moves.
        public List<SgrRun>[] Runs;
        // Which palette the runs were parsed against; a scheme change re-parses them.
        public int RunsRev = -1;
    }

    // Owns the WebSocket, reconnects with backoff, and is pumped once per frame on
    // the main thread.
    public class SessionHub
    {
        public static readonly SessionHub Instance = new SessionHub();

        public List<SessionInfo> Sessions = new List<SessionInfo>();
        // Pushed on connect and on any edit, so the dropdown that picks one can draw
        // without asking first.
        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<ShortcutInfo> Shortcuts = new List<ShortcutInfo>();
        // Fetched once per process: it is compiled into slopd.
        public List<PresetInfo> Presets = new List<PresetInfo>();
        // Never null: an empty one draws as "no numbers", which is what a daemon that has
        // not answered yet honestly means.
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

        // Called every frame from the Root.Update patch.
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

                // Safe here: Update() is the Root.Update patch, so this is the main thread and
                // Shutdown is being called from where the menu would call it. The daemon waits
                // for the process to go before it launches.
                case "quit":
                    Log.Message("[SlopWorld] slopd asked for a restart; saving and quitting");
                    AutoSaver.SaveNow();
                    Root.Shutdown();
                    break;

                case "screen":
                    var s = ev["screen"];
                    string name = s["name"].AsString();
                    int off = s["off"].AsInt(0);
                    // Scrolled frames answer one wheel request; kept apart so they never clobber the
                    // live view.
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
                    buf.Title = s["title"].AsString();
                    buf.Lines = s["lines"].Items.Select(l => l.AsString()).ToArray();
                    buf.Runs = null; // force a re-parse on next draw
                    break;
            }
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
            if (_ws == null || !_ws.Connected) return;
            var arr = string.Join(",", keys.Select(JVal.Q).ToArray());
            _ws.SendText($"{{\"t\":\"keys\",\"name\":{JVal.Q(name)},\"keys\":[{arr}]," +
                         $"\"literal\":{JVal.B(literal)}}}");
        }

        public void RequestScroll(string name, int off)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"scroll\",\"name\":{JVal.Q(name)},\"off\":{off}}}");
        }

        // `action` is press/release/drag/wheelup/wheeldown, `button` is 0/1/2 =
        // left/middle/right and ignored for the wheel.
        public void SendMouse(string name, string action, int button, int col, int row)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText($"{{\"t\":\"mouse\",\"name\":{JVal.Q(name)},\"action\":{JVal.Q(action)}," +
                         $"\"button\":{button},\"col\":{col},\"row\":{row}}}");
        }

        // The daemon wraps it in bracketed-paste markers when the app has that mode on.
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

        // Session mutations go over HTTP, not the socket: they rewrite config.toml and we
        // want the error body back.
        public void Refresh() =>
            SlopClient.Get("/api/sessions",
                j => Sessions = j["sessions"].Items.Select(SessionInfo.FromJson).ToList());

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // The socket pushes these too, but a window opened while the socket is down still
        // has to draw something, and this road returns an error body.
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

        // The sessions list is fetched again before the daemon's answer is handed on: a
        // terminal opened on a session this end has never heard of closes itself on the
        // next frame. `project` and `temp` are the same message whether they answer an
        // entry that does not say where to run or override one that does.
        public void RunShortcut(string name, Action<string> started, Action<string> fail = null,
                                string project = null, bool temp = false) =>
            SlopClient.Post($"/api/shortcuts/{Esc(name)}/run",
                "{" + $"\"project\":{(string.IsNullOrEmpty(project) ? "null" : JVal.Q(project))}," +
                $"\"temp\":{JVal.B(temp)}" + "}",
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

        // A shortcut's name is free-form, so it can carry anything a path segment would
        // object to.
        static string Esc(string name) => Uri.EscapeDataString(name ?? "");

        // Once per process: the catalogue is compiled into slopd.
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

        public void Remove(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/sessions/{name}", _ => Refresh(), fail);

        // `origName` addresses the edit, because the name in `s` may be a new one the
        // daemon has not heard of - that is how a rename is spelled.
        public void Save(SessionInfo s, bool isNew, string origName, Action ok, Action<string> fail)
        {
            Action<JVal> done = _ => { Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/sessions", s.ToJson(), done, fail);
            else SlopClient.Put($"/api/sessions/{origName}", s.ToJson(), done, fail);
        }
    }
}
