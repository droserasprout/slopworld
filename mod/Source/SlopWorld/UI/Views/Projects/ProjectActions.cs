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
                $"Remove project '{name}'? Its board will be archived with its cards. The working directory is kept.",
                () => SessionHub.Instance.Catalog.RemoveProject(name, UiLayout.Fail),
                destructive: true
            );
    }
}
