using NUnit.Framework;
using UnityEngine;
using Verse;

namespace SlopWorld.Tests
{
    static class WorkspaceFrameTests
    {
        public static void SettingsAndViewportChangesTakeEffectTogetherOnNextFrame()
        {
            var settings = Settings.S;
            float width = UI.screenWidth, height = UI.screenHeight, sidebarWidth = settings.sidebarWidth;
            string side = settings.sidebarSide, density = settings.uiDensity;
            bool hidden = settings.sidebarHidden, cutscene = Cutscene.Playing;
            try
            {
                Time.frameCount++;
                UI.screenWidth = 1000;
                UI.screenHeight = 600;
                settings.sidebarWidth = 210;
                settings.sidebarSide = "left";
                settings.sidebarHidden = false;
                settings.uiDensity = "default";
                Cutscene.Playing = false;
                var initial = WorkspaceLayout.Current;
                int metrics = UiMetrics.Revision;
                Assert.That(initial.LeftInset, Is.EqualTo(210f));
                Assert.That(initial.RightInset, Is.Zero);
                Assert.That(initial.TopInset, Is.EqualTo(TopBar.H));
                Assert.That(initial.Content.width, Is.EqualTo(790f));
                Assert.That(WorkspaceLayout.Revision, Is.EqualTo(initial.Revision));

                settings.sidebarSide = "RIGHT";
                settings.sidebarWidth = 250;
                settings.uiDensity = "compact";
                UI.screenWidth = 1200;
                UI.screenHeight = 700;
                var sameFrame = WorkspaceLayout.Current;
                Assert.That(sameFrame.Revision, Is.EqualTo(initial.Revision));
                Assert.That(sameFrame.Content, Is.EqualTo(initial.Content), "input and drawing retain the same rectangles");
                Assert.That(UiMetrics.Revision, Is.EqualTo(metrics), "metrics also stay stable within the frame");

                Time.frameCount++;
                var next = WorkspaceLayout.Current;
                Assert.That(next.Revision, Is.Not.EqualTo(initial.Revision));
                Assert.That(UiMetrics.Revision, Is.Not.EqualTo(metrics));
                Assert.That(next.IsRight, Is.True);
                Assert.That(next.LeftInset, Is.Zero);
                Assert.That(next.RightInset, Is.EqualTo(250f));
                Assert.That(next.Navigation.x, Is.EqualTo(950f));
                Assert.That(next.Content.width, Is.EqualTo(950f));
                Assert.That(next.Viewport.height, Is.EqualTo(700f));
                Time.frameCount++;
                Assert.That(WorkspaceLayout.Current.Revision, Is.EqualTo(next.Revision), "unchanged frame reuses geometry revision");

                settings.sidebarHidden = true;
                Time.frameCount++;
                var noSidebar = WorkspaceLayout.Current;
                Assert.That(noSidebar.NavigationVisible, Is.False);
                Assert.That(noSidebar.LeftInset + noSidebar.RightInset, Is.Zero);
                Assert.That(noSidebar.Content.width, Is.EqualTo(1200f));
                Assert.That(noSidebar.TopInset, Is.EqualTo(TopBar.H));
                Cutscene.Playing = true;
                Time.frameCount++;
                var movie = WorkspaceLayout.Current;
                Assert.That(movie.TopInset, Is.Zero);
                Assert.That(movie.Content, Is.EqualTo(movie.Viewport), "cutscene reclaims full viewport");
                Cutscene.Playing = false;
                settings.sidebarHidden = false;
                Time.frameCount++;
                Assert.That(WorkspaceLayout.Current.RightInset, Is.EqualTo(250f), "chrome returns after cutscene");
            }
            finally
            {
                UI.screenWidth = width; UI.screenHeight = height;
                settings.sidebarWidth = sidebarWidth; settings.sidebarSide = side;
                settings.uiDensity = density; settings.sidebarHidden = hidden;
                Cutscene.Playing = cutscene;
                // Advance rather than rewind the frame so static geometry/metrics caches cannot
                // leak the test snapshot into another caller after globals are restored.
                Time.frameCount++;
                _ = WorkspaceLayout.Current;
            }
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
