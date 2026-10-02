namespace SlopWorld
{
    // The opening and closing scenes share these controls.
    // Other components use these properties to suspend their work during either scene.
    public static class Cutscene
    {
        // Both directors keep this state only in memory.
        // Loading a save during a scene restores the interface.
        public static bool Playing => IntroDirector.UiHidden || NextPlanet.Leaving;

        // Hold agent-linked colony work before intro arrivals and throughout departure.
        public static bool AgentsHeld => IntroDirector.AgentsHeld || NextPlanet.Leaving;
    }
}
