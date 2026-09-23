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
                $"Save agent-owned settings from the saved agent '{_source}'. Project mounts stay contextual.",
                "agent-template.name", _name, _error, UiTheme.RowH * 2f, TitleRect(rect));

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiTheme.Btn.Ghost)) Close();
            if (foot.Right("Save", UiTheme.Btn.Primary)) Save();
        }

        void Save()
        {
            string name = (_name ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                _error = "Enter a template name.";
                return;
            }
            SessionHub.Instance.SaveAgentTemplate(_source, name, "",
                () => Close(), message => _error = message);
        }
    }
}
