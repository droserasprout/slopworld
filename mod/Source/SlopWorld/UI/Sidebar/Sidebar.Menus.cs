using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Context menus and menu-backed filtering controls.
    public static partial class Sidebar
    {
        static Rect _visibilityRect;
        static Rect _agentVisibilityRect;

        static void OpenAgentVisibilityMenu()
        {
            var selected = StatusFilter;
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle(
                    "All",
                    selected == AgentStatusFilter.All,
                    () => SetAgentStatusAndReopen(AgentStatusSelection.ToggleAll(StatusFilter))
                ),
                UiLayout.MenuToggle(
                    "Active",
                    (selected & AgentStatusFilter.Active) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Active)
                ),
                UiLayout.MenuToggle(
                    "Idle",
                    (selected & AgentStatusFilter.Idle) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Idle)
                ),
                UiLayout.MenuToggle(
                    "Down",
                    (selected & AgentStatusFilter.Down) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Down)
                ),
            };
            WorkspaceWindow.OpenOverPane(
                UiSelector.Menu(opts, _agentVisibilityRect, matchWidth: false)
            );
        }

        static void SetAgentStatusAndReopen(AgentStatusFilter filter)
        {
            SetStatusFilter(filter);
            OpenAgentVisibilityMenu();
        }

        static void ToggleAgentStatus(AgentStatusFilter status)
        {
            SetStatusFilter(AgentStatusSelection.Toggle(StatusFilter, status));
            OpenAgentVisibilityMenu();
        }

        static void OpenVisibilityMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle(
                    "Dotfiles",
                    Settings.SidebarShowHidden,
                    () =>
                    {
                        ToggleDotfiles();
                        OpenVisibilityMenu();
                    }
                ),
                UiLayout.MenuToggle(
                    "Gitignored",
                    Settings.SidebarShowGitignored,
                    () =>
                    {
                        ToggleGitignored();
                        OpenVisibilityMenu();
                    }
                ),
            };
            WorkspaceWindow.OpenOverPane(UiSelector.Menu(opts, _visibilityRect, matchWidth: false));
        }

        static void Menus()
        {
            if (!ColonistBarStrip.Interactive)
                return;

            var e = Event.current;
            // Fixed chrome runs first. Respect a press it consumed. RawType intentionally
            // survives Use(), and could otherwise be reinterpreted after entering the
            // agent scroll group's local coordinate space.
            if (e.type != EventType.MouseDown)
                return;
            if (e.button != 0 && e.button != 1 && e.button != 2)
                return;

            foreach (var head in Layout.Heads)
            {
                if (!ColonistBarStrip.MouseOver(head.Rect))
                    continue;
                if (e.button == 0)
                    Fold(head.Label, !head.Folded);
                else if (e.button == 1)
                    HeadMenu(head);
                else
                    return;
                e.Use();
                return;
            }

            foreach (var row in Layout.Rows)
            {
                if (row.Session == null)
                    continue;
                if (!ColonistBarStrip.MouseOver(row.Line))
                    continue;

                var info = SnapshotGet(row.Session);
                if (e.button == 2 && info != null)
                {
                    if (info.Alive)
                    {
                        WorkspaceWindow.OpenOverPane(
                            ConfirmDialog.Create(
                                $"Stop '{row.Session}'? This closes the tmux session. "
                                    + "The current work in that session will end.",
                                () =>
                                    SessionHub.Instance.SessionStore.Stop(
                                        row.Session,
                                        UiLayout.Fail
                                    ),
                                destructive: true
                            )
                        );
                    }
                    else
                    {
                        WorkspaceWindow.OpenOverPane(
                            info.Host
                                ? HostTerminalActions.RemoveHost(row.Session)
                                : SessionActions.RemoveSession(row.Session)
                        );
                    }
                    e.Use();
                    return;
                }

                if (e.button != 1)
                    return;
                RowMenu(row.Session, row.Pawn);
                e.Use();
                return;
            }
        }

        // Workspace chrome uses the same menus even when sidebar rows are hidden or filtered.
        internal static void OpenItemMenu(SessionInfo info)
        {
            if (info == null)
                return;
            if (IsRouted(info))
                OpenFileMenu(info);
            else
                RowMenu(info.Name, AgentColony.Current?.PawnOf(info.Name));
        }

        static void RowMenu(string name, Pawn pawn = null)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(name);
            var opts = new List<FloatMenuOption>();

            bool alive = info != null && info.Alive;
            opts.Add(
                new FloatMenuOption(
                    alive ? "Stop" : "Start",
                    () =>
                    {
                        if (alive)
                            hub.SessionStore.Stop(name, UiLayout.Fail);
                        else
                            SessionActions.Start(name);
                    }
                )
            );

            var term = new FloatMenuOption("Terminal", () => WorkspaceWindow.Open(name));
            term.Disabled = !alive;
            opts.Add(term);

            if (info != null)
                opts.Add(
                    new FloatMenuOption(
                        "Edit label",
                        () =>
                        {
                            var current = hub.Get(name);
                            if (current != null)
                                LabelDialog.Open(name, current.Label);
                        }
                    )
                );

            if (DelegateTaskDialog.Eligible(info))
                opts.Add(
                    new FloatMenuOption(
                        "Delegate task",
                        () => WorkspaceWindow.OpenOverPane(new DelegateTaskDialog(name))
                    )
                );

            if (info != null && !info.Host && !string.IsNullOrEmpty(info.Project))
                opts.Add(
                    new FloatMenuOption(
                        "Spawn worker",
                        () =>
                            WorkspaceWindow.OpenOverPane(new SpawnWorkerDialog(name, info.Project))
                    )
                );

            if (info != null && !info.Ephemeral && !info.Host && !info.Worker)
                opts.Add(
                    new FloatMenuOption(
                        "Edit",
                        () => WorkspaceWindow.OpenOverPane(new EditSessionDialog(info))
                    )
                );

            if (info != null && !info.Host && !info.Worker && !string.IsNullOrEmpty(info.Project))
                opts.Add(
                    new FloatMenuOption("Duplicate", () => SessionActions.DuplicateAgent(info))
                );

            if (
                info != null
                && !info.Ephemeral
                && !info.Host
                && !info.Worker
                && !string.IsNullOrEmpty(info.Project)
            )
                opts.Add(new FloatMenuOption("Shell", () => SessionActions.AgentShell(info)));

            if (info != null && !info.Ephemeral && !info.Host)
                opts.Add(new FloatMenuOption("Storage", () => ModOptions.OpenAgentStorage(name)));

            if (info != null && info.Host)
                opts.Add(
                    new FloatMenuOption(
                        "Remove",
                        () =>
                        {
                            WorkspaceWindow.OpenOverPane(HostTerminalActions.RemoveHost(name));
                        }
                    )
                );

            if (info != null && !info.Ephemeral && !info.Host)
                opts.Add(
                    new FloatMenuOption(
                        "Remove",
                        () => WorkspaceWindow.OpenOverPane(SessionActions.RemoveSession(name))
                    )
                );

            // Unlike the core's colony-wide reroll, a row owns one particular agent.
            if (pawn != null)
                opts.Add(
                    new FloatMenuOption(
                        "New look",
                        () =>
                        {
                            if (!pawn.Destroyed)
                                AgentLook.Reroll(pawn);
                        }
                    )
                );

            WorkspaceWindow.OpenOverPane(new UiMenu(opts));
        }

        static void HeadMenu(Head head)
        {
            var hub = SessionHub.Instance;
            var p = hub.Project(head.Label);
            if (p == null)
                return;

            string name = p.Name;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit", () => ProjectActions.EditProject(p)),
                new FloatMenuOption("Duplicate", () => ProjectActions.DuplicateProject(p)),
                new FloatMenuOption("Terminal (host)", () => ProjectActions.ProjectTerminal(p)),
                new FloatMenuOption(
                    "Spawn worker",
                    () => WorkspaceWindow.OpenOverPane(new SpawnWorkerDialog(null, name))
                ),
            };

            int configured = 0;
            foreach (var s in hub.Sessions)
                if (s.Project == name && !s.Ephemeral && !s.Host)
                    configured++;
            int worktrees = SidebarScopes.All(name).Count(scope => scope.Worktree != "main");
            var blockers = new List<string>();
            if (configured > 0)
                blockers.Add($"{configured} configured session{(configured == 1 ? "" : "s")}");
            if (worktrees > 0)
                blockers.Add($"{worktrees} worktree{(worktrees == 1 ? "" : "s")}");
            // Cached counts explain known blockers; the daemon rechecks both stores.
            var del = new FloatMenuOption(
                blockers.Count > 0 ? "Delete (" + string.Join(", ", blockers) + ")" : "Delete",
                () => WorkspaceWindow.OpenOverPane(ProjectActions.RemoveProject(name))
            );
            del.Disabled = blockers.Count > 0;
            opts.Add(del);

            WorkspaceWindow.OpenOverPane(new UiMenu(opts));
        }

        static bool AddClick()
        {
            if (!ColonistBarStrip.Interactive)
                return false;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown || e.button != 0)
                return false;
            if (!ColonistBarStrip.MouseOver(AddHitBar))
                return false;

            e.Use();
            SessionHub.Instance.Catalog.RefreshTemplates();

            WorkspaceWindow.OpenOverPane(new UiMenu(CreationMenu(CreationActions.Add.Children())));
            return true;
        }

        static List<FloatMenuOption> CreationMenu(List<CreationActions.Entry> entries) =>
            entries
                .Select(entry =>
                {
                    if (entry == CreationActions.Entry.Separator)
                        return UiMenu.Separator();
                    FloatMenuOption option =
                        entry.Children == null
                            ? new FloatMenuOption(entry.Label, entry.Select)
                            : new UiSubmenu(entry.Label, () => CreationMenu(entry.Children()));
                    option.Disabled = !entry.Enabled;
                    return option;
                })
                .ToList();

        sealed class ProjectFilterMenu : UiMenu
        {
            int _revision;

            public void RestoreProject(string project) =>
                OpenSubmenu(
                    project
                        + (
                            string.IsNullOrEmpty(SidebarScopes.Error(project))
                                ? ""
                                : " (unavailable)"
                        )
                );

            public ProjectFilterMenu(List<FloatMenuOption> rows, Vector2 at)
                : base(rows, at, selector: true)
            {
                _revision = SidebarScopes.MenuRevision;
            }

            public override void DoWindowContents(Rect rect)
            {
                int revision = SidebarScopes.MenuRevision;
                if (revision != _revision)
                {
                    _revision = revision;
                    ReplaceOptions(FilterOptions());
                    var size = InitialSize;
                    windowRect.width = size.x;
                    windowRect.height = size.y;
                    windowRect.x = Mathf.Clamp(
                        windowRect.x,
                        0f,
                        Mathf.Max(0f, UI.screenWidth - size.x)
                    );
                    windowRect.y = Mathf.Clamp(
                        windowRect.y,
                        0f,
                        Mathf.Max(0f, UI.screenHeight - size.y)
                    );
                }
                base.DoWindowContents(rect);
            }
        }

        public static void OpenFilterMenu() =>
            WorkspaceWindow.OpenOverPane(
                new ProjectFilterMenu(
                    FilterOptions(),
                    new Vector2(_projectFilterAnchor.x, _projectFilterAnchor.yMax)
                )
            );

        static void OpenWorktreeFilterMenu(string project)
        {
            var menu = new ProjectFilterMenu(
                FilterOptions(),
                new Vector2(_projectFilterAnchor.x, _projectFilterAnchor.yMax)
            );
            WorkspaceWindow.OpenOverPane(menu);
            menu.RestoreProject(project);
        }

        static List<FloatMenuOption> FilterOptions()
        {
            _ = SidebarScopes.MenuRevision;
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All projects", !Filtering, () => Tick("")),
            };

            // Ordered the way every view orders its headings. Therefore, the menu and the column
            // under it read down in the same order - the loose one last.
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects)
                names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            foreach (var name in names)
            {
                var key = name;
                var selection = SidebarScopes.Selection(key);
                FloatMenuOption option = SidebarScopes.HasWorktrees(key)
                    ? UiLayout.MenuSubmenu(
                        key,
                        () => WorktreeFilterOptions(key),
                        selection == BrowseScopeSelection.All,
                        selection == BrowseScopeSelection.Some,
                        () => Tick(key)
                    )
                    : UiLayout.MenuToggle(key, Ticked(key), () => Tick(key));
                string error = SidebarScopes.Error(key);
                if (!string.IsNullOrEmpty(error))
                {
                    option.Label += " (unavailable)";
                    option.tooltip = error;
                }
                opts.Add(option);
            }

            opts.Add(UiLayout.MenuToggle(NoProject, Ticked(NoProject), () => Tick(NoProject)));

            return opts;
        }

        static List<FloatMenuOption> WorktreeFilterOptions(string project)
        {
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle(
                    "All worktrees",
                    Passes(project) && SidebarScopes.AllChosen(project),
                    () =>
                    {
                        SidebarScopes.ToggleAll(project);
                        OpenWorktreeFilterMenu(project);
                    }
                ),
            };
            foreach (
                var scope in SidebarScopes
                    .All(project)
                    .OrderBy(s => s.Worktree == "main" ? 0 : 1)
                    .ThenBy(s => s.Name, StringComparer.Ordinal)
            )
            {
                var child = scope;
                string label = child.Worktree == "main" ? "main" : child.Name;
                if (!child.Ready)
                    label +=
                        " ("
                        + ((child.Error ?? "").Contains("missing") ? "missing" : child.Phase)
                        + ")";
                var option = UiLayout.MenuToggle(
                    label,
                    Passes(project) && SidebarScopes.Chosen(child),
                    child.Ready
                        ? (Action)(
                            () =>
                            {
                                // Selecting a hidden project's checkout restores that project and its saved choices.
                                bool hidden = !Passes(project);
                                if (hidden)
                                    ToggleFilter(project);
                                if (!hidden || !SidebarScopes.Chosen(child))
                                    SidebarScopes.Toggle(child);
                                OpenWorktreeFilterMenu(project);
                            }
                        )
                        : null
                );
                option.tooltip =
                    child.Path
                    + (string.IsNullOrEmpty(child.Branch) ? "" : "\n" + child.Branch)
                    + (string.IsNullOrEmpty(child.Error) ? "" : "\n" + child.Error);
                opts.Add(option);
            }
            return opts;
        }

        static void Tick(string key)
        {
            // An empty parent checkbox represents no visible checkouts, including a
            // hidden project. Checking it enables All rather than hiding an empty project.
            if (
                key.Length > 0
                && key != NoProject
                && SidebarScopes.HasWorktrees(key)
                && SidebarScopes.Selection(key) == BrowseScopeSelection.None
            )
                SidebarScopes.SelectAll(key);
            else
                ToggleFilter(key);
            if (
                key.Length > 0
                && key != NoProject
                && Passes(key)
                && !SidebarScopes.HasWorktrees(key)
            )
            {
                var main = SidebarScopes.All(key).FirstOrDefault(s => s.Worktree == "main");
                if (main != null && main.Ready && !SidebarScopes.Chosen(main))
                    SidebarScopes.Toggle(main);
            }
            OpenFilterMenu();
        }
    }
}
