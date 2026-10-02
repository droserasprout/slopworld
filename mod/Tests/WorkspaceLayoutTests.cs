using NUnit.Framework;
using System;

namespace SlopWorld.Tests
{
    static class WorkspaceLayoutTests
    {
        public static void Placement()
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

        }

        public static void TinyBoundsRemainNonnegative()
        {
            var narrow = WorkspaceLayout.Compute(100f, 50f, true, true,
                NavigationSide.Left, 210f, 80f);
            AssertEx.True(narrow.Navigation.width >= 0f && narrow.Content.width >= 0f,
                "narrow geometry is nonnegative");
            AssertEx.True(narrow.Navigation.width + narrow.Content.width <= 100.001f,
                "narrow navigation preserves content");

            var zero = WorkspaceLayout.Compute(0f, 0f, true, true,
                NavigationSide.Right, 210f, 80f);
            AssertEx.True(zero.Navigation.width >= 0f && zero.Content.width >= 0f
                && zero.Content.height >= 0f && zero.TopBar.height >= 0f,
                "zero viewport geometry is nonnegative");

            var tinyRight = WorkspaceLayout.Compute(1f, 1f, true, true,
                NavigationSide.Right, 210f, 80f);
            AssertEx.True(tinyRight.Navigation.width >= 0f && tinyRight.Content.width >= 0f
                && tinyRight.Content.height >= 0f,
                "tiny right-side geometry is nonnegative");

        }

        public static void RevisionStability()
        {
            var first = WorkspaceLayout.Compute(800f, 600f, true, true,
                NavigationSide.Left, 210f, 26f, 1);
            var second = WorkspaceLayout.Compute(800f, 600f, true, true,
                NavigationSide.Left, 210f, 26f, 2);
            AssertEx.True(second.Revision != first.Revision,
                "pure computation preserves the supplied revision");
            var unchanged = WorkspaceLayout.Compute(800f, 600f, true, true,
                NavigationSide.Left, 210f, 26f, 2);
            AssertEx.Equal(second.Revision, unchanged.Revision,
                "unchanged geometry keeps its revision");
            UnityEngine.Time.frameCount++;
            var current = WorkspaceLayout.Current;
            WorkspaceLayout.Compute(3f, 2f, false, false, NavigationSide.Right, 1f, 1f, 900);
            UnityEngine.Time.frameCount++;
            AssertEx.Equal(current.Revision, WorkspaceLayout.Current.Revision,
                "ad hoc computation cannot invalidate the retained workspace snapshot");

        }

        public static void Normalization()
        {
            AssertEx.Equal(NavigationSide.Left,
                NavigationSide.Normalize("unknown"), "unknown side falls back left");
            AssertEx.Equal(UiDensityPreset.Default,
                UiDensityPreset.Normalize("unknown"), "unknown density falls back default");
        }
        public static void PreferenceLabelsUseNormalizedFallbacks()
        {
            Assert.That(NavigationSide.Label("RIGHT"), Is.EqualTo("Right"));
            Assert.That(NavigationSide.Label(null), Is.EqualTo("Left"));
            Assert.That(NavigationSide.Label("unknown"), Is.EqualTo("Left"));
            Assert.That(UiDensityPreset.Label("COMPACT"), Is.EqualTo("Compact"));
            Assert.That(UiDensityPreset.Label(null), Is.EqualTo("Default"));
            Assert.That(UiDensityPreset.Label("unknown"), Is.EqualTo("Default"));
        }

    }
}
