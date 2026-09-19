namespace SlopWorld.Tests
{
    static class FramePolicyTests
    {
        public static void Transitions()
        {
            var policy = new FramePolicy();
            int target = 75, sync = 2;
            AssertEx.Equal(true, policy.Follow(true, "game", 60, ref target, ref sync),
                "legacy game mode uses VSync");
            AssertEx.Equal(-1, target, "legacy game target");
            AssertEx.Equal(1, sync, "legacy game VSync");
            Step(policy, false, "game", 60, ref target, ref sync, 15, 0);
            Step(policy, false, "sync", 60, ref target, ref sync, 15, 0);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);
            Step(policy, true, "limit", 144, ref target, ref sync, 144, 0);
            Step(policy, false, "limit", 144, ref target, ref sync, 15, 0);
            Step(policy, false, "game", 144, ref target, ref sync, 15, 0);
            Step(policy, true, "game", 144, ref target, ref sync, -1, 1);

            target = 90;
            sync = 0;
            Step(policy, true, "limit", -1, ref target, ref sync, 15, 0);
            Step(policy, true, "limit", 9999, ref target, ref sync, 240, 0);
            Step(policy, true, "invalid", 60, ref target, ref sync, -1, 1);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);

            target = 60;
            sync = 0;
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1);
            Step(policy, true, "game", 60, ref target, ref sync, -1, 1);
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
