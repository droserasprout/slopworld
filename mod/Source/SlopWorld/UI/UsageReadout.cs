using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The resource readout, top left, where RimWorld's own used to be - except that a
    // colony of agents mines no steel. What it spends is quota, counted the way the
    // game counts everything else: an icon and a number per window. The number is
    // what is *left* - see Count, the one place the daemon's spent figure is turned
    // round.
    //
    // A bar is a widget this game has nowhere else, and this corner is the one place
    // a player already knows how to read; the detail a bar carried was in the tooltip
    // anyway. The geometry is vanilla's own simple readout.
    //
    // A MapComponent rather than a window, so it sits on the map layer behind every
    // window - right, because an open terminal is fullscreen and opaque. The numbers
    // are never computed here; the countdown is, off the frame clock, so it keeps
    // ticking between polls and when the daemon goes away.
    public class UsageReadout : MapComponent
    {
        // Vanilla's own corner and row, from ResourceReadout.DoReadoutSimple.
        const float X = 7f;
        const float Y = 7f;
        const float RowH = 24f;
        const float IconSize = 27f;
        const float TextX = 34f;
        const float RowW = 110f;

        // Twice the daemon's default poll and then some: one missed poll is nothing, a
        // minute of silence is the socket being down.
        const float StaleAfter = 150f;

        // Worked out once per key and kept - see IconFor.
        readonly Dictionary<string, ThingDef> _icons = new Dictionary<string, ThingDef>();

        int _next;

        static ThingDef[] _pool;

        // The colonist bar's own clock face, which is the one clock this game draws.
        static Texture2D _clock;
        static bool _looked;

        public UsageReadout(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (Cutscene.Playing) return; // a scene plays bare

            var usage = SessionHub.Instance.Usage;

            // Nothing ever heard and nothing wrong: usage polling is off, or the daemon has
            // not answered yet. The clock is the colony's own and stays either way.
            bool quota = usage.Any || !string.IsNullOrEmpty(usage.Error);

            // Vanilla's legibility trick for this corner, which went out with the readout
            // Patch_HideGui strips. It leaves GUI.color white behind it, so it goes before
            // anything is tinted.
            GenUI.DrawTextWinterShadow(new Rect(256f, 512f, -256f, -512f));

            var old = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float y = Y;

            // First row, above the quota: the resource nothing here can spend or make back.
            GUI.color = Color.white;
            DrawClock(new Rect(X, y, RowW, RowH));
            y += RowH;

            if (quota)
            {
                // Stale numbers stay on screen but stop looking authoritative.
                bool stale = !usage.Ok || usage.Age > StaleAfter;
                float alpha = stale ? 0.55f : 1f;

                GUI.color = new Color(1f, 1f, 1f, alpha);

                if (!usage.Any)
                {
                    DrawUnknown(new Rect(X, y, RowW, RowH), usage);
                }
                else
                {
                    foreach (var w in usage.Windows)
                    {
                        DrawWindow(new Rect(X, y, RowW, RowH), usage, w, alpha);
                        y += RowH;
                    }
                }
            }

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = old;
        }

        // The wall clock, which is also the game's - RealClock steers the calendar off it,
        // so the hour in this row is the hour the sun outside the window is keeping.
        void DrawClock(Rect row)
        {
            DateTime now = DateTime.Now;

            if (!_looked)
            {
                _looked = true;
                _clock = ContentFinder<Texture2D>.Get("UI/Icons/ColonistBar/Idle", false);
            }

            if (_clock != null)
            {
                // A colonist bar icon is drawn small; a resource icon has the whole row, so
                // it is inset to sit at the weight of the ThingIcons under it.
                var box = new Rect(row.x, row.y, IconSize, IconSize).ContractedBy(3f);
                GUI.DrawTexture(box, _clock);
            }

            Widgets.Label(new Rect(row.x + TextX, row.y, row.width - TextX, row.height),
                now.ToString("HH:mm"));

            TooltipHandler.TipRegion(row, new TipSignal(
                now.ToString("dddd, d MMMM yyyy") + "\n" + now.ToString("HH:mm:ss")
                + "\n" + "one real day to the colony's day, landing day being day one",
                0x51_0F_0001));
        }

        void DrawWindow(Rect row, UsageInfo usage, UsageWindow w, float alpha)
        {
            var icon = IconFor(w.Key);
            if (icon != null)
            {
                // ThingIcon leaves GUI.color on the def's own tint, so the row's white has to be
                // put back before the number is drawn.
                Widgets.ThingIcon(new Rect(row.x, row.y, IconSize, IconSize),
                    icon, null, null, 1f, null, null, alpha);
                GUI.color = new Color(1f, 1f, 1f, alpha);
            }

            Widgets.Label(new Rect(row.x + TextX, row.y, row.width - TextX, row.height), Count(w));
            Tip(row, usage, w);
        }

        // Say so in the same space rather than leaving the corner blank, because
        // "unknown" and "0%" must never look alike. No icon: there is no resource to
        // stand for a number nobody has.
        void DrawUnknown(Rect row, UsageInfo usage)
        {
            Widgets.Label(row, "quota: unknown");
            Tip(row, usage, null);
        }

        // It counts what is *left* rather than what is spent, which is the whole of why
        // this is a resource row: a number in this corner that grew as the colony worked
        // would be read as stock going up. The daemon sends the spent figure either way,
        // so the subtraction is this readout's and the tooltip says both ends of it.
        static string Count(UsageWindow w)
        {
            // A money row whose limit the daemon could not read falls back to the percentage,
            // which is the only figure that can say "left" when the size is unsaid.
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

        // With the label gone from the row itself this is also where a window is named,
        // which is how the game's own resources work.
        void Tip(Rect row, UsageInfo usage, UsageWindow w)
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

        // It leads with what the row says and carries the spent figure behind it, that
        // being the number an agent's own /usage will agree with.
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

        // Arbitrary and deliberately so, but stable, which is all an icon has to be: the
        // session window burns down fast and comes back, so chemfuel; the weekly limit is
        // the bulk, so steel; opus the expensive one, sonnet the everyday one. Remembered
        // per key rather than worked out per frame, or an icon would move about between
        // polls depending on which other windows were in one.
        ThingDef IconFor(string key)
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

        // The point is only that no two rows wear the same icon. Built on first use
        // rather than in a field initialiser, because ThingDefOf is filled in during
        // startup and a static touched too early caches a row of nulls.
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
