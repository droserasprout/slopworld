using System;

namespace SlopWorld
{
    // Preference keys shared by settings and workspace geometry.
    public static class NavigationSide
    {
        public const string Left = "left";
        public const string Right = "right";

        public static string Normalize(string value) =>
            string.Equals(value, Right, StringComparison.OrdinalIgnoreCase) ? Right : Left;

        public static string Label(string value) => Normalize(value) == Right ? "Right" : "Left";
    }

    public static class UiDensityPreset
    {
        public const string Default = "default";
        public const string Compact = "compact";

        public static string Normalize(string value) =>
            string.Equals(value, Compact, StringComparison.OrdinalIgnoreCase)
                ? Compact : Default;

        public static string Label(string value) =>
            Normalize(value) == Compact ? "Compact" : "Default";
    }

}
