using UnityEngine;

namespace SlopWorld
{
    // Native text capture must not outlive its form. Focus memory and restoration
    // belong to FieldFocusScope; this owner only releases a retiring form's focus.
    internal static class FieldKeyboardFocus
    {
        static FieldLifetime _owner;
        static string _name;
        static int _control;

        internal static void Track(FieldLifetime lifetime, string name, int control)
        {
            if (
                !lifetime.Alive
                || control == 0
                || GUIUtility.keyboardControl != control
                || GUI.GetNameOfFocusedControl() != name
            )
                return;
            _owner = lifetime;
            _name = name;
            _control = control;
        }

        internal static void Retire(FieldLifetime lifetime)
        {
            if (_owner != lifetime)
                return;
            // A different window can acquire focus before the old one closes.
            // Control IDs alone are not identities: IMGUI can reuse them.
            bool ownsFocus =
                GUIUtility.keyboardControl == _control && GUI.GetNameOfFocusedControl() == _name;
            _owner = null;
            _name = null;
            _control = 0;
            if (!ownsFocus)
                return;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }
    }
}
