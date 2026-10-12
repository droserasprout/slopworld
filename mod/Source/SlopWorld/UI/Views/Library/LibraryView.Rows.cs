using System.Collections.Generic;
using UnityEngine;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        // Each row retains its typed catalog snapshot and owns variant-specific presentation
        // and dispatch. Build details only for selection, and tooltips only on hover.
        abstract class Row
        {
            public readonly LibraryRowTarget Target;
            public string Key => Target.Key;
            public readonly string Label;
            public readonly Texture2D Icon;

            protected Row(LibraryRowTarget target, string label, Texture2D icon)
            {
                Target = target;
                Label = label;
                Icon = icon;
            }

            public abstract RowDetails Details();
            public abstract string Tooltip();
        }

        sealed class ItemRow : Row
        {
            readonly LibraryItemInfo _item;

            public ItemRow(LibraryItemInfo item)
                : base(Templates.TryGetValue(item, out var template)
                    ? (LibraryRowTarget)new LibraryRowTarget.Template(template)
                    : new LibraryRowTarget.Item(item), item.Name, Templates.ContainsKey(item) ? Icons.Agents :
                    item.Kind == LibraryItemKind.Shell ? Icons.Terminal :
                    item.Kind == LibraryItemKind.FileAction ? Icons.Files :
                    item.Kind == LibraryItemKind.Breadcrumb ? Icons.Keyboard : Icons.Library)
            {
                _item = item;
            }

            public override RowDetails Details() => PrepareItemDetails(_item, Target.DetailsActions(RowEffects));
            public override string Tooltip()
            {
                var lines = ItemSummary(_item);
                string content = ItemContent(_item);
                if (!string.IsNullOrEmpty(content)) lines.Add(OneLine(content));
                return string.Join("\n", lines);
            }
        }

        sealed class ProjectRow : Row
        {
            readonly ProjectInfo _project;

            public ProjectRow(ProjectInfo project)
                : base(new LibraryRowTarget.Project(project), project.Name, Icons.Files)
            {
                _project = project;
            }

            public override RowDetails Details() => ActionDetails(new List<string>
                { _project.Name, "Project", _project.Dir, ProjectSummary.Of(_project) },
                Target.DetailsActions(RowEffects));
            public override string Tooltip() => _project.Name + "  -  " + _project.Dir;
        }

        sealed class WorktreeRow : Row
        {
            readonly WorktreeEntry _entry;

            public static string TreeName(WorktreeEntry entry) => entry.Tree.Id == "main" ? "main" :
                string.IsNullOrEmpty(entry.Tree.Name) ? entry.Tree.Id : entry.Tree.Name;

            public WorktreeRow(WorktreeEntry entry)
                : base(new LibraryRowTarget.Worktree(entry),
                    entry.Project + "  ·  " + TreeName(entry), Icons.Git)
            {
                _entry = entry;
            }

            public override RowDetails Details() => ActionDetails(WorktreeDetails(_entry), Target.DetailsActions(RowEffects));
            public override string Tooltip() => Label + "\n" + _entry.Tree.Path;
        }

        sealed class SandboxPresetRow : Row
        {
            readonly PresetInfo _preset;

            public SandboxPresetRow(PresetInfo preset)
                : base(new LibraryRowTarget.SandboxPreset(preset),
                    PresetLabel(preset.Name, preset.Source), Icons.Shield)
            {
                _preset = preset;
            }

            public override RowDetails Details() => ActionDetails(SandboxDetails(_preset), Target.DetailsActions(RowEffects));
            public override string Tooltip() => string.Join("\n", SandboxDetails(_preset));
        }

        sealed class AppPresetRow : Row
        {
            readonly CommandInfo _command;

            public AppPresetRow(CommandInfo command)
                : base(new LibraryRowTarget.AppPreset(command),
                    PresetLabel(command.Name, command.Source), Icons.Terminal)
            {
                _command = command;
            }

            public override RowDetails Details() => ActionDetails(AppPresetDetails(_command), Target.DetailsActions(RowEffects));
            public override string Tooltip() => string.Join("\n", AppPresetDetails(_command));
        }
    }
}
