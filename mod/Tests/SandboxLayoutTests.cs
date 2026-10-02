namespace SlopWorld.Tests
{
    static class SandboxLayoutTests
    {
        const float Gap = 16f;

        public static void Placement()
        {
            var wide = Arrange(1000f, 500f, Gap);
            AssertEx.False(wide.Stacked, "wide layout stays side by side");
            AssertEx.Equal(SandboxLayout.EditorPreferredWidth, wide.Editor.Width,
                "wide editor honors its preferred cap");
            AssertEx.True(wide.List.Width >= SandboxLayout.ListMinimumWidth,
                "wide list honors its minimum");
            AssertContained(wide, 1000f, 500f);

            float breakpoint = SandboxLayout.Breakpoint(Gap);
            var atBreakpoint = Arrange(breakpoint, 500f, Gap);
            AssertEx.Equal(SandboxLayout.ListMinimumWidth + SandboxLayout.EditorMinimumWidth + Gap, breakpoint, "breakpoint is both minima plus gap");
            AssertEx.Equal(SandboxLayout.ListMinimumWidth, atBreakpoint.List.Width, "breakpoint list minimum");
            AssertEx.Equal(SandboxLayout.EditorMinimumWidth, atBreakpoint.Editor.Width, "breakpoint editor minimum");
            AssertEx.Equal(Gap, atBreakpoint.Editor.X - atBreakpoint.List.XMax, "breakpoint pane separation");
            AssertEx.False(atBreakpoint.Stacked,
                "breakpoint fits both panes");
            AssertEx.True(Arrange(breakpoint - 0.01f, 500f, Gap).Stacked,
                "below the breakpoint stacks panes");

            var narrow = Arrange(500f, 500f, Gap);
            AssertEx.True(narrow.Stacked, "narrow layout stacks panes");
            AssertEx.Equal(narrow.List.Height, narrow.Editor.Height,
                "stacked panes split the height");
            AssertContained(narrow, 500f, 500f);

            var tiny = Arrange(3f, 2f, Gap);
            AssertContained(tiny, 3f, 2f);
        }

        static SandboxSplit Arrange(float width, float height, float gap) =>
            SandboxLayout.Arrange(new UiLayoutRect(0f, 0f, width, height), gap);

        static void AssertContained(SandboxSplit split, float width, float height)
        {
            AssertEx.True(split.List.Width >= 0f && split.List.Height >= 0f &&
                split.Editor.Width >= 0f && split.Editor.Height >= 0f,
                "pane geometry is nonnegative");
            AssertEx.True(split.List.X >= 0f && split.List.Y >= 0f &&
                split.Editor.X >= 0f && split.Editor.Y >= 0f &&
                split.List.XMax <= width && split.Editor.XMax <= width &&
                split.List.YMax <= height && split.Editor.YMax <= height,
                "panes stay inside their content rectangle");
            AssertEx.True(split.Stacked
                    ? split.List.YMax <= split.Editor.Y
                    : split.List.XMax <= split.Editor.X,
                "panes do not overlap");
        }
    }

}
