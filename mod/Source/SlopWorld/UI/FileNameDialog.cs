using System;
using UnityEngine;

namespace SlopWorld
{
    // The file tree only needs one small form: a single path component, followed by an
    // operation that is already named by the menu row which opened it.
    public sealed class FileNameDialog : SlopWindow
    {
        readonly string _title;
        readonly Action<string> _done;
        string _name;
        string _error;

        FileNameDialog(string title, string initial, Action<string> done)
        {
            _title = title;
            _name = initial ?? "";
            _done = done;
            AcceptOnEnter(Save);
        }

        public static void Open(string title, string initial, Action<string> done) =>
            TerminalWindow.OpenOverPane(new FileNameDialog(title, initial, done));

        public override Vector2 InitialSize => new Vector2(420f, 176f);

        protected override void DoBody(Rect rect)
        {
            SlopWidgets.Title(rect, _title);

            var field = new Rect(rect.x, rect.y + SlopWidgets.HeaderH + SlopWidgets.GapM,
                rect.width, SlopWidgets.FieldH);
            _name = SlopWidgets.Field(field, "file-name", _name);

            if (!string.IsNullOrEmpty(_error))
            {
                GUI.color = SlopWidgets.Bad;
                SlopWidgets.RowLabel(new Rect(rect.x, field.yMax + SlopWidgets.GapS,
                    rect.width, SlopWidgets.RowH), _error);
                GUI.color = Color.white;
            }

            var foot = new SlopWidgets.Bar(SlopWidgets.FooterBar(rect));
            if (foot.Left("Cancel", SlopWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", SlopWidgets.Btn.Primary)) Save();
        }

        void Save()
        {
            string name = (_name ?? "").Trim();
            if (name.Length == 0 || name == "." || name == "..")
            {
                _error = "Enter a name.";
                return;
            }
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0 || name.IndexOf('\0') >= 0)
            {
                _error = "Use one file or folder name.";
                return;
            }

            Close();
            _done?.Invoke(name);
        }
    }
}
