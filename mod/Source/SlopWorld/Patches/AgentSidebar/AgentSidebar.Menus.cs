using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Context menus and menu-backed filtering controls.
    public static partial class AgentSidebar
    {
        static Rect _visibilityRect;
        static Rect _agentVisibilityRect;

        static void OpenAgentVisibilityMenu()
        {
            var selected = StatusFilter;
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All", selected == AgentStatusFilter.All,
                    () => SetAgentStatusAndReopen(AgentStatusFilter.All)),
                UiLayout.MenuToggle("Active", (selected & AgentStatusFilter.Active) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Active)),
                UiLayout.MenuToggle("Idle", (selected & AgentStatusFilter.Idle) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Idle)),
                UiLayout.MenuToggle("Down", (selected & AgentStatusFilter.Down) != 0,
                    () => ToggleAgentStatus(AgentStatusFilter.Down)),
            };
            TerminalWindow.OpenOverPane(
                new UiMenu(opts, new Vector2(_agentVisibilityRect.x, _agentVisibilityRect.yMax)));
        }

        static void SetAgentStatusAndReopen(AgentStatusFilter filter)
        {
            SetStatusFilter(filter);
            OpenAgentVisibilityMenu();
        }

        static void ToggleAgentStatus(AgentStatusFilter status)
        {
            var selected = StatusFilter;
            var next = selected == AgentStatusFilter.All
                ? status
                : (selected & status) != 0 ? selected & ~status : selected | status;

            SetStatusFilter(next);
            OpenAgentVisibilityMenu();
        }

        static void OpenVisibilityMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("Dotfiles", Settings.SidebarShowHidden,
                    () => { ToggleDotfiles(); OpenVisibilityMenu(); }),
                UiLayout.MenuToggle("Gitignored", Settings.SidebarShowGitignored,
                    () => { ToggleGitignored(); OpenVisibilityMenu(); }),
            };
            TerminalWindow.OpenOverPane(
                new UiMenu(opts, new Vector2(_visibilityRect.x, _visibilityRect.yMax)));
        }

        static void Menus()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            // Fixed chrome runs first. Respect a press it consumed. RawType intentionally
            // survives Use(), and could otherwise be reinterpreted after entering the
            // agent scroll group's local coordinate space.
            if (e.type != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1 && e.button != 2) return;

            foreach (var head in Layout.Heads)
            {
                if (!ColonistBarStrip.MouseOver(head.Rect)) continue;
                if (e.button == 0) Fold(head.Label, !head.Folded);
                else if (e.button == 1) HeadMenu(head);
                else return;
                e.Use();
                return;
            }

            foreach (var row in Layout.Rows)
            {
                if (row.Session == null) continue;
                if (!ColonistBarStrip.MouseOver(row.Line)) continue;

                var info = SnapshotGet(row.Session);
                if (e.button == 2 && info != null)
                {
                    if (info.Alive)
                    {
                        TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                            $"Stop '{row.Session}'? This closes the tmux session. " +
                            "The current work in that session will end.",
                            () => SessionHub.Instance.SessionStore.Stop(row.Session, UiLayout.Fail),
                            destructive: true));
                    }
                    else
                    {
                        TerminalWindow.OpenOverPane(info.Host
                            ? CatalogActions.RemoveHost(row.Session)
                            : CatalogActions.RemoveSession(row.Session));
                    }
                    e.Use();
                    return;
                }

                if (e.button != 1) return;
                RowMenu(row.Session, row.Pawn);
                e.Use();
                return;
            }
        }

        static void RowMenu(string name, Pawn pawn = null)
        {
            var hub = SessionHub.Instance;
            var info = hub.Get(name);
            var opts = new List<FloatMenuOption>();

            bool alive = info != null && info.Alive;
            opts.Add(new FloatMenuOption(alive ? "Stop" : "Start", () =>
            {
                if (alive) hub.SessionStore.Stop(name, UiLayout.Fail);
                else hub.SessionStore.Start(name, UiLayout.Fail);
            }));

            var term = new FloatMenuOption("Terminal", () => TerminalWindow.Open(name));
            term.Disabled = !alive;
            opts.Add(term);

            if (info != null)
                opts.Add(new FloatMenuOption("Edit label", () =>
                {
                    var current = hub.Get(name);
                    if (current != null) LabelDialog.Open(name, current.Label);
                }));

            if (info != null && !info.Host)
                opts.Add(new FloatMenuOption("Delegate task", () =>
                    TerminalWindow.OpenOverPane(new DelegateTaskDialog(name))));

            if (info != null && !info.Host && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Spawn worker",
                    () => TerminalWindow.OpenOverPane(new SpawnWorkerDialog(name, info.Project))));

            if (info != null && !info.Ephemeral && !info.Host && !info.Worker)
                opts.Add(new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(new EditSessionDialog(info))));

            if (info != null && !info.Host && !info.Worker && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info))));

            if (info != null && !info.Host && !info.Worker && !string.IsNullOrEmpty(info.Project))
                opts.Add(new FloatMenuOption("Shell", () =>
                    hub.SessionStore.Run(info.Project, "", "", session => TerminalWindow.Open(session),
                        UiLayout.Fail, like: name)));

            if (info != null && !info.Ephemeral && !info.Host)
                opts.Add(new FloatMenuOption("Storage", () => StoragePage.FocusAgent(name)));

            if (info != null && info.Host)
                opts.Add(new FloatMenuOption("Remove", () =>
                {
                    TerminalWindow.OpenOverPane(CatalogActions.RemoveHost(name));
                }));

            if (info != null && !info.Ephemeral && !info.Host)
                opts.Add(new FloatMenuOption("Remove", () =>
                    TerminalWindow.OpenOverPane(CatalogActions.RemoveSession(name))));

            // Unlike the core's colony-wide reroll, a row owns one particular agent.
            if (pawn != null)
                opts.Add(new FloatMenuOption("New look", () =>
                {
                    if (!pawn.Destroyed) AgentLook.Reroll(pawn);
                }));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        static void HeadMenu(Head head)
        {
            var hub = SessionHub.Instance;
            var p = hub.Project(head.Label);
            if (p == null) return;

            string name = p.Name;
            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(p))),
                new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(p))),
                new FloatMenuOption("Terminal (host)", () =>
                    hub.SessionStore.RunHostShell(name, session => TerminalWindow.Open(session),
                        UiLayout.Fail)),
                new FloatMenuOption("Spawn worker",
                    () => TerminalWindow.OpenOverPane(new SpawnWorkerDialog(null, name))),
            };

            int agents = 0;
            foreach (var s in hub.Sessions)
                if (s.Project == name) agents++;

            var del = new FloatMenuOption(
                agents > 0 ? $"Delete ({agents} agent{(agents == 1 ? "" : "s")})" : "Delete",
                () => TerminalWindow.OpenOverPane(CatalogActions.RemoveProject(name)));
            del.Disabled = agents > 0;
            opts.Add(del);

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        static bool AddClick()
        {
            if (!ColonistBarStrip.Interactive) return false;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown || e.button != 0) return false;
            if (!ColonistBarStrip.MouseOver(AddHitBar)) return false;

            e.Use();
            SessionHub.Instance.Catalog.RefreshTemplates();

            var opts = new List<FloatMenuOption>
            {
                new FloatMenuOption("Project", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(null))),
                new UiSubmenu("Agent", AgentCreationOptions),
                new UiSubmenu("Worker", WorkerOptions),
                new FloatMenuOption("Task", () =>
                    TerminalWindow.OpenOverPane(new DelegateTaskDialog(null))),
                new UiSubmenu("Library", LibraryItemOptions),
                new FloatMenuOption("Sandbox preset", ModOptions.OpenNewSandboxPreset),
                new FloatMenuOption("Command preset", ModOptions.OpenNewCommand),
                new UiSubmenu("Host shell", HostShellOptions),
            };
            TerminalWindow.OpenOverPane(new UiMenu(opts));
            return true;
        }

        static List<FloatMenuOption> AgentCreationOptions()
        {
            var options = new List<FloatMenuOption>();
            foreach (var template in SessionHub.Instance.Templates.OrderBy(t => t.Name,
                System.StringComparer.OrdinalIgnoreCase))
            {
                var captured = template.Copy();
                options.Add(new FloatMenuOption(template.DisplayLabel, () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.FromTemplate(captured))));
            }
            if (options.Count > 0) options.Add(UiMenu.Separator());
            options.Add(new FloatMenuOption("Custom", () =>
                TerminalWindow.OpenOverPane(new EditSessionDialog(null))));
            return options;
        }

        static List<FloatMenuOption> WorkerOptions()
        {
            var options = new List<FloatMenuOption>();
            foreach (var project in SessionHub.Instance.Projects
                .Where(p => Passes(p.Name))
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                var captured = project.Name;
                options.Add(new FloatMenuOption($"{project.Name}  -  {project.Dir}", () =>
                    TerminalWindow.OpenOverPane(new SpawnWorkerDialog(null, captured))));
            }
            if (options.Count == 0)
                options.Add(new FloatMenuOption("(no projects)", null));
            return options;
        }

        static List<FloatMenuOption> LibraryItemOptions() => new List<FloatMenuOption>
        {
            new FloatMenuOption("Agent template", () =>
                TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate())),
            new FloatMenuOption("Prompt", () =>
                TerminalWindow.OpenOverPane(new EditPromptDialog(null))),
            BreadcrumbAddOption(),
            new FloatMenuOption("Shell script", () =>
                TerminalWindow.OpenOverPane(new EditShellDialog(null))),
            new FloatMenuOption("File action", () =>
                TerminalWindow.OpenOverPane(new EditFileActionDialog(null))),
        };

        static FloatMenuOption BreadcrumbAddOption()
        {
            var option = new FloatMenuOption("Breadcrumb", () =>
                TerminalWindow.OpenOverPane(new EditBreadcrumbDialog(null)));
            return option;
        }

        static List<FloatMenuOption> HostShellOptions()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("~", () =>
                    SessionHub.Instance.SessionStore.RunHostShell("",
                        session => TerminalWindow.Open(session), UiLayout.Fail)),
            };
            foreach (var p in SessionHub.Instance.Projects)
            {
                if (!Passes(p.Name)) continue;
                string name = p.Name;
                options.Add(new FloatMenuOption($"{name}  -  {p.Dir}", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(name,
                        session => TerminalWindow.Open(session), UiLayout.Fail)));
            }
            return options;
        }

        public static void OpenFilterMenu()
        {
            var opts = new List<FloatMenuOption>
            {
                UiLayout.MenuToggle("All projects", !Filtering, () => Tick("")),
            };

            // Ordered the way every view orders its headings. Therefore, the menu and the column
            // under it read down in the same order - the loose one last.
            var names = new List<string>();
            foreach (var p in SessionHub.Instance.Projects) names.Add(p.Name);
            names.Sort(System.StringComparer.Ordinal);
            foreach (var name in names)
            {
                var key = name;
                opts.Add(UiLayout.MenuToggle(key, Ticked(key), () => Tick(key)));
            }

            opts.Add(UiLayout.MenuToggle(NoProject, Ticked(NoProject),
                () => Tick(NoProject)));

            TerminalWindow.OpenOverPane(
                new UiMenu(opts, new Vector2(FilterRect.x, FilterRect.yMax)));
        }

        static void Tick(string key)
        {
            ToggleFilter(key);
            OpenFilterMenu();
        }
    }
}
