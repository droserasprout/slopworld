using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // When the last poll fails, Ok is false and Error explains the failure.
    // Keep the windows from the last successful poll.
    public class UsageInfo
    {
        public bool Ok;
        public string Error;
        public string Plan = "";
        public List<UsageWindow> Windows = new List<UsageWindow>();
        public List<UsageCatalogInfo> Catalog = new List<UsageCatalogInfo>();
        public List<UsageRow> Rows = new List<UsageRow>();

        // Providers that the daemon polls, including providers that do not respond.
        // Keep their display rows visible during failures.
        public List<string> Sources = new List<string>();
        // A merged snapshot can include both failed and successful providers.
        // Use this list to dim only rows from failed providers.
        public List<string> FailedSources = new List<string>();

        // Monotonic timestamp of the last good data. Failed polls retain it so stale snapshots
        // do not appear fresh or restart reset countdowns.
        public float Heard;

        public bool Any => Windows.Count > 0;

        // Identify failed providers without marking other providers as failed.
        public bool SourceFailed(string source) => FailedSources.Contains(source);

        public UsageRow Row(string key) => Rows.FirstOrDefault(row => row.Key == key);

        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        // Clamp remaining time to zero so an expired window shows that its reset is due.
        public long Remaining(UsageWindow w) =>
            w.ResetsIn < 0 ? -1 : Math.Max(0L, w.ResetsIn - (long)Age);

        // prev contains the displayed snapshot.
        // A failed poll returns previous values without making them current again.
        public static UsageInfo FromWire(Wire.UsageSnapshot j, UsageInfo prev = null)
        {
            bool ok = j.Ok;
            return Read(j, ok || prev == null
                ? UnityEngine.Time.realtimeSinceStartup
                : prev.Heard);
        }

        static UsageInfo Read(Wire.UsageSnapshot j, float heard) => new UsageInfo
        {
            Ok = j.Ok,
            Error = !j.HasError ? null : j.Error,
            Plan = j.Plan,
            Sources = j.Sources.ToList(),
            FailedSources = j.FailedSources.ToList(),
            Catalog = j.Catalog.Select(UsageCatalogInfo.FromWire).ToList(),
            Rows = j.Rows.Select(UsageRow.FromWire).ToList(),
            Heard = heard,
            Windows = j.Windows.Select(UsageWindow.FromWire).ToList(),
        };
    }

    public sealed class UsageRow
    {
        public string Key = "";
        public string Label = "";
        public string Provider = "";
        public string Unit = WireProtocol.UsageUnit.Pct;
        public int Rank;
        public bool Poll;
        public bool Stale;
        public UsageWindow Window;

        public static UsageRow FromWire(Wire.UsageRow j)
        {
            var value = new UsageRow
            {
                Key = j.Key,
                Label = j.Label,
                Provider = j.Provider,
                Unit = j.Unit,
                Rank = (int)j.Rank,
                Poll = j.Poll,
                Stale = j.Stale,
            };
            if (j.Window != null)
                value.Window = UsageWindow.FromWire(j.Window);
            return value;
        }
    }
}
