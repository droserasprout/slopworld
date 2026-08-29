using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // The single source of truth for the daemon connection, now a thin coordinator over five
    // focused services: HubTransport owns the socket, TerminalIO and AudioBus push at it,
    // SessionStore holds the sessions and screens, and HubCatalog holds the projects, shortcuts
    // and presets. The public surface here is unchanged so the ~40 call sites need not move —
    // every member delegates to the service that owns it, and Handle routes socket events to
    // the same stores.
    public partial class SessionHub
    {
        public static readonly SessionHub Instance = new SessionHub();

        readonly HubTransport _transport = new HubTransport();
        readonly SessionStore _sessions = new SessionStore();
        readonly HubCatalog _catalog;
        readonly TerminalIO _terminal;
        readonly AudioBus _audio;

        // Defaults for transient host applications, refreshed on connect and after any settings
        // page saves. Settable because those pages write it back optimistically before the
        // daemon answers; the initial object keeps file actions usable before the first response.
        public SlopConfig Config = new SlopConfig();
        public DaemonCapabilities Capabilities = new DaemonCapabilities();
        // Never null: an empty one draws as "no numbers", which is what a daemon that has not
        // answered yet means.
        public UsageInfo Usage = new UsageInfo();
        public DaemonHealth Health = new DaemonHealth();

        SessionHub()
        {
            _catalog = new HubCatalog(() => _sessions.Refresh());
            _terminal = new TerminalIO(_transport);
            _audio = new AudioBus(_transport);
            _transport.OnConnected = () =>
            {
                SlopClipboard.Reset();
                RefreshConfig();
                RefreshHealth();
                _terminal.Resubscribe();
            };
            _transport.OnMessage = Handle;
        }

        // ---- state, read straight off the owning service -------------------------------

        public List<SessionInfo> Sessions => _sessions.Sessions;
        public List<ProjectInfo> Projects => _catalog.Projects;
        public List<ShortcutInfo> Shortcuts => _catalog.Shortcuts;
        public List<PresetInfo> Presets => _catalog.Presets;
        public List<CommandInfo> Commands => _catalog.Commands;
        public string Status => _transport.Status;
        public bool Online => _transport.Connected;

        public SessionInfo Get(string name) => _sessions.Get(name);
        public bool TryPendingRename(string oldName, out string newName) =>
            _sessions.TryPendingRename(oldName, out newName);
        public ScreenBuf Screen(string name) => _sessions.Screen(name);
        public ScreenBuf ScrollScreen(string name) => _sessions.ScrollScreen(name);
        public ProjectInfo Project(string name) => _catalog.Project(name);
        public ShortcutInfo Shortcut(string name) => _catalog.Shortcut(name);
        public CommandInfo Command(string name) => _catalog.Command(name);

        // ---- connection ----------------------------------------------------------------

        public void Connect() => _transport.Connect();
        public void Disconnect() => _transport.Disconnect();

        // Called every frame from the Root.Update patch.
        public void Update()
        {
            SlopClient.PumpCompletions();
            _transport.Update();
        }

        // ---- terminal I/O --------------------------------------------------------------

        public void Subscribe(string name) => _terminal.Subscribe(name);
        public void Unsubscribe(string name) => _terminal.Unsubscribe(name);

        public void SendKeys(string name, IEnumerable<string> keys, bool literal) =>
            _terminal.SendKeys(name, keys, literal);

        public void SendKeys(string name, IEnumerable<string> keys, bool literal,
                             List<string> randomTips) =>
            _terminal.SendKeys(name, keys, literal, randomTips);

        public void RequestScroll(string name, int off, ulong requestId) =>
            _terminal.RequestScroll(name, off, requestId);

        public void SendMouse(string name, string action, int button, int col, int row,
                              int count = 1) =>
            _terminal.SendMouse(name, action, button, col, row, count);

        public void Paste(string name, string text) => _terminal.Paste(name, text);

        public void PasteBreadcrumb(string name, string breadcrumb, List<string> randomTips) =>
            _terminal.PasteBreadcrumb(name, breadcrumb, randomTips);

        public void Resize(string name, int cols, int rows) => _terminal.Resize(name, cols, rows);

        public void RefreshPanels() => _terminal.RefreshPanels();

        public void RefreshPanels(int cols, int rows) => _terminal.RefreshPanels(cols, rows);

        public void SendAudio(string station, string stream, string file, float volume) =>
            _audio.SendAudio(station, stream, file, volume);

        public void SendVolume(float volume) => _audio.SendVolume(volume);

        // ---- config --------------------------------------------------------------------

        // Mutations go over HTTP rather than the socket: they rewrite config.toml, and the
        // error body matters.
        public void RefreshConfig(Action<string> fail = null) =>
            SlopClient.Get("/api/config",
                j => Config = SlopConfig.FromJson(j["values"]), fail);

        public void RefreshHealth(Action<string> fail = null) =>
            SlopClient.Get("/api/health", j => Health = DaemonHealth.FromJson(j), fail);

        // ---- sessions ------------------------------------------------------------------

        public void Refresh() => _sessions.Refresh();

        public void CurrentPath(string name, Action<string> done, Action<string> fail = null) =>
            _sessions.CurrentPath(name, done, fail);

        public void Run(string project, string command, string label,
                        Action<string> started, Action<string> fail = null,
                        bool shell = true, string text = "", bool host = false, bool temp = false,
                        string path = "", bool hold = false, string like = "") =>
            _sessions.Run(project, command, label, started, fail, shell, text, host, temp, path, hold, like);

        public void RunHostShell(string project, Action<string> started,
                                 Action<string> fail = null) =>
            _sessions.RunHostShell(project, started, fail);

        public void RunShortcut(string name, Action<string> started, Action<string> fail = null,
                                string project = null, bool temp = false,
                                List<string> randomTips = null) =>
            _sessions.RunShortcut(name, started, fail, project, temp, randomTips);

        public void Start(string name, Action<string> fail = null) => _sessions.Start(name, fail);
        public void Stop(string name, Action<string> fail = null) => _sessions.Stop(name, fail);
        public void Restart(string name, Action<string> fail = null) => _sessions.Restart(name, fail);
        public void ResetState(string name, Action<string> fail = null) =>
            _sessions.ResetState(name, fail);

        public void SetLabel(string name, string label, Action ok = null,
                             Action<string> fail = null) =>
            _sessions.SetLabel(name, label, ok, fail);

        public void Remove(string name, Action<string> fail = null) => _sessions.Remove(name, fail);

        public void Save(SessionInfo s, bool isNew, string origName, Action ok,
                         Action<string> fail) =>
            _sessions.Save(s, isNew, origName, () =>
            {
                if (!isNew && origName != s.Name)
                    _terminal.Rename(origName, s.Name);
                ok?.Invoke();
            }, fail);

        // ---- projects, shortcuts, presets ----------------------------------------------

        public void RefreshProjects(Action<string> fail = null) => _catalog.RefreshProjects(fail);

        public void SaveProject(ProjectInfo p, bool isNew, string origName, Action ok,
                                Action<string> fail) =>
            _catalog.SaveProject(p, isNew, origName, ok, fail);

        public void RemoveProject(string name, Action<string> fail = null) =>
            _catalog.RemoveProject(name, fail);

        public void RefreshShortcuts(Action<string> fail = null) => _catalog.RefreshShortcuts(fail);

        public void SaveShortcut(ShortcutInfo s, bool isNew, string origName, Action ok,
                                 Action<string> fail) =>
            _catalog.SaveShortcut(s, isNew, origName, ok, fail);

        public void RemoveShortcut(string name, Action<string> fail = null) =>
            _catalog.RemoveShortcut(name, fail);

        public void LoadPresets(Action ok = null, Action<string> fail = null) =>
            _catalog.LoadPresets(ok, fail);

        public void CopyPreset(string kind, string name, string newName, Action ok,
                               Action<string> fail) =>
            _catalog.CopyPreset(kind, name, newName, ok, fail);

        public void SavePreset(PresetInfo p, Action ok, Action<string> fail) =>
            _catalog.SavePreset(p, ok, fail);

        public void RemovePreset(string kind, string name, Action ok, Action<string> fail) =>
            _catalog.RemovePreset(kind, name, ok, fail);

        public void SaveCommand(CommandInfo c, Action ok, Action<string> fail) =>
            _catalog.SaveCommand(c, ok, fail);
    }
}
