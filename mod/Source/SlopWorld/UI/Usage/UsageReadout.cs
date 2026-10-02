using System;
using System.Collections.Generic;
using System.Globalization;
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
                    : "$" + w.Amount.ToString("0.00", CultureInfo.CurrentCulture);

            if (w.Limit < 0f)
                return Mathf.RoundToInt(Left(w)) + "%";

            float left = Mathf.Max(0f, w.Limit - w.Amount);
            return left >= 10f
                ? "$" + Mathf.RoundToInt(left)
                : "$" + left.ToString("0.00", CultureInfo.CurrentCulture);
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
            if (key.StartsWith("claude_week_", StringComparison.Ordinal))
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
