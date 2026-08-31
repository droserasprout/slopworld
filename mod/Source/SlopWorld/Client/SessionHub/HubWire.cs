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
    }
}
