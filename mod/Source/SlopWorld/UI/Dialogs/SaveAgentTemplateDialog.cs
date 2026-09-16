using UnityEngine;
using Verse;

namespace SlopWorld
{
    // Small naming step for the agent editor's Save as template action. The daemon performs the
    // actual snapshot, validation, and atomic persistence from the named source agent.
    public sealed class SaveAgentTemplateDialog : UiWindow
    {
        readonly string _source;
        string _name;
        string _error;
        bool _includeInherited;

        public SaveAgentTemplateDialog(string source)
        {
            _source = source ?? "";
            _name = _source;
            AcceptOnEnter(Save);
        }

        public override Vector2 InitialSize => new Vector2(540f, 320f);

        protected override void DoBody(Rect rect)
        {
            _name = TextDialog.Draw(rect, "Save agent as template",
                $"Capture explicit choices from '{_source}'. Project defaults stay inherited.",
                "agent-template.name", _name, _error, UiTheme.RowH * 2f, TitleRect(rect));

            _includeInherited = UiControls.Checkbox(new Rect(rect.x, rect.yMax - UiTheme.BtnH - UiTheme.RowH * 2f,
                rect.width, UiTheme.RowH), "Include inherited project settings", _includeInherited,
                "Copies effective network, DNS, limits, sandbox presets and breadcrumbs from the source project.");
            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void Save()
        {
            string name = (_name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                _error = "Template name is required.";
                return;
            }
            SessionHub.Instance.SaveAgentTemplate(_source, name, "",
                () => Close(), message => _error = message, _includeInherited);
        }
    }
}
