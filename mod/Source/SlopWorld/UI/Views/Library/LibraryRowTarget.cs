using System;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    // Catalog targets own action availability and identity. The view supplies game effects;
    // deferred actions retain the target that was selected, rather than resolving it again.
    internal abstract class LibraryRowTarget
    {
        internal enum ItemAction { Run, Edit, CopyText, Menu }
        internal enum TemplateAction { Create, Edit, Menu }
        internal enum ProjectAction { Edit, Worktrees, Menu }
        internal enum WorktreeAction { Terminal, Project, Menu }
        internal enum PresetAction { Edit, Remove }

        internal interface IEffects
        {
            void Execute(LibraryItemInfo item, ItemAction action);
            void Execute(AgentTemplateInfo template, TemplateAction action);
            void Execute(ProjectInfo project, ProjectAction action);
            void Execute(WorktreeEntry entry, WorktreeAction action);
            void Execute(PresetInfo preset, PresetAction action);
            void Execute(CommandInfo command, PresetAction action);
            void RememberLibrary(string name, bool template);
        }

        internal sealed class Actions
        {
            internal readonly string PrimaryLabel, SecondaryLabel;
            internal readonly Action Primary, Secondary, More;

            internal Actions(string primaryLabel, Action primary,
                string secondaryLabel = null, Action secondary = null, Action more = null)
            {
                PrimaryLabel = primaryLabel;
                Primary = primary;
                SecondaryLabel = secondaryLabel;
                Secondary = secondary;
                More = more;
            }
        }

        internal string Key { get; }
        protected LibraryRowTarget(string key) { Key = key; }
        internal abstract Actions DetailsActions(IEffects effects);
        internal virtual bool HasMenu => false;
        internal virtual void OpenMenu(IEffects effects) { }
        internal virtual void Remember(IEffects effects) { }

        internal sealed class Item : LibraryRowTarget
        {
            readonly LibraryItemInfo _item;
            internal Item(LibraryItemInfo item) : base("item:" + item.Name) { _item = item; }

            internal override Actions DetailsActions(IEffects effects)
            {
                bool runnable = _item.Kind == LibraryItemKind.Prompt || _item.Kind == LibraryItemKind.Shell;
                string primaryLabel = runnable
                    ? (_item.Link == LibraryItemLink.Ask ? "Choose a project" : "Run") : "Edit";
                string secondaryLabel = runnable ? "Edit" :
                    _item.Kind == LibraryItemKind.Breadcrumb ? "Copy text" : null;
                Action secondary = secondaryLabel == null ? null :
                    () => effects.Execute(_item, runnable ? ItemAction.Edit : ItemAction.CopyText);
                return new Actions(primaryLabel,
                    () => effects.Execute(_item, runnable ? ItemAction.Run : ItemAction.Edit),
                    secondaryLabel, secondary, () => OpenMenu(effects));
            }

            internal override bool HasMenu => true;
            internal override void OpenMenu(IEffects effects) => effects.Execute(_item, ItemAction.Menu);
            internal override void Remember(IEffects effects) => effects.RememberLibrary(_item.Name, false);
        }

        internal sealed class Template : LibraryRowTarget
        {
            readonly AgentTemplateInfo _template;
            internal Template(AgentTemplateInfo template) : base("template:" + template.Name)
            { _template = template; }

            internal override Actions DetailsActions(IEffects effects) => new Actions(
                "Create agent", () => effects.Execute(_template, TemplateAction.Create),
                "Edit", () => effects.Execute(_template, TemplateAction.Edit), () => OpenMenu(effects));
            internal override bool HasMenu => true;
            internal override void OpenMenu(IEffects effects) => effects.Execute(_template, TemplateAction.Menu);
            internal override void Remember(IEffects effects) => effects.RememberLibrary(_template.Name, true);
        }

        internal sealed class Project : LibraryRowTarget
        {
            readonly ProjectInfo _project;
            internal Project(ProjectInfo project) : base("action:project:" + project.Name)
            { _project = project; }

            internal override Actions DetailsActions(IEffects effects) => new Actions(
                "Edit", () => effects.Execute(_project, ProjectAction.Edit),
                "Worktrees", () => effects.Execute(_project, ProjectAction.Worktrees), () => OpenMenu(effects));
            internal override bool HasMenu => true;
            internal override void OpenMenu(IEffects effects) => effects.Execute(_project, ProjectAction.Menu);
        }

        internal sealed class Worktree : LibraryRowTarget
        {
            readonly WorktreeEntry _entry;
            internal Worktree(WorktreeEntry entry)
                : base("action:worktree:" + entry.Project + ":" + entry.Tree.Id) { _entry = entry; }

            internal override Actions DetailsActions(IEffects effects) => new Actions(
                "Terminal", () => effects.Execute(_entry, WorktreeAction.Terminal),
                "Project", () => effects.Execute(_entry, WorktreeAction.Project), () => OpenMenu(effects));
            internal override bool HasMenu => true;
            internal override void OpenMenu(IEffects effects) => effects.Execute(_entry, WorktreeAction.Menu);
        }

        internal sealed class SandboxPreset : LibraryRowTarget
        {
            readonly PresetInfo _preset;
            internal SandboxPreset(PresetInfo preset) : base("action:sandbox:" + preset.Name)
            { _preset = preset; }

            internal override Actions DetailsActions(IEffects effects) => new Actions(
                "Edit", () => effects.Execute(_preset, PresetAction.Edit),
                _preset.Source == "override" ? "Reset" : "Remove",
                () => effects.Execute(_preset, PresetAction.Remove));
        }

        internal sealed class AppPreset : LibraryRowTarget
        {
            readonly CommandInfo _command;
            internal AppPreset(CommandInfo command) : base("action:app:" + command.Name)
            { _command = command; }

            internal override Actions DetailsActions(IEffects effects) => new Actions(
                "Edit", () => effects.Execute(_command, PresetAction.Edit),
                _command.Source == "override" ? "Reset" : "Remove",
                () => effects.Execute(_command, PresetAction.Remove));
        }
    }
}
