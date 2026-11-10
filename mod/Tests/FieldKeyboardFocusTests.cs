using UnityEngine;

namespace SlopWorld.Tests
{
    static class FieldKeyboardFocusTests
    {
        static void Focus(FieldLifetime lifetime, string name, int id)
        {
            GUI.FocusControl(name);
            GUIUtility.keyboardControl = id;
            FieldKeyboardFocus.Track(lifetime, name, id);
        }

        public static void ClosingFormReleasesKeyboardCaptureAndRepeatedCleanupIsInert()
        {
            var form = new FieldLifetime();
            Focus(form, "query", 42);
            form.Cancel();
            AssertEx.Equal(0, GUIUtility.keyboardControl, "closed field releases keyboard capture");
            AssertEx.Equal<string>(null, GUI.GetNameOfFocusedControl(), "native focus is cleared");
            var next = new FieldLifetime();
            Focus(next, "query", 42);
            form.Cancel();
            AssertEx.Equal(42, GUIUtility.keyboardControl, "repeated close preserves new focus");
            next.Cancel();
        }

        public static void ClosingOlderFormPreservesNewOwnerWithReusedNameAndId()
        {
            var old = new FieldLifetime();
            var current = new FieldLifetime();
            Focus(old, "name", 7);
            Focus(current, "name", 7);
            old.Cancel();
            AssertEx.Equal(7, GUIUtility.keyboardControl, "new lifetime owns the reused field");
            AssertEx.Equal("name", GUI.GetNameOfFocusedControl(), "new field stays focused");
            current.Cancel();
            AssertEx.Equal(0, GUIUtility.keyboardControl, "new owner releases its own focus");
        }

        public static void UnfocusedFieldsCannotClaimAnotherFormsFocus()
        {
            var focused = new FieldLifetime();
            var background = new FieldLifetime();
            Focus(focused, "query", 9);
            FieldKeyboardFocus.Track(background, "other", 9);
            FieldKeyboardFocus.Track(background, "query", 10);
            background.Cancel();
            AssertEx.Equal(9, GUIUtility.keyboardControl, "background form cannot steal ownership");
            focused.Cancel();
            AssertEx.Equal(0, GUIUtility.keyboardControl, "actual owner still releases focus");
        }

        public static void ClosingFormPreservesUntrackedNativeControl()
        {
            foreach (bool reuseId in new[] { false, true })
            {
                var form = new FieldLifetime();
                Focus(form, "query", 12);
                GUI.FocusControl("native-dialog");
                int id = reuseId ? 12 : 13;
                GUIUtility.keyboardControl = id;
                form.Cancel();
                AssertEx.Equal(id, GUIUtility.keyboardControl, "unrelated native focus survives");
                AssertEx.Equal("native-dialog", GUI.GetNameOfFocusedControl(), "name survives");
            }
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
        }
    }
}
