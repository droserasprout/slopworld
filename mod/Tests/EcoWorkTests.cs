using System;
using System.Collections.Generic;
using System.Globalization;

namespace SlopWorld.Tests
{
    static class EcoWorkTests
    {
        public static void Maintenance()
        {
            foreach (int fps in new[] { 15, 60, 144 })
            {
                var gate = new HiddenWorkCadence();
                int runs = 0;
                for (int frame = 0; frame < fps * 10; frame++)
                    if (gate.Run(true, frame / (double)fps)) runs++;
                AssertEx.Equal(true, runs >= 37 && runs <= 40, "bounded hidden maintenance at " + fps);
                for (int frame = 0; frame < fps; frame++)
                    AssertEx.Equal(true, gate.Run(false, 10 + frame / (double)fps), "visible frames always run");
            }
            var transitions = new HiddenWorkCadence();
            AssertEx.Equal(true, transitions.Run(true, 0), "first hidden update");
            AssertEx.Equal(false, transitions.Run(true, 0.01), "hidden wait");
            AssertEx.Equal(true, transitions.Run(false, 0.02), "reveal before deadline");
            AssertEx.Equal(true, transitions.Run(true, 0.03), "hide again");
            AssertEx.Equal(false, transitions.Run(true, 0.25), "deadline resets on hide");
            AssertEx.Equal(true, transitions.Run(true, 100), "stall runs once");
            AssertEx.Equal(false, transitions.Run(true, 100), "no catch-up");
            AssertEx.Equal(true, transitions.Run(true, 0), "clock reset recovers");
        }

        public static void Membership()
        {
            var index = new ColonySessionIndex();
            var sessions = new List<SessionInfo>
            {
                new SessionInfo { Name = "a" },
                new SessionInfo { Name = "worker", Worker = true },
                new SessionInfo { Name = "viewer", Ephemeral = true }
            };
            AssertEx.Equal(true, index.Refresh(sessions, 1), "initial snapshot");
            AssertEx.Equal(true, index.Contains("a"), "colony member");
            AssertEx.Equal(false, index.Contains("worker"), "worker excluded");
            AssertEx.Equal(false, index.Contains("viewer"), "viewer excluded");
            AssertEx.Equal(false, index.Refresh(sessions, 1), "unchanged revision");
            sessions[0].State = AgentState.Working;
            AssertEx.Equal(false, index.Refresh(sessions, 2), "status preserves membership");
            sessions[0] = new SessionInfo { Name = "renamed" };
            AssertEx.Equal(true, index.Refresh(sessions, 3), "same-count rename");
            AssertEx.Equal(false, index.Contains("a"), "old name removed");
            sessions[1].Worker = false;
            AssertEx.Equal(true, index.Refresh(sessions, 4), "role change");
            AssertEx.Equal(true, index.Contains("worker"), "promoted member");
            sessions.Clear();
            AssertEx.Equal(true, index.Refresh(sessions, 5), "empty online snapshot");
            AssertEx.Equal(false, index.Contains("renamed"), "removed members cleared");
        }

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
            AssertEx.Equal(true, cache.Prepare(usage, null), "first usage");
            AssertEx.Equal("claude_session,claude_week,openai_week",
                string.Join(",", cache.Rows), "daemon-resolved rows ordered");
            AssertEx.Equal(false, cache.Prepare(usage, null), "snapshot cached");
            var config = new DaemonConfig();
            cache.Prepare(usage, config);
            AssertEx.Equal(false, cache.Rows.Contains("openrouter_balance"), "daemon disabled row stays absent");
            config.UsageItems["openai_week"] = new DaemonConfig.UsageItemConfig { Poll = false };
            AssertEx.Equal(true, cache.Prepare(usage, config), "new flag invalidates");
            AssertEx.Equal(true, cache.Rows.Contains("openai_week"), "display follows daemon rows");
            config.UsageItems["openai_week"].Poll = true;
            AssertEx.Equal(true, cache.Prepare(usage, config), "in-place flag invalidates");
            AssertEx.Equal(true, cache.Rows.Contains("openai_week"), "row remains authoritative");
            config.UsageItems["openai_week"] = null;
            AssertEx.Equal(true, cache.Prepare(usage, config), "null entry invalidates");
            AssertEx.Equal(true, cache.Rows.Contains("openai_week"), "null retains daemon row");
            AssertEx.Equal(true, cache.Prepare(new UsageInfo(), config), "replacement snapshot invalidates");
            AssertEx.Equal(0, cache.Rows.Count, "old rows removed");
            cache.Prepare(null, null);
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
        }
    }
}
