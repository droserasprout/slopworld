using System;
using System.Collections.Generic;

namespace SlopWorld
{
    // Owns connection lifecycle and operations spanning multiple stores. Callers use the
    // owning service directly for independent mutations and terminal/audio commands.
    public partial class SessionHub
    {
        public static readonly SessionHub Instance = new SessionHub();

        readonly HubTransport _transport = new HubTransport();
        readonly SessionStore _sessions = new SessionStore();
        readonly TaskStore _tasks = new TaskStore();
        readonly HubCatalog _catalog;
        readonly TerminalIO _terminal;
        readonly AudioBus _audio;
        internal SessionStore SessionStore => _sessions;
        internal TaskStore TaskStore => _tasks;
        internal HubCatalog Catalog => _catalog;
        internal TerminalIO Terminal => _terminal;
        internal AudioBus Audio => _audio;
        int _connectionGeneration;

        // Defaults for transient host applications, refreshed on connect and after any settings
        // page saves. Settable because those pages write it back optimistically before the
        // daemon answers; the initial object keeps file actions usable before the first response.
        public DaemonConfig Config = new DaemonConfig();
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
                _connectionGeneration++;
                _sessions.ResetConnectionScreens();
                DaemonClipboard.Reset();
                RefreshConfig();
                RefreshHealth();
                _catalog.RefreshTemplates();
                _tasks.Refresh();
                _terminal.Resubscribe();
            };
            _transport.OnMessage = Handle;
        }

        // ---- state, read straight off the owning service -------------------------------

        public List<SessionInfo> Sessions => _sessions.Sessions;
        public List<TaskInfo> Tasks => _tasks.Tasks;
        public int OpenTasks => _tasks.OpenTasks;
        public List<ProjectInfo> Projects => _catalog.Projects;
        public List<LibraryItemInfo> Library => _catalog.Library;
        public List<PresetInfo> Presets => _catalog.Presets;
        public List<CommandInfo> Commands => _catalog.Commands;
        public List<AgentTemplateInfo> Templates => _catalog.Templates;
        public string Status => _transport.Status;
        public bool Online => _transport.Connected;
        public int ConnectionGeneration => _connectionGeneration;
        public long SessionsVersion => _sessions.Version;
        public int ProjectsRevision => _catalog.ProjectsRevision;

        public SessionInfo Get(string name) => _sessions.Get(name);
        public bool TryPendingRename(string oldName, out string newName) =>
            _sessions.TryPendingRename(oldName, out newName);
        public bool TryPendingRenameSource(string newName, out string oldName) =>
            _sessions.TryPendingRenameSource(newName, out oldName);
        internal string PendingRenameDestination(string oldName) =>
            _sessions.TryPendingRename(oldName, out var newName) ? newName : null;
        internal string PendingRenameSource(string newName) =>
            _sessions.TryPendingRenameSource(newName, out var oldName) ? oldName : null;
        public ScreenBuf Screen(string name) => _sessions.Screen(name);
        public bool TryScrollScreen(string name, out ScreenBuf screen) =>
            _sessions.TryScrollScreen(name, out screen);
        public ProjectInfo Project(string name) => _catalog.Project(name);
        public LibraryItemInfo LibraryItem(string name) => _catalog.LibraryItem(name);
        public CommandInfo Command(string name) => _catalog.Command(name);

        // ---- connection ----------------------------------------------------------------

        public void Connect() => _transport.Connect();
        public void Disconnect() => _transport.Disconnect();

        // Called every frame from the Root.Update patch.
        public void Update()
        {
            DaemonClient.PumpCompletions();
            _transport.Update();
            _tasks.Update(AgentSidebar.TasksVisible);
            PerfTrace.Report();
        }

        // ---- terminal I/O --------------------------------------------------------------

        public void Subscribe(string name)
        {
            _sessions.BeginSubscription(name);
            _terminal.Subscribe(name);
        }

        public void Unsubscribe(string name)
        {
            _sessions.EndSubscription(name);
            _terminal.Unsubscribe(name);
        }

        // ---- config --------------------------------------------------------------------

        // Mutations go over HTTP rather than the socket: they rewrite config.toml, and the
        // error body matters.
        public void RefreshConfig(Action<string> fail = null) =>
            DaemonClient.Get(WireProtocol.Routes.Config,
                j => Config = DaemonConfig.FromJson(j["values"], j["metadata"]), fail);

        public void RefreshHealth(Action<string> fail = null) =>
            DaemonClient.Get(WireProtocol.Routes.Health, j => Health = DaemonHealth.FromJson(j), fail);

        // ---- sessions ------------------------------------------------------------------

        public void Save(SessionInfo s, bool isNew, string origName, Action ok,
                         Action<string> fail) =>
            _sessions.Save(s, isNew, origName, () =>
            {
                if (!isNew && origName != s.Name)
                    _terminal.Rename(origName, s.Name);
                ok?.Invoke();
            }, fail);

        public void CreateFromTemplate(string template, SessionInfo s, Action ok,
                                       Action<string> fail)
        {
            string body = "{" +
                $"\"name\":{JVal.Q(s.Name)},\"project\":{JVal.Q(s.Project)}," +
                $"\"overrides\":{s.ToJson()}}}";
            DaemonClient.Post($"{WireProtocol.Routes.Templates}/{HubWire.Esc(template)}/create",
                body,
                _ =>
                {
                    _sessions.Refresh();
                    ok?.Invoke();
                }, fail);
        }

        public void SaveAgentTemplate(string source, string name, string description,
                                      Action ok, Action<string> fail, bool includeInherited = false) =>
            _catalog.SaveAgentTemplate(source, name, description, ok, fail, includeInherited);

    }
}
