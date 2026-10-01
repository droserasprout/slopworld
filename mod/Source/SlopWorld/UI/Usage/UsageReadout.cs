using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // TopBar quota readout. The daemon supplies the figures while this class computes remaining
    // amounts and the off-frame countdown. TopBarMapComponent owns the map-layer draw call.
    public static partial class UsageReadout
    {
        const float IconSize = 27f;

        const float ClockIconSize = 18f;

        // Twice the daemon's default poll and then some: one missed poll is nothing, a
        // minute of silence is the socket being down.
        const float StaleAfter = 150f;

        // Worked out once per key and kept - see IconFor. Static because the top bar draws
        // the same numbers from outside any map, and an icon that changed with the layout
        // would be a different resource for the same window.
        static readonly Dictionary<string, ThingDef> _icons = new Dictionary<string, ThingDef>();

        static PeriodicWork ClockSample;
        static DateTime _clockNow;

        // IMGUI can visit the top bar several times for one rendered frame. The displayed
        // clock has one-second resolution, so keep the wall-clock sample on that cadence too.
        // Monotonic time drives the deadline. DateTime is sampled only when the text can change.
        internal static DateTime ClockNow()
        {
            if (ClockSample.Due(Time.realtimeSinceStartupAsDouble, 1.0))
                _clockNow = DateTime.Now;
            return _clockNow;
        }

        static int _next;

        static ThingDef[] _pool;

        public static float ClockWidth(DateTime now)
        {
            PrepareClock(now);
            return ClockIconSize + 2f + Width(Clock.Short) + 2f;
        }

        public static void DrawClock(Rect row, DateTime now)
        {
            PrepareClock(now);
            var icon = new Rect(row.x, row.y + (row.height - ClockIconSize) / 2f,
                ClockIconSize, ClockIconSize);
            GUI.DrawTexture(icon, Icons.Time);
            UiText.RowLabel(new Rect(icon.xMax + 2f, row.y,
                row.width - ClockIconSize - 2f, row.height), Clock.Short);

            TooltipHandler.TipRegion(row, new TipSignal(
                Clock.Tooltip,
                0x51_0F_0001));
        }

        // The same rows along a line instead of down a column, right-aligned in the room they are
        // given and laid out from that end. Therefore, the first window keeps its place as later
        // ones come and go. The clock owns the final slot beside the colony doors when it is in
        // Right mode. Nothing is drawn where there is no room for it.
        public static void DrawStrip(Rect area, bool showUsage, bool showClock)
        {
            long started = PerfTrace.Start();
            try { using (WidgetState.Save()) DrawStripCore(area, showUsage, showClock); }
            finally { PerfTrace.End("topbar-usage", started, showUsage ? Quotas.Count : 0); }
        }

        static void DrawStripCore(Rect area, bool showUsage, bool showClock)
        {
            var usage = showUsage ? SessionHub.Instance.Usage : null;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = UiTheme.Name;

            if (showUsage) PrepareQuotas(usage);

            float x = area.xMax;
            DateTime now = showClock ? ClockNow() : default;
            float clockNeed = showClock ? ClockWidth(now) : 0f;
            if (showClock && x - clockNeed >= area.x)
            {
                x -= clockNeed;
                // Usage health belongs to quota rows, not to the wall clock.
                GUI.color = UiTheme.Name;
                DrawClock(new Rect(x, area.y, clockNeed, area.height), now);
                x -= ChipGap;
            }

            for (int i = showUsage ? Quotas.Count - 1 : -1; i >= 0; i--)
            {
                var cached = Quotas[i];
                string key = cached.Key;
                var w = cached.Window;
                string count = cached.Count;

                float need = IconSize + 2f + Width(count) + 2f;
                if (x - need < area.x) break;

                x -= need;
                var chip = new Rect(x, area.y, need, area.height);

                // A row holding a place is fainter than a stale one, whatever the rest of the strip
                // is doing. The icon is there to keep the line from re-flowing, and one drawn as
                // live would be a number nobody sent.
                bool stale = w != null && Stale(usage, key);
                float a = w == null ? 0.4f : stale ? 0.55f : 1f;

                var icon = IconFor(key);
                if (icon != null)
                {
                    var box = new Rect(chip.x, chip.y + (chip.height - IconSize) / 2f,
                        IconSize, IconSize);
                    Widgets.ThingIcon(box, icon, null, null, 1f, null, null, a);
                }

                // After the icon: ThingIcon leaves GUI.color on the def's own tint.
                GUI.color = UiTheme.Fade(UiTheme.Name, a);
                UiText.RowLabel(
                    new Rect(chip.x + IconSize + 2f, chip.y,
                        chip.width - IconSize - 2f, chip.height),
                    count);
                CachedTip(chip, usage, cached);

                x -= ChipGap;
            }

        }

        const float ChipGap = 10f;

        // What a row draws where the daemon has sent no figure for it. Not "0" and not "-":
        // one reads as a spent window and the other as a row that has been switched off.
        const string Unsaid = "...";

        static UsageWindow Window(UsageInfo usage, string key)
        {
            foreach (var w in usage.Windows)
                if (w.Key == key) return w;
            return null;
        }

        // Left is the default because a number in this corner that grew as the colony worked
        // would read as stock coming in. The daemon sends spent, so the subtraction is here.
        // the global display setting can instead lead with the provider's spent figure.
        static string Count(UsageWindow w)
        {
            // In Left mode, a money row whose limit the daemon could not read falls back to
            // the percentage, the only figure that can say "left" when the size is unsaid.
            // Unsaid rather than zero: a wallet with nothing in it has $0 left, and rounding
            // that to a percentage would draw an empty account as a full bar.
            if (!w.IsMoney)
                return Mathf.RoundToInt(Settings.UsageSpent ? w.Pct : Left(w)) + "%";

            if (Settings.UsageSpent)
                return w.Amount >= 10f
                    ? "$" + Mathf.RoundToInt(w.Amount)
                    : "$" + w.Amount.ToString("0.00");

            if (w.Limit < 0f)
                return Mathf.RoundToInt(Left(w)) + "%";

            float left = Mathf.Max(0f, w.Limit - w.Amount);
            return left >= 10f
                ? "$" + Mathf.RoundToInt(left)
                : "$" + left.ToString("0.00");
        }

        // Floored at zero: a window can be spent past its limit.
        static float Left(UsageWindow w)
        {
            return Mathf.Max(0f, 100f - w.Pct);
        }

        // With no label on the row, this is also where a window is named - as the game's own
        // resources work.
        static string TipText(UsageInfo usage, string key, UsageWindow w)
        {
            var lines = new List<string>();

            if (w != null)
            {
                lines.Add(Detail(w));

                long left = usage.Remaining(w);
                if (left > 0) lines.Add("resets in " + Span(left));
                else if (left == 0) lines.Add("resets any moment");
            }
            else
            {
                // Named even with nothing to say about it. An icon and three dots is a question,
                // and the answer to "which one is this?" must not wait on a poll that is failing.
                lines.Add(Label(usage, key) + ": nothing heard yet");
            }

            if (!string.IsNullOrEmpty(usage.Plan)) lines.Add("plan: " + usage.Plan);

            // Each provider retains its own last-good age across partial failures.
            if (usage.Heard > 0f && w != null)
                lines.Add("refreshed " + Span((long)usage.AgeFor(key)) + " ago");

            var row = usage.Row(key);
            if ((row != null && (row.Stale || usage.SourceFailed(row.Provider))) &&
                !string.IsNullOrEmpty(usage.Error))
            {
                lines.Add(usage.Any
                    ? $"last poll failed: {usage.Error}"
                    : usage.Error);
            }

            return string.Join("\n", lines);
        }

        // The global setting chooses the one quota view shown in the tooltip. The other figure
        // is useful for deriving it, but repeating it makes the preference meaningless.
        static string Detail(UsageWindow w)
        {
            if (!w.IsMoney)
                return Settings.UsageSpent
                    ? $"{Long(w)}: {w.Pct:0.#}% spent"
                    : $"{Long(w)}: {Left(w):0.#}% left";

            if (w.Limit < 0f)
                return Settings.UsageSpent
                    ? $"{Long(w)}: ${w.Amount:0.00} spent"
                    : $"{Long(w)}: {Left(w):0.#}% left";

            return Settings.UsageSpent
                ? $"{Long(w)}: ${w.Amount:0.00} spent"
                : $"{Long(w)}: ${Mathf.Max(0f, w.Limit - w.Amount):0.00} left of ${w.Limit:0.##}";
        }

        static string Long(UsageWindow w) => Long(w.Key, w.Label);

        static bool Stale(UsageInfo usage, string key)
        {
            if (usage == null) return false;
            var row = usage.Row(key);
            if (row != null && (row.Stale || usage.SourceFailed(row.Provider))) return true;

            // A daemon without provider-local status (or a disconnected client) still gets
            // the old age-based warning. Once the daemon identifies a failed seller, age must
            // not dim healthy providers along with it.
            return usage.FailedSources.Count == 0 && usage.AgeFor(key) > StaleAfter;
        }

        // Keyed rather than windowed, so the settings page can name a row the daemon is not
        // currently reporting - the whole point of choosing an icon for it in advance.
        public static string Long(string key, string fallback = null)
        {
            if (key.StartsWith("claude_week_"))
                return "Claude weekly " + key.Substring(12).Replace('_', ' ');
            return string.IsNullOrEmpty(fallback) ? key : fallback;
        }

        static string Label(UsageInfo usage, string key)
        {
            var row = usage?.Row(key);
            if (row != null && !string.IsNullOrEmpty(row.Label)) return row.Label;
            foreach (var entry in usage?.Catalog ?? new List<UsageCatalogInfo>())
                if (entry.Key == key && !string.IsNullOrEmpty(entry.Label)) return entry.Label;
            return Long(key);
        }

        // Arbitrary but stable, which is all an icon has to be - unless somebody has said
        // otherwise, which is what Settings.UsageIcons holds. Remembered per key rather than
        // worked out per frame, or an icon would move between polls depending on which other
        // windows were in one.
        public static ThingDef IconFor(string key)
        {
            ThingDef def;
            if (_icons.TryGetValue(key, out def)) return def;

            def = Chosen(key) ?? Known(key);
            while (def == null && _next < Pool.Length)
            {
                var next = Pool[_next++];
                if (next != null && !_icons.ContainsValue(next)) def = next;
            }

            // Null is cached too: a def this build does not have is a row that draws its
            // number and nothing else.
            _icons[key] = def;
            return def;
        }

        // The choices, dropped so the next draw makes them again. Called when the page that
        // edits them saves: the table above is a cache and this is the only thing that
        // invalidates it.
        public static void Invalidate()
        {
            _icons.Clear();
            _next = 0;
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

        // A countdown to a reset four hours out does not need its seconds.
        static string Span(long secs)
        {
            if (secs > 86400)
                return $"{secs / 86400}d {(secs % 86400) / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 3600) return $"{secs / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 60) return $"{secs / 60}m";
            return $"{secs}s";
        }
    }
}
