namespace SlopWorld
{
    // Callers retain visibility rules and window placement. Confirmations share the action.
    static class CatalogActions
    {
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
