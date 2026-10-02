using System;

namespace Verse
{
    public static class DurationTranslations
    {
        public static string Translate(this string key)
        {
            switch (key)
            {
                case "LetterDay": return "d";
                case "LetterHour": return "h";
                case "LetterMinute": return "m";
                case "LetterSecond": return "s";
                case "Period1Day": return "1 day";
                case "Period1Hour": return "1 hour";
                case "Period1Second": return "1 second";
                default: throw new ArgumentException(key);
            }
        }
        public static string Translate(this string key, string number) =>
            number + " " + key.Substring("Period".Length).ToLowerInvariant();
    }
}
