using System;
using UnityEngine;

namespace SlopWorld
{
    // A blank label is meaningful: it returns the session to automatic title summaries.
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

        public override Vector2 InitialSize => new Vector2(500f, 204f);

        protected override void DoBody(Rect rect)
        {
            _label = TextDialog.Draw(rect, $"Label '{_session}'",
                "Set a manual third-line label. Leave it blank to resume automatic summaries.",
                "agent.label", _label, _error, UiWidgets.RowH * 2f);

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost)) Close();
            if (!string.IsNullOrWhiteSpace(_label) &&
                foot.Left("Remove", UiWidgets.Btn.Danger))
                Save("");
            if (foot.Right("Save", UiWidgets.Btn.Primary)) Save(_label);
        }

        void Save(string value)
        {
            string label = (value ?? "").Trim();
            if (label.Length > 60)
            {
                _error = "Use at most 60 characters.";
                return;
            }

            string session = _session;
            SessionHub.Instance.SetLabel(session, label, () => Close(), UiWidgets.Fail);
        }
    }
}
