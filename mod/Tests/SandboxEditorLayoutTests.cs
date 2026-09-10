namespace SlopWorld.Tests
{
    static class SandboxEditorLayoutTests
    {
        public static void Geometry()
        {
            var readOnly = SandboxEditorLayout.Measure(240f, new[]
            {
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Area, 60f,
                    SandboxEditorLayout.OptionalVisible(false, ""), null),
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Actions, 24f, true, null),
            });
            AssertEx.False(readOnly.Rows[0].Visible, "empty read-only area is hidden");
            AssertEx.Equal(0f, readOnly.Rows[0].Bounds.Height,
                "hidden read-only area has no bounds");
            AssertEx.Equal(24f, readOnly.ContentHeight,
                "hidden read-only area does not move the action row");

            var editable = SandboxEditorLayout.Measure(240f, new[]
            {
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Area, 60f,
                    SandboxEditorLayout.OptionalVisible(true, ""), null),
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Actions, 24f, true, null),
            });
            AssertEx.True(editable.Rows[0].Visible, "editable empty area stays visible");
            AssertEx.Equal(60f, editable.Rows[1].Bounds.Y,
                "visible area places the action row after its bounds");

            float wrapped = SandboxEditorLayout.WrappedAreaHeight(100f, "long", 48f,
                6f, 2f, (text, width) => width < 90f ? 90f : 20f);
            AssertEx.Equal(98f, wrapped, "wrapped area includes field padding");
            AssertEx.Equal(48f, SandboxEditorLayout.WrappedAreaHeight(100f, "short", 48f,
                6f, 2f, (text, width) => 20f), "area keeps its minimum height");

            var narrow = SandboxEditorLayout.Measure(-10f, new[]
            {
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Field, 12f, true, null),
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Actions, 18f, true, null),
            });
            AssertEx.Equal(0f, narrow.Rows[0].Bounds.Width,
                "narrow layout clamps row width");
            AssertEx.True(narrow.Rows[0].Bounds.XMax <= 0f &&
                narrow.Rows[1].Bounds.XMax <= 0f,
                "narrow rows stay within their zero-width edge");
            AssertEx.Equal(12f, narrow.Rows[1].Bounds.Y,
                "action row follows the preceding row at narrow width");
            AssertEx.Equal(30f, narrow.ContentHeight,
                "content extent includes every visible row");

            bool painted = false;
            var deferred = SandboxEditorLayout.Measure(80f, new[]
            {
                new SandboxEditorLayout.Row(SandboxEditorRowKind.Field, 10f, true,
                    bounds => painted = true),
            });
            AssertEx.False(painted, "measurement does not paint rows");
            deferred.Rows[0].Paint(deferred.Rows[0].Bounds);
            AssertEx.True(painted, "row painting is deferred until the draw pass");
        }
    }
}
