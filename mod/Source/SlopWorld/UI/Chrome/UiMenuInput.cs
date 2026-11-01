using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // An editable menu row owns its value; the feature owns validation and submission.
    public sealed class UiMenuInput : FloatMenuOption
    {
        readonly string _control = "menu.input." + Guid.NewGuid().ToString("N");
        readonly Func<string, string> _validate;
        readonly Action<string> _submit;
        string _value = "";
        string _error;

        public UiMenuInput(string placeholder, Func<string, string> validate, Action<string> submit)
            : base(placeholder, () => { })
        {
            _validate = validate;
            _submit = submit;
        }

        internal bool Focused => GUI.GetNameOfFocusedControl() == _control;

        internal void Focus() => GUI.FocusControl(_control);

        internal void Draw(Rect rect)
        {
            _value = UiText.FieldWithPlaceholder(rect, _control, _value, Label);
            if (!string.IsNullOrEmpty(_error))
                TooltipHandler.TipRegion(rect, _error);
        }

        internal void Submit()
        {
            string value = _value.Trim();
            _error = _validate(value);
            if (_error != null)
                return;
            _submit(value);
            _value = "";
        }
    }
}
