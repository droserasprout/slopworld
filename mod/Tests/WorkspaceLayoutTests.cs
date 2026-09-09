using System;

namespace SlopWorld.Tests
{
    static class WorkspaceLayoutTests
    {
        public static void Geometry()
        {
            var left = WorkspaceLayout.Compute(1920f, 1080f, true, true,
                NavigationSide.Left, 210f, 26f);
            AssertEx.Equal(0f, left.Navigation.x, "left navigation origin");
            AssertEx.Equal(210f, left.Navigation.width, "left navigation width");
            AssertEx.Equal(210f, left.TopBar.x, "left top bar starts after navigation");
            AssertEx.Equal(1710f, left.Content.width, "left content width");
            AssertEx.Equal(26f, left.Content.y, "top bar height");

            var right = WorkspaceLayout.Compute(1920f, 1080f, true, true,
                NavigationSide.Right, 210f, 26f);
            AssertEx.Equal(1710f, right.Navigation.x, "right navigation origin");
            AssertEx.Equal(0f, right.Content.x, "right content origin");
            AssertEx.Equal(1710f, right.TopBar.width, "right top bar avoids navigation");
            AssertEx.Equal(0f, right.Content.xMax - right.Navigation.x,
                "right content and navigation do not overlap");

            var hidden = WorkspaceLayout.Compute(1920f, 1080f, true, false,
                NavigationSide.Right, 210f, 26f);
            AssertEx.Equal(0f, hidden.Navigation.width, "hidden navigation consumes no width");
            AssertEx.Equal(1920f, hidden.Content.width, "hidden navigation preserves content");

            var cutscene = WorkspaceLayout.Compute(1920f, 1080f, false, true,
                NavigationSide.Left, 210f, 26f);
            AssertEx.Equal(0f, cutscene.TopBar.height, "cutscene hides top bar layout");
            AssertEx.Equal(1920f, cutscene.Content.width, "cutscene restores full width");

            var narrow = WorkspaceLayout.Compute(100f, 50f, true, true,
                NavigationSide.Left, 210f, 80f);
            AssertEx.True(narrow.Navigation.width >= 0f && narrow.Content.width >= 0f,
                "narrow geometry is nonnegative");
            AssertEx.True(narrow.Navigation.width + narrow.Content.width <= 100.001f,
                "narrow navigation preserves content");

            var first = WorkspaceLayout.Compute(800f, 600f, true, true,
                NavigationSide.Left, 210f, 26f, 1);
            var second = WorkspaceLayout.Compute(800f, 600f, true, true,
                NavigationSide.Left, 210f, 26f, 2);
            AssertEx.True(second.Revision != first.Revision,
                "metric changes advance the workspace revision");
            AssertEx.Equal(NavigationSide.Left,
                NavigationSide.Normalize("unknown"), "unknown side falls back left");
            AssertEx.Equal(UiDensityPreset.Default,
                UiDensityPreset.Normalize("unknown"), "unknown density falls back default");
        }
    }
}
