using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace SlopWorld
{
    // Down is "the process is not running": the colonist is put on the floor rather than
    // killed, the same process being able to get it back up.
    public enum AgentState { Down, Working, Waiting, Idle }

    public enum ShortcutKind { Prompt, Shell, Breadcrumb }

    // Temp is a fresh scratch directory per run; Ask is decided at the button.
    public enum ShortcutLink { Project, Temp, Ask }

    public class SessionInfo
    {
        public string Name = "";
        public string Project = "";
        // Repeated on the wire so a session list reads without a join. Blank when the entry
        // names a project that has gone.
        public string Dir = "";
        // A command preset's explicit name. Blank means this agent states a command line of
        // its own, or falls through to the daemon default when Cmd is also blank.
        public string Command = "";
        // The command preset after the daemon resolves [defaults] agent. Unlike Command,
        // this is also populated when the session leaves its command blank for that default.
        public string CommandPreset = "";
        // This agent's own answer to what that preset runs. Blank is the preset's.
        public string Cmd = "";
        // Sandbox presets it adds to its command's and its project's.
        public List<string> Sandbox = new List<string>();
        // As the daemon will exec it, preset and defaults resolved. Read-only here.
        public string Agent = "";
        public AgentState State = AgentState.Down;
        public bool Alive;
        // The project's, and read-only here: edit the project to change it.
        public bool Net = true;
        public bool Autostart;
        public List<string> Breadcrumbs = new List<string>();

        // Read-only here: this process has breadcrumbs waiting for its first Enter. What the
        // terminal reads to know whether a keystroke is worth carrying tips for.
        public bool BreadcrumbsPending;

        // A shortcut's errand or a tmux session started by hand: it leaves the colony when its
        // process exits, and there is no entry to edit or delete.
        public bool Ephemeral;

        // Read-only here: the terminal window measures itself and sends the resize.
        public int Cols;
        public int Rows;

        // What the app calls itself over OSC 0/2, for every agent and not just the one whose
        // pane is open - the daemon watches every emulator and the mod only sees the pane it
        // subscribed to. Blank for anything that never says.
        public string Title = "";

        // The app rang the bell and nobody has looked since. Cleared by the daemon the moment
        // a pane is subscribed to, so opening the terminal is what answers it.
        public bool Bell;

        // Unix millis of the last pane change. Zero before it has ever drawn anything.
        public long LastChange;

        // Unix millis of the last state move, and a different clock from the one above: a
        // working agent redraws several times a second, so its LastChange is always now and
        // an age off it reads "0s" forever. This is what "working 3m" is measured from.
        public long StateSince;

        public bool Gone => !Alive;

        // The daemon's clock is this machine's, so the two agree without anything being sent
        // to keep them in step: an age is the difference and not a countdown the daemon owns.
        static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static long NowMs => (long)(DateTime.UtcNow - Epoch).TotalMilliseconds;

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
            Command = j["command"].AsString(),
            CommandPreset = j["command_preset"].AsString(),
            Cmd = j["cmd"].IsNull ? "" : j["cmd"].AsString(),
            Sandbox = j["sandbox"].Items.Select(i => i.AsString()).ToList(),
            Agent = j["agent"].AsString(),
            State = ParseState(j["state"].AsString()),
            Alive = j["alive"].AsBool(),
            Net = j["net"].AsBool(true),
            Autostart = j["autostart"].AsBool(false),
            Breadcrumbs = j["breadcrumbs"].Items.Select(i => i.AsString()).ToList(),
            BreadcrumbsPending = j["breadcrumbs_pending"].AsBool(false),
            Ephemeral = j["ephemeral"].AsBool(false),
            Cols = j["cols"].AsInt(0),
            Rows = j["rows"].AsInt(0),
            Title = j["title"].AsString(),
            Bell = j["bell"].AsBool(false),
            LastChange = j["last_change"].AsLong(0),
            StateSince = j["state_since"].AsLong(0),
        };

        // `Agent` never rides along: it is what the daemon resolved, and writing it back
        // would pin today's answer into the file forever. An empty override is sent as null
        // rather than as a blank, which is the difference between "the preset's" and "none".
        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"project\":{JVal.Q(Project)}," +
            $"\"command\":{JVal.Q(Command)}," +
            $"\"cmd\":{(string.IsNullOrEmpty((Cmd ?? "").Trim()) ? "null" : JVal.Q(Cmd))}," +
            $"\"sandbox\":[{string.Join(",", Sandbox.Select(JVal.Q).ToArray())}]," +
            $"\"breadcrumbs\":[{string.Join(",", Breadcrumbs.Select(JVal.Q).ToArray())}]," +
            $"\"autostart\":{JVal.B(Autostart)}}}";
    }

    public class ProjectInfo
    {
        public string Name = "";
        public string Dir = "";
        // The daemon coins TempRoot/name and makes it when the first agent starts there. It
        // is /tmp that is temporary, not the entry.
        public bool Temp;
        // Sandbox presets by name. Every agent runs in a sandbox; this says what it reaches.
        public List<string> Sandbox = new List<string>();
        public bool Net = true;
        public List<string> Breadcrumbs = new List<string>();

        // The daemon coins the path and is the only thing that writes it; this is so the dialog
        // can show what a name is about to become before anything is saved.
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
            Sandbox = Strings(j["sandbox"]),
            Breadcrumbs = Strings(j["breadcrumbs"]),
            Net = j["net"].AsBool(true),
        };

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)},\"dir\":{JVal.Q(Dir)},\"temp\":{JVal.B(Temp)}," +
            $"\"sandbox\":{Arr(Sandbox)}," +
            $"\"breadcrumbs\":{Arr(Breadcrumbs)},\"net\":{JVal.B(Net)}}}";

        public ProjectInfo Copy() => new ProjectInfo
        {
            Name = Name,
            Dir = Dir,
            Temp = Temp,
            Sandbox = new List<string>(Sandbox),
            Breadcrumbs = new List<string>(Breadcrumbs),
            Net = Net,
        };

        static List<string> Strings(JVal a) => a.Items.Select(i => i.AsString()).ToList();

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";
    }

    // The agent it lands is temporary and never written to config.toml, so what is saved is
    // the errand. Spelled out rather than pointing at an existing agent, which would stop
    // working the day that agent was deleted.
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

        // Shipped with the daemon rather than written in config.toml: it cannot be edited or
        // deleted, and the shortcuts table leaves it out. The breadcrumb lists still offer it,
        // which is the only place a shipped entry is meant to be seen.
        public bool Builtin;

        public static ShortcutInfo FromJson(JVal j) => new ShortcutInfo
        {
            Name = j["name"].AsString(),
            Kind = j["kind"].AsString() == "shell" ? ShortcutKind.Shell :
                   j["kind"].AsString() == "breadcrumb" ? ShortcutKind.Breadcrumb : ShortcutKind.Prompt,
            Link = ParseLink(j["link"].AsString()),
            Project = j["project"].AsString(),
            Text = j["text"].AsString(),
            Command = j["command"].IsNull ? "" : j["command"].AsString(),
            Builtin = j["builtin"].AsBool(false),
        };

        // An unknown link reads as Project, the way an unknown state reads as Down: a version
        // skew has to stay survivable.
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

        static string KindName(ShortcutKind k) =>
            k == ShortcutKind.Shell ? "shell" :
            k == ShortcutKind.Breadcrumb ? "breadcrumb" : "prompt";

        public string ToJson() =>
            "{" +
            $"\"name\":{JVal.Q(Name)}," +
            $"\"kind\":{JVal.Q(KindName(Kind))}," +
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
            Builtin = Builtin,
        };
    }

    // Fetched rather than listed here: a preset the daemon does not have is a
    // checkbox that saves and then does nothing.
    public class PresetInfo
    {
        public string Name = "";
        // How the checkbox list groups itself. A category this build has never heard of is
        // a heading, not a problem: the table is a directory of files now.
        public string Category = "";
        public string Description = "";
        // Automatically included before this preset.  The daemon resolves the authoritative
        // closure; the client keeps it to show why a checkbox is unavailable.
        public List<string> Requires = new List<string>();
        // `system` is compiled into slopd, `user` exists only in the preset directory, and
        // `override` is a user definition replacing a system entry with the same name.
        public string Source = "";
        // Kept apart because the project dialog groups what a sandbox is handed the way it is
        // edited. A device node goes with the read-only binds: it is bound rather than passed,
        // and which flag bwrap gets is not this screen's business.
        public List<string> Ro = new List<string>();
        public List<string> Rw = new List<string>();
        public List<string> Dev = new List<string>();
        public List<string> Env = new List<string>();
        public List<string> Seed = new List<string>();
        public List<string> Skip = new List<string>();
        public Dictionary<string, string> Setenv = new Dictionary<string, string>();
        // Bound to a copy of its own rather than to the host's. Its own group because "where
        // did my ~/.claude go" is the question a session's own copy raises, and a path that
        // read as an ordinary bind would answer it wrongly.
        public List<string> Private = new List<string>();
        // The host's own file, read-write, inside one of those copies: a credential that
        // rotates cannot be a copy. Grouped with Rw rather than Private because that is what
        // it is - the one path here something inside can write and the host will read.
        public List<string> Shared = new List<string>();

        // What ticking this costs, when it costs anything: a socket whose far end runs on the
        // host, a display every other window shares. Empty for an ordinary preset.
        public string Escapes = "";
        public bool IsEscape => !string.IsNullOrEmpty(Escapes);

        // Every path and env var the preset asks for, for the tooltip.
        public List<string> Gives =>
            Ro.Concat(Rw).Concat(Dev).Concat(Shared).Concat(Private).Concat(Env)
                .Concat(Setenv.Select(x => $"{x.Key}={x.Value}")).ToList();

        public PresetInfo Copy() => new PresetInfo
        {
            Name = Name,
            Category = Category,
            Description = Description,
            Requires = new List<string>(Requires),
            Source = Source,
            Ro = new List<string>(Ro),
            Rw = new List<string>(Rw),
            Dev = new List<string>(Dev),
            Env = new List<string>(Env),
            Private = new List<string>(Private),
            Shared = new List<string>(Shared),
            Seed = new List<string>(Seed),
            Skip = new List<string>(Skip),
            Escapes = Escapes,
            Setenv = new Dictionary<string, string>(Setenv),
        };

        public string ToJson() =>
            "{" + $"\"name\":{JVal.Q(Name)},\"category\":{JVal.Q(Category)}," +
            $"\"description\":{JVal.Q(Description)},\"ro\":{Arr(Ro)}," +
            $"\"requires\":{Arr(Requires)}," +
            $"\"rw\":{Arr(Rw)},\"dev\":{Arr(Dev)},\"private\":{Arr(Private)}," +
            $"\"seed\":{Arr(Seed)},\"skip\":{Arr(Skip)},\"shared\":{Arr(Shared)}," +
            $"\"escapes\":{JVal.Q(Escapes)},\"env\":{Arr(Env)}," +
            $"\"setenv\":{Map(Setenv)}}}";

        static string Arr(List<string> items) =>
            "[" + string.Join(",", items.Select(JVal.Q).ToArray()) + "]";

        static string Map(Dictionary<string, string> items) =>
            "{" + string.Join(",", items.Select(x =>
                JVal.Q(x.Key) + ":" + JVal.Q(x.Value)).ToArray()) + "}";

        public static PresetInfo FromJson(JVal j)
        {
            var p = new PresetInfo
            {
                Name = j["name"].AsString(),
                Category = j["category"].AsString(),
                Description = j["description"].AsString(),
                Source = j["source"].AsString("system"),
                Escapes = j["escapes"].AsString(),
            };
            p.Ro.AddRange(j["ro"].Items.Select(i => i.AsString()));
            p.Requires.AddRange(j["requires"].Items.Select(i => i.AsString()));
            p.Rw.AddRange(j["rw"].Items.Select(i => i.AsString()));
            p.Dev.AddRange(j["dev"].Items.Select(i => i.AsString()));
            p.Private.AddRange(j["private"].Items.Select(i => i.AsString()));
            p.Seed.AddRange(j["seed"].Items.Select(i => i.AsString()));
            p.Skip.AddRange(j["skip"].Items.Select(i => i.AsString()));
            p.Shared.AddRange(j["shared"].Items.Select(i => i.AsString()));
            p.Env.AddRange(j["env"].Items.Select(i => i.AsString()));
            if (j["setenv"].Obj != null)
                foreach (var pair in j["setenv"].Obj)
                    p.Setenv[pair.Key] = pair.Value.AsString();
            return p;
        }
    }

    // What an agent runs, and the sandbox presets that come with it. Fetched for the same
    // reason presets are: a command file added while the game was up is one the dropdown
    // has to be able to show.
    public class CommandInfo
    {
        public string Name = "";
        public string Category = "";
        public string Description = "";
        public string Source = "";
        // What it runs before this machine's `[defaults]` and the agent's own override.
        public string Cmd = "";
        public List<string> Sandbox = new List<string>();

        public string ToJson() =>
            "{" + $"\"name\":{JVal.Q(Name)},\"category\":{JVal.Q(Category)}," +
            $"\"description\":{JVal.Q(Description)},\"cmd\":{JVal.Q(Cmd)}," +
            $"\"sandbox\":[{string.Join(",", Sandbox.Select(JVal.Q).ToArray())}]}}";

        public CommandInfo Copy() => new CommandInfo
        {
            Name = Name,
            Category = Category,
            Description = Description,
            Source = Source,
            Cmd = Cmd,
            Sandbox = new List<string>(Sandbox),
        };

        public static CommandInfo FromJson(JVal j) => new CommandInfo
        {
            Name = j["name"].AsString(),
            Category = j["category"].AsString(),
            Description = j["description"].AsString(),
            Source = j["source"].AsString("system"),
            Cmd = j["cmd"].AsString(),
            Sandbox = j["sandbox"].Items.Select(i => i.AsString()).ToList(),
        };
    }

    // A rate-limit window, or the extra-usage budget in money. The reset is a duration rather
    // than an instant, so the countdown stays honest when the socket dies.
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

        // The sellers the daemon is polling, answering or not. What the readout draws a row
        // for, so a source that is down keeps its place on the line.
        public List<string> Sources = new List<string>();

        // realtimeSinceStartup when these *numbers* were current - what ages them and what the
        // countdown runs from. A failed poll carries the last good windows, so it carries this
        // with them: taking the arrival time would make a snapshot half an hour old read as
        // fresh, and would hand every reset countdown back its full span once a minute.
        public float Heard;

        public bool Any => Windows.Count > 0;

        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        // Floored at zero: a spent window reads as due rather than as a negative number.
        public long Remaining(UsageWindow w) =>
            w.ResetsIn < 0 ? -1 : Math.Max(0L, w.ResetsIn - (long)Age);

        // `prev` is what is on screen now: a failed poll answers with the last good windows,
        // and they are no fresher for having been sent again.
        public static UsageInfo FromJson(JVal j, UsageInfo prev = null)
        {
            bool ok = j["ok"].AsBool();
            return Read(j, ok || prev == null
                ? UnityEngine.Time.realtimeSinceStartup
                : prev.Heard);
        }

        static UsageInfo Read(JVal j, float heard) => new UsageInfo
        {
            Ok = j["ok"].AsBool(),
            Error = j["error"].IsNull ? null : j["error"].AsString(),
            Plan = j["plan"].AsString(),
            Sources = j["sources"].Items.Select(s => s.AsString()).ToList(),
            Heard = heard,
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
        // Echoed from the scroll request this frame answers; 0 for a live frame. The terminal
        // accepts only the response to its latest request, so a stale reply cannot clamp it.
        public ulong ScrollRequestId;
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
        // Pushed on connect and on any edit, so the dropdown that picks one draws without
        // asking first.
        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<ShortcutInfo> Shortcuts = new List<ShortcutInfo>();
        // Fetched rather than listed here, and fetched again on every dialog that draws
        // them: both tables are TOML files under the daemon's config directory, so a preset
        // can arrive without slopd being rebuilt or restarted.
        public List<PresetInfo> Presets = new List<PresetInfo>();
        public List<CommandInfo> Commands = new List<CommandInfo>();
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
                    Usage = UsageInfo.FromJson(ev["usage"], Usage);
                    break;

                case "audio":
                    Radio.Report(ev["audio"]["playing"].AsBool(false),
                        ev["audio"]["error"].AsString(null),
                        ev["audio"]["title"].AsString(null));
                    break;

                // Update() is the Root.Update patch, so this is the main thread and Shutdown is
                // called from where the menu would call it.
                case "quit":
                    Log.Message("[SlopWorld] slopd asked for a restart; saving and quitting");
                    AutoSaver.SaveNow();
                    Root.Shutdown();
                    break;

                case "screen":
                    var s = ev["screen"];
                    string name = s["name"].AsString();
                    int off = s["off"].AsInt(0);
                    // Scrolled frames answer one wheel request; kept apart from the live view.
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
                    buf.ScrollRequestId = (ulong)s["request_id"].AsLong(0);
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

        // The jukebox. `source` is a station's URL or the absolute path of a file, null
        // stops it, and leaving the key out altogether is the volume moving on its own -
        // which must not restart what is playing. See Sim/Radio.cs.
        public void SendAudio(string source, float volume)
        {
            if (_ws == null || !_ws.Connected) return;
            // JVal.Q writes an empty string for a null, and the daemon reads the JSON null
            // as "stop" and an absent key as "volume only" - three cases, so the literal
            // is written here rather than quoted.
            string src = source == null ? "null" : JVal.Q(source);
            _ws.SendText($"{{\"t\":\"audio\",\"source\":{src},\"volume\":{Num(volume)}}}");
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

        // The same temporary agent an errand makes, for an errand nobody wrote down: the files
        // view opens `less` on whatever the cursor is over, which is not a `[[shortcut]]` and
        // should never become one. `text` empty is a command with nothing to type after it,
        // which the daemon allows here and refuses of an entry.
        //
        // The sessions list is refetched before the answer is handed on, for the reason
        // RunShortcut does it: a terminal opened on a session this end has never heard of
        // closes itself next frame.
        public void Run(string project, string command, string label,
                        Action<string> started, Action<string> fail = null,
                        bool shell = true, string text = "", bool host = false) =>
            SlopClient.Post("/api/run",
                "{" + $"\"project\":{JVal.Q(project ?? "")}," +
                $"\"kind\":{JVal.Q(shell ? "shell" : "prompt")}," +
                $"\"command\":{JVal.Q(command ?? "")}," +
                $"\"label\":{JVal.Q(label ?? "")}," +
                $"\"text\":{JVal.Q(text ?? "")}," +
                $"\"host\":{JVal.B(host)}" + "}",
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

        // The project's own directory, in a shell, outside the sandbox every other agent runs
        // in - the sidebar's "Terminal (host)" in both views. Both the command and the label
        // are left empty on purpose: the shell is `$SHELL`, the login shell of whoever the
        // daemon runs as, and the entry is named for the project and that shell together -
        // `slopworld-zsh` here, `tmp-bash` in a project called `tmp`. Which shell this machine
        // has is the daemon's answer rather than the game's, so the game asks for neither and
        // reads the name it was given back off the reply.
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
