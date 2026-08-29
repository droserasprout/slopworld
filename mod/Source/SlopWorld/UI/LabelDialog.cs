using System;
using UnityEngine;

namespace SlopWorld
{
    // A blank label is meaningful: it returns the session to automatic title summaries.
    public sealed class LabelDialog : SlopWindow
    {
        readonly string _session;
        string _label;
        string _error;

        LabelDialog(string session, string initial)
        {
            _session = session;
            _label = initial ?? "";
            closeOnAccept = true;
        }

        public static void Open(string session, string initial) =>
            TerminalWindow.OpenOverPane(new LabelDialog(session, initial));

        public override Vector2 InitialSize => new Vector2(500f, 204f);

        public override void OnAcceptKeyPressed()
        {
            Save(_label);
            Event.current.Use();
        }

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, $"Label '{_session}'");

            var note = new Rect(rect.x, rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM,
                rect.width, SlopWidgets.RowH * 2f);
            GUI.color = SlopWidgets.Dim;
            SlopWidgets.RowLabel(note,
                "Set a manual third-line label. Leave it blank to resume automatic summaries.");
            GUI.color = Color.white;

            var field = new Rect(rect.x, note.yMax + SlopWidgets.GapS,
                rect.width, SlopWidgets.FieldH);
            _label = SlopWidgets.Field(field, "agent.label", _label);

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(new Rect(rect.x, field.yMax + SlopWidgets.GapXS,
                    rect.width, SlopWidgets.RowH), _error);
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (!string.IsNullOrWhiteSpace(_label) &&
                foot.Left("Remove", SlopWidgets.Btn.Danger))
                Save("");
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save(_label);
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
            SessionHub.Instance.SetLabel(session, label, () => Close(), SlopWidgets.Fail);
        }
    }
}
