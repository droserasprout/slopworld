using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // The reference data the daemon keeps in TOML: the projects an agent can run in, the
    // library items that launch them, and the sandbox presets and command templates. Projects and
    // library items are pushed on connect and on any edit; presets and commands are fetched afresh
    // for every dialog that draws them, so one can arrive without slopd being rebuilt.
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

        // A project list request can outlive the edit that started another one. Without a
        // generation, a slow pre-edit GET can put the old network default back after a Host
        // save has succeeded.
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

        // The socket pushes these, and each also has an HTTP road below for a window opened
        // while the socket is down.
        public void ApplyProjects(Wire.ProjectsReply ev) => _projects.Apply(ev, SetProjects);
        public void ApplyLibrary(Wire.LibraryReply ev) => _library.Apply(ev, SetLibrary);

        void SetProjects(Wire.ProjectsReply j) =>
            Projects = j.Projects.Select(ProjectInfo.FromWire).ToList();
        void SetLibrary(Wire.LibraryReply j) =>
            Library = j.Library.Select(LibraryItemInfo.FromWire).ToList();

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // A window opened while the socket is down still has to draw something, and this road
        // returns an error body.
        public void RefreshProjects(Action<string> fail = null) =>
            _projects.Refresh<Wire.ProjectsReply>(ProjectsPath, SetProjects, fail);

        public LibraryItemInfo LibraryItem(string name) =>
            Library.FirstOrDefault(s => s.Name == name);

        public void RefreshLibrary(Action<string> fail = null) =>
            _library.Refresh<Wire.LibraryReply>(LibraryPath, SetLibrary, fail);

        public void SaveLibraryItem(LibraryItemInfo s, bool isNew, string origName,
                                 Action ok, Action<string> fail)
        {
            _library.Invalidate();
            Action<Wire.Ack> done = _ => { RefreshLibrary(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(LibraryPath, s.ToWire(), done, fail);
            else DaemonClient.Put($"{LibraryPath}/{HubWire.Esc(origName)}", s.ToWire(), done, fail);
        }

        public void RemoveLibraryItem(string name, Action<string> fail = null)
        {
            _library.Invalidate();
            DaemonClient.Delete($"{LibraryPath}/{HubWire.Esc(name)}",
                _ => RefreshLibrary(), fail);
        }

        // The old lists stay up until the answer lands, so a dialog opened with the socket
        // down draws what it knew rather than nothing.
        public void LoadPresets(Action ok = null, Action<string> fail = null) =>
            _presets.Refresh<Wire.PresetsReply>(PresetsPath, j =>
            {
                Presets = j.Presets.Select(PresetInfo.FromWire).ToList();
                Commands = j.Commands.Select(CommandInfo.FromWire).ToList();
                ok?.Invoke();
            }, fail);

        public void RefreshTemplates(Action<string> fail = null, Action loaded = null) =>
            _templates.Refresh<Wire.TemplatesReply>(TemplatesPath,
                j =>
                {
                    Templates = j.Templates.Select(AgentTemplateInfo.FromWire).ToList();
                    loaded?.Invoke();
                },
                fail, loaded);

        public void SaveAgentTemplate(string source, string name, string description,
                                       Action ok, Action<string> fail)
        {
            _templates.Invalidate();
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
            _templates.Invalidate();
            Action<Wire.Ack> done = _ => { RefreshTemplates(); ok?.Invoke(); };
            if (isNew)
                DaemonClient.Post<Wire.TemplateResult>(TemplatesPath, new Wire.SaveTemplateRequest { Name = body.Name, Description = body.Description, Version = body.Version, Origin = body.Origin, Defaults = body.Defaults }, _ => done(null), fail);
            else
                DaemonClient.Put<Wire.TemplateResult>($"{TemplatesPath}/{HubWire.Esc(originalName)}", body, _ => done(null), fail);
        }

        public void DuplicateAgentTemplate(string source, string name, string description,
                                           Action ok, Action<string> fail)
        {
            _templates.Invalidate();
            var body = new Wire.SaveTemplateRequest { Name = name, Description = description, Duplicate = source };
            DaemonClient.Post<Wire.TemplateResult>(TemplatesPath, body,
                _ => { RefreshTemplates(); ok?.Invoke(); }, fail);
        }

        public void RemoveAgentTemplate(AgentTemplateInfo template, string originalName,
                                        Action ok, Action<string> fail)
        {
            _templates.Invalidate();
            string path = $"{TemplatesPath}/{HubWire.Esc(originalName ?? template.Name)}?{WireProtocol.TemplateVersionQuery}={template.Version}";
            DaemonClient.Delete(path, _ => { RefreshTemplates(); ok?.Invoke(); }, fail);
        }

        public void CopyPreset(string kind, string name, string newName,
                               Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Post($"{PresetsPath}/{kind}/{Uri.EscapeDataString(name)}/copy",
                new Wire.CopyPresetReq { Name = newName ?? "" }, PresetsSaved(ok, fail), fail);
        }

        public void SavePreset(PresetInfo p, Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Put($"{PresetsPath}/sandbox_presets/{Uri.EscapeDataString(p.Name)}", new Wire.PresetRequest { Sandbox = p.ToWire() },
                PresetsSaved(ok, fail), fail);
        }

        public void RemovePreset(string kind, string name, Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Delete($"{PresetsPath}/{kind}/{Uri.EscapeDataString(name)}",
                PresetsSaved(ok, fail), fail);
        }

        public void SaveCommand(CommandInfo c, Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Put($"{PresetsPath}/app_presets/{Uri.EscapeDataString(c.Name)}", new Wire.PresetRequest { Command = c.ToWire() },
                PresetsSaved(ok, fail), fail);
        }

        Action<Wire.Ack> PresetsSaved(Action ok, Action<string> fail) => _ =>
        {
            // The write completed even if another catalog GET supersedes this reload;
            // do not make the caller's completion depend on which snapshot wins.
            LoadPresets(fail: fail);
            ok?.Invoke();
        };

        public CommandInfo Command(string name) =>
            string.IsNullOrEmpty(name) ? null : Commands.FirstOrDefault(c => c.Name == name);

        public void SaveProject(ProjectInfo p, bool isNew, string origName,
                                Action ok, Action<string> fail)
        {
            // Invalidate every list request already in flight before the write starts. The
            // successful write starts a fresh refresh below, and only that refresh may replace
            // the catalog while this edit is settling.
            _projects.Invalidate();
            Action<Wire.Ack> done = _ => { RefreshProjects(); _refreshSessions(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(ProjectsPath, p.ToWire(), done, fail);
            else DaemonClient.Put($"{ProjectsPath}/{HubWire.Esc(origName)}", p.ToWire(), done, fail);
        }

        // The daemon refuses this while agents still work there, and says which ones.
        public void RemoveProject(string name, Action<string> fail = null) =>
            DeleteProject($"{ProjectsPath}/{HubWire.Esc(name)}", fail);

        void DeleteProject(string path, Action<string> fail)
        {
            _projects.Invalidate();
            DaemonClient.Delete(path,
                _ => { RefreshProjects(); _refreshSessions(); }, fail);
        }
    }

    sealed class CatalogRequest
    {
        public int Revision { get; private set; }
        public void Invalidate() => Revision++;

        public void Apply<T>(T value, Action<T> apply)
        {
            Invalidate();
            apply(value);
        }

        public void Refresh<T>(string path, Action<T> apply, Action<string> fail,
                            Action superseded = null) where T : Google.Protobuf.IMessage<T>, new()
        {
            int revision = ++Revision;
            DaemonClient.Get<T>(path,
                value => { if (revision == Revision) apply(value); else superseded?.Invoke(); },
                error => { if (revision == Revision) fail?.Invoke(error); else superseded?.Invoke(); });
        }
    }
}
