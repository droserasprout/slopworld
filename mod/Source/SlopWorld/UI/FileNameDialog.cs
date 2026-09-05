using System;
using UnityEngine;

namespace SlopWorld
{
    // The file tree only needs one small form: a single path component, followed by an
    // operation that is already named by the menu row which opened it.
    public sealed class FileNameDialog : UiWindow
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
            _name = TextDialog.Draw(rect, _title, null, "file-name", _name, _error);

            var foot = TextDialog.Footer(rect);
            if (foot.Left("Cancel", UiWidgets.Btn.Ghost)) Close();
            if (foot.Right("Save", UiWidgets.Btn.Primary)) Save();
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
