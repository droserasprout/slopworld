using System.Collections.Generic;
using Verse;

namespace SlopWorld
{
    // Command registration table.
    public partial class CommandPalette
    {
        // --------------------------------------------------------------- command table

        // The palette is a catalogue of data. Availability is evaluated while the list is
        // rebuilt, so game-state changes do not require rebuilding the definitions.
        static readonly List<CommandDef> CommandTable = new List<CommandDef>
        {
            CommandDef.ForAgent("agent.start", "Agent: Start",
                () => AgentsSub(AgentState.Down),
                s => SessionHub.Instance.Start(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.stop", "Agent: Stop",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.Stop(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.restart", "Agent: Restart",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.Restart(s.Name, SlopWidgets.Fail)),
            CommandDef.ForAgent("agent.edit", "Agent: Edit", AgentsSubAll,
                s => Find.WindowStack.Add(new EditSessionDialog(s))),
            CommandDef.ForAgent("agent.terminal", "Agent: Open Terminal",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => { if (!s.Gone) TerminalWindow.Open(s.Name); }),
            CommandDef.ForAgent("agent.delete", "Agent: Delete", AgentsSubAll, s =>
            {
                var name = s.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
                    "its private state to recoverable trash for 14 days.",
                    () => SessionHub.Instance.Remove(name, SlopWidgets.Fail), destructive: true));
            }),
            CommandDef.ForAgent("agent.duplicate", "Agent: Duplicate", AgentsSubWithProject,
                s =>
                {
                    if (!string.IsNullOrEmpty(s.Project))
                        TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
                }),

