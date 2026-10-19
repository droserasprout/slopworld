namespace SlopWorld
{
    // Project edit, duplication, removal, and host-terminal entry points.
    static class ProjectActions
    {
        public static void EditProject(ProjectInfo project) =>
            WorkspaceWindow.OpenOverPane(new EditProjectDialog(project));

        public static void DuplicateProject(ProjectInfo project) =>
            WorkspaceWindow.OpenOverPane(EditProjectDialog.Copy(project));

        public static void ProjectTerminal(ProjectInfo project) =>
            HostTerminalActions.HostShell(project.Name);

        public static Verse.Window RemoveProject(string name) =>
            ConfirmDialog.Create(
                $"Remove project '{name}'? This keeps the directory. It removes only the entry in config.toml.",
                () => SessionHub.Instance.Catalog.RemoveProject(name, UiLayout.Fail),
                destructive: true
            );
    }
}
