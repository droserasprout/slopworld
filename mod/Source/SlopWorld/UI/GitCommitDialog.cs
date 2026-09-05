using UnityEngine;

namespace SlopWorld
{
    // A short commit message is enough for the first writable Git pass. The commit itself still
    // runs on the host, where the Git view read its repository and where the agent worktrees do
    // not hide the repository metadata from the user.
    public sealed class GitCommitDialog : SlopWindow
    {
        readonly string _project;
        string _message;
        string _error;

        GitCommitDialog(string project)
        {
            _project = project;
            AcceptOnEnter(Save);
        }

        public static void Open(string project) =>
            TerminalWindow.OpenOverPane(new GitCommitDialog(project));

        public override Vector2 InitialSize => new Vector2(500f, 220f);

        protected override void DoBody(Rect rect)
        {
            _message = SlopTextDialog.Draw(rect, $"Commit '{_project}'",
                "Only staged changes will be committed.", "git.commit.message", _message,
                _error);

            var foot = SlopTextDialog.Footer(rect);
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Commit", SlopWidgets.Btn.Primary)) Save();
        }

        void Save()
        {
            string message = (_message ?? "").Trim();
            if (message.Length == 0)
            {
                _error = "Enter a commit message.";
                return;
            }

            Close();
            GitView.Commit(_project, message);
        }
    }
}
