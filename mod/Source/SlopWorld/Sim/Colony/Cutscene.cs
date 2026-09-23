namespace SlopWorld
{
    // The opening and closing scenes share these controls.
    // Other components use these properties to suspend their work during either scene.
    public static class Cutscene
    {
        // Both directors keep this state only in memory.
        // Loading a save during a scene restores the interface.
        public static bool Playing => IntroDirector.UiHidden || NextPlanet.Leaving;

        // Hold agent arrivals until the opening scene permits them.
        // Also hold arrivals while the colony leaves the planet.
        public static bool AgentsHeld => IntroDirector.AgentsHeld || NextPlanet.Leaving;
    }
}
