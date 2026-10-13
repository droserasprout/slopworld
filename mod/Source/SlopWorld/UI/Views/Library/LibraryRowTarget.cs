using System;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    // Catalog targets own action availability and identity. The view supplies game effects;
    // deferred actions retain the target that was selected, rather than resolving it again.
    internal abstract class LibraryRowTarget
    {
        internal interface IEffects
        {
            void Run(LibraryItemInfo target);
            void Edit(LibraryItemInfo target);
            void CopyText(LibraryItemInfo target);
            void Menu(LibraryItemInfo target);
            void Create(AgentTemplateInfo target);
            void Edit(AgentTemplateInfo target);
            void Menu(AgentTemplateInfo target);
            void Edit(ProjectInfo target);
            void Worktrees(ProjectInfo target);
            void Menu(ProjectInfo target);
            void Terminal(WorktreeEntry target);
            void Project(WorktreeEntry target);
            void Menu(WorktreeEntry target);
            void Edit(PresetInfo target);
            void Remove(PresetInfo target);
            void Edit(CommandInfo target);
            void Remove(CommandInfo target);
            void RememberLibrary(string name, bool template);
        }

        internal sealed class Actions
        {
            internal readonly string PrimaryLabel,
                SecondaryLabel;
            internal readonly Action Primary,
                Secondary,
                More;

            internal Actions(
                string primaryLabel,
                Action primary,
                string secondaryLabel = null,
                Action secondary = null,
                Action more = null
            )
            {
                PrimaryLabel = primaryLabel;
                Primary = primary;
                SecondaryLabel = secondaryLabel;
                Secondary = secondary;
                More = more;
            }
        }

        internal string Key { get; }

        protected LibraryRowTarget(string key)
        {
            Key = key;
        }

        internal abstract Actions DetailsActions(IEffects effects);
        internal virtual bool HasMenu => false;

        internal virtual void OpenMenu(IEffects effects) { }

        internal virtual void Remember(IEffects effects) { }

        internal sealed class Item : LibraryRowTarget
        {
            readonly LibraryItemInfo _item;

            internal Item(LibraryItemInfo item)
                : base("item:" + item.Name)
            {
                _item = item;
            }

            internal override Actions DetailsActions(IEffects effects)
            {
                bool runnable =
                    _item.Kind == LibraryItemKind.Prompt || _item.Kind == LibraryItemKind.Shell;
                string primaryLabel = runnable
                    ? (_item.Link == LibraryItemLink.Ask ? "Choose a project" : "Run")
                    : "Edit";
                string secondaryLabel =
                    runnable ? "Edit"
                    : _item.Kind == LibraryItemKind.Breadcrumb ? "Copy text"
                    : null;
                Action secondary =
                    secondaryLabel == null
                        ? null
                        : () =>
                        {
                            if (runnable)
                                effects.Edit(_item);
                            else
                                effects.CopyText(_item);
                        };
                return new Actions(
                    primaryLabel,
                    () =>
                    {
                        if (runnable)
                            effects.Run(_item);
                        else
                            effects.Edit(_item);
                    },
                    secondaryLabel,
                    secondary,
                    () => OpenMenu(effects)
                );
            }

            internal override bool HasMenu => true;

            internal override void OpenMenu(IEffects effects) => effects.Menu(_item);

            internal override void Remember(IEffects effects) =>
                effects.RememberLibrary(_item.Name, false);
        }

        internal sealed class Template : LibraryRowTarget
        {
            readonly AgentTemplateInfo _template;

            internal Template(AgentTemplateInfo template)
                : base("template:" + template.Name)
            {
                _template = template;
            }

            internal override Actions DetailsActions(IEffects effects) =>
                new Actions(
                    "Create agent",
                    () => effects.Create(_template),
                    "Edit",
                    () => effects.Edit(_template),
                    () => OpenMenu(effects)
                );

            internal override bool HasMenu => true;

            internal override void OpenMenu(IEffects effects) => effects.Menu(_template);

            internal override void Remember(IEffects effects) =>
                effects.RememberLibrary(_template.Name, true);
        }

        internal sealed class Project : LibraryRowTarget
        {
            readonly ProjectInfo _project;

            internal Project(ProjectInfo project)
                : base("action:project:" + project.Name)
            {
                _project = project;
            }

            internal override Actions DetailsActions(IEffects effects) =>
                new Actions(
                    "Edit",
                    () => effects.Edit(_project),
                    "Worktrees",
                    () => effects.Worktrees(_project),
                    () => OpenMenu(effects)
                );

            internal override bool HasMenu => true;

            internal override void OpenMenu(IEffects effects) => effects.Menu(_project);
        }

        internal sealed class Worktree : LibraryRowTarget
        {
            readonly WorktreeEntry _entry;

            internal Worktree(WorktreeEntry entry)
                : base("action:worktree:" + entry.Project + ":" + entry.Tree.Id)
            {
                _entry = entry;
            }

            internal override Actions DetailsActions(IEffects effects) =>
                new Actions(
                    "Terminal",
                    () => effects.Terminal(_entry),
                    "Project",
                    () => effects.Project(_entry),
                    () => OpenMenu(effects)
                );

            internal override bool HasMenu => true;

            internal override void OpenMenu(IEffects effects) => effects.Menu(_entry);
        }

        internal sealed class SandboxPreset : LibraryRowTarget
        {
            readonly PresetInfo _preset;

            internal SandboxPreset(PresetInfo preset)
                : base("action:sandbox:" + preset.Name)
            {
                _preset = preset;
            }

            internal override Actions DetailsActions(IEffects effects) =>
                new Actions(
                    "Edit",
                    () => effects.Edit(_preset),
                    _preset.Source == "override" ? "Reset" : "Remove",
                    () => effects.Remove(_preset)
                );
        }

        internal sealed class AppPreset : LibraryRowTarget
        {
            readonly CommandInfo _command;

            internal AppPreset(CommandInfo command)
                : base("action:app:" + command.Name)
            {
                _command = command;
            }

            internal override Actions DetailsActions(IEffects effects) =>
                new Actions(
                    "Edit",
                    () => effects.Edit(_command),
                    _command.Source == "override" ? "Reset" : "Remove",
                    () => effects.Remove(_command)
                );
        }
    }
}
