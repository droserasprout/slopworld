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

        // The sellers the daemon is polling, answering or not. What the readout draws a row
        // for, so a source that is down keeps its place on the line.
        public List<string> Sources = new List<string>();
        // The merged snapshot can be unhealthy because one seller failed while another is
        // current. The readout uses this list to dim only rows belonging to the failed seller.
        public List<string> FailedSources = new List<string>();

        // realtimeSinceStartup when these *numbers* were current - what ages them and what the
        // countdown runs from. A failed poll carries the last good windows, so it carries this
        // with them: taking the arrival time would make a snapshot half an hour old read as
        // fresh, and would hand every reset countdown back its full span once a minute.
        public float Heard;

        public bool Any => Windows.Count > 0;

        // The snapshot names the failed seller and leaves other providers live.
        public bool SourceFailed(string source) => FailedSources.Contains(source);

        public float Age => UnityEngine.Time.realtimeSinceStartup - Heard;

        // Floored at zero: a spent window reads as due rather than as a negative number.
        public long Remaining(UsageWindow w) =>
            w.ResetsIn < 0 ? -1 : Math.Max(0L, w.ResetsIn - (long)Age);

        // `prev` is what is on screen now: a failed poll answers with the last good windows,
        // and they are no fresher for having been sent again.
        public static UsageInfo FromJson(JVal j, UsageInfo prev = null)
        {
            bool ok = j["ok"].AsBool();
            return Read(j, ok || prev == null
                ? UnityEngine.Time.realtimeSinceStartup
                : prev.Heard);
        }

        static UsageInfo Read(JVal j, float heard) => new UsageInfo
        {
            Ok = j["ok"].AsBool(),
            Error = j["error"].IsNull ? null : j["error"].AsString(),
            Plan = j["plan"].AsString(),
            Sources = j["sources"].Items.Select(s => s.AsString()).ToList(),
            FailedSources = j["failed_sources"].Items.Select(s => s.AsString()).ToList(),
            Heard = heard,
            Windows = j["windows"].Items.Select(w => new UsageWindow
            {
                Key = w["key"].AsString(),
                Label = w["label"].AsString(),
                Pct = w["pct"].AsFloat(),
                Unit = w["unit"].AsString(WireProtocol.UsageUnit.Pct),
                Amount = w["amount"].AsFloat(-1f),
                Limit = w["limit"].AsFloat(-1f),
                ResetsIn = w["resets_in"].IsNull ? -1 : w["resets_in"].AsLong(-1),
            }).ToList(),
        };
    }
}
