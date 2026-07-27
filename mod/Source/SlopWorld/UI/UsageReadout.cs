using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The resource readout, top left, where RimWorld's own used to be - except
    /// that a colony of agents mines no steel. What it spends is quota, so quota
    /// is what the readout counts, and it counts it the way the game counts
    /// everything else: an icon and a number, one row per rate-limit window, plus
    /// the extra-usage budget in dollars when there is one. The number is what is
    /// left, not what is gone - see <see cref="Count"/>, which is the one place
    /// the daemon's spent figure is turned round.
    ///
    /// Drawn as a resource rather than as a bar deliberately. A bar is a widget
    /// this game does not have anywhere else, and the corner it sits in is the
    /// one place a player already knows how to read - icon, white number, hover
    /// for the rest. The detail a bar was carrying was always in the tooltip
    /// anyway.
    ///
    /// This is deliberately the vanilla spot. <see cref="Patch_HideGui"/> strips
    /// ResourceReadout wholesale, which leaves the corner empty and leaves the
    /// eye going there anyway. The geometry is vanilla's own simple readout,
    /// down to the 27px icon in a 24px row and the shadow under the text.
    ///
    /// Drawn from a MapComponent rather than a window, so it sits on the map
    /// layer with the colonist bar and the alerts - behind every window, which is
    /// right: an open terminal is fullscreen and opaque, and a number drawn over
    /// it would be a number drawn over the thing the player is actually reading.
    ///
    /// The numbers come from slopd (see usage.rs) and are never computed here.
    /// The one piece of arithmetic that is local is the countdown, which runs off
    /// the frame clock so it keeps ticking between polls and keeps ticking when
    /// the daemon goes away.
    /// </summary>
    public class UsageReadout : MapComponent
    {
        // Vanilla's own corner and row, from ResourceReadout.DoReadoutSimple: a
        // 27px icon drawn at the top of a 24px row, with the count at 34.
        const float X = 7f;
        const float Y = 7f;
        const float RowH = 24f;
        const float IconSize = 27f;
        const float TextX = 34f;
        const float RowW = 110f;

        /// A snapshot older than this is drawn dimmed. Twice the daemon's default
        /// poll and then some: one missed poll is nothing, a minute of silence is
        /// the socket being down.
        const float StaleAfter = 150f;

        /// Which resource stands in for which window, worked out once per key and
        /// kept - see <see cref="IconFor"/> for why it is remembered rather than
        /// recomputed.
        readonly Dictionary<string, ThingDef> _icons = new Dictionary<string, ThingDef>();

        /// Next slot in <see cref="Pool"/> a window nothing in the table covers
        /// will take.
        int _next;

        static ThingDef[] _pool;

        public UsageReadout(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (IntroDirector.UiHidden) return; // the opening scene plays bare

            var usage = SessionHub.Instance.Usage;

            // Nothing ever heard and nothing wrong: the daemon has usage polling
            // off, or has not answered yet. Either way there is no readout to
            // draw and no bad news to report.
            if (!usage.Any && string.IsNullOrEmpty(usage.Error)) return;

            // Vanilla's legibility trick for this corner, which went out with the
            // readout Patch_HideGui strips: a soft darkening under the text,
            // drawn only where the ground is bright enough to need it. It leaves
            // GUI.color white behind it, so it goes before anything is tinted.
            GenUI.DrawTextWinterShadow(new Rect(256f, 512f, -256f, -512f));

            // Stale numbers stay on screen but stop looking authoritative.
            bool stale = !usage.Ok || usage.Age > StaleAfter;
            float alpha = stale ? 0.55f : 1f;

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            float y = Y;
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

            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = old;
        }

        void DrawWindow(Rect row, UsageInfo usage, UsageWindow w, float alpha)
        {
            var icon = IconFor(w.Key);
            if (icon != null)
            {
                // ThingIcon leaves GUI.color on the def's own tint, so the row's
                // white has to be put back before the number is drawn.
                Widgets.ThingIcon(new Rect(row.x, row.y, IconSize, IconSize),
                    icon, null, null, 1f, null, null, alpha);
                GUI.color = new Color(1f, 1f, 1f, alpha);
            }

            Widgets.Label(new Rect(row.x + TextX, row.y, row.width - TextX, row.height), Count(w));
            Tip(row, usage, w);
        }

        /// The daemon answered but had nothing usable - no login, no network, a
        /// payload it did not recognise. Say so in the same space rather than
        /// leaving the corner blank, because "unknown" and "0%" must never look
        /// alike. No icon: there is no resource to stand for a number nobody has.
        void DrawUnknown(Rect row, UsageInfo usage)
        {
            Widgets.Label(row, "quota: unknown");
            Tip(row, usage, null);
        }

        /// What the row says: a percentage for a rate-limit window, dollars for
        /// the extra-usage budget. Cents survive while the sum is small, because
        /// that is when they are the whole of it, and go once it is round money.
        ///
        /// It counts what is *left* rather than what is spent, which is the whole
        /// of why this is a resource row. A steel count is how much steel there
        /// is; a number in this corner that grew as the colony worked would be
        /// read as stock going up by anyone who has played the game once. The
        /// figure the daemon sends is the spent one either way - the subtraction
        /// is this readout's, and the tooltip is where both ends of it are said.
        static string Count(UsageWindow w)
        {
            // Dollars left needs a budget to subtract from. A money row whose
            // limit the daemon could not read falls back to the percentage,
            // which is the only figure that can say "left" when the size of the
            // thing is unsaid.
            if (!w.IsMoney || w.Limit <= 0f) return Mathf.RoundToInt(Left(w)) + "%";

            float left = Mathf.Max(0f, w.Limit - w.Amount);
            return left >= 10f
                ? "$" + Mathf.RoundToInt(left)
                : "$" + left.ToString("0.00");
        }

        /// The percentage the daemon sent, turned round. Floored at zero: a
        /// window can be spent past its limit, and a readout in negative numbers
        /// says less than an empty one does.
        static float Left(UsageWindow w)
        {
            return Mathf.Max(0f, 100f - w.Pct);
        }

        /// Everything the row cannot fit: what the window is, when it comes back,
        /// how old the number is and whatever went wrong last. With the label
        /// gone from the row itself this is also where a window is named, which
        /// is how the game's own resources work.
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

        /// The first tooltip line: the row spelled out. It leads with what the
        /// row itself says - what is left - and carries the spent figure behind
        /// it, since that is the number the daemon actually sent and the one an
        /// agent's own /usage will agree with. A money row keeps its percentage
        /// as well, because the dollars alone do not say how much of the budget
        /// that is.
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

        /// <summary>
        /// Which of the game's own resources stands in for a window.
        ///
        /// Arbitrary, and deliberately so - quota is not chemfuel - but stable,
        /// which is the only thing an icon has to be to become the thing the eye
        /// goes to. The picks lean on what each material reads as: the session
        /// window burns down fast and comes back, so chemfuel; the weekly limit
        /// is the bulk of it, so steel; opus is the expensive one and sonnet the
        /// everyday one; and the money is the game's own money.
        ///
        /// Remembered per key rather than worked out per frame, because a window
        /// nothing here has heard of takes the next unused resource from
        /// <see cref="Pool"/> - and an icon that depended on which other windows
        /// happened to be in this poll would move about between polls.
        /// </summary>
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

            // Null is cached too: a def this build does not have is a row that
            // draws its number and nothing else, and asking again every frame
            // would not change that.
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

        /// What a window this build has never heard of gets, in order. The point
        /// is only that no two rows wear the same icon: the plan that grows a new
        /// limit next month draws it without an update, and the tooltip is what
        /// says which one it is.
        ///
        /// Built on first use rather than in a field initialiser, because
        /// ThingDefOf is filled in during startup and a static this class touched
        /// too early would cache a row of nulls.
        static ThingDef[] Pool => _pool ?? (_pool = new[]
        {
            ThingDefOf.Uranium,
            ThingDefOf.Jade,
            ThingDefOf.ComponentSpacer,
            ThingDefOf.MedicineIndustrial,
            ThingDefOf.WoodLog,
        });

        /// Coarse on purpose: a countdown to a reset four hours out does not need
        /// its seconds, and one under a minute is about to happen anyway.
        static string Span(long secs)
        {
            if (secs >= 3600) return $"{secs / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 60) return $"{secs / 60}m";
            return $"{secs}s";
        }
    }
}
