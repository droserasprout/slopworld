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
        }

        public static void Open(string project) =>
            TerminalWindow.OpenOverPane(new GitCommitDialog(project));

        public override Vector2 InitialSize => new Vector2(500f, 220f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, $"Commit '{_project}'");

            var note = new Rect(rect.x, rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM,
                rect.width, SlopWidgets.RowH);
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(note, "Only staged changes will be committed.");
            GUI.color = Color.white;

            var field = new Rect(rect.x, note.yMax + SlopWidgets.GapS,
                rect.width, SlopWidgets.FieldH);
            _message = SlopWidgets.Field(field, "git.commit.message", _message ?? "");

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(new Rect(rect.x, field.yMax + SlopWidgets.GapXS,
                    rect.width, SlopWidgets.RowH), _error);
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
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
