using System;
using UnityEngine;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;
using static SlopWorld.LibraryRowTarget;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        static readonly IEffects RowEffects = new LibraryRowEffects();

        // Translate typed row requests into game windows, clipboard writes and catalog actions.
        sealed class LibraryRowEffects : IEffects
        {
            public void Execute(LibraryItemInfo item, ItemAction action)
            {
                switch (action)
                {
                    case ItemAction.Run: Run(item); break;
                    case ItemAction.Edit: LibraryActions.Edit(item); break;
                    case ItemAction.CopyText: GUIUtility.systemCopyBuffer = item.Text ?? ""; break;
                    case ItemAction.Menu: RowMenu(item); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void Execute(AgentTemplateInfo template, TemplateAction action)
            {
                switch (action)
                {
                    case TemplateAction.Create:
                        TerminalWindow.OpenOverPane(EditSessionDialog.FromTemplate(template)); break;
                    case TemplateAction.Edit:
                        TerminalWindow.OpenOverPane(EditSessionDialog.EditTemplate(template)); break;
                    case TemplateAction.Menu: TemplateMenu(template); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void Execute(ProjectInfo project, ProjectAction action)
            {
                switch (action)
                {
                    case ProjectAction.Edit:
                        TerminalWindow.OpenOverPane(new EditProjectDialog(project)); break;
                    case ProjectAction.Worktrees:
                        TerminalWindow.OpenOverPane(EditProjectDialog.ForWorktrees(project)); break;
                    case ProjectAction.Menu: ProjectMenu(project); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void Execute(WorktreeEntry entry, WorktreeAction action)
            {
                switch (action)
                {
                    case WorktreeAction.Terminal:
                        SessionHub.Instance.SessionStore.RunHostShell(entry.Project,
                            name => TerminalWindow.Open(name), UiLayout.Fail, entry.Tree.Id); break;
                    case WorktreeAction.Project: OpenProjectWorktrees(entry.Project); break;
                    case WorktreeAction.Menu: WorktreeMenu(entry); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void Execute(PresetInfo preset, PresetAction action)
            {
                switch (action)
                {
                    case PresetAction.Edit: ModOptions.OpenSandboxPreset(preset.Name); break;
                    case PresetAction.Remove: RemovePreset("sandbox_presets", preset); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void Execute(CommandInfo command, PresetAction action)
            {
                switch (action)
                {
                    case PresetAction.Edit: ModOptions.OpenAppPreset(command.Name); break;
                    case PresetAction.Remove: RemovePreset("app_presets", command); break;
                    default: throw new ArgumentOutOfRangeException(nameof(action));
                }
            }

            public void RememberLibrary(string name, bool template) =>
                AgentSidebar.RememberLibrary(name, template);
        }
    }
}
