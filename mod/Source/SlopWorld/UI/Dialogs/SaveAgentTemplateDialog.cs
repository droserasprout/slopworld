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

        public override Vector2 InitialSize => new Vector2(500f, 250f);

        protected override void DoBody(Rect rect)
        {
            _name = TextDialog.Draw(rect, "Save agent as template",
                $"Capture '{_source}' as a personal template. Presets and prompts are snapshotted.",
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
                _error = "Template name is required.";
                return;
            }
            SessionHub.Instance.SaveAgentTemplate(_source, name, "",
                () => Close(), message => _error = message);
        }
    }
}
