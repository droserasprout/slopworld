namespace SlopWorld.Tests
{
    static class FramePolicyTests
    {
        public static void Transitions()
        {
            AssertEx.Equal(
                15,
                FramePolicy.NearestPreset(-2147483633),
                "extreme negative FPS does not overflow"
            );
            var policy = new FramePolicy();
            int target = 75,
                sync = 2;
            AssertEx.Equal(
                true,
                policy.Follow(true, "auto", 60, true, ref target, ref sync),
                "focused mode uses VSync"
            );
            AssertEx.Equal(-1, target, "focused target");
            AssertEx.Equal(1, sync, "focused VSync");
            Step(policy, false, "auto", 60, true, ref target, ref sync, 15, 1, true);
            Step(policy, false, "auto", 60, true, ref target, ref sync, 15, 1, false);
            Step(policy, true, "auto", 60, true, ref target, ref sync, -1, 1, true);
            Step(policy, true, "limit", 144, false, ref target, ref sync, 144, 0, true);
            Step(policy, false, "limit", 144, false, ref target, ref sync, 15, 0, true);
            Step(policy, false, "auto", 144, true, ref target, ref sync, 15, 1, true);
            Step(policy, true, "auto", 144, true, ref target, ref sync, -1, 1, true);

            target = 90;
            sync = 0;
            Step(policy, true, "limit", -1, false, ref target, ref sync, 15, 0, true);
            Step(policy, true, "limit", 9999, false, ref target, ref sync, 240, 0, true);
            Step(policy, true, "invalid", 60, false, ref target, ref sync, 60, 0, true);
            Step(policy, true, "auto", 60, true, ref target, ref sync, -1, 1, true);

            target = 60;
            sync = 0;
            Step(policy, true, "auto", 60, true, ref target, ref sync, -1, 1, true);
            Step(policy, true, "auto", 60, true, ref target, ref sync, -1, 1, false);
        }

        public static void FocusChangesKeepPresentationModeAndRestoreForegroundLimit()
        {
            foreach (bool vsync in new[] { false, true })
            {
                var policy = new FramePolicy();
                int target = 0,
                    sync = -1;
                policy.Follow(true, FramePolicy.Limit, 120, vsync, ref target, ref sync);
                int foregroundSync = sync;
                for (int i = 0; i < 3; i++)
                {
                    policy.Follow(false, FramePolicy.Limit, 120, vsync, ref target, ref sync);
                    AssertEx.Equal(15, target, "unfocused cap");
                    AssertEx.Equal(foregroundSync, sync, "focus loss keeps presentation mode");
                    policy.Follow(true, FramePolicy.Limit, 120, vsync, ref target, ref sync);
                    AssertEx.Equal(vsync ? -1 : 120, target, "foreground restored");
                    AssertEx.Equal(foregroundSync, sync, "focus gain keeps presentation mode");
                }
            }
        }

        public static void VSyncOverridesAndRestoresSelectedFps()
        {
            var policy = new FramePolicy();
            int target = 60,
                sync = 0;
            Step(policy, true, FramePolicy.Limit, 144, false, ref target, ref sync, 144, 0, true);
            Step(policy, true, FramePolicy.Limit, 144, true, ref target, ref sync, -1, 1, true);
            Step(policy, true, FramePolicy.Limit, 30, true, ref target, ref sync, -1, 1, false);
            Step(policy, true, FramePolicy.Limit, 144, false, ref target, ref sync, 144, 0, true);
            AssertEx.Equal(
                FramePolicy.Auto,
                FramePolicy.Normalize("sync"),
                "legacy sync mode is not an FPS policy"
            );
        }

        public static void AutoMatchesMonitorAndTracksFocusAndRefreshChanges()
        {
            AssertEx.Equal(60, FramePolicy.AutoFps(59.94), "fractional refresh rounds");
            AssertEx.Equal(75, FramePolicy.AutoFps(75), "not snapped to presets");
            AssertEx.Equal(360, FramePolicy.AutoFps(360), "high refresh retained");
            foreach (
                double invalid in new[]
                {
                    0d,
                    -1d,
                    double.NaN,
                    double.PositiveInfinity,
                    double.MaxValue,
                }
            )
                AssertEx.Equal(60, FramePolicy.AutoFps(invalid), "invalid detection fallback");
            var policy = new FramePolicy();
            int target = -1,
                sync = 1;
            policy.Follow(true, FramePolicy.Auto, 15, false, ref target, ref sync, 75);
            AssertEx.Equal(75, target, "auto ignores manual preset");
            AssertEx.Equal(0, sync, "auto disables VSync");
            AssertEx.Equal(
                false,
                policy.Follow(true, FramePolicy.Auto, 15, false, ref target, ref sync, 75),
                "stable detection"
            );
            policy.Follow(false, FramePolicy.Auto, 15, false, ref target, ref sync, 144);
            AssertEx.Equal(15, target, "background limit");
            AssertEx.Equal(0, sync, "background preserves presentation");
            policy.Follow(true, FramePolicy.Auto, 15, false, ref target, ref sync, 144);
            AssertEx.Equal(144, target, "new monitor on focus gain");
            policy.Follow(true, FramePolicy.Auto, 15, false, ref target, ref sync, 120);
            AssertEx.Equal(120, target, "monitor changes while focused");
        }

        static void Step(
            FramePolicy policy,
            bool focused,
            string mode,
            int fps,
            bool vsync,
            ref int target,
            ref int sync,
            int expectedTarget,
            int expectedSync,
            bool expectedChanged
        )
        {
            AssertEx.Equal(
                expectedChanged,
                policy.Follow(focused, mode, fps, vsync, ref target, ref sync),
                "change after " + mode
            );
            AssertEx.Equal(expectedTarget, target, "target after " + mode);
            AssertEx.Equal(expectedSync, sync, "VSync after " + mode);
        }
    }
}
