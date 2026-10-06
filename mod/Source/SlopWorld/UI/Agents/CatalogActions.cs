namespace SlopWorld
{
    // Callers retain visibility rules and window placement. Shared actions own their failure UI.
    static class CatalogActions
    {
        static void StartFailed(string name, string message) =>
            TerminalWindow.OpenOverPane(AlertDialog.Create("Session failed to start",
                $"Could not start '{name}':\n{message}", "OK", null,
                primaryKind: UiTheme.Btn.Danger));

        public static void Start(string name) =>
            SessionHub.Instance.SessionStore.Start(name, message => StartFailed(name, message));

        public static void Restart(string name) =>
            SessionHub.Instance.SessionStore.Restart(name, message => StartFailed(name, message));

        public static void AgentShell(SessionInfo info)
        {
            if (!string.IsNullOrEmpty(info.Project))
                SessionHub.Instance.SessionStore.Run(info.Project, "", "", session => TerminalWindow.Open(session),
                    UiLayout.Fail, options: new SessionRunOptions { Like = info.Name });
        }

        public static void DuplicateAgent(SessionInfo info)
        {
            if (!string.IsNullOrEmpty(info.Project)) TerminalWindow.OpenOverPane(EditSessionDialog.Copy(info));
        }

        public static void EditProject(ProjectInfo project) =>
            TerminalWindow.OpenOverPane(new EditProjectDialog(project));
        public static void DuplicateProject(ProjectInfo project) =>
            TerminalWindow.OpenOverPane(EditProjectDialog.Copy(project));
        public static void ProjectTerminal(ProjectInfo project) =>
            SessionHub.Instance.SessionStore.RunHostShell(project.Name, session => TerminalWindow.Open(session), UiLayout.Fail);

        public static Verse.Window ResetState(string name) => ConfirmDialog.Create(
            $"Reset private state for '{name}'? This stops the agent and gives its tools " +
            "a fresh state on next start. The old state stays recoverable for 14 days.",
            () => SessionHub.Instance.SessionStore.ResetState(name, UiLayout.Fail), destructive: true);

        public static Verse.Window RemoveSession(string name) => ConfirmDialog.Create(
            $"Remove session '{name}'? This kills it, drops it from config.toml, and moves " +
            "its private state to recoverable trash for 14 days.",
            () => SessionHub.Instance.SessionStore.Remove(name, UiLayout.Fail), destructive: true);

        public static Verse.Window RemoveHost(string name) => ConfirmDialog.Create(
            $"Remove {SessionHub.Instance.Capabilities.TerminalName} '{name}'? This kills its tmux pane and forgets " +
            "the saved sidebar tab.",
            () => SessionHub.Instance.SessionStore.Remove(name, UiLayout.Fail), destructive: true);

        public static Verse.Window RemoveProject(string name) => ConfirmDialog.Create(
            $"Remove project '{name}'? This keeps the directory. It removes only the entry in config.toml.",
            () => SessionHub.Instance.Catalog.RemoveProject(name, UiLayout.Fail), destructive: true);
    }
}
