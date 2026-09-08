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
            new CommandDef("agent.new", "Agent: New", "Agent",
                _ => TerminalWindow.OpenOverPane(new EditSessionDialog(null))),
            CommandDef.ForAgent("agent.label", "Agent: Label", AgentsSubAll,
                s => LabelDialog.Open(s.Name, s.Label)),
            CommandDef.ForAgent("agent.delegate-task", "Agent: Delegate task", AgentsSubAll,
                s => TerminalWindow.OpenOverPane(new DelegateTaskDialog(s.Name))),
            CommandDef.ForAgent("agent.new-look", "Agent: New look", AgentsSubWithPawn,
                s =>
                {
                    var pawn = AgentColony.Current?.PawnOf(s.Name);
                    if (pawn != null && !pawn.Destroyed) AgentLook.Reroll(pawn);
                }),
            CommandDef.ForAgent("agent.reset-state", "Agent: Reset private state", AgentsSubAll,
                s =>
                {
                    if (s.Ephemeral || s.Host) return;
                    var name = s.Name;
                    TerminalWindow.OpenOverPane(CatalogActions.ResetState(name));
                }),
            CommandDef.ForAgent("agent.start", "Agent: Start",
                () => AgentsSub(AgentState.Down),
                s => SessionHub.Instance.SessionStore.Start(s.Name, UiWidgets.Fail)),
            CommandDef.ForAgent("agent.stop", "Agent: Stop",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.SessionStore.Stop(s.Name, UiWidgets.Fail)),
            CommandDef.ForAgent("agent.restart", "Agent: Restart",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => SessionHub.Instance.SessionStore.Restart(s.Name, UiWidgets.Fail)),
            CommandDef.ForAgent("agent.edit", "Agent: Edit", AgentsSubEditable,
                s => Find.WindowStack.Add(new EditSessionDialog(s))),
            CommandDef.ForAgent("agent.terminal", "Agent: Open Terminal",
                () => AgentsSub(AgentState.Working, AgentState.Waiting, AgentState.Idle),
                s => { if (!s.Gone) TerminalWindow.Open(s.Name); }),
            CommandDef.ForAgent("agent.storage", "Agent: Open Storage", AgentsSubAll,
                s => StoragePage.FocusAgent(s.Name)),
            CommandDef.ForAgent("agent.delete", "Agent: Delete", AgentsSubAll, s =>
                Find.WindowStack.Add(CatalogActions.RemoveSession(s.Name))),
            CommandDef.ForAgent("agent.duplicate", "Agent: Duplicate", AgentsSubWithProject,
                s =>
                {
                    if (!string.IsNullOrEmpty(s.Project))
                        TerminalWindow.OpenOverPane(EditSessionDialog.Copy(s));
                }),
            CommandDef.ForAgent("agent.shell", "Agent: Shell", AgentsSubWithProject,
                s =>
                {
                    if (!string.IsNullOrEmpty(s.Project))
                        SessionHub.Instance.SessionStore.Run(s.Project, "", "", session => TerminalWindow.Open(session),
                            UiWidgets.Fail, like: s.Name);
                }),

            new CommandDef("project.new", "Project: New", "Project",
                _ => TerminalWindow.OpenOverPane(new EditProjectDialog(null))),
            CommandDef.ForProject("project.edit", "Project: Edit", ProjectsSub,
                p => TerminalWindow.OpenOverPane(new EditProjectDialog(p))),
            CommandDef.ForProject("project.delete", "Project: Delete", DeletableProjectsSub, p =>
                Find.WindowStack.Add(CatalogActions.RemoveProject(p.Name))),
            CommandDef.ForProject("project.duplicate", "Project: Duplicate", ProjectsSub,
                p => TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
            CommandDef.ForProject("project.host-terminal", "Project: Open Host Terminal",
                ProjectsSub,
                p => SessionHub.Instance.SessionStore.RunHostShell(p.Name,
                    session => TerminalWindow.Open(session), UiWidgets.Fail)),

            new CommandDef("task.new", "Task: New", "Task",
                _ => TerminalWindow.OpenOverPane(new DelegateTaskDialog(null)),
                enabled: HasTaskRecipients),
            CommandDef.ForTask("task.open", "Task: Open", TasksSub,
                t => TaskDetailView.Open(t), enabled: HasTasks),
            CommandDef.ForTask("task.cancel", "Task: Cancel", CancelableTasksSub,
                t => TaskActions.CancelTask(t), enabled: HasCancelableTasks),
            CommandDef.ForTask("task.remove", "Task: Remove", TerminalTasksSub,
                t => TaskActions.RemoveTask(t), enabled: HasTerminalTasks),
            new CommandDef("task.status", "Task: Set Status", "Task", _ => { },
                enabled: HasStatusTasks, subAction: TaskStatusSub),
            new CommandDef("task.filter", "Task: Filter", "Task",
                _ => TasksView.OpenFilterMenu()),

            CommandDef.ForLibraryItem("library.run", "Library: Run", LibraryItemsSub, s =>
            {
                if (s.Kind == LibraryItemKind.Breadcrumb || s.Kind == LibraryItemKind.FileAction) return;
                if (s.Link == LibraryItemLink.Ask) AskWhere(s);
                else RunLibraryItemWith(s.Name);
            }),
            new CommandDef("library.new", "Library: New", "Library",
                _ => { }, subAction: NewLibraryItemSub),
            CommandDef.ForLibraryItem("library.edit", "Library: Edit", LibraryManageSub,
                s => TerminalWindow.OpenOverPane(new EditLibraryItemDialog(s))),
            CommandDef.ForLibraryItem("library.delete", "Library: Delete", LibraryManageSub, s =>
            {
                var name = s.Name;
                TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                    $"Remove library entry '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiWidgets.Fail), destructive: true));
            }),
            CommandDef.ForLibraryItem("library.duplicate", "Library: Duplicate", LibraryManageSub,
                s => TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(s))),

            new CommandDef("host.open-shell", "Host: Open Shell", "Host",
                _ => { }, subAction: HostShellSub),
            CommandDef.ForAgent("host.remove", "Host: Remove", HostSessionsSub, s =>
            {
                if (!s.Host) return;
                var name = s.Name;
                TerminalWindow.OpenOverPane(CatalogActions.RemoveHost(name));
            }),

            new CommandDef("daemon.reconnect", "Daemon: Reconnect", "Daemon",
                _ => SessionHub.Instance.Connect()),
            new CommandDef("agents.refresh", "Agents: Refresh", "Refresh",
                _ => SessionHub.Instance.SessionStore.Refresh()),
            new CommandDef("projects.refresh", "Projects: Refresh", "Refresh",
                _ => SessionHub.Instance.Catalog.RefreshProjects(UiWidgets.Fail)),
            new CommandDef("library.refresh", "Library: Refresh", "Refresh",
                _ => SessionHub.Instance.Catalog.RefreshLibrary(UiWidgets.Fail)),
            new CommandDef("tasks.refresh", "Tasks: Refresh", "Refresh",
                _ => SessionHub.Instance.TaskStore.Refresh(fail: UiWidgets.Fail)),
            new CommandDef("files.reload", "Files: Reload", "Refresh",
                _ => FilesView.Reload()),
            new CommandDef("search.open", "Search: Find in Files", "View",
                _ => AgentSidebar.ShowSearch()),
            new CommandDef("search.run", "Search: Run Search", "Search",
                _ => SearchView.Search()),
            new CommandDef("git.refresh", "Git: Refresh", "Refresh",
                _ => GitView.Refresh()),
            CommandDef.ForProject("git.diff-all", "Git: Diff All", ProjectsSub,
                p => GitView.DiffAll(p.Name)),
            CommandDef.ForProject("git.stage-all", "Git: Stage All", ProjectsSub,
                p => GitView.StageAll(p.Name)),
            CommandDef.ForProject("git.unstage-all", "Git: Unstage All", ProjectsSub,
                p => GitView.UnstageAll(p.Name)),
            CommandDef.ForProject("git.commit-staged", "Git: Commit Staged", ProjectsSub,
                p => GitView.CommitStaged(p.Name)),

            new CommandDef("view.refresh-sidebar", "View: Refresh Sidebar", "View",
                _ => AgentSidebar.RefreshCurrentView()),
            new CommandDef("focus.agents", "Focus: Agents", "Focus",
                _ => AgentSidebar.ShowAgents()),
            new CommandDef("focus.files", "Focus: Files", "Focus",
                _ => AgentSidebar.ShowFiles()),
            new CommandDef("focus.search", "Focus: Search", "Focus",
                _ => AgentSidebar.ShowSearch()),
            new CommandDef("focus.git", "Focus: Git", "Focus",
                _ => AgentSidebar.ShowGit()),
            new CommandDef("focus.library", "Focus: Library", "Focus",
                _ => AgentSidebar.ShowLibrary()),
            new CommandDef("focus.tasks", "Focus: Tasks", "Focus",
                _ => AgentSidebar.ShowTasks()),
            new CommandDef("view.fold-all", "View: Fold All", "View",
                _ => AgentSidebar.SetAllFolds(true),
                enabled: () => AgentSidebar.CanFoldCurrent && !AgentSidebar.CurrentViewAllFolded),
            new CommandDef("view.unfold-all", "View: Unfold All", "View",
                _ => AgentSidebar.SetAllFolds(false),
                enabled: () => AgentSidebar.CanFoldCurrent && AgentSidebar.CurrentViewAllFolded),
            new CommandDef("view.dotfiles", "View: Toggle Dotfiles", "View",
                _ => AgentSidebar.ToggleDotfiles(),
                enabled: () => AgentSidebar.CanToggleDotfiles),
            new CommandDef("view.gitignored", "View: Toggle Gitignored", "View",
                _ => AgentSidebar.ToggleGitignored(),
                enabled: () => AgentSidebar.CanToggleDotfiles),
            new CommandDef("view.toggle-sidebar", "View: Toggle Sidebar", "View",
                _ => AgentSidebar.ToggleSidebar()),

            new CommandDef("view.config", "Settings: General", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Config))),
            new CommandDef("view.storage", "Settings: Storage", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Storage))),
            new CommandDef("view.commands", "Settings: Commands - Defaults", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.CommandDefaults))),
            new CommandDef("view.command-binaries", "Settings: Commands - Binaries", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.CommandBinaries))),
            new CommandDef("view.command-presets", "Settings: Commands - Presets", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.CommandPresets))),
            new CommandDef("view.terminal-settings", "Settings: Terminal", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Terminal))),
            new CommandDef("view.statusbar-settings", "Settings: Appearance - Statusbar", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(
                    ModOptions.PageId.AppearanceStatusbar))),
            new CommandDef("view.appearance", "Settings: Appearance", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Appearance))),
            new CommandDef("view.audio", "Settings: Audio", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Audio))),
            new CommandDef("view.integrations", "Settings: Integrations", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Integrations))),
            new CommandDef("view.credentials", "Settings: Integrations - Credentials", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Credentials))),
            new CommandDef("view.usage", "Settings: Integrations - Usage", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Usage))),
            new CommandDef("view.summaries", "Settings: Integrations - Summaries", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Summaries))),
            new CommandDef("view.instructions", "Settings: Integrations - Instructions", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Instructions))),
            new CommandDef("view.sandbox", "Settings: Sandbox", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Sandbox))),
            new CommandDef("view.keyboard-settings", "Settings: Keyboard", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Keyboard))),
            new CommandDef("view.rimworld-settings", "Settings: RimWorld", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.RimWorld))),
            new CommandDef("view.about", "Settings: About", "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.About))),
            new CommandDef("sandbox.new-preset", "Sandbox: New Preset", "Sandbox",
                _ => ModOptions.OpenNewSandboxPreset()),
            new CommandDef("command.new", "Command: New", "Commands",
                _ => ModOptions.OpenNewCommand()),
            new CommandDef("config.toml", "Configuration: Edit config.toml", "Configuration",
                _ => ConfigWindow.Open()),
            new CommandDef("view.filter", "View: Filter Projects", "View",
                v => { if (v != null) AgentSidebar.ToggleFilter(v); }, subAction: () => FilterSub()),
            new CommandDef("view.agent-status", "View: Filter Agents by Status", "View",
                SetAgentStatusFromPalette, subAction: AgentStatusSub),
            new CommandDef("view.zoom-in", "View: Zoom In", "View",
                _ => UiScale.Zoom(1)),
            new CommandDef("view.zoom-out", "View: Zoom Out", "View",
                _ => UiScale.Zoom(-1)),
            new CommandDef("window.fullscreen", "Window: Toggle Fullscreen", "View",
                _ => WindowMaximizer.Toggle()),

            new CommandDef("help.shortcuts", "Help: Keyboard Shortcuts", "Help",
                _ => ShortcutHelpWindow.Toggle(), enabled: Playing),

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
            new CommandDef("jukebox.stop-on-exit", "Jukebox: Toggle Stop on Exit", "Jukebox",
                _ => Radio.ToggleStopOnExit()),
            new CommandDef("jukebox.edit-likes", "Jukebox: Edit Likes", "Jukebox",
                _ => StoragePage.EditLikes()),

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
