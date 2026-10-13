using System;
using System.Collections.Generic;
using UnityEngine;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        // Presentation captures the same catalog snapshot as the typed action target.
        // Details and tooltips remain lazy, without a second hierarchy of row variants.
        sealed class Row
        {
            public readonly LibraryRowTarget Target;
            public string Key => Target.Key;
            public readonly string Label;
            public readonly Texture2D Icon;
            public readonly Func<RowDetails> Details;
            public readonly Func<string> Tooltip;

            public Row(
                LibraryRowTarget target,
                string label,
                Texture2D icon,
                Func<RowDetails> details,
                Func<string> tooltip
            )
            {
                Target = target;
                Label = label;
                Icon = icon;
                Details = details;
                Tooltip = tooltip;
            }
        }

        static Row RowFor(LibraryItemInfo item)
        {
            bool isTemplate = Templates.TryGetValue(item, out var template);
            LibraryRowTarget target = isTemplate
                ? (LibraryRowTarget)new LibraryRowTarget.Template(template)
                : new LibraryRowTarget.Item(item);
            var icon =
                isTemplate ? Icons.Agents
                : item.Kind == LibraryItemKind.Shell ? Icons.Terminal
                : item.Kind == LibraryItemKind.FileAction ? Icons.Files
                : item.Kind == LibraryItemKind.Breadcrumb ? Icons.Keyboard
                : Icons.Library;
            return new Row(
                target,
                item.Name,
                icon,
                () => PrepareItemDetails(item, target.DetailsActions(RowEffects)),
                () =>
                {
                    var lines = ItemSummary(item);
                    string content = ItemContent(item);
                    if (!string.IsNullOrEmpty(content))
                        lines.Add(OneLine(content));
                    return string.Join("\n", lines);
                }
            );
        }

        static Row RowFor(ProjectInfo project)
        {
            var target = new LibraryRowTarget.Project(project);
            return new Row(
                target,
                project.Name,
                Icons.Files,
                () =>
                    ActionDetails(
                        new List<string>
                        {
                            project.Name,
                            "Project",
                            project.Dir,
                            ProjectSummary.Of(project),
                        },
                        target.DetailsActions(RowEffects)
                    ),
                () => project.Name + "  -  " + project.Dir
            );
        }

        static string TreeName(WorktreeEntry entry) =>
            entry.Tree.Id == "main" ? "main"
            : string.IsNullOrEmpty(entry.Tree.Name) ? entry.Tree.Id
            : entry.Tree.Name;

        static Row RowFor(WorktreeEntry entry)
        {
            var target = new LibraryRowTarget.Worktree(entry);
            string label = entry.Project + "  ·  " + TreeName(entry);
            return new Row(
                target,
                label,
                Icons.Git,
                () => ActionDetails(WorktreeDetails(entry), target.DetailsActions(RowEffects)),
                () => label + "\n" + entry.Tree.Path
            );
        }

        static Row RowFor(PresetInfo preset)
        {
            var target = new LibraryRowTarget.SandboxPreset(preset);
            return new Row(
                target,
                PresetLabel(preset.Name, preset.Source),
                Icons.Shield,
                () => ActionDetails(SandboxDetails(preset), target.DetailsActions(RowEffects)),
                () => string.Join("\n", SandboxDetails(preset))
            );
        }

        static Row RowFor(CommandInfo command)
        {
            var target = new LibraryRowTarget.AppPreset(command);
            return new Row(
                target,
                PresetLabel(command.Name, command.Source),
                Icons.Terminal,
                () => ActionDetails(AppPresetDetails(command), target.DetailsActions(RowEffects)),
                () => string.Join("\n", AppPresetDetails(command))
            );
        }
    }
}
