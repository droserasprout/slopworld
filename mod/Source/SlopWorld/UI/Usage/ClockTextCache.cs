using System;
using System.Globalization;

namespace SlopWorld
{
    internal sealed class ClockTextCache
    {
        long _second = -1;
        string _format;
        CultureInfo _culture;
        public string Short { get; private set; }
        public string Tooltip { get; private set; }

        public bool Prepare(DateTime now, string format, CultureInfo culture)
        {
            culture = culture ?? CultureInfo.CurrentCulture;
            long second = now.Ticks / TimeSpan.TicksPerSecond;
            format = TimeFormat.Normalize(format);
            // Mutable cultures can change their date/time data without changing identity.
            // Cache only read-only cultures; editable ones are formatted on every call.
            if (culture.IsReadOnly && _second == second && _format == format && ReferenceEquals(_culture, culture)) return false;
            _second = second;
            _format = format;
            _culture = culture;
            bool twelve = format == TimeFormat.TwelveHour;
            Short = now.ToString(twelve ? "h:mm tt" : "HH:mm", culture);
            Tooltip = now.ToString("dddd, d MMMM yyyy", culture) + "\n" +
                now.ToString(twelve ? "h:mm:ss tt" : "HH:mm:ss", culture);
            return true;
        }
    }
}
