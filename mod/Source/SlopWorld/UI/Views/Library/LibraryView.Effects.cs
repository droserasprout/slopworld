using UnityEngine;
using static SlopWorld.LibraryRowTarget;
using WorktreeEntry = SlopWorld.WorktreeInventory.WorktreeEntry;

namespace SlopWorld
{
    public static partial class LibraryView
    {
        static readonly IEffects RowEffects = new LibraryRowEffects();

        // Translate typed row requests into game windows, clipboard writes and catalog actions.
        sealed class LibraryRowEffects : IEffects
        {
            public void Run(LibraryItemInfo item) => LibraryView.Run(item);

            public void Edit(LibraryItemInfo item) => LibraryActions.Edit(item);

            public void CopyText(LibraryItemInfo item) =>
                GUIUtility.systemCopyBuffer = item.Text ?? "";

            public void Menu(LibraryItemInfo item) => RowMenu(item);

            public void Create(AgentTemplateInfo template) =>
                WorkspaceWindow.OpenOverPane(EditSessionDialog.FromTemplate(template));

            public void Edit(AgentTemplateInfo template) =>
                WorkspaceWindow.OpenOverPane(EditSessionDialog.EditTemplate(template));

            public void Menu(AgentTemplateInfo template) => TemplateMenu(template);

            public void Edit(ProjectInfo project) =>
                WorkspaceWindow.OpenOverPane(new EditProjectDialog(project));

            public void Worktrees(ProjectInfo project) =>
                WorkspaceWindow.OpenOverPane(EditProjectDialog.ForWorktrees(project));

            public void Menu(ProjectInfo project) => ProjectMenu(project);

            public void Terminal(WorktreeEntry entry) =>
                HostTerminalActions.HostShell(entry.Project, worktree: entry.Tree.Id);

            public void Project(WorktreeEntry entry) => OpenProjectWorktrees(entry.Project);

            public void Menu(WorktreeEntry entry) => WorktreeMenu(entry);

            public void Edit(PresetInfo preset) => ModOptions.OpenSandboxPreset(preset.Name);

            public void Remove(PresetInfo preset) => RemovePreset("sandbox_presets", preset);

            public void Edit(CommandInfo command) => ModOptions.OpenAppPreset(command.Name);

            public void Remove(CommandInfo command) => RemovePreset("app_presets", command);

            public void RememberLibrary(string name, bool template) =>
                Sidebar.RememberLibrary(name, template);
        }
    }
}
