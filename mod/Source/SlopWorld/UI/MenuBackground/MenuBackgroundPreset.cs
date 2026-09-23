using static SlopWorld.MenuBackgroundTuning;

namespace SlopWorld
{
    // Preset parameters and the frame set shape shared by runtime playback and baking.
    internal sealed class MenuBackgroundPreset
    {
        public readonly string Name;
        // A single depth has no moving layer.
        public readonly int Depths;
        public readonly int Phases;
        // Closed motion must be played in phase order. Independent noise does not.
        public readonly bool Closed;

        public MenuBackgroundPreset(string name, int depths, int phases, bool closed)
        {
            Name = name; Depths = depths; Phases = phases; Closed = closed;
        }

        public int Total => Onset + Depths * Phases;
    }

    internal static class MenuBackgroundPresets
    {
        static readonly MenuBackgroundPreset Rotting =
            new MenuBackgroundPreset("rot", 5, 8, false);

        internal static readonly MenuBackgroundPreset Sparkling =
            new MenuBackgroundPreset("glow", 1, 24, true);

        internal static MenuBackgroundPreset Chosen =>
            Settings.GrandmaMode ? Sparkling : Rotting;
    }
}
