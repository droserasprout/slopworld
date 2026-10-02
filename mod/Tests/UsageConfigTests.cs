using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class UsageConfigTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("merges configured and live usage catalogs", MergesCatalogs);
        }

        static void MergesCatalogs()
        {
            var configured = new List<UsageCatalogInfo>
            {
                new UsageCatalogInfo { Key = "static", Rank = 7, DefaultPoll = false },
                new UsageCatalogInfo { Key = "shared", Rank = 3, Label = "Configured", Provider = "configured", Unit = "usd", DefaultPoll = false },
            };
            var live = new List<UsageCatalogInfo>
            {
                new UsageCatalogInfo { Key = "dynamic_window", Rank = 9, DefaultPoll = true },
                new UsageCatalogInfo { Key = "shared", Rank = 0, Label = "Live", Provider = "live", Unit = "pct", DefaultPoll = true },
                new UsageCatalogInfo { Key = "first", Rank = 1, DefaultPoll = true },
            };
            var merged = DaemonConfig.MergeUsageCatalogs(configured, live);
            AssertEx.Equal(4, merged.Count, "shared key is deduplicated");
            AssertEx.Sequence(new[] { "first", "shared", "static", "dynamic_window" },
                merged.ConvertAll(item => item.Key), "merged entries sort by rank regardless of input order");
            AssertEx.Equal("Configured", merged[1].Label, "configured label wins");
            AssertEx.Equal("configured", merged[1].Provider, "configured provider wins");
            AssertEx.Equal("usd", merged[1].Unit, "configured unit wins");
            AssertEx.Equal(3, merged[1].Rank, "configured rank wins");
            AssertEx.False(merged[1].DefaultPoll, "configured poll policy wins");
            AssertEx.True(merged[3].DefaultPoll, "live-only entry retains daemon poll policy");

        }
    }
}
