namespace SlopWorld.Tests
{
    static class OpenFilesVisibilityTests
    {
        public static void DismissingOnlyVisibleDiffClosesManuallyOpenedEmptyPane()
        {
            var pane = new OpenFilesVisibility();
            var visibility = new ReaderSessionVisibility();
            var diff = new SessionInfo
            {
                Name = "diff",
                Intent = "diff",
                Alive = true,
            };
            AssertEx.False(pane.ShouldHide(false, false), "manually opened empty pane remains");
            // Another reader can exist outside the current project/checkout filters.
            pane.ShouldHide(true, false);
            visibility.Claim(diff.Name);
            visibility.Retire(diff.Name);
            AssertEx.False(visibility.Includes(diff), "dismissal hides row before daemon reply");
            AssertEx.False(pane.ShouldHide(true, false), "unfiltered inventory still has a reader");
            AssertEx.True(pane.ReaderDismissed(0, false), "last visible diff dismissal hides pane");
            AssertEx.False(pane.ReaderDismissed(1, false), "another visible reader keeps pane");
            AssertEx.False(pane.ReaderDismissed(0, true), "pending replacement keeps pane");
        }

        public static void LastReaderExitHidesPaneEvenWhenStoppedSessionRemains()
        {
            foreach (string intent in new[] { "view", "edit", "search", "diff" })
            {
                var visibility = new ReaderSessionVisibility();
                var pane = new OpenFilesVisibility();
                var info = new SessionInfo
                {
                    Name = "reader",
                    Intent = intent,
                    Alive = true,
                };
                visibility.Claim(info.Name);
                AssertEx.False(
                    pane.ShouldHide(visibility.Includes(info), false),
                    "live reader occupies pane"
                );
                info.Alive = false;
                AssertEx.False(visibility.Includes(info), "claimed stopped reader is excluded");
                AssertEx.True(
                    pane.ShouldHide(visibility.Includes(info), false),
                    "last reader exit hides pane"
                );
                AssertEx.False(
                    pane.ShouldHide(false, false),
                    "empty pane may be reopened manually"
                );
            }
        }

        public static void PendingReplacementKeepsPaneUntilCompletionOrCancellation()
        {
            var pane = new OpenFilesVisibility();
            AssertEx.False(pane.ShouldHide(false, false), "manual empty pane stays visible");
            AssertEx.False(pane.ShouldHide(true, false), "reader opens");
            AssertEx.False(pane.ShouldHide(false, true), "pending replacement keeps pane");
            AssertEx.False(pane.ShouldHide(true, false), "replacement completes");
            AssertEx.True(pane.ShouldHide(false, false), "replacement closes");

            AssertEx.False(pane.ShouldHide(false, true), "startup counts before a session arrives");
            AssertEx.True(
                pane.ShouldHide(false, false),
                "cancelled or failed startup hides empty pane"
            );
        }

        public static void RetiringLastReaderHidesPaneBeforeStopReply()
        {
            var visibility = new ReaderSessionVisibility();
            var pane = new OpenFilesVisibility();
            var info = new SessionInfo { Name = "reader", Alive = true };
            pane.ShouldHide(visibility.Includes(info), false);
            visibility.Retire(info.Name);
            AssertEx.True(
                pane.ShouldHide(visibility.Includes(info), false),
                "local dismissal hides pane while stop is pending"
            );
        }
    }
}
