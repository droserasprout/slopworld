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
        const string ProjectsPath = WireContract.Routes.Projects;
        const string LibraryPath = WireContract.Routes.Library;
        const string PresetsPath = WireContract.Routes.Presets;

        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<LibraryItemInfo> Library = new List<LibraryItemInfo>();
        public List<PresetInfo> Presets = new List<PresetInfo>();
        public List<CommandInfo> Commands = new List<CommandInfo>();

        // A project list request can outlive the edit that started another one. Without a
        // generation, a slow pre-edit GET can put the old network default back after a Host
        // save has succeeded.
        readonly CatalogRequest _projects = new CatalogRequest();
        readonly CatalogRequest _library = new CatalogRequest();
        readonly CatalogRequest _presets = new CatalogRequest();

        public int ProjectsRevision => _projects.Revision;

        // A project edit also changes which sessions exist, so the catalog asks the session
        // store to refresh without owning it.
        readonly Action _refreshSessions;

        public HubCatalog(Action refreshSessions)
        {
            _refreshSessions = refreshSessions;
        }

        // The socket pushes these, and each also has an HTTP road below for a window opened
        // while the socket is down.
        public void ApplyProjects(JVal ev) => _projects.Apply(ev, SetProjects);
        public void ApplyLibrary(JVal ev) => _library.Apply(ev, SetLibrary);

        void SetProjects(JVal j) =>
            Projects = j["projects"].Items.Select(ProjectInfo.FromJson).ToList();
        void SetLibrary(JVal j) =>
            Library = j["library"].Items.Select(LibraryItemInfo.FromJson).ToList();

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // A window opened while the socket is down still has to draw something, and this road
        // returns an error body.
        public void RefreshProjects(Action<string> fail = null) =>
            _projects.Refresh(ProjectsPath, SetProjects, fail);

        public LibraryItemInfo LibraryItem(string name) =>
            Library.FirstOrDefault(s => s.Name == name);

        public void RefreshLibrary(Action<string> fail = null) =>
            _library.Refresh(LibraryPath, SetLibrary, fail);

        public void SaveLibraryItem(LibraryItemInfo s, bool isNew, string origName,
                                 Action ok, Action<string> fail)
        {
            _library.Invalidate();
            Action<JVal> done = _ => { RefreshLibrary(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(LibraryPath, s.ToJson(), done, fail);
            else DaemonClient.Put($"{LibraryPath}/{HubWire.Esc(origName)}", s.ToJson(), done, fail);
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
            _presets.Refresh(PresetsPath, j =>
            {
                Presets = j["presets"].Items.Select(PresetInfo.FromJson).ToList();
                Commands = j["commands"].Items.Select(CommandInfo.FromJson).ToList();
                ok?.Invoke();
            }, fail);

        public void CopyPreset(string kind, string name, string newName,
                               Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Post($"{PresetsPath}/{kind}/{Uri.EscapeDataString(name)}/copy",
                $"{{\"name\":{JVal.Q(newName ?? "")}}}", PresetsSaved(ok, fail), fail);
        }

        public void SavePreset(PresetInfo p, Action ok, Action<string> fail)
        {
            _presets.Invalidate();
            DaemonClient.Put($"{PresetsPath}/sandbox/{Uri.EscapeDataString(p.Name)}", p.ToJson(),
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
            DaemonClient.Put($"{PresetsPath}/command/{Uri.EscapeDataString(c.Name)}", c.ToJson(),
                PresetsSaved(ok, fail), fail);
        }

        Action<JVal> PresetsSaved(Action ok, Action<string> fail) => _ =>
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
            Action<JVal> done = _ => { RefreshProjects(); _refreshSessions(); ok?.Invoke(); };
            if (isNew) DaemonClient.Post(ProjectsPath, p.ToJson(), done, fail);
            else DaemonClient.Put($"{ProjectsPath}/{HubWire.Esc(origName)}", p.ToJson(), done, fail);
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

        public void Apply(JVal value, Action<JVal> apply)
        {
            Invalidate();
            apply(value);
        }

        public void Refresh(string path, Action<JVal> apply, Action<string> fail)
        {
            int revision = ++Revision;
            DaemonClient.Get(path,
                value => { if (revision == Revision) apply(value); },
                error => { if (revision == Revision) fail?.Invoke(error); });
        }
    }
}
