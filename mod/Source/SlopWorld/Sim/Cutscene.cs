namespace SlopWorld
{
    /// <summary>
    /// Whether a scene has the board. Two of them do - the opening one
    /// (<see cref="IntroDirector"/>) and the closing one (<see cref="NextPlanet"/>)
    /// - and what they want of the rest of the mod is the same thing: the interface
    /// away, nothing on the map clickable, the reconcile standing still. So
    /// everything that stands down asks here rather than naming one of them, which
    /// is what stops the second scene from being eight files each learning about it
    /// and one of them being missed.
    /// </summary>
    public static class Cutscene
    {
        /// <summary>Nothing is drawn over the map and nothing on it can be picked
        /// up. Runtime state in both directors, so a save loaded mid-scene comes
        /// back with the interface on rather than stuck without it.</summary>
        public static bool Playing => IntroDirector.UiHidden || NextPlanet.Leaving;

        /// <summary>The reconcile holds off. An agent must not arrive in the middle
        /// of the opening scene, and there is nothing worth spawning onto a map that
        /// is on fire and about to be thrown away.</summary>
        public static bool AgentsHeld => IntroDirector.AgentsHeld || NextPlanet.Leaving;
    }
}
