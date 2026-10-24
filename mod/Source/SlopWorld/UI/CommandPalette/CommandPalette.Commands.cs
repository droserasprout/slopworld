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
            CommandDef.ForCreation("add", "Add…", "Create", CreationActions.Add),
            CommandDef.ForAgent(
                "agent.label",
                "Agent: Label",
                AgentsSubAll,
                s => LabelDialog.Open(s.Name, s.Label)
            ),
            CommandDef.ForAgent(
                "agent.delegate-task",
                "Agent: Delegate task",
                AgentsSubAll,
                s => WorkspaceWindow.OpenOverPane(new DelegateTaskDialog(s.Name))
            ),
            CommandDef.ForAgent(
                "agent.new-look",
                "Agent: New look",
                AgentsSubWithPawn,
                s =>
                {
                    var pawn = AgentColony.Current?.PawnOf(s.Name);
                    if (pawn != null && !pawn.Destroyed)
                        AgentLook.Reroll(pawn);
                }
            ),
            CommandDef.ForAgent(
                "agent.reset-state",
                "Agent: Reset private state",
                AgentsSubAll,
                s =>
                {
                    if (s.Ephemeral || s.Host)
                        return;
                    var name = s.Name;
                    WorkspaceWindow.OpenOverPane(SessionActions.ResetState(name));
                }
            ),
            CommandDef.ForAgent(
                "agent.start",
                "Agent: Start",
                () => AgentsSub(AgentState.Down),
                s => SessionActions.Start(s.Name)
            ),
            CommandDef.ForAgent(
                "agent.stop",
                "Agent: Stop",
                () => AgentsSub(AgentState.Working, AgentState.Idle),
                s => SessionHub.Instance.SessionStore.Stop(s.Name, UiLayout.Fail)
            ),
            CommandDef.ForAgent(
                "agent.restart",
                "Agent: Restart",
                () => AgentsSub(AgentState.Working, AgentState.Idle),
                s => SessionActions.Restart(s.Name)
            ),
            CommandDef.ForAgent(
                "agent.edit",
                "Agent: Edit",
                AgentsSubEditable,
                s => Find.WindowStack.Add(new EditSessionDialog(s))
            ),
            CommandDef.ForAgent(
                "agent.terminal",
                "Agent: Open Terminal",
                () => AgentsSub(AgentState.Working, AgentState.Idle),
                s =>
                {
                    if (!s.Gone)
                        WorkspaceWindow.Open(s.Name);
                }
            ),
            CommandDef.ForAgent(
                "agent.storage",
                "Agent: Open Storage",
                AgentsSubAll,
                s => ModOptions.OpenAgentStorage(s.Name)
            ),
            CommandDef.ForAgent(
                "agent.delete",
                "Agent: Delete",
                AgentsSubAll,
                s => Find.WindowStack.Add(SessionActions.RemoveSession(s.Name))
            ),
            CommandDef.ForAgent(
                "agent.duplicate",
                "Agent: Duplicate",
                AgentsSubWithProject,
                SessionActions.DuplicateAgent
            ),
            CommandDef.ForAgent(
                "agent.shell",
                "Agent: Shell",
                AgentsSubForShell,
                SessionActions.AgentShell
            ),
            CommandDef.ForProject(
                "project.edit",
                "Project: Edit",
                ProjectsSub,
                ProjectActions.EditProject
            ),
            CommandDef.ForProject(
                "project.delete",
                "Project: Delete",
                DeletableProjectsSub,
                p => Find.WindowStack.Add(ProjectActions.RemoveProject(p.Name))
            ),
            CommandDef.ForProject(
                "project.duplicate",
                "Project: Duplicate",
                ProjectsSub,
                ProjectActions.DuplicateProject
            ),
            CommandDef.ForProject(
                "project.host-terminal",
                "Project: Open Host Terminal",
                ProjectsSub,
                ProjectActions.ProjectTerminal
            ),
            CommandDef.ForTask("task.open", "Task: Open", TasksSub, t => TaskDetailView.Open(t)),
            CommandDef.ForTask(
                "task.cancel",
                "Task: Cancel",
                CancelableTasksSub,
                t => TaskActions.CancelTask(t)
            ),
            CommandDef.ForTask(
                "task.remove",
                "Task: Remove",
                TerminalTasksSub,
                t => TaskActions.RemoveTask(t)
            ),
            new CommandDef(
                "task.status",
                "Task: Set Status",
                "Task",
                _ => { },
                subAction: TaskStatusSub
            ),
            new CommandDef("task.filter", "Task: Filter", "Task", _ => TasksView.OpenFilterMenu()),
            CommandDef.ForLibraryItem(
                "library.run",
                "Library: Run",
                LibraryItemsSub,
                s => LibraryActions.Run(s)
            ),
            CommandDef.ForLibraryItem(
                "library.edit",
                "Library: Edit",
                LibraryManageSub,
                LibraryActions.Edit
            ),
            CommandDef.ForLibraryItem(
                "library.delete",
                "Library: Delete",
                LibraryManageSub,
                LibraryActions.Remove
            ),
            CommandDef.ForLibraryItem(
                "library.duplicate",
                "Library: Duplicate",
                LibraryManageSub,
                LibraryActions.Duplicate
            ),
            CommandDef.ForAgent(
                "host.remove",
                "Host: Remove",
                HostSessionsSub,
                s =>
                {
                    if (!s.Host)
                        return;
                    var name = s.Name;
                    WorkspaceWindow.OpenOverPane(HostTerminalActions.RemoveHost(name));
                },
                group: "Host"
            ),
            new CommandDef(
                "daemon.reconnect",
                "Daemon: Reconnect",
                "Daemon",
                _ => SessionHub.Instance.Connect()
            ),
            new CommandDef(
                "agents.refresh",
                "Agents: Refresh",
                "Refresh",
                _ => SessionHub.Instance.SessionStore.Refresh()
            ),
            new CommandDef(
                "projects.refresh",
                "Projects: Refresh",
                "Refresh",
                _ => SessionHub.Instance.Catalog.RefreshProjects(UiLayout.Fail)
            ),
            new CommandDef(
                "library.refresh",
                "Library: Refresh",
                "Refresh",
                _ => SessionHub.Instance.Catalog.RefreshLibrary(UiLayout.Fail)
            ),
            new CommandDef(
                "tasks.refresh",
                "Tasks: Refresh",
                "Refresh",
                _ => SessionHub.Instance.TaskStore.Refresh(fail: UiLayout.Fail)
            ),
            new CommandDef("files.reload", "Files: Reload", "Refresh", _ => FilesView.Reload()),
            new CommandDef(
                "search.open",
                "Search: Find in Files",
                "View",
                _ => Sidebar.ShowSearch()
            ),
            new CommandDef("search.run", "Search: Run Search", "Search", _ => SearchView.Search()),
            new CommandDef("git.refresh", "Git: Refresh", "Refresh", _ => GitView.Refresh()),
            CommandDef.ForProject(
                "git.diff-all",
                "Git: Diff All",
                ProjectsSub,
                p => GitView.DiffAll(p.Name)
            ),
            CommandDef.ForProject(
                "git.stage-all",
                "Git: Stage All",
                ProjectsSub,
                p => GitView.StageAll(p.Name)
            ),
            CommandDef.ForProject(
                "git.unstage-all",
                "Git: Unstage All",
                ProjectsSub,
                p => GitView.UnstageAll(p.Name)
            ),
            CommandDef.ForProject(
                "git.commit-staged",
                "Git: Commit Staged",
                ProjectsSub,
                p => GitView.CommitStaged(p.Name)
            ),
            new CommandDef(
                "view.refresh-sidebar",
                "View: Refresh Sidebar",
                "View",
                _ => Sidebar.RefreshCurrentView()
            ),
            new CommandDef(
                "view.back",
                "View: Back",
                "View",
                _ => Sidebar.ViewBack(),
                enabled: () => Sidebar.CanViewBack
            ),
            new CommandDef(
                "view.forward",
                "View: Forward",
                "View",
                _ => Sidebar.ViewForward(),
                enabled: () => Sidebar.CanViewForward
            ),
            new CommandDef("focus.agents", "Focus: Agents", "Focus", _ => Sidebar.ShowAgents()),
            new CommandDef("focus.files", "Focus: Files", "Focus", _ => Sidebar.ShowFiles()),
            new CommandDef("focus.search", "Focus: Search", "Focus", _ => Sidebar.ShowSearch()),
            new CommandDef("focus.git", "Focus: Git", "Focus", _ => Sidebar.ShowGit()),
            new CommandDef("focus.library", "Focus: Library", "Focus", _ => Sidebar.ShowLibrary()),
            new CommandDef("focus.tasks", "Focus: Tasks", "Focus", _ => Sidebar.ShowTasks()),
            new CommandDef(
                "view.fold-all",
                "View: Fold All",
                "View",
                _ => Sidebar.SetAllFolds(true),
                enabled: () => Sidebar.CanFoldCurrent && !Sidebar.CurrentViewAllFolded
            ),
            new CommandDef(
                "view.unfold-all",
                "View: Unfold All",
                "View",
                _ => Sidebar.SetAllFolds(false),
                enabled: () => Sidebar.CanFoldCurrent && Sidebar.CurrentViewAllFolded
            ),
            new CommandDef(
                "view.dotfiles",
                "View: Toggle Dotfiles",
                "View",
                _ => Sidebar.ToggleDotfiles(),
                enabled: () => Sidebar.CanToggleDotfiles
            ),
            new CommandDef(
                "view.gitignored",
                "View: Toggle Gitignored",
                "View",
                _ => Sidebar.ToggleGitignored(),
                enabled: () => Sidebar.CanToggleDotfiles
            ),
            new CommandDef(
                "view.toggle-sidebar",
                "View: Toggle Sidebar",
                "View",
                _ => Sidebar.ToggleSidebar()
            ),
            new CommandDef(
                "view.toggle-open-files",
                "View: Toggle Open files",
                "View",
                _ => Sidebar.ToggleOpenFiles()
            ),
            new CommandDef(
                "view.toggle-terminal",
                "View: Toggle Terminal",
                "View",
                _ => WorkspaceHotkeys.Toggle(),
                enabled: Playing
            ),
            new CommandDef(
                "view.screensaver",
                "Screensaver",
                "View",
                _ => Screensaver.Enter(),
                enabled: Playing
            ),
            new CommandDef(
                "view.config",
                "Settings: General",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Config))
            ),
            new CommandDef(
                "view.display",
                "Settings: Display",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Display))
            ),
            new CommandDef(
                "view.storage",
                "Settings: Storage",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Storage))
            ),
            new CommandDef(
                "view.commands",
                "Settings: Commands - Defaults",
                "Settings",
                _ =>
                    ModOptions.OpenCategory(
                        ModOptions.CategoryFor(ModOptions.PageId.CommandDefaults)
                    )
            ),
            new CommandDef(
                "view.command-binaries",
                "Settings: Commands - Binaries",
                "Settings",
                _ =>
                    ModOptions.OpenCategory(
                        ModOptions.CategoryFor(ModOptions.PageId.CommandBinaries)
                    )
            ),
            new CommandDef(
                "view.command-presets",
                "Settings: Commands - Apps",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.AppPresets))
            ),
            new CommandDef(
                "view.terminal-settings",
                "Settings: Appearance - Terminal",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Terminal))
            ),
            new CommandDef(
                "view.workspace-settings",
                "Settings: Appearance - Workspace",
                "Settings",
                _ =>
                    ModOptions.OpenCategory(
                        ModOptions.CategoryFor(ModOptions.PageId.AppearanceWorkspace)
                    )
            ),
            new CommandDef(
                "view.appearance",
                "Settings: Appearance - Interface",
                "Settings",
                _ =>
                    ModOptions.OpenCategory(
                        ModOptions.CategoryFor(ModOptions.PageId.AppearanceInterface)
                    )
            ),
            new CommandDef(
                "view.code-settings",
                "Settings: Appearance - Code",
                "Settings",
                _ =>
                    ModOptions.OpenCategory(
                        ModOptions.CategoryFor(ModOptions.PageId.AppearanceCode)
                    )
            ),
            new CommandDef(
                "view.audio",
                "Settings: Audio",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Audio))
            ),
            new CommandDef(
                "view.credentials",
                "Settings: Integrations - Credentials",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Credentials))
            ),
            new CommandDef(
                "view.usage",
                "Settings: Integrations - Usage",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Usage))
            ),
            new CommandDef(
                "view.summaries",
                "Settings: Agents - Summaries",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Summaries))
            ),
            new CommandDef(
                "view.workers",
                "Settings: Agents - Workers",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Workers))
            ),
            new CommandDef(
                "view.sandbox",
                "Settings: Sandbox",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Sandbox))
            ),
            new CommandDef(
                "view.keyboard-settings",
                "Settings: Keyboard",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.Keyboard))
            ),
            new CommandDef(
                "view.rimworld-settings",
                "Settings: RimWorld",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.RimWorld))
            ),
            new CommandDef(
                "view.about",
                "Settings: About",
                "Settings",
                _ => ModOptions.OpenCategory(ModOptions.CategoryFor(ModOptions.PageId.About))
            ),
            new CommandDef(
                "config.toml",
                "Configuration: Edit config.toml",
                "Configuration",
                _ => ConfigWindow.Open()
            ),
            new CommandDef(
                "view.filter",
                "View: Filter Projects",
                "View",
                v =>
                {
                    if (v != null)
                        Sidebar.ToggleFilter(v);
                },
                subAction: () => FilterSub()
            ),
            new CommandDef(
                "view.agent-status",
                "View: Filter Agents by Status",
                "View",
                SetAgentStatusFromPalette,
                subAction: AgentStatusSub
            ),
            new CommandDef("view.zoom-in", "View: Zoom In", "View", _ => UiScale.Zoom(1)),
            new CommandDef("view.zoom-out", "View: Zoom Out", "View", _ => UiScale.Zoom(-1)),
            new CommandDef(
                "window.fullscreen",
                "Window: Toggle Fullscreen",
                "View",
                _ => LinuxGameWindow.Toggle()
            ),
            new CommandDef(
                "help.shortcuts",
                "Help: Keyboard Shortcuts",
                "Help",
                _ => ShortcutHelpView.Toggle(),
                enabled: () => ShortcutHelpView.CanOpen
            ),
            new CommandDef("jukebox.mute", "Jukebox: Mute", "Jukebox", _ => Radio.ToggleMute()),
            new CommandDef("jukebox.random", "Jukebox: Random", "Jukebox", _ => Radio.PickRandom()),
            new CommandDef(
                "jukebox.tune",
                "Jukebox: Tune",
                "Jukebox",
                _ => { },
                subAction: JukeboxSub
            ),
            new CommandDef(
                "jukebox.recognize",
                "Jukebox: Recognize",
                "Jukebox",
                _ => Radio.Recognize(),
                enabled: () => !Radio.Recognizing
            ),
            new CommandDef(
                "jukebox.cancel-recognition",
                "Jukebox: Cancel recognition",
                "Jukebox",
                _ => Radio.CancelRecognition(),
                enabled: () => Radio.Recognizing
            ),
            new CommandDef("jukebox.like", "Jukebox: Like", "Jukebox", _ => JukeboxActions.Like()),
            new CommandDef(
                "jukebox.history",
                "Jukebox: History",
                "Jukebox",
                _ => JukeboxHistoryView.Open()
            ),
            new CommandDef(
                "jukebox.stop-on-exit",
                "Jukebox: Toggle Stop on Exit",
                "Jukebox",
                _ => Radio.ToggleStopOnExit()
            ),
            new CommandDef(
                "jukebox.edit-likes",
                "Jukebox: Edit Likes",
                "Jukebox",
                _ => JukeboxLikesFile.Edit()
            ),
            new CommandDef(
                "game.new-looks",
                "Game: New looks",
                "Game",
                _ => CoreTip.NewLooks(),
                enabled: Playing
            ),
            new CommandDef(
                "game.kill-something",
                "Game: Kill something",
                "Game",
                _ => CoreTip.KillSomething(),
                enabled: () => Playing() && !Settings.GrandmaMode && !Settings.EcoMode
            ),
            new CommandDef(
                "game.hint",
                "Game: Hint",
                "Game",
                _ => CoreTip.ShowHint(),
                enabled: Playing
            ),
            new CommandDef(
                "game.nextplanet",
                "Game: Next Planet",
                "Game",
                _ => NextPlanet.Begin(),
                enabled: () => Playing() && !Settings.EcoMode
            ),
        };
    }
}
