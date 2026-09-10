namespace SlopWorld.Tests
{
    static class FieldFocusTests
    {
        public static void Availability()
        {
            var order = new FieldFocusOrder();
            AssertEx.Equal<string>(null, order.Restore(), "empty forms have no focus target");
            order.Register("name");
            order.Register("description");
            order.Register("command");
            order.Register("command");
            AssertEx.Equal(3, order.Count, "duplicate registration does not duplicate a field");
            order.Remember("description");
            order.Begin();
            order.Register("name");
            order.Register("command");
            AssertEx.Equal<string>(null, order.Restore(), "removed or disabled fields cannot regain focus");
        }

        public static void Restoration()
        {
            var form = new FieldFocusOrder();
            var dialog = new FieldFocusOrder();
            form.Register("name");
            form.Register("description");
            form.Remember("description");
            dialog.Register("name");
            dialog.Remember("name");
            form.Begin();
            form.Register("description");
            form.Register("name");
            AssertEx.Equal("description", form.Restore(), "focus follows identity through reordered rows");
            AssertEx.Equal("name", dialog.Restore(), "dialog focus has a separate owner");
            form.Remember("unrelated-control");
            AssertEx.Equal("description", form.Restore(), "another control cannot overwrite form memory");
            form.Begin();
            AssertEx.Equal<string>(null, form.Restore(), "an empty page has no stale focus target");
        }
    }
}
