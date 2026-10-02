using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Catalog data includes projects, library items, sandbox presets, and command templates.
    // The daemon pushes projects and library items on connection and after edits.
    // Dialogs fetch presets and commands again so new definitions do not require rebuilding slopd.
    class HubCatalog
    {
        const string ProjectsPath = WireProtocol.Routes.Projects;
        const string LibraryPath = WireProtocol.Routes.Library;
        const string PresetsPath = WireProtocol.Routes.Presets;
        const string TemplatesPath = WireProtocol.Routes.Templates;

        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<LibraryItemInfo> Library = new List<LibraryItemInfo>();
        public List<PresetInfo> Presets = new List<PresetInfo>();
        public List<CommandInfo> Commands = new List<CommandInfo>();
        public List<AgentTemplateInfo> Templates = new List<AgentTemplateInfo>();

        // A project list request can finish after a later edit.
        // Use a generation number to prevent an old GET response from replacing successfully saved settings.
        readonly CatalogRequest _projects = new CatalogRequest();
        readonly CatalogRequest _library = new CatalogRequest();
        readonly CatalogRequest _presets = new CatalogRequest();
        readonly CatalogRequest _templates = new CatalogRequest();

        public int ProjectsRevision => _projects.Revision;
        public int TemplatesRevision => _templates.Revision;
        public string SettingsRevision => $"{_projects.Revision}/{_presets.Revision}/{_library.Revision}/{_templates.Revision}";

        // A project edit also changes which sessions exist, so the catalog asks the session
        // store to refresh without owning it.
        readonly Action _refreshSessions;

        public HubCatalog(Action refreshSessions)
        {
            _refreshSessions = refreshSessions;
        }

        // Receive pushed updates through the socket.
        // Also provide HTTP requests for windows opened while the socket is disconnected.
        public void ApplyProjects(Wire.ProjectsReply ev) => _projects.Apply(ev, SetProjects);
        public void ApplyLibrary(Wire.LibraryReply ev) => _library.Apply(ev, SetLibrary);

        void SetProjects(Wire.ProjectsReply j) =>
            Projects = j.Projects.Select(ProjectInfo.FromWire).ToList();
        void SetLibrary(Wire.LibraryReply j) =>
            Library = j.Library.Select(LibraryItemInfo.FromWire).ToList();

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // Fetch data through HTTP when the socket is disconnected.
        // HTTP responses also supply error details.
        public void RefreshProjects(Action<string> fail = null) =>
            _projects.Refresh<Wire.ProjectsReply>(ProjectsPath, SetProjects, fail);

        public LibraryItemInfo LibraryItem(string name) =>
            Library.FirstOrDefault(s => s.Name == name);

        public void RefreshLibrary(Action<string> fail = null) =>
            _library.Refresh<Wire.LibraryReply>(LibraryPath, SetLibrary, fail);

        public void SaveLibraryItem(LibraryItemInfo s, bool isNew, string origName,
                                 Action ok, Action<string> fail)
        {
            fail = _library.Invalidate(fail);
            Action<Wire.Ack> done = _ => { RefreshLibrary(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(LibraryPath, s.ToWire(), done, fail);
            else DaemonClient.Put($"{LibraryPath}/{HubWire.Esc(origName)}", s.ToWire(), done, fail);
        }

        public void RemoveLibraryItem(string name, Action<string> fail = null)
        {
            fail = _library.Invalidate(fail);
            DaemonClient.Delete($"{LibraryPath}/{HubWire.Esc(name)}",
                _ => RefreshLibrary(), fail);
        }

        // Keep cached lists until the response arrives.
        // Dialogs can then display existing data while the socket is disconnected.
        public void LoadPresets(Action ok = null, Action<string> fail = null) =>
            _presets.Refresh<Wire.PresetsReply>(PresetsPath, j =>
            {
                Presets = j.Presets.Select(PresetInfo.FromWire).ToList();
                Commands = j.Commands.Select(CommandInfo.FromWire).ToList();
            }, fail, ok);

        public void RefreshTemplates(Action<string> fail = null, Action loaded = null) =>
            _templates.Refresh<Wire.TemplatesReply>(TemplatesPath,
                j =>
                {
                    Templates = j.Templates.Select(AgentTemplateInfo.FromWire).ToList();
                },
                fail, loaded);

        public void SaveAgentTemplate(string source, string name, string description,
                                       Action ok, Action<string> fail)
        {
            fail = _templates.Invalidate(fail);
            DaemonClient.Post<Wire.TemplateResult>(TemplatesPath,
                new Wire.SaveTemplateRequest { Name = name, Description = description, Source = source },
                _ => { RefreshTemplates(); ok?.Invoke(); }, fail);
        }

        public void SaveAgentTemplateForm(AgentTemplateInfo template, SessionInfo form,
                                          bool isNew, string originalName,
                                          Action ok, Action<string> fail)
        {
            Wire.AgentTemplate body;
            try { body = template.ToWire(form); }
            catch (InvalidOperationException error) { fail?.Invoke(error.Message); return; }
            fail = _templates.Invalidate(fail);
            Action done = () => { RefreshTemplates(); ok?.Invoke(); };
            if (isNew)
                DaemonClient.Post<Wire.TemplateResult>(TemplatesPath, new Wire.SaveTemplateRequest { Name = body.Name, Description = body.Description, Version = body.Version, Defaults = body.Defaults }, _ => done(), fail);
            else
                DaemonClient.Put<Wire.TemplateResult>($"{TemplatesPath}/{HubWire.Esc(originalName)}", body, _ => done(), fail);
        }

        public void DuplicateAgentTemplate(string source, string name, string description,
                                           Action ok, Action<string> fail)
        {
            fail = _templates.Invalidate(fail);
            var body = new Wire.SaveTemplateRequest { Name = name, Description = description, Duplicate = source };
            DaemonClient.Post<Wire.TemplateResult>(TemplatesPath, body,
                _ => { RefreshTemplates(); ok?.Invoke(); }, fail);
        }

        public void RemoveAgentTemplate(AgentTemplateInfo template, string originalName,
                                        Action ok, Action<string> fail)
        {
            fail = _templates.Invalidate(fail);
            string path = $"{TemplatesPath}/{HubWire.Esc(originalName ?? template.Name)}?{WireProtocol.TemplateVersionQuery}={template.Version}";
            DaemonClient.Delete(path, _ => { RefreshTemplates(); ok?.Invoke(); }, fail);
        }

        public void CopyPreset(string kind, string name, string newName,
                               Action ok, Action<string> fail)
        {
            fail = _presets.Invalidate(fail);
            DaemonClient.Post($"{PresetsPath}/{kind}/{Uri.EscapeDataString(name)}/copy",
                new Wire.CopyPresetReq { Name = newName ?? "" }, PresetsSaved(ok, fail), fail);
        }

        public void SavePreset(PresetInfo p, Action ok, Action<string> fail)
        {
            fail = _presets.Invalidate(fail);
            DaemonClient.Put($"{PresetsPath}/sandbox_presets/{Uri.EscapeDataString(p.Name)}", new Wire.PresetRequest { Sandbox = p.ToWire() },
                PresetsSaved(ok, fail), fail);
        }

        public void RemovePreset(string kind, string name, Action ok, Action<string> fail)
        {
            fail = _presets.Invalidate(fail);
            DaemonClient.Delete($"{PresetsPath}/{kind}/{Uri.EscapeDataString(name)}",
                PresetsSaved(ok, fail), fail);
        }

        public void SaveCommand(CommandInfo c, Action ok, Action<string> fail)
        {
            fail = _presets.Invalidate(fail);
            DaemonClient.Put($"{PresetsPath}/app_presets/{Uri.EscapeDataString(c.Name)}", new Wire.PresetRequest { Command = c.ToWire() },
                PresetsSaved(ok, fail), fail);
        }

        Action<Wire.Ack> PresetsSaved(Action ok, Action<string> fail) => _ =>
        {
            // The write succeeded even if a newer catalog GET replaces this reload.
            // Complete the caller's operation regardless of which snapshot supplies the catalog.
            LoadPresets(fail: fail);
            ok?.Invoke();
        };

        public CommandInfo Command(string name) =>
            string.IsNullOrEmpty(name) ? null : Commands.FirstOrDefault(c => c.Name == name);

        public void SaveProject(ProjectInfo p, bool isNew, string origName,
                                Action ok, Action<string> fail)
        {
            // Invalidate pending list requests before starting the write.
            // After a successful write, request a new catalog snapshot.
            // Earlier responses must not replace the catalog.
            fail = _projects.Invalidate(fail);
            Action<Wire.Ack> done = _ => { RefreshProjects(); _refreshSessions(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(ProjectsPath, p.ToWire(), done, fail);
            else DaemonClient.Put($"{ProjectsPath}/{HubWire.Esc(origName)}", p.ToWire(), done, fail);
        }

        // The daemon refuses this while agents still work there, and says which ones.
        public void RemoveProject(string name, Action<string> fail = null) =>
            DeleteProject($"{ProjectsPath}/{HubWire.Esc(name)}", fail);

        void DeleteProject(string path, Action<string> fail)
        {
            fail = _projects.Invalidate(fail);
            DaemonClient.Delete(path,
                _ => { RefreshProjects(); _refreshSessions(); }, fail);
        }
    }

    sealed class CatalogRequest
    {
        sealed class Waiter
        {
            public Action Loaded;
            public Action<string> Failed;
        }
        readonly List<Waiter> _waiters = new List<Waiter>();
        public int Revision { get; private set; }

        // Keep refresh callers pending through a mutation. Its reload supplies the
        // winning snapshot; a failed mutation must also release those callers.
        public Action<string> Invalidate(Action<string> fail)
        {
            int revision = ++Revision;
            return error =>
            {
                if (revision == Revision) Settle(null, error);
                fail?.Invoke(error);
            };
        }

        public void Apply<T>(T value, Action<T> apply)
        {
            Revision++;
            Settle(() => apply(value), null);
        }

        public void Refresh<T>(string path, Action<T> apply, Action<string> fail,
                            Action loaded = null) where T : Google.Protobuf.IMessage<T>, new()
        {
            int revision = ++Revision;
            _waiters.Add(new Waiter { Loaded = loaded, Failed = fail });
            DaemonClient.Get<T>(path,
                value => { if (revision == Revision) Settle(() => apply(value), null); },
                error => { if (revision == Revision) Settle(null, error); });
        }

        void Settle(Action publish, string error)
        {
            // Detach before publishing/callbacks so reentrant refreshes belong to
            // the next outcome. Every success callback sees the winning catalog.
            var waiters = _waiters.ToArray();
            _waiters.Clear();
            try { publish?.Invoke(); }
            catch (Exception e) { error = e.Message; }
            foreach (var waiter in waiters)
            {
                try
                {
                    if (error == null) waiter.Loaded?.Invoke();
                    else waiter.Failed?.Invoke(error);
                }
                catch (Exception e) { Verse.Log.Warning("[SlopWorld] catalog callback: " + e); }
            }
        }
    }
}
