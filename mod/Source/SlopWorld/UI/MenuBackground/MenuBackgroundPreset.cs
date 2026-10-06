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

        public MenuBackgroundPreset(string name, int depths, int phases)
        {
            Name = name; Depths = depths; Phases = phases;
        }

        public int Total => Onset + Depths * Phases;
    }

    internal static class MenuBackgroundPresets
    {
        static readonly MenuBackgroundPreset Rotting =
            new MenuBackgroundPreset("rot", 5, 8);

        internal static MenuBackgroundPreset Chosen => Rotting;
    }
}
