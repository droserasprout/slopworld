using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Owns the creation tree, dynamic choices, and launch callbacks shared by the sidebar
    // and palette. Each surface adapts entries to its own rows and navigation.
    internal static class CreationActions
    {
        internal static readonly Entry Project = new Entry(
            "Project",
            () => WorkspaceWindow.OpenOverPane(new EditProjectDialog(null))
        );
        internal static readonly Entry Agent = new Entry("Agent", children: Agents);
        internal static readonly Entry Worker = new Entry("Worker", children: Workers);
        internal static readonly Entry Task = new Entry(
            "Task",
            () => WorkspaceWindow.OpenOverPane(new DelegateTaskDialog(null)),
            enabled: () => SessionHub.Instance.Sessions.Any(DelegateTaskDialog.Eligible)
        );
        internal static readonly Entry Library = new Entry("Library", children: LibraryItems);
        internal static readonly Entry SandboxPreset = new Entry(
            "Sandbox preset",
            ModOptions.OpenNewSandboxPreset
        );
        internal static readonly Entry CommandPreset = new Entry(
            "Command preset",
            ModOptions.OpenNewCommand
        );
        internal static readonly Entry HostShell = new Entry("Host shell", children: HostShells);
        internal static readonly Entry Add = new Entry(
            "Add",
            children: () =>
                new List<Entry>
                {
                    Project,
                    Agent,
                    Worker,
                    Task,
                    Library,
                    SandboxPreset,
                    CommandPreset,
                    HostShell,
                }
        );

        static List<Entry> Agents()
        {
            var entries = new List<Entry>();
            foreach (
                var template in SessionHub.Instance.Templates.OrderBy(
                    t => t.Name,
                    StringComparer.OrdinalIgnoreCase
                )
            )
            {
                // Retain the selected template snapshot even if the catalog refreshes.
                var captured = template.Copy();
                entries.Add(
                    new Entry(
                        template.DisplayLabel,
                        () => WorkspaceWindow.OpenOverPane(EditSessionDialog.FromTemplate(captured))
                    )
                );
            }
            if (entries.Count > 0)
                entries.Add(Entry.Separator);
            entries.Add(
                new Entry("Custom", () => WorkspaceWindow.OpenOverPane(new EditSessionDialog(null)))
            );
            return entries;
        }

        static List<Entry> Workers()
        {
            var entries = new List<Entry>();
            foreach (
                var project in SessionHub
                    .Instance.Projects.Where(p => Sidebar.Passes(p.Name))
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            )
            {
                var name = project.Name;
                entries.Add(
                    new Entry(
                        $"{name}  -  {project.Dir}",
                        () => WorkspaceWindow.OpenOverPane(new SpawnWorkerDialog(null, name))
                    )
                );
            }
            if (entries.Count == 0)
                entries.Add(new Entry("(no projects)"));
            return entries;
        }

        static List<Entry> LibraryItems() =>
            new List<Entry>
            {
                new Entry(
                    "Agent template",
                    () => WorkspaceWindow.OpenOverPane(EditSessionDialog.EditTemplate())
                ),
                LibraryItem("Prompt", LibraryItemKind.Prompt),
                LibraryItem("Breadcrumb", LibraryItemKind.Breadcrumb),
                LibraryItem("Shell script", LibraryItemKind.Shell),
                LibraryItem("File action", LibraryItemKind.FileAction),
            };

        static Entry LibraryItem(string label, LibraryItemKind kind) =>
            new Entry(label, () => WorkspaceWindow.OpenOverPane(EditLibraryItemDialog.New(kind)));

        static List<Entry> HostShells()
        {
            var entries = new List<Entry>
            {
                new Entry("~", () => HostTerminalActions.HostShell("")),
            };
            foreach (var project in SessionHub.Instance.Projects)
            {
                if (!Sidebar.Passes(project.Name))
                    continue;
                var name = project.Name;
                entries.Add(
                    new Entry(
                        $"{name}  -  {project.Dir}",
                        () => HostTerminalActions.HostShell(name)
                    )
                );
            }
            return entries;
        }

        internal sealed class Entry
        {
            internal static readonly Entry Separator = new Entry(null);

            internal readonly string Label;
            internal readonly Action Select;
            internal readonly Func<List<Entry>> Children;
            readonly Func<bool> _enabled;

            internal Entry(
                string label,
                Action select = null,
                Func<List<Entry>> children = null,
                Func<bool> enabled = null
            )
            {
                Label = label;
                Select = select;
                Children = children;
                _enabled = enabled;
            }

            internal bool Enabled =>
                (Select != null || Children != null) && (_enabled == null || _enabled());
        }
    }
}
