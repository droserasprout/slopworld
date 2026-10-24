namespace SlopWorld
{
    // Session lifecycle, duplication, and agent-shell actions with shared failure UI.
    static class SessionActions
    {
        static void StartFailed(string name, string message) =>
            WorkspaceWindow.OpenOverPane(
                AlertDialog.Create(
                    "Session failed to start",
                    $"Could not start '{name}':\n{message}",
                    "OK",
                    null,
                    primaryKind: UiTheme.Btn.Danger
                )
            );

        public static void Start(string name) =>
            SessionHub.Instance.SessionStore.Start(name, message => StartFailed(name, message));

        public static void Restart(string name) =>
            SessionHub.Instance.SessionStore.Restart(name, message => StartFailed(name, message));

        public static void AgentShell(SessionInfo info)
        {
            // Like resolves a saved agent; temporary errands have no config entry to clone.
            if (!info.Ephemeral && !string.IsNullOrEmpty(info.Project))
                SessionHub.Instance.SessionStore.Run(
                    info.Project,
                    "",
                    "",
                    session => WorkspaceWindow.Open(session),
                    UiLayout.Fail,
                    options: new SessionRunOptions { Like = info.Name }
                );
        }

        public static void DuplicateAgent(SessionInfo info)
        {
            if (!string.IsNullOrEmpty(info.Project))
                WorkspaceWindow.OpenOverPane(EditSessionDialog.Copy(info));
        }

        public static Verse.Window ResetState(string name) =>
            ConfirmDialog.Create(
                $"Reset private state for '{name}'? This stops the agent and gives its tools "
                    + "a fresh state on next start. The old state stays recoverable for 14 days.",
                () => SessionHub.Instance.SessionStore.ResetState(name, UiLayout.Fail),
                destructive: true
            );

        public static Verse.Window RemoveSession(string name) =>
            ConfirmDialog.Create(
                $"Remove session '{name}'? This kills it, drops it from config.toml, and moves "
                    + "its private state to recoverable trash for 14 days.",
                () => SessionHub.Instance.SessionStore.Remove(name, UiLayout.Fail),
                destructive: true
            );
    }
}
