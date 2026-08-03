using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The quota readout, drawn from TopBar across the top of the screen. The number is
    // what is *left* - see Count, the one place the daemon's spent figure is turned round.
    //
    // A MapComponent rather than a window, so it sits behind every window. The numbers are
    // never computed here; the countdown is, off the frame clock, so it keeps ticking between
    // polls and when the daemon goes away.
    public class UsageReadout : MapComponent
    {
        const float IconSize = 27f;

        // Twice the daemon's default poll and then some: one missed poll is nothing, a
        // minute of silence is the socket being down.
        const float StaleAfter = 150f;

        // Worked out once per key and kept - see IconFor. Static because the top bar draws
        // the same numbers from outside any map, and an icon that changed with the layout
        // would be a different resource for the same window.
        static readonly Dictionary<string, ThingDef> _icons = new Dictionary<string, ThingDef>();

        static int _next;

        static ThingDef[] _pool;

        static Texture2D _clock;
        static bool _looked;

        public UsageReadout(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return; // a scene plays bare

            TopBar.DrawOnMap();
        }

        public static void DrawClock(Rect row, TextAnchor anchor)
        {
            DateTime now = DateTime.Now;

            if (!_looked)
            {
                _looked = true;
                _clock = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/Idle", false);
            }

            var was = Text.Anchor;
            Text.Anchor = anchor;
            Widgets.Label(row, now.ToString("HH:mm"));
            Text.Anchor = was;

            TooltipHandler.TipRegion(row, new TipSignal(
                now.ToString("dddd, d MMMM yyyy") + "\n" + now.ToString("HH:mm:ss"),
                0x51_0F_0001));
        }

        // The same rows along a line instead of down a column, right-aligned in the room they
        // are given and laid out from that end, so the first window keeps its place as later
        // ones come and go. Nothing is drawn where there is no room for it.
        public static void DrawStrip(Rect area)
        {
            var usage = SessionHub.Instance.Usage;
            if (!usage.Any && string.IsNullOrEmpty(usage.Error)) return;

            bool stale = !usage.Ok || usage.Age > StaleAfter;
            float alpha = stale ? 0.55f : 1f;

            var was = GUI.color;
            var anchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            if (!usage.Any)
            {
                float w = Mathf.Min(Text.CalcSize("quota: unknown").x + 6f, area.width);
                var only = new Rect(area.xMax - w, area.y, w, area.height);
                Widgets.Label(only, "quota: unknown");
                Tip(only, usage, null);
            }
            else
            {
                float x = area.xMax;
                for (int i = usage.Windows.Count - 1; i >= 0; i--)
                {
                    var w = usage.Windows[i];
                    float need = ChipW(w);
                    if (x - need < area.x) break;

                    x -= need;
                    var chip = new Rect(x, area.y, need, area.height);

                    var icon = IconFor(w.Key);
                    if (icon != null)
                    {
                        var box = new Rect(chip.x, chip.y + (chip.height - IconSize) / 2f,
                            IconSize, IconSize);
                        // ThingIcon leaves GUI.color on the def's own tint.
                        Widgets.ThingIcon(box, icon, null, null, 1f, null, null, alpha);
                        GUI.color = new Color(1f, 1f, 1f, alpha);
                    }

                    Widgets.Label(
                        new Rect(chip.x + IconSize + 2f, chip.y,
                            chip.width - IconSize - 2f, chip.height),
                        Count(w));
                    Tip(chip, usage, w);

                    x -= ChipGap;
                }
            }

            Text.Anchor = anchor;
            GUI.color = was;
        }

        const float ChipGap = 10f;

        static float ChipW(UsageWindow w)
        {
            Text.Font = GameFont.Small;
            return IconSize + 2f + Text.CalcSize(Count(w)).x + 2f;
        }

        // What is *left*: a number in this corner that grew as the colony worked would read as
        // stock coming in. The daemon sends the spent figure, so the subtraction is here and
        // the tooltip says both ends of it.
        static string Count(UsageWindow w)
        {
            // A money row whose limit the daemon could not read falls back to the percentage,
            // the only figure that can say "left" when the size is unsaid.
            if (!w.IsMoney || w.Limit <= 0f) return Mathf.RoundToInt(Left(w)) + "%";

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
        static void Tip(Rect row, UsageInfo usage, UsageWindow w)
        {
            var lines = new List<string>();

            if (w != null)
            {
                lines.Add(Detail(w));

                long left = usage.Remaining(w);
                if (left > 0) lines.Add("resets in " + Span(left));
                else if (left == 0) lines.Add("resets any moment");
            }

            if (!string.IsNullOrEmpty(usage.Plan)) lines.Add("plan: " + usage.Plan);

            if (usage.Ok)
            {
                if (usage.Age > StaleAfter)
                    lines.Add($"last heard {Span((long)usage.Age)} ago");
            }
            else if (!string.IsNullOrEmpty(usage.Error))
            {
                lines.Add(usage.Any
                    ? $"stale - the last poll failed: {usage.Error}"
                    : usage.Error);
            }

            // Keyed off the window so two rows don't share one tooltip.
            TooltipHandler.TipRegion(row, new TipSignal(
                string.Join("\n", lines.ToArray()),
                0x51_0F_0000 ^ (w?.Key?.GetHashCode() ?? 0)));
        }

        // Leads with what the row says and carries the spent figure behind it, that being the
        // number an agent's own /usage will agree with.
        static string Detail(UsageWindow w)
        {
            if (!w.IsMoney) return $"{Long(w)}: {Left(w):0.#}% left ({w.Pct:0.#}% spent)";

            return w.Limit > 0f
                ? $"{Long(w)}: ${Mathf.Max(0f, w.Limit - w.Amount):0.00} left of ${w.Limit:0.##} (${w.Amount:0.00} spent, {w.Pct:0.#}%)"
                : $"{Long(w)}: ${w.Amount:0.00} spent ({Left(w):0.#}% left)";
        }

        static string Long(UsageWindow w)
        {
            if (w.Key == "session") return "session window (5 hours)";
            if (w.Key == "week") return "weekly limit";
            if (w.Key == "spend") return "extra usage";
            if (w.Key.StartsWith("week_"))
                return "weekly " + w.Key.Substring(5).Replace('_', ' ') + " limit";
            return w.Label;
        }

        // Arbitrary but stable, which is all an icon has to be. Remembered per key rather than
        // worked out per frame, or an icon would move between polls depending on which other
        // windows were in one.
        static ThingDef IconFor(string key)
        {
            ThingDef def;
            if (_icons.TryGetValue(key, out def)) return def;

            def = Known(key);
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

        static ThingDef Known(string key)
        {
            switch (key)
            {
                case "session": return ThingDefOf.Chemfuel;
                case "week": return ThingDefOf.Steel;
                case "week_opus": return ThingDefOf.Plasteel;
                case "week_sonnet": return ThingDefOf.ComponentIndustrial;
                case "week_cowork": return ThingDefOf.Gold;
                case "spend": return ThingDefOf.Silver;
                default: return null;
            }
        }

        // Only so no two rows wear the same icon. Built on first use rather than in a field
        // initialiser: ThingDefOf is filled during startup, and a static touched too early
        // caches a row of nulls.
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
            if (secs >= 3600) return $"{secs / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 60) return $"{secs / 60}m";
            return $"{secs}s";
        }
    }
}
