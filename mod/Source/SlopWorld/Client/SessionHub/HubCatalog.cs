using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // The reference data the daemon keeps in TOML: the projects an agent can run in, the
    // shortcuts that launch them, and the sandbox presets and command templates. Projects and
    // shortcuts are pushed on connect and on any edit; presets and commands are fetched afresh
    // for every dialog that draws them, so one can arrive without slopd being rebuilt.
    class HubCatalog
    {
        public List<ProjectInfo> Projects = new List<ProjectInfo>();
        public List<ShortcutInfo> Shortcuts = new List<ShortcutInfo>();
        public List<PresetInfo> Presets = new List<PresetInfo>();
        public List<CommandInfo> Commands = new List<CommandInfo>();

        // A project list request can outlive the edit that started another one. Without a
        // generation, a slow pre-edit GET can put the old network default back after a Host
        // save has succeeded.
        int _projectsRevision;

        // A project edit also changes which sessions exist, so the catalog asks the session
        // store to refresh without owning it.
        readonly Action _refreshSessions;

        public HubCatalog(Action refreshSessions)
        {
            _refreshSessions = refreshSessions;
        }

        // The socket pushes these, and each also has an HTTP road below for a window opened
        // while the socket is down.
        public void ApplyProjects(JVal ev)
        {
            _projectsRevision++;
            Projects = ev["projects"].Items.Select(ProjectInfo.FromJson).ToList();
        }

        public void ApplyShortcuts(JVal ev) =>
            Shortcuts = ev["shortcuts"].Items.Select(ShortcutInfo.FromJson).ToList();

        public ProjectInfo Project(string name) =>
            Projects.FirstOrDefault(p => p.Name == name);

        // A window opened while the socket is down still has to draw something, and this road
        // returns an error body.
        public void RefreshProjects(Action<string> fail = null)
        {
            int revision = ++_projectsRevision;
            SlopClient.Get("/api/projects",
                j =>
                {
                    if (revision == _projectsRevision)
                        Projects = j["projects"].Items.Select(ProjectInfo.FromJson).ToList();
                },
                msg =>
                {
                    if (revision == _projectsRevision) fail?.Invoke(msg);
                });
        }

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
            else SlopClient.Put($"/api/shortcuts/{HubWire.Esc(origName)}", s.ToJson(), done, fail);
        }

        public void RemoveShortcut(string name, Action<string> fail = null) =>
            SlopClient.Delete($"/api/shortcuts/{HubWire.Esc(name)}",
                _ => RefreshShortcuts(), fail);

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
            // Invalidate every list request already in flight before the write starts. The
            // successful write starts a fresh refresh below, and only that refresh may replace
            // the catalog while this edit is settling.
            _projectsRevision++;
            Action<JVal> done = _ => { RefreshProjects(); _refreshSessions(); ok?.Invoke(); };
            if (isNew) SlopClient.Post("/api/projects", p.ToJson(), done, fail);
            else SlopClient.Put($"/api/projects/{HubWire.Esc(origName)}", p.ToJson(), done, fail);
        }

        // The daemon refuses this while agents still work there, and says which ones.
        public void RemoveProject(string name, Action<string> fail = null) =>
            DeleteProject($"/api/projects/{HubWire.Esc(name)}", fail);

        void DeleteProject(string path, Action<string> fail)
        {
            _projectsRevision++;
            SlopClient.Delete(path,
                _ => { RefreshProjects(); _refreshSessions(); }, fail);
        }
    }
}
