using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        // ------------------------------------------------------------------ clicks
        //
        // Called from Sidebar's back pass where Menus is in the agents view and
        // Clicks is in the files/git views.

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive)
                return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown)
                return;
            if (e.button != 0 && e.button != 1)
                return;

            // The "+" is the column's, at the foot of the panel below this body, and
            // Sidebar answers it before this is ever asked.
            foreach (var line in Lines)
            {
                // Screen() zeroes a line clipped out of the scroll view, and Rect.zero is
                // nowhere the mouse can be.
                if (!ColonistBarStrip.MouseOver(line.Rect))
                    continue;

                if (line.Head)
                {
                    // Headings only fold; row variants own their actions.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key))
                            Folded.Add(line.Key);
                    }
                    e.Use();
                    return;
                }

                var row = line.Row;
                if (row == null)
                    continue;
                if (Selection.Click(row.Target, e.button, RowEffects))
                    _revealSelection = true;
                e.Use();
                return;
            }
        }

        // ------------------------------------------------------------------ menus

        static void TemplateMenu(AgentTemplateInfo template)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "Create agent",
                    () => WorkspaceWindow.OpenOverPane(EditSessionDialog.FromTemplate(template))
                ),
                new FloatMenuOption(
                    "Edit",
                    () => WorkspaceWindow.OpenOverPane(EditSessionDialog.EditTemplate(template))
                ),
                new FloatMenuOption(
                    "Duplicate",
                    () =>
                        WorkspaceWindow.OpenOverPane(EditSessionDialog.EditTemplate(template, true))
                ),
                new FloatMenuOption(
                    "Delete",
                    () =>
                        WorkspaceWindow.OpenOverPane(
                            ConfirmDialog.Create(
                                "Remove template '"
                                    + template.Name
                                    + "'? Existing agents keep their snapshots.",
                                () =>
                                    SessionHub.Instance.Catalog.RemoveAgentTemplate(
                                        template,
                                        template.Name,
                                        null,
                                        UiLayout.Fail
                                    ),
                                destructive: true
                            )
                        )
                ),
            };
            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        static void RowMenu(LibraryItemInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary runnable items exist. Breadcrumbs are definitions only.
            if (s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            if (Runnable(s) && s.Link == LibraryItemLink.Ask)
                opts.Add(new UiSubmenu("Choose a project", () => LibraryActions.WhereOptions(s)));

            var edit = new FloatMenuOption("Edit", () => LibraryActions.Edit(s));
            opts.Add(edit);

            var duplicate = new FloatMenuOption("Duplicate", () => LibraryActions.Duplicate(s));
            opts.Add(duplicate);

            opts.Add(new FloatMenuOption("Delete", () => LibraryActions.Remove(s)));

            WorkspaceWindow.OpenOverPane(new UiMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        static void Run(LibraryItemInfo item)
        {
            Sidebar.RememberLibrary(item?.Name);
            LibraryActions.Run(item);
        }

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(LibraryItemInfo s)
        {
            switch (s.Link)
            {
                case LibraryItemLink.Temp:
                    return "a temporary project";
                case LibraryItemLink.Ask:
                    return "a project you choose at run time";
                default:
                    return string.IsNullOrEmpty(s.Project) ? "no project" : s.Project;
            }
        }

        // The first line of a prompt, which is all a row has space for.
        static string OneLine(string text)
        {
            text = (text ?? "").Replace("\r", "");
            int nl = text.IndexOf('\n');
            return nl < 0 ? text : text.Substring(0, nl) + " ...";
        }

        static void OpenProjectWorktrees(string project)
        {
            var info = SessionHub.Instance.Catalog.Project(project);
            if (info != null)
                WorkspaceWindow.OpenOverPane(EditProjectDialog.ForWorktrees(info));
        }

        static void ProjectMenu(ProjectInfo project)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit", () => ProjectActions.EditProject(project)),
                new FloatMenuOption("Duplicate", () => ProjectActions.DuplicateProject(project)),
                new FloatMenuOption("Manage worktrees", () => OpenProjectWorktrees(project.Name)),
                new FloatMenuOption(
                    "Terminal (host)",
                    () => ProjectActions.ProjectTerminal(project)
                ),
                new FloatMenuOption(
                    "Delete",
                    () => WorkspaceWindow.OpenOverPane(ProjectActions.RemoveProject(project.Name))
                ),
            };
            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        static void WorktreeMenu(WorktreeEntry entry)
        {
            var tree = entry.Tree;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption(
                    "Terminal",
                    () => HostTerminalActions.HostShell(entry.Project, worktree: tree.Id)
                ),
                new FloatMenuOption("Project", () => OpenProjectWorktrees(entry.Project)),
            };
            if (tree.Id != "main")
                options.Add(new FloatMenuOption("Remove", () => RemoveWorktree(entry)));
            WorkspaceWindow.OpenOverPane(new UiMenu(options));
        }

        static void RemoveWorktree(WorktreeEntry entry)
        {
            if (entry.Tree.Attachments.Count > 0)
                return;
            WorkspaceWindow.OpenOverPane(
                ConfirmDialog.Create(
                    "Remove worktree '"
                        + entry.Tree.Name
                        + "'? This removes managed files. "
                        + "It unregisters unmanaged files without deleting them.",
                    () =>
                    {
                        Worktrees.Invalidate();
                        DaemonClient.Send<Wire.Ack>(
                            "DELETE",
                            WireProtocol.Routes.Worktrees
                                + "/"
                                + Uri.EscapeDataString(entry.Tree.Id)
                                + "?project="
                                + Uri.EscapeDataString(entry.Project),
                            null,
                            _ => { },
                            UiLayout.Fail,
                            TaskInfo.Host,
                            60000
                        );
                    },
                    destructive: true
                )
            );
        }

        static void RemovePreset(string kind, PresetInfo preset) =>
            RemovePreset(kind, preset.Name, preset.Source);

        static void RemovePreset(string kind, CommandInfo command) =>
            RemovePreset(kind, command.Name, command.Source);

        static void RemovePreset(string kind, string name, string source)
        {
            string message =
                source == "override"
                    ? "Reset this user override and return to the system preset?"
                    : "Remove this user preset?";
            WorkspaceWindow.OpenOverPane(
                ConfirmDialog.Create(
                    message,
                    () =>
                        SessionHub.Instance.Catalog.RemovePreset(
                            kind,
                            name,
                            ClearActionSelection,
                            UiLayout.Fail
                        ),
                    destructive: true
                )
            );
        }
    }
}
