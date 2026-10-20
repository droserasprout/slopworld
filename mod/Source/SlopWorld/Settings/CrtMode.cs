namespace SlopWorld
{
    // Owns profile CRT choices; the display overlay owns texture construction and lifetime.
    public static class CrtMode
    {
        public const string Off = "off";
        public const string Mild = "mild";
        public const string Intense = "intense";
        public const string FullOn = "full-on";

        public static string Normalize(string value) =>
            value == Off || value == Intense || value == FullOn ? value : Mild;

        public static string Label(string value) =>
            Normalize(value) == Off ? "Meh"
            : Normalize(value) == Intense ? "Moderate"
            : Normalize(value) == FullOn ? "Massive"
            : "Mild";

        // Absolute alpha values keep tuning here, independent of renderer multipliers.
        // Mild is the calibrated baseline; higher levels add roughly equal opacity steps
        // while keeping corner shading near 1.5 times the darkest scanline.
        public static float ScanlineOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Intense ? 0.25f
            : Normalize(value) == FullOn ? 0.36f
            : 0.139755f;

        public static float CornerOpacity(string value) =>
            Normalize(value) == Off ? 0f
            : Normalize(value) == Intense ? 0.38f
            : Normalize(value) == FullOn ? 0.54f
            : 0.215985f;

        // Hardware cursors bypass the overlay. A uniform RGB dim keeps them in tone
        // without tying cursor updates to pointer position or the game's frame rate.
        public static float CursorBrightness(string value) => 1f - ScanlineOpacity(value);

        public static float CursorOpacity(string value) => 1f - ScanlineOpacity(value) * 0.5f;
    }
}