            new CommandDef("project.new", "Project: New", "Project",
                _ => Find.WindowStack.Add(new EditProjectDialog(null))),
            CommandDef.ForProject("project.edit", "Project: Edit", ProjectsSub,
                p => Find.WindowStack.Add(new EditProjectDialog(p))),
            CommandDef.ForProject("project.delete", "Project: Delete", ProjectsSub, p =>
            {
                var name = p.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove project '{name}'? The directory is left alone; only the entry in config.toml goes.",
                    () => SessionHub.Instance.RemoveProject(name, SlopWidgets.Fail), destructive: true));
            }),
            CommandDef.ForProject("project.duplicate", "Project: Duplicate", ProjectsSub,
                p => TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
            CommandDef.ForProject("project.host-terminal", "Project: Open Host Terminal",
                ProjectsSub,
                p => SessionHub.Instance.RunHostShell(p.Name,
                    session => TerminalWindow.Open(session), SlopWidgets.Fail)),

            CommandDef.ForShortcut("shortcut.run", "Shortcut: Run", ShortcutsSub, s =>
            {
                if (s.Kind == ShortcutKind.Breadcrumb || s.Kind == ShortcutKind.FileAction) return;
                if (s.Link == ShortcutLink.Ask) AskWhere(s);
                else RunShortcutWith(s.Name);
            }),
            new CommandDef("shortcut.new", "Shortcut: New", "Shortcut",
                _ => Find.WindowStack.Add(new EditShortcutDialog(null))),
            CommandDef.ForShortcut("shortcut.edit", "Shortcut: Edit", ShortcutsSub,
                s => Find.WindowStack.Add(new EditShortcutDialog(s))),
            CommandDef.ForShortcut("shortcut.delete", "Shortcut: Delete", ShortcutsSub, s =>
            {
                var name = s.Name;
                Find.WindowStack.Add(SlopConfirmDialog.Create(
                    $"Remove shortcut '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.RemoveShortcut(name, SlopWidgets.Fail), destructive: true));
            }),

            new CommandDef("daemon.reconnect", "Daemon: Reconnect", "Daemon",
                _ => SessionHub.Instance.Connect()),
            new CommandDef("agents.refresh", "Agents: Refresh", "Refresh",
                _ => SessionHub.Instance.Refresh()),
            new CommandDef("projects.refresh", "Projects: Refresh", "Refresh",
                _ => SessionHub.Instance.RefreshProjects(SlopWidgets.Fail)),
            new CommandDef("shortcuts.refresh", "Shortcuts: Refresh", "Refresh",
                _ => SessionHub.Instance.RefreshShortcuts(SlopWidgets.Fail)),
            new CommandDef("files.reload", "Files: Reload", "Refresh",
                _ => FilesView.Reload()),
            new CommandDef("search.open", "Search: Find in Files", "View",
                _ => AgentSidebar.ShowSearch()),
            new CommandDef("git.refresh", "Git: Refresh", "Refresh",
                _ => GitView.Refresh()),

            new CommandDef("view.config", "Settings: General", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Config))),
            new CommandDef("view.storage", "Settings: Storage", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Storage))),
            new CommandDef("view.commands", "Settings: Commands - Defaults", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.CommandDefaults))),
            new CommandDef("view.command-presets", "Settings: Commands - Presets", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.CommandPresets))),
            new CommandDef("view.terminal-settings", "Settings: Terminal", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Terminal))),
            new CommandDef("view.appearance", "Settings: Appearance", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Appearance))),
            new CommandDef("view.audio", "Settings: Audio", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Audio))),
            new CommandDef("view.integrations", "Settings: Integrations", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Integrations))),
            new CommandDef("view.credentials", "Settings: Integrations - Credentials", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Credentials))),
            new CommandDef("view.usage", "Settings: Integrations - Usage", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Usage))),
            new CommandDef("view.summaries", "Settings: Integrations - Summaries", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Summaries))),
            new CommandDef("view.sandbox", "Settings: Sandbox", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Sandbox))),
            new CommandDef("view.shortcuts-settings", "Settings: Keyboard", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.Keyboard))),
            new CommandDef("view.rimworld-settings", "Settings: RimWorld", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.RimWorld))),
            new CommandDef("view.about", "Settings: About", "Settings",
                _ => SlopOptions.OpenCategory(SlopOptions.CategoryFor(SlopOptions.PageId.About))),
            new CommandDef("config.toml", "Configuration: Edit config.toml", "Configuration",
                _ => ConfigWindow.Open()),
            new CommandDef("view.filter", "View: Filter Projects", "View",
                v => { if (v != null) AgentSidebar.ToggleFilter(v); }, subAction: () => FilterSub()),
            new CommandDef("view.zoom-in", "View: Zoom In", "View",
                _ => SlopUIScale.Zoom(1)),
            new CommandDef("view.zoom-out", "View: Zoom Out", "View",
                _ => SlopUIScale.Zoom(-1)),
            new CommandDef("window.fullscreen", "Window: Toggle Fullscreen", "View",
                _ => WindowMaximizer.Toggle()),

            new CommandDef("jukebox.mute", "Jukebox: Mute", "Jukebox",
                _ => Radio.ToggleMute()),
            new CommandDef("jukebox.random", "Jukebox: Random", "Jukebox",
                _ => Radio.PickRandom()),
            new CommandDef("jukebox.tune", "Jukebox: Tune", "Jukebox",
                _ => { }, subAction: JukeboxSub),
            new CommandDef("jukebox.recognize", "Jukebox: Recognize", "Jukebox",
                _ => Radio.Recognize(), enabled: () => !Radio.Recognizing),
            new CommandDef("jukebox.cancel-recognition", "Jukebox: Cancel recognition", "Jukebox",
                _ => Radio.CancelRecognition(), enabled: () => Radio.Recognizing),
            new CommandDef("jukebox.like", "Jukebox: Like", "Jukebox",
                _ => Radio.Like()),
            new CommandDef("jukebox.history", "Jukebox: History", "Jukebox",
                _ => JukeboxHistoryView.Open()),

            new CommandDef("game.new-looks", "Game: New looks", "Game",
                _ => CoreTip.NewLooks(), enabled: Playing),
            new CommandDef("game.kill-something", "Game: Kill something", "Game",
                _ => CoreTip.KillSomething(), enabled: () => Playing()
                    && !Settings.GrandmaMode && !Settings.EcoMode),
            new CommandDef("game.hint", "Game: Hint", "Game",
                _ => CoreTip.ShowHint(), enabled: Playing),
            new CommandDef("game.nextplanet", "Game: Next Planet", "Game",
                _ => NextPlanet.Begin(), enabled: () => Playing() && !Settings.EcoMode),
        };
    }
}
