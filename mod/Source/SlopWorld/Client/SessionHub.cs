using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace SlopWorld
{
    public enum AgentState { Dead, Working, Waiting, Idle }

    public class SessionInfo
    {
        public string Name = "";
        public string Dir = "";
        public string Agent = "";
        public AgentState State = AgentState.Dead;
        public bool Alive;
        public bool Net = true;
        public bool Sandbox = true;
        public bool Autostart;
        public int Cols = 120;
        public int Rows = 34;

        public static AgentState ParseState(string s)
        {
            switch (s)
            {
                case "working": return AgentState.Working;
                case "waiting": return AgentState.Waiting;
                case "idle": return AgentState.Idle;
                default: return AgentState.Dead;
            }
        }

        public static SessionInfo FromJson(JVal j) => new SessionInfo
        {
            Name = j["name"].AsString(),
            Dir = j["dir"].AsString(),
            Agent = j["agent"].AsString(),
            State = ParseState(j["state"].AsString()),
            Alive = j["alive"].AsBool(),
            Net = j["net"].AsBool(true),
            Sandbox = j["sandbox"].AsBool(true),
            Autostart = j["autostart"].AsBool(false),
            Cols = j["cols"].AsInt(120),
            Rows = j["rows"].AsInt(34),
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
            _backoff = Math.Min(_backoff * 2, 30);
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
            string body =
                $"{{\"name\":{JVal.Q(s.Name)},\"dir\":{JVal.Q(s.Dir)}," +
                $"\"agent\":{(string.IsNullOrEmpty(s.Agent) ? "null" : JVal.Q(s.Agent))}," +
                $"\"net\":{JVal.B(s.Net)},\"sandbox\":{JVal.B(s.Sandbox)}," +
                $"\"autostart\":{JVal.B(s.Autostart)},\"cols\":{s.Cols},\"rows\":{s.Rows}}}";

            Action<JVal> done = _ => { Refresh(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/sessions", body, done, fail);
            else SlopClient.Put($"/api/sessions/{origName}", body, done, fail);
        }
    }
}
