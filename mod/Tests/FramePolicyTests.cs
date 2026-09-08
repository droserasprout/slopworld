namespace SlopWorld.Tests
{
    static class FramePolicyTests
    {
        public static void Transitions()
        {
            var policy = new FramePolicy();
            int target = 75, sync = 2;
            AssertEx.Equal(false, policy.Follow(true, "game", 60, ref target, ref sync), "inherit untouched");
            Step(policy, false, "game", 60, ref target, ref sync, 15, 0);
            Step(policy, false, "sync", 60, ref target, ref sync, 15, 0);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);
            Step(policy, true, "limit", 144, ref target, ref sync, 144, 0);
            Step(policy, false, "limit", 144, ref target, ref sync, 15, 0);
            Step(policy, false, "game", 144, ref target, ref sync, 15, 0);
            Step(policy, true, "game", 144, ref target, ref sync, 75, 2);
            // A later override must capture updated game preferences, not the first pair.
            target = 90;
            sync = 0;
            Step(policy, true, "limit", -1, ref target, ref sync, 30, 0);
            Step(policy, true, "limit", 9999, ref target, ref sync, 360, 0);
            Step(policy, true, "invalid", 60, ref target, ref sync, 90, 0);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);
            // Reapplying game preferences while owned must not cancel the chosen override.
            target = 60;
            sync = 0;
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);
            Step(policy, true, "game", 60, ref target, ref sync, 90, 0);
        }

        static void Step(FramePolicy policy, bool focused, string mode, int fps,
            ref int target, ref int sync, int expectedTarget, int expectedSync)
        {
            policy.Follow(focused, mode, fps, ref target, ref sync);
            AssertEx.Equal(expectedTarget, target, "target after " + mode);
            AssertEx.Equal(expectedSync, sync, "VSync after " + mode);
        }
    }
}
