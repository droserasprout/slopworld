using System;

namespace SlopWorld
{
    // Host-terminal launch and removal; callers retain scope and window placement.
    static class HostTerminalActions
    {
        // Pass scope keys through unchanged: SessionStore resolves project/worktree identity.
        // Worktree controls can supply an explicit ID and keep their local failure presentation.
        public static void HostShell(
            string project,
            string worktree = "",
            Action<string> fail = null
        ) =>
            SessionHub.Instance.SessionStore.RunHostShell(
                project,
                session => WorkspaceWindow.Open(session),
                fail ?? UiLayout.Fail,
                worktree
            );

        public static Verse.Window RemoveHost(string name) =>
            ConfirmDialog.Create(
                $"Remove {SessionHub.Instance.Capabilities.TerminalName} '{name}'? This kills its tmux pane and forgets "
                    + "the saved sidebar tab.",
                () => SessionHub.Instance.SessionStore.Remove(name, UiLayout.Fail),
                destructive: true
            );
    }
}
