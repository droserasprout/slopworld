using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld
{
    // Ok false means the last poll failed, in which case the windows are the previous
    // good ones and Error says what went wrong.
    public class UsageInfo
    {
        public bool Ok;
        public string Error;
        public string Plan = "";
        public List<UsageWindow> Windows = new List<UsageWindow>();
        public List<UsageCatalogInfo> Catalog = new List<UsageCatalogInfo>();
        public List<UsageRow> Rows = new List<UsageRow>();

        // The sellers the daemon is polling, answering or not. What the readout draws a row
        // for, so a source that is down keeps its place on the line.
        public List<string> Sources = new List<string>();
        // The merged snapshot can be unhealthy because one seller failed while another is
        // current. The readout uses this list to dim only rows belonging to the failed seller.
        public List<string> FailedSources = new List<string>();

        // Monotonic timestamp of the last good data. Failed polls retain it so stale snapshots
        // do not appear fresh or restart reset countdowns.
        public float Heard;

        public bool Any => Windows.Count > 0;

        // The snapshot names the failed seller and leaves other providers live.
        public bool SourceFailed(string source) => FailedSources.Contains(source);

        public UsageRow Row(string key) => Rows.FirstOrDefault(row => row.Key == key);

        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        // Floored at zero: a spent window reads as due rather than as a negative number.
        public long Remaining(UsageWindow w) =>
            w.ResetsIn < 0 ? -1 : Math.Max(0L, w.ResetsIn - (long)Age);

        // `prev` is what is on screen now: a failed poll answers with the last good windows,
        // and they are no fresher for having been sent again.
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
