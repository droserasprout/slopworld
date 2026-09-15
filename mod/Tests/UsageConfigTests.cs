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
                new UsageCatalogInfo { Key = "static", Rank = 1, DefaultPoll = false },
            };
            var live = new List<UsageCatalogInfo>
            {
                new UsageCatalogInfo { Key = "dynamic_window", Rank = 9, DefaultPoll = true },
            };

            var merged = DaemonConfig.MergeUsageCatalogs(configured, live);
            AssertEx.Equal(2, merged.Count, "live dynamic usage window is visible");
            AssertEx.Equal("static", merged[0].Key, "configured catalog keeps its order");
            AssertEx.Equal("dynamic_window", merged[1].Key, "dynamic catalog entry is appended");
            AssertEx.True(merged[1].DefaultPoll,
                          "dynamic entry keeps daemon-provided default poll policy");
        }
    }
}
