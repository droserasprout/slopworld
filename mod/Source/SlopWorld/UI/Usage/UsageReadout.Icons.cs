using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace SlopWorld
{
    public static partial class UsageReadout
    {
        static readonly Dictionary<string, ThingDef> _icons = new Dictionary<string, ThingDef>();
        static readonly string[] KnownKeys =
        {
            "claude_session", "claude_week", "claude_week_opus", "claude_week_sonnet",
            "claude_week_cowork", "claude_spend", "openrouter_balance",
        };
        static UsageInfo _iconUsage;
        static DaemonConfig _iconConfig;
        static int _next;
        static ThingDef[] _pool;

        // Arbitrary but stable, which is all an icon has to be - unless somebody has said
        // otherwise, which is what Settings.UsageIcons holds. Remembered per key rather than
        // worked out per frame, or an icon would move between polls depending on which other
        // windows were in one.
        public static ThingDef IconFor(string key)
        {
            PrepareIcons(key);
            return _icons[key];
        }

        static void PrepareIcons(string requestedKey)
        {
            var usage = SessionHub.Instance.Usage;
            var config = SessionHub.Instance.Config;
            if (_icons.ContainsKey(requestedKey) && ReferenceEquals(_iconUsage, usage) &&
                ReferenceEquals(_iconConfig, config)) return;
            _iconUsage = usage;
            _iconConfig = config;
            var active = new SortedSet<string>(StringComparer.Ordinal);
            var keys = new SortedSet<string>(StringComparer.Ordinal) { requestedKey };
            foreach (var key in KnownKeys) keys.Add(key);
            if (usage != null)
            {
                foreach (var row in usage.Rows)
                {
                    keys.Add(row.Key);
                    if (row.Poll) active.Add(row.Key);
                }
                foreach (var row in usage.Catalog) keys.Add(row.Key);
            }
            if (config?.UsageItems != null)
                foreach (var key in config.UsageItems.Keys) keys.Add(key);
            foreach (var line in Settings.UsageIcons.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq > 0) keys.Add(line.Substring(0, eq).Trim());
            }

            // Reserve every built-in, even when its row is absent or overridden. Explicit
            // choices may share icons, but a fallback must never claim one of those icons.
            var reserved = new HashSet<ThingDef>();
            foreach (var key in KnownKeys)
            {
                var known = Known(key);
                if (known != null) reserved.Add(known);
            }
            foreach (var key in keys)
            {
                var chosen = Chosen(key);
                if (chosen != null) reserved.Add(chosen);
            }
            foreach (var assigned in _icons.Values)
                if (assigned != null) reserved.Add(assigned);

            // Give polled rows first access to the finite fallback pool, then the remaining
            // catalog/settings keys. Each batch uses ordinal order, independent of painting.
            // Existing assignments remain stable across replacement usage snapshots.
            AssignIcons(active, reserved);
            AssignIcons(keys, reserved);
        }

        static void AssignIcons(IEnumerable<string> keys, HashSet<ThingDef> reserved)
        {
            foreach (var key in keys)
            {
                if (_icons.ContainsKey(key)) continue;
                var def = Chosen(key) ?? Known(key);
                while (def == null && _next < Pool.Length)
                {
                    var candidate = Pool[_next++];
                    if (candidate != null && reserved.Add(candidate)) def = candidate;
                }
                _icons[key] = def;
                if (def != null) reserved.Add(def);
            }
        }

        // The choices, dropped so the next draw makes them again. Called when the page that
        // edits them saves: the table above is a cache and this is the only thing that
        // invalidates it.
        public static void Invalidate()
        {
            _icons.Clear();
            _next = 0;
            _iconUsage = null;
            _iconConfig = null;
        }

        // Store one `key=defName` pair per line, as for folded projects.
        // Treat a missing key or unknown def as no selection.
        // In that case, use Known and the pool to select a def.
        public static ThingDef Chosen(string key)
        {
            foreach (var line in Settings.UsageIcons.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.Substring(0, eq).Trim() != key) continue;

                string defName = line.Substring(eq + 1).Trim();
                return defName.Length == 0
                    ? null
                    : DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            }
            return null;
        }

        // Written back by the settings page. A blank def name is the line removed rather than
        // a row with no icon: what "none" means here is "whatever this would have picked".
        public static void Choose(string key, ThingDef def)
        {
            var kept = new List<string>();
            foreach (var line in Settings.UsageIcons.Split('\n'))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.Trim().Length == 0) continue;
                if (line.Substring(0, eq).Trim() == key) continue;
                kept.Add(line.Trim());
            }

            if (def != null) kept.Add(key + "=" + def.defName);

            Settings.S.usageIcons = string.Join("\n", kept.ToArray());
            // Written on the click rather than on the way out of a window, the way the column's own
            // width and folds are. Nothing here closes to save it.
            Settings.S.Write();
            Invalidate();
        }

        static ThingDef Known(string key)
        {
            switch (key)
            {
                case "claude_session": return ThingDefOf.Chemfuel;
                case "claude_week": return ThingDefOf.Steel;
                case "claude_week_opus": return ThingDefOf.Plasteel;
                case "claude_week_sonnet": return ThingDefOf.ComponentIndustrial;
                case "claude_week_cowork": return ThingDefOf.Jade;
                case "claude_spend": return ThingDefOf.Silver;
                // Money like the row above it, and the two are never the same coin. What is left of
                // a budget and what is left of a wallet are different questions.
                case "openrouter_balance": return ThingDefOf.Gold;
                default: return null;
            }
        }

        // Only so no two rows wear the same icon. Built on first use rather than in a field
        // initialiser. ThingDefOf is filled during startup, and a static touched too early caches a
        // row of nulls.
        static ThingDef[] Pool => _pool ?? (_pool = new[]
        {
            ThingDefOf.Uranium,
            ThingDefOf.Jade,
            ThingDefOf.ComponentSpacer,
            ThingDefOf.MedicineIndustrial,
            ThingDefOf.WoodLog,
        });

    }
}
