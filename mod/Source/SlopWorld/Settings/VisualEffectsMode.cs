namespace SlopWorld
{
    // Owns profile visual-effect strengths; renderers own texture construction and lifetime.
    public static class VisualEffectsMode
    {
        public const string Off = "off";
        public const string Mild = "mild";
        public const string Moderate = "moderate";
        public const string Massive = "massive";

        // Absolute alpha values keep tuning here, independent of renderer multipliers.
        // Equal steps above Mild keep the progression smooth for each effect.
        static readonly Preset OffPreset = new Preset(
            Off,
            "Off",
            scanlines: 0f,
            corners: 0f,
            grain: 0f
        );
        static readonly Preset MildPreset = new Preset(
            Mild,
            "Mild",
            scanlines: 0.05f,
            corners: 0.195f,
            grain: 0.07f
        );
        static readonly Preset ModeratePreset = new Preset(
            Moderate,
            "Moderate",
            scanlines: 0.135f,
            corners: 0.32f,
            grain: 0.115f
        );
        static readonly Preset MassivePreset = new Preset(
            Massive,
            "Massive",
            scanlines: 0.22f,
            corners: 0.445f,
            grain: 0.16f
        );

        readonly struct Preset
        {
            internal readonly string Key;
            internal readonly string Label;
            internal readonly float Scanlines;
            internal readonly float Corners;
            internal readonly float Grain;

            internal Preset(string key, string label, float scanlines, float corners, float grain)
            {
                Key = key;
                Label = label;
                Scanlines = scanlines;
                Corners = corners;
                Grain = grain;
            }
        }

        static Preset Resolve(string value) =>
            value == Off ? OffPreset
            : value == Moderate ? ModeratePreset
            : value == Massive ? MassivePreset
            : MildPreset;

        public static string Normalize(string value) => Resolve(value).Key;

        public static string Label(string value) => Resolve(value).Label;

        public static float ScanlineOpacity(string value) => Resolve(value).Scanlines;

        public static float CornerOpacity(string value) => Resolve(value).Corners;

        public static float GrainOpacity(string value) => Resolve(value).Grain;

        // Hardware cursors bypass the overlay. Match the average dimming of scanlines
        // covering one-third of the rows, keeping alpha intact for pointer visibility.
        public static float CursorBrightness(string value) => 1f - ScanlineOpacity(value) / 3f;

        public static float CursorOpacity(string value) => 1f;
    }
}
