using UnityEngine;

namespace SlopWorld
{
    public sealed class GitCommitDialog : UiWindow
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
            _message = TextDialog.Draw(rect, $"Commit '{SidebarScopes.Label(_project)}'",
                "Only staged changes will be committed.", "git.commit.message", _message,
                _error, titleRect: TitleRect(rect));

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Commit", UiTheme.Btn.Primary)) Save();
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
