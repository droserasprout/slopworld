using System.Globalization;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class UsageReadoutTests
    {
        public static void Usage()
        {
            var cache = new UsageRowsCache();
            var usage = new UsageInfo();
            usage.Sources.Add("openai");
            usage.Sources.Add("anthropic");
            usage.Sources.Add("openrouter");
            usage.Windows.Add(new UsageWindow { Key = "openai_week" });
            usage.Rows.Add(new UsageRow { Key = "claude_session", Rank = 0, Poll = true });
            usage.Rows.Add(new UsageRow { Key = "claude_week", Rank = 1, Poll = true });
            usage.Rows.Add(new UsageRow { Key = "openai_week", Rank = 3, Poll = true });
            usage.Rows.Add(new UsageRow { Key = "openrouter_balance", Rank = 4, Poll = false });
            AssertEx.Equal(true, cache.Prepare(usage), "first usage");
            AssertEx.Equal("claude_session,claude_week,openai_week",
                string.Join(",", cache.Rows), "daemon-resolved rows ordered");
            AssertEx.Equal(false, cache.Prepare(usage), "snapshot cached");
            AssertEx.Equal(false, cache.Rows.Contains("openrouter_balance"), "daemon disabled row stays absent");
            var replacement = new UsageInfo();
            replacement.Rows.Add(new UsageRow { Key = "openrouter_balance", Rank = 0, Poll = true });
            replacement.Rows.Add(new UsageRow { Key = "openai_week", Rank = 1, Poll = false });
            AssertEx.Equal(true, cache.Prepare(replacement), "replacement polling invalidates");
            AssertEx.Equal("openrouter_balance", string.Join(",", cache.Rows), "replacement owns polling");
            AssertEx.Equal(true, cache.Prepare(new UsageInfo()), "empty replacement invalidates");
            AssertEx.Equal(0, cache.Rows.Count, "old rows removed");
            cache.Prepare(null);
            AssertEx.Equal(0, cache.Rows.Count, "missing usage safe");
        }
        public static void Clock()
        {
            var cache = new ClockTextCache();
            var culture = CultureInfo.InvariantCulture;
            var now = new DateTime(2026, 9, 8, 23, 59, 59);
            AssertEx.Equal(true, cache.Prepare(now, TimeFormat.TwentyFourHour, culture), "initial clock");
            AssertEx.Equal("23:59", cache.Short, "24-hour clock");
            AssertEx.Equal(false, cache.Prepare(now.AddMilliseconds(900), TimeFormat.TwentyFourHour, culture), "subsecond reuse");
            AssertEx.Equal(true, cache.Prepare(now.AddSeconds(1), TimeFormat.TwentyFourHour, culture), "midnight refresh");
            AssertEx.Equal("00:00", cache.Short, "date rollover");
            AssertEx.Equal(true, cache.Tooltip.Contains("9 September 2026"), "tooltip date rolls over");
            AssertEx.Equal(true, cache.Prepare(now.AddSeconds(1), TimeFormat.TwelveHour, culture), "format change same second");
            AssertEx.Equal("12:00 AM", cache.Short, "12-hour clock");
            AssertEx.Equal(true, cache.Prepare(now.AddSeconds(1), TimeFormat.TwelveHour, CultureInfo.GetCultureInfo("fr-FR")), "locale refresh");
            AssertEx.Equal(true, cache.Tooltip.Contains("septembre"), "localized date");
            var editable = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            cache.Prepare(now, TimeFormat.TwelveHour, editable);
            editable.DateTimeFormat.PMDesignator = "evening";
            AssertEx.Equal(true, cache.Prepare(now, TimeFormat.TwelveHour, editable), "mutable locale refresh same second");
            AssertEx.Equal("11:59 evening", cache.Short, "in-place designator change");
            editable.DateTimeFormat.TimeSeparator = ".";
            cache.Prepare(now, TimeFormat.TwentyFourHour, editable);
            AssertEx.Equal("23.59", cache.Short, "in-place time separator change");
        }

    }
}
