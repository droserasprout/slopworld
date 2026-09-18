namespace SlopWorld.Tests
{
    static class ScrollSampleDeltaTests
    {
        public static void FractionalMovementSurvivesRecentSamplesIncludingSameFrameRefresh()
        {
            var motion = new ScrollSampleDelta();
            AssertEx.Equal(false, motion.Read(5, 10, 1, 1.01, out _, out _), "first snapshot is baseline");
            AssertEx.Equal(true, motion.Read(5.125, 9.5, 1.01, 1.02, out var x, out var y), "fresh delta");
            AssertEx.Equal(0.125, x, "fractional horizontal movement");
            AssertEx.Equal(-0.5, y, "fractional reverse movement");
        }

        public static void LogicalFallbackDiscardsDelayedSnapshotsAndDoesNotReplayMovement()
        {
            var motion = new ScrollSampleDelta();
            motion.Read(0, 0, 1, 1, out _, out _);
            motion.Discard(1.1);
            AssertEx.Equal(false, motion.Read(0, 4, 1.09, 1.11, out _, out _), "in-flight sample predates fallback");
            AssertEx.Equal(false, motion.Read(0, 6, 1.12, 1.13, out _, out _), "fresh baseline omits spent movement");
            AssertEx.Equal(true, motion.Read(0, 6.25, 1.14, 1.15, out _, out var y), "precision resumes");
            AssertEx.Equal(0.25, y, "only new motion is delivered");
        }

        public static void StallsAndValuatorResetsNeverJumpTheViewport()
        {
            var motion = new ScrollSampleDelta();
            motion.Read(0, 0, 1, 1, out _, out _);
            AssertEx.Equal(false, motion.Read(0, 8, 1.1, 2, out _, out _), "blocked query is stale");
            AssertEx.Equal(false, motion.Read(0, 9, 2.01, 2.02, out _, out _), "resume with baseline");
            AssertEx.Equal(false, motion.Read(0, 1000, 2.03, 2.04, out _, out _), "valuator reset is not movement");
            AssertEx.Equal(true, motion.Read(0, 1000.5, 2.05, 2.06, out _, out var y), "new baseline works");
            AssertEx.Equal(0.5, y, "no stale jump");
            AssertEx.Equal(false, motion.Read(0, 1001, 3, 3.01, out _, out _), "inactive viewport restarts baseline");
        }
    }
}
