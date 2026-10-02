using System;
using UnityEngine;

namespace SlopWorld
{
    // A blank label is meaningful: it returns the session to its native or generated title.
    public sealed class LabelDialog : UiWindow
    {
        readonly string _session;
        string _label;
        string _error;

        LabelDialog(string session, string initial)
        {
            _session = session;
            _label = initial ?? "";
            AcceptOnEnter(() => Save(_label));
        }

        public static void Open(string session, string initial) =>
            TerminalWindow.OpenOverPane(new LabelDialog(session, initial));

        // Leave room for the two-line note, the field, a validation row, and the footer.
        // the error row is conditional but must not collide with the footer when shown.
        public override Vector2 InitialSize => new Vector2(500f, 240f);

        protected override void DoBody(Rect rect)
        {
            bool host = SessionHub.Instance.Get(_session)?.Host == true;
            string previous = _label;
            _label = TextDialog.Draw(rect, $"Label '{_session}'",
                host
                    ? "Set a fixed label for this host terminal. Leave it blank to use its terminal title."
                    : "Set a fixed label. Leave it blank to use the generated title.",
                "agent.label", _label, _error, UiTheme.RowH * 2f, TitleRect(rect));

            if (_label != previous) _error = ValidationError(_label);

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (!string.IsNullOrWhiteSpace(_label) &&
                foot.Left("Remove", UiTheme.Btn.Danger))
                Save("");
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save(_label);
        }

        static string ValidationError(string value) =>
            TerminalColumns.ScalarCount((value ?? "").Trim()) > 60
                ? "Use at most 60 characters." : null;

        void Save(string value)
        {
            string label = (value ?? "").Trim();
            _error = ValidationError(label);
            if (_error != null) return;

            string session = _session;
            SessionHub.Instance.SessionStore.SetLabel(session, label, () => Close(), UiLayout.Fail);
        }
    }
}
