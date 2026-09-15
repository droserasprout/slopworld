using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SlopWorld
{
    // The little JSON-shaping helpers the hub services share: escaping a name into a path
    // segment, rendering a tip list, and formatting a slider value the daemon will accept.
    static class HubWire
    {
        // A library item's (or session's) name is free-form, so it can carry anything a path
        // segment objects to.
        public static string Esc(string name) => Uri.EscapeDataString(name ?? "");

        // `randomTips` fills a waiting breadcrumb's `{{ random_tip }}`, one per mention.
        public static string Tips(List<string> tips) =>
            tips == null ? "[]" : "[" + string.Join(",", tips.Select(JVal.Q).ToArray()) + "]";

        // Invariant, and short: a comma for a decimal point is not JSON, and the daemon
        // has no use for the last four digits of a slider.
        public static string Num(float f) => f.ToString("0.###", CultureInfo.InvariantCulture);

        // Only field selection belongs here; skipped payloads use the decoder's JSON grammar.
        internal static bool TryLiveScreenName(string json, out string name)
        {
            name = null;
            try
            {
                var reader = new JsonReader(json);
                reader.Expect('{');
                bool first = true, typeSeen = false, screenSeen = false;
                string candidate = null;
                while (reader.More(ref first, '}'))
                {
                    string key = reader.String();
                    reader.Expect(':');
                    switch (key)
                    {
                        case "t":
                            if (typeSeen || reader.String() != WireContract.Events.Screen) return false;
                            typeSeen = true;
                            break;
                        case "screen":
                            if (screenSeen || !ReadScreen(ref reader, out candidate)) return false;
                            screenSeen = true;
                            break;
                        default: reader.Value(false, 1); break;
                    }
                }
                reader.Finish();
                if (!typeSeen || !screenSeen || string.IsNullOrEmpty(candidate)) return false;
                name = candidate;
                return true;
            }
            catch (FormatException) { return false; }
        }

        static bool ReadScreen(ref JsonReader reader, out string name)
        {
            name = null;
            reader.Expect('{');
            bool first = true;
            int seen = 0;
            while (reader.More(ref first, '}'))
            {
                string key = reader.String();
                reader.Expect(':');
                int field = key == "name" ? 1 : key == "off" ? 2 : key == "request_id" ? 4 : 0;
                if ((seen & field) != 0) return false;
                seen |= field;
                if (field == 1) name = reader.String();
                else if (field != 0)
                {
                    if (reader.Number() != 0) return false;
                }
                else reader.Value(false, 2);
            }
            return true;
        }
    }
}
