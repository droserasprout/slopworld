using UnityEngine;

namespace SlopWorld
{
    public sealed class GitCommitDialog : UiWindow
    {
        readonly string _project;
        readonly DialogSubmitState _submit = new DialogSubmitState();
        string _message;

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
            _message = TextDialog.Draw(
                rect,
                $"Commit '{SidebarScopes.Label(_project)}'",
                "Only staged changes will be committed.",
                "git.commit.message",
                _message,
                _submit.Error,
                titleRect: TitleRect(rect)
            );

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiTheme.Btn.Ghost))
                Close();
            if (foot.Right("Commit", UiTheme.Btn.Primary, !_submit.Pending))
                Save();
        }

        void Save()
        {
            string message = (_message ?? "").Trim();
            _submit.Submit(
                message.Length == 0 ? "Enter a commit message." : null,
                (ok, fail) => GitView.Commit(_project, message, ok, fail),
                () => Close()
            );
        }
    }
}
