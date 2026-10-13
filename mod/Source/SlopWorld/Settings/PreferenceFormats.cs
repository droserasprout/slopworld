using System;
using System.Globalization;

namespace SlopWorld
{
    // Normalizes preference keys and formats timestamps from caller-supplied choices.
    public static class StatusbarClockMode
    {
        public const string Right = "right";
        public const string Center = "center";
        public const string Hidden = "hidden";

        public static string Normalize(string mode)
        {
            if (mode == Center)
                return Center;
            if (mode == Hidden)
                return Hidden;
            return Right;
        }

        public static string Label(string mode)
        {
            if (mode == Center)
                return "Center";
            if (mode == Hidden)
                return "Hidden";
            return "Right";
        }
    }

    public static class StatusbarSummaryMode
    {
        public const string Left = "left";
        public const string Center = "center";

        public static string Normalize(string mode) => mode == Center ? Center : Left;

        public static string Label(string mode) => Normalize(mode) == Center ? "Center" : "Left";
    }

    public static class TimeFormat
    {
        public const string TwentyFourHour = "24-hour";
        public const string TwelveHour = "12-hour";

        public static string Normalize(string format) =>
            format == TwelveHour ? TwelveHour : TwentyFourHour;

        public static string Label(string format) =>
            Normalize(format) == TwelveHour ? "12-hour" : "24-hour";

        public static string Short(DateTime time, string format) =>
            time.ToString(
                Normalize(format) == TwelveHour ? "h:mm tt" : "HH:mm",
                CultureInfo.CurrentCulture
            );

        public static string Long(DateTime time, string format) =>
            time.ToString(
                Normalize(format) == TwelveHour ? "h:mm:ss tt" : "HH:mm:ss",
                CultureInfo.CurrentCulture
            );
    }

    public static class DateFormat
    {
        public const string YearFirst = "yyyy-MM-dd";
        public const string DayFirst = "dd/MM/yyyy";
        public const string MonthFirst = "MM/dd/yyyy";

        public static string Normalize(string format) =>
            format == DayFirst || format == MonthFirst ? format : YearFirst;

        public static string Label(string format) => Normalize(format).ToUpperInvariant();

        // Numeric date choices use Gregorian dates and literal separators regardless of locale.
        public static string Short(DateTime date, string format) =>
            date.ToString(Normalize(format), CultureInfo.InvariantCulture);
    }

    // The meme choice changes only the displayed label; temperatures still follow game preferences.
    public static class TemperatureUnit
    {
        public const string MemeMode = "glazed-bald";
        public const string MemeLabel = "glazed🍩/bald🦅";

        public static bool IsMemeMode(string unit) => unit == MemeMode;

        public static string Normalize(string unit) => IsMemeMode(unit) ? MemeMode : "";

        public static string Label(string unit, string fallback) =>
            IsMemeMode(unit) ? MemeLabel : fallback;
    }
}
