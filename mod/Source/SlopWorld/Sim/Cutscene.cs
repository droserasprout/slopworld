namespace SlopWorld
{
    // Two scenes have the board - the opening one and the closing one - and they want
    // the same things of everything else. So everything that stands down asks here
    // rather than naming one of them, which is what stops a second scene meaning
    // eight files each learning about it and one of them being missed.
    public static class Cutscene
    {
        // Runtime state in both directors, so a save loaded mid-scene comes back with the
        // interface on rather than stuck without it.
        public static bool Playing => IntroDirector.UiHidden || NextPlanet.Leaving;

        // An agent must not arrive in the middle of the opening scene, and there is
        // nothing worth spawning onto a map that is on fire.
        public static bool AgentsHeld => IntroDirector.AgentsHeld || NextPlanet.Leaving;
    }
}
