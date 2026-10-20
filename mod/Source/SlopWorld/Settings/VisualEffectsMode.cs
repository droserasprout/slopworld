namespace SlopWorld
{
    // Owns profile visual-effect strengths; renderers own texture construction and lifetime.
    public static class VisualEffectsMode
    {
        public const string Off = "off";
        public const string Mild = "mild";
        public const string Moderate = "moderate";
        public const string Massive = "massive";

        public static string Normalize(string value) =>
            value == Off || value == Moderate || value == Massive ? value : Mild;

        public static string Label(string value) =>
            Normalize(value) == Off ? "Meh"
            : Normalize(value) == Moderate ? "Moderate"
            : Normalize(value) == Massive ? "Massive"
            : "Mild";

        // Absolute alpha values keep tuning here, independent of renderer multipliers.
        // Equal steps above Mild keep the progression smooth for each effect.
        public static float ScanlineOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 0.17f
            : Normalize(value) == Massive ? 0.26f
            : 0.08f;

        public static float CornerOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 0.32f
            : Normalize(value) == Massive ? 0.445f
            : 0.195f;

        public static float GrainOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 37f / 255f
            : Normalize(value) == Massive ? 49f / 255f
            : 25f / 255f;

        // Hardware cursors bypass the overlay. Match the average dimming of scanlines
        // covering one-third of the rows, keeping alpha intact for pointer visibility.
        public static float CursorBrightness(string value) => 1f - ScanlineOpacity(value) / 3f;

        public static float CursorOpacity(string value) => 1f;
    }
}
