using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        // ------------------------------------------------------------------ clicks
        //
        // Called from AgentSidebar's back pass where Menus is in the agents view and
        // Clicks is in the files/git views.

        public static void Clicks()
        {
            if (!ColonistBarStrip.Interactive) return;

            var e = Event.current;
            if (e.rawType != EventType.MouseDown) return;
            if (e.button != 0 && e.button != 1) return;

            // The "+" is the column's, at the foot of the panel below this body, and
            // AgentSidebar answers it before this is ever asked.
            foreach (var line in Lines)
            {
                // Screen() zeroes a line clipped out of the scroll view, and Rect.zero is
                // nowhere the mouse can be.
                if (!ColonistBarStrip.MouseOver(line.Rect)) continue;

                if (line.Head)
                {
                    // Headings only fold; row variants own their actions.
                    if (e.button == 0)
                    {
                        if (!Folded.Remove(line.Key)) Folded.Add(line.Key);
                    }
                    e.Use();
                    return;
                }

                var row = line.Row;
                if (row == null) continue;
                bool select = row.Item != null || e.button == 0 || HasMainMenu(row);
                if (select)
                {
                    _selection = row.Key;
                    _revealSelection = true;
                }
                if (row.Item != null)
                {
                    AgentSidebar.RememberLibrary(row.Item.Name, Templates.ContainsKey(row.Item));
                    if (e.button == 1) Menu(row.Item);
                }
                else if (e.button == 1)
                    OpenMainMenu(row);
                e.Use();
                return;
            }
        }

        static bool HasMainMenu(Row row) =>
            row.MainKind == MainRowKind.Project || row.MainKind == MainRowKind.Worktree;

        static void OpenMainMenu(Row row)
        {
            if (row.MainKind == MainRowKind.Project)
                ProjectMenu((ProjectInfo)row.Model);
            else if (row.MainKind == MainRowKind.Worktree)
                WorktreeMenu((WorktreeEntry)row.Model);
        }

        // ------------------------------------------------------------------ menus

        static void TemplateMenu(AgentTemplateInfo template)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Create agent", () => TerminalWindow.OpenOverPane(
                    EditSessionDialog.FromTemplate(template))),
                new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template))),
                new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template, true))),
                new FloatMenuOption("Delete", () => TerminalWindow.OpenOverPane(
                    ConfirmDialog.Create("Remove template '" + template.Name + "'? Existing agents keep their snapshots.",
                        () => SessionHub.Instance.Catalog.RemoveAgentTemplate(template, template.Name,
                            null, UiLayout.Fail), destructive: true))),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void RowMenu(LibraryItemInfo s)
        {
            var opts = new List<FloatMenuOption>();

            // Run is the reason ordinary runnable items exist. Breadcrumbs are definitions only.
            if (s.Kind != LibraryItemKind.Breadcrumb && s.Kind != LibraryItemKind.FileAction)
                opts.Add(new FloatMenuOption("Run", () => Run(s)));

            if (Runnable(s) && s.Link == LibraryItemLink.Ask)
                opts.Add(new UiSubmenu("Choose a project", () => WhereOptions(s)));

            var edit = new FloatMenuOption("Edit", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(s)));
            opts.Add(edit);

            var duplicate = new FloatMenuOption("Duplicate", () =>
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.Copy(s)));
            opts.Add(duplicate);

            var name = s.Name;
            opts.Add(new FloatMenuOption("Delete", () =>
                TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                    $"Remove library entry '{name}'? Anything it already started keeps running.",
                    () => SessionHub.Instance.Catalog.RemoveLibraryItem(name, UiLayout.Fail),
                    destructive: true))));

            TerminalWindow.OpenOverPane(new UiMenu(opts));
        }

        // ------------------------------------------------------------------ actions

        // Do not reopen AskWhere after resolving a temporary project: `temp` marks the return
        // path where `project == null` is intentional.
        static void Run(LibraryItemInfo s, string project = null, bool temp = false)
        {
            AgentSidebar.RememberLibrary(s?.Name);
            // An entry that never said where goes through a menu first.
            if (s.Link == LibraryItemLink.Ask && project == null && !temp)
            {
                AskWhere(s);
                return;
            }

            bool scratch = temp || s.Link == LibraryItemLink.Temp;
            SessionHub.Instance.SessionStore.RunLibraryItem(s.Name,
                session => TerminalWindow.Open(session),
                UiLayout.Fail,
                // A project named outright wins. A temporary run has none, whichever of
                // the two said so. Otherwise the entry's own.
                project ?? (scratch ? null : s.Project),
                scratch, Patch_LoadingTips.RandomTips(Patch_LoadingTips.TipBatch));
        }

        // Every project, plus a temporary one - last, being the answer for the run that
        // belongs nowhere in particular. Hung off the row's own menu where there is one, and
        // opened as a menu of its own where the run was asked for from somewhere else.
        static List<FloatMenuOption> WhereOptions(LibraryItemInfo s)
        {
            var options = SessionHub.Instance.Projects
                .Select(p => new FloatMenuOption($"{p.Name}  -  {p.Dir}",
                    () => Run(s, p.Name)))
                .ToList();

            options.Add(new FloatMenuOption(
                $"A temporary project under {ProjectInfo.TempRoot}",
                () => Run(s, null, true)));

            return options;
        }

        static void AskWhere(LibraryItemInfo s) =>
            TerminalWindow.OpenOverPane(new UiMenu(WhereOptions(s)));

        // Where an errand runs, in the few words a row and a tooltip have.
        static string Where(LibraryItemInfo s)
        {
            switch (s.Link)
            {
                case LibraryItemLink.Temp: return "a temporary project";
                case LibraryItemLink.Ask: return "a project you choose at run time";
                default:
                    return string.IsNullOrEmpty(s.Project)
                        ? "no project"
                        : s.Project;
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
            if (info != null) TerminalWindow.OpenOverPane(EditProjectDialog.ForWorktrees(info));
        }

        static void ProjectMenu(ProjectInfo project)
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Edit", () =>
                    TerminalWindow.OpenOverPane(new EditProjectDialog(project))),
                new FloatMenuOption("Duplicate", () =>
                    TerminalWindow.OpenOverPane(EditProjectDialog.Copy(project))),
                new FloatMenuOption("Manage worktrees", () => OpenProjectWorktrees(project.Name)),
                new FloatMenuOption("Terminal (host)", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(project.Name,
                        name => TerminalWindow.Open(name), UiLayout.Fail)),
                new FloatMenuOption("Delete", () => TerminalWindow.OpenOverPane(
                    CatalogActions.RemoveProject(project.Name))),
            };
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void WorktreeMenu(WorktreeEntry entry)
        {
            var tree = entry.Tree;
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("Terminal", () =>
                    SessionHub.Instance.SessionStore.RunHostShell(entry.Project,
                        name => TerminalWindow.Open(name), UiLayout.Fail, tree.Id)),
                new FloatMenuOption("Project", () => OpenProjectWorktrees(entry.Project)),
            };
            if (tree.Id != "main")
                options.Add(new FloatMenuOption("Remove", () => RemoveWorktree(entry)));
            TerminalWindow.OpenOverPane(new UiMenu(options));
        }

        static void RemoveWorktree(WorktreeEntry entry)
        {
            if (entry.Tree.Attachments.Count > 0) return;
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(
                "Remove worktree '" + entry.Tree.Name + "'? This removes managed files. " +
                "It unregisters unmanaged files without deleting them.",
                () =>
                {
                    _worktreeRequestKey = null;
                    DaemonClient.Send<Wire.Ack>("DELETE",
                        WireProtocol.Routes.Worktrees + "/" + Uri.EscapeDataString(entry.Tree.Id) +
                        "?project=" + Uri.EscapeDataString(entry.Project), null,
                        _ => { }, UiLayout.Fail, TaskInfo.Host, 60000);
                }, destructive: true));
        }

        static void RemovePreset(string kind, PresetInfo preset)
        {
            string message = preset.Source == "override"
                ? "Reset this user override and return to the system preset?"
                : "Remove this user preset?";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(message, () =>
                SessionHub.Instance.Catalog.RemovePreset(kind, preset.Name, () =>
                {
                    ClearActionSelection();
                }, UiLayout.Fail), destructive: true));
        }

        static void RemovePreset(string kind, CommandInfo command)
        {
            string message = command.Source == "override"
                ? "Reset this user override and return to the system preset?"
                : "Remove this user preset?";
            TerminalWindow.OpenOverPane(ConfirmDialog.Create(message, () =>
                SessionHub.Instance.Catalog.RemovePreset(kind, command.Name, () =>
                {
                    ClearActionSelection();
                }, UiLayout.Fail), destructive: true));
        }

        static void Edit(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template))
                TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template));
            else
                TerminalWindow.OpenOverPane(EditLibraryItemDialog.ForEdit(item));
        }

        static void Menu(LibraryItemInfo item)
        {
            if (Templates.TryGetValue(item, out var template)) TemplateMenu(template);
            else RowMenu(item);
        }

    }
}
