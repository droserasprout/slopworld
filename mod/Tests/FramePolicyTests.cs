namespace SlopWorld.Tests
{
    static class FramePolicyTests
    {
        public static void Transitions()
        {
            AssertEx.Equal(15, FramePolicy.NearestPreset(-2147483633), "extreme negative FPS does not overflow");
            var policy = new FramePolicy();
            int target = 75, sync = 2;
            AssertEx.Equal(true, policy.Follow(true, "sync", 60, ref target, ref sync),
                "focused mode uses VSync");
            AssertEx.Equal(-1, target, "focused target");
            AssertEx.Equal(1, sync, "focused VSync");
            Step(policy, false, "sync", 60, ref target, ref sync, 15, 0, true);
            Step(policy, false, "sync", 60, ref target, ref sync, 15, 0, false);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1, true);
            Step(policy, true, "limit", 144, ref target, ref sync, 144, 0, true);
            Step(policy, false, "limit", 144, ref target, ref sync, 15, 0, true);
            Step(policy, false, "sync", 144, ref target, ref sync, 15, 0, false);
            Step(policy, true, "sync", 144, ref target, ref sync, -1, 1, true);

            target = 90;
            sync = 0;
            Step(policy, true, "limit", -1, ref target, ref sync, 15, 0, true);
            Step(policy, true, "limit", 9999, ref target, ref sync, 240, 0, true);
            Step(policy, true, "invalid", 60, ref target, ref sync, -1, 1, true);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1, false);

            target = 60;
            sync = 0;
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1, true);
            Step(policy, true, "sync", 60, ref target, ref sync, -1, 1, false);
        }

        static void Step(FramePolicy policy, bool focused, string mode, int fps,
            ref int target, ref int sync, int expectedTarget, int expectedSync, bool expectedChanged)
        {
            AssertEx.Equal(expectedChanged, policy.Follow(focused, mode, fps, ref target, ref sync),
                "change after " + mode);
            AssertEx.Equal(expectedTarget, target, "target after " + mode);
            AssertEx.Equal(expectedSync, sync, "VSync after " + mode);
        }
    }
}
