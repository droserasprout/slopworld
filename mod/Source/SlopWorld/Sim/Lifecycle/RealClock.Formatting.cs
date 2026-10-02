using System;
using Verse;

namespace SlopWorld
{
    public partial class RealClock
    {
        // Ticks the game runs per real second at Normal speed.
        public const float TicksPerRealSecond = 60f;

        // Real durations use seconds through days, never calendar years or quadrums.
        // Calendar vague bounds are deliberately replaced with a concrete duration.
        public static string GameTickDuration(int ticks, bool allowSeconds = true,
            bool shortForm = false, bool canUseDecimals = true,
            bool canUseDecimalsShortForm = false, bool allowHours = true, string format = null) =>
            FormatPeriod(ticks / TicksPerRealSecond, allowSeconds, shortForm, allowHours,
                format ?? (canUseDecimals && (!shortForm || canUseDecimalsShortForm) ? "0.#" : "0"));

        // Log ages retain whole-unit display; game duration callers provide their display options above.
        public static string Period(float seconds, bool shortForm = false) =>
            FormatPeriod(seconds, true, shortForm, true, null);

        static string FormatPeriod(float seconds, bool allowSeconds, bool shortForm, bool allowHours, string format)
        {
            float s = Math.Max(seconds, 0f);
            if (s >= 86400f || !allowHours)
                return Unit(s / 86400f, "Period1Day", "PeriodDays", "LetterDay", shortForm, format);
            if (s >= 3600f) return Unit(s / 3600f, "Period1Hour", "PeriodHours", "LetterHour", shortForm, format);
            if (s >= 60f || !allowSeconds) return Unit(s / 60f, null, null, "LetterMinute", shortForm, format);
            return Unit(s, "Period1Second", "PeriodSeconds", "LetterSecond", shortForm, format);
        }

        // The base-game calendar has no minute unit. Supply the mod's own words for minutes.
        static string Unit(float count, string oneKey, string manyKey, string letterKey, bool shortForm, string format)
        {
            string number = format == null ? Math.Floor(count).ToString() : count.ToString(format);
            if (shortForm) return number + letterKey.Translate();
            if (oneKey == null) return number == "1" ? "1 minute" : number + " minutes";
            return number == "1" ? oneKey.Translate() : manyKey.Translate(number);
        }
    }
}
