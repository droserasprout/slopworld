namespace SlopWorld
{
    // Persisted phases also own the gates needed to resume the scene under Eco.
    internal enum IntroPhase
    {
        Waiting,
        Populate,
        Core,
        Fume,
        Seed,
        Bloom,
        Done,
    }

    internal static class IntroSceneState
    {
        public static bool UiHidden(IntroPhase phase) =>
            phase != IntroPhase.Waiting && phase != IntroPhase.Done;

        public static bool AgentsHeld(IntroPhase phase) =>
            UiHidden(phase) && phase != IntroPhase.Bloom;

        // The player must also wait before the first update arms the opening scene.
        public static bool PlayerHeld(IntroPhase phase) =>
            phase != IntroPhase.Bloom && phase != IntroPhase.Done;
    }
}
