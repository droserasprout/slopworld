using System;
using System.Globalization;

namespace SlopWorld
{
    // Numeric settings are intentionally validated before they are copied into DaemonConfig.
    // Keeping this parser game-free also makes the accepted range a testable wire contract.
    public static class DaemonConfigValidation
    {
        public const int MinimumSeconds = 10;
        public const int MaximumSeconds = 3600;
        public const int MinimumTitleChars = 0;
        public const int MaximumTitleChars = 2000;

        public static bool WholeSeconds(string text, bool blankMeansInheritance,
                                        out int value, out string error)
        {
            string input = (text ?? "").Trim();
            if (input.Length == 0 && blankMeansInheritance)
            {
                value = 0;
                error = null;
                return true;
            }

            if (!Digits(input) || !int.TryParse(input, NumberStyles.None,
                                                CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                error = blankMeansInheritance
                    ? "Leave blank to inherit, or enter whole seconds from 10 to 3,600."
                    : "Enter whole seconds from 10 to 3,600.";
                return false;
            }

            if (value < MinimumSeconds || value > MaximumSeconds)
            {
                error = blankMeansInheritance
                    ? "Leave blank to inherit, or enter a value from 10 to 3,600 seconds."
                    : "Enter a value from 10 to 3,600 seconds.";
                return false;
            }

            error = null;
            return true;
        }

        public static bool TitleMinimum(string text, out int value, out string error)
        {
            string input = (text ?? "").Trim();
            if (!Digits(input) || !int.TryParse(input, NumberStyles.None,
                                                CultureInfo.InvariantCulture, out value))
            {
                value = 0;
                error = "Enter a whole number from 0 to 2,000 characters.";
                return false;
            }

            if (value < MinimumTitleChars || value > MaximumTitleChars)
            {
                error = "Enter a value from 0 to 2,000 characters.";
                return false;
            }

            error = null;
            return true;
        }

        static bool Digits(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
                if (text[i] < '0' || text[i] > '9') return false;
            return true;
        }
    }
}
