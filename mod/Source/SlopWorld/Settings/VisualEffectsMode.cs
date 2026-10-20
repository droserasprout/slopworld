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
        // Mild keeps scanlines subtle; vignette and grain strengths are tuned independently.
        public static float ScanlineOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 0.25f
            : Normalize(value) == Massive ? 0.36f
            : 0.08f;

        public static float CornerOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 0.38f
            : Normalize(value) == Massive ? 0.54f
            : 0.195f;

        public static float GrainOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Moderate ? 40f / 255f
            : Normalize(value) == Massive ? 56f / 255f
            : 25f / 255f;

        // Hardware cursors bypass the overlay. A uniform RGB dim keeps them in tone
        // without tying cursor updates to pointer position or the game's frame rate.
        public static float CursorBrightness(string value) => 1f - ScanlineOpacity(value);

        public static float CursorOpacity(string value) => 1f - ScanlineOpacity(value) * 0.5f;
    }
}
