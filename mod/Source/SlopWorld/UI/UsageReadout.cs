using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    /// <summary>
    /// The resource readout, top left, where RimWorld's own used to be - except
    /// that a colony of agents mines no steel. What it spends is quota, so quota
    /// is what the readout counts: one bar per rate-limit window, filling up as
    /// the agents work through it.
    ///
    /// This is deliberately the vanilla spot. <see cref="Patch_HideGui"/> strips
    /// ResourceReadout wholesale, which leaves the corner empty and leaves the
    /// eye going there anyway; putting the one number this game still has where
    /// the numbers used to be costs nothing and reads immediately.
    ///
    /// Drawn from a MapComponent rather than a window, so it sits on the map
    /// layer with the colonist bar and the alerts - behind every window, which is
    /// right: an open terminal is fullscreen and opaque, and a bar drawn over it
    /// would be a bar drawn over the thing the player is actually reading.
    ///
    /// The numbers come from slopd (see usage.rs) and are never computed here.
    /// The one piece of arithmetic that is local is the countdown, which runs off
    /// the frame clock so it keeps ticking between polls and keeps ticking when
    /// the daemon goes away - a reset that quietly stops moving would be a lie.
    /// </summary>
    public class UsageReadout : MapComponent
    {
        // The panel, laid out from the top-left corner of the screen.
        const float X = 8f;
        const float Y = 8f;
        const float RowH = 20f;
        const float PadX = 8f;
        const float PadY = 6f;
        const float LabelW = 52f;
        const float BarW = 96f;
        const float BarH = 9f;
        const float PctW = 40f;
        const float Width = PadX * 2f + LabelW + BarW + PctW + 8f;

        /// A snapshot older than this is drawn dimmed. Twice the daemon's default
        /// poll and then some: one missed poll is nothing, a minute of silence is
        /// the socket being down.
        const float StaleAfter = 150f;

        /// Where the bar stops being reassuring, and where it starts being a
        /// warning. Same thresholds the fill colour uses.
        const float Warn = 60f;
        const float Danger = 85f;

        static readonly Color Panel = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color Track = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color Text_ = new Color(0.82f, 0.84f, 0.86f);

        public UsageReadout(Map map) : base(map) { }

        public override void MapComponentOnGUI()
        {
            if (IntroDirector.UiHidden) return; // the opening scene plays bare

            var usage = SessionHub.Instance.Usage;

            // Nothing ever heard and nothing wrong: the daemon has usage polling
            // off, or has not answered yet. Either way there is no readout to
            // draw and no bad news to report.
            if (!usage.Any && string.IsNullOrEmpty(usage.Error)) return;

            var rows = usage.Any ? usage.Windows : null;
            int count = rows?.Count ?? 1;
            var panel = new Rect(X, Y, Width, PadY * 2f + count * RowH);
            Widgets.DrawBoxSolid(panel, Panel);

            // Stale numbers stay on screen but stop looking authoritative.
            bool stale = !usage.Ok || usage.Age > StaleAfter;
            var old = GUI.color;
            if (stale) GUI.color = new Color(1f, 1f, 1f, 0.55f);

            Text.Font = GameFont.Tiny;
            float y = Y + PadY;

            if (rows == null)
            {
                DrawUnknown(new Rect(X + PadX, y, Width - PadX * 2f, RowH), usage);
            }
            else
            {
                foreach (var w in rows)
                {
                    DrawWindow(new Rect(X + PadX, y, Width - PadX * 2f, RowH), usage, w);
                    y += RowH;
                }
            }

            Text.Font = GameFont.Small;
            GUI.color = old;
        }

        void DrawWindow(Rect row, UsageInfo usage, UsageWindow w)
        {
            GUI.color = new Color(Text_.r, Text_.g, Text_.b, GUI.color.a);
            Widgets.Label(new Rect(row.x, row.y - 1f, LabelW, RowH), Short(w));

            var bar = new Rect(row.x + LabelW, row.y + (RowH - BarH) / 2f, BarW, BarH);
            Widgets.DrawBoxSolid(bar, Track);

            float frac = Mathf.Clamp01(w.Pct / 100f);
            if (frac > 0f)
            {
                var fill = new Rect(bar.x, bar.y, bar.width * frac, bar.height);
                Widgets.DrawBoxSolid(fill, Tint(Fill(w.Pct), GUI.color.a));
            }

            GUI.color = new Color(Text_.r, Text_.g, Text_.b, GUI.color.a);
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(bar.xMax + 6f, row.y - 1f, PctW, RowH),
                Mathf.RoundToInt(w.Pct) + "%");
            Text.Anchor = TextAnchor.UpperLeft;

            Tip(row, usage, w);
        }

        /// The daemon answered but had nothing usable - no login, no network, a
        /// payload it did not recognise. Say so in the same space rather than
        /// leaving the corner blank, because "unknown" and "0%" must never look
        /// alike.
        void DrawUnknown(Rect row, UsageInfo usage)
        {
            GUI.color = new Color(Text_.r, Text_.g, Text_.b, GUI.color.a);
            Widgets.Label(new Rect(row.x, row.y - 1f, row.width, RowH), "quota: unknown");
            Tip(row, usage, null);
        }

        /// Everything the row cannot fit: what the window is, when it comes back,
        /// how old the number is and whatever went wrong last.
        void Tip(Rect row, UsageInfo usage, UsageWindow w)
        {
            var lines = new List<string>();

            if (w != null)
            {
                lines.Add($"{Long(w)}: {w.Pct:0.#}% spent");

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

        /// What a row is called in the width a row has: a window's length, which
        /// is the thing worth knowing about a limit. The per-model weeks keep
        /// their model ("7d opus"), and anything the daemon names that this does
        /// not recognise falls back to the daemon's own label, so a window
        /// nobody has seen yet still draws.
        static string Short(UsageWindow w)
        {
            if (w.Key == "session") return "5h";
            if (w.Key == "week") return "7d";
            if (w.Key.StartsWith("week_")) return "7d " + w.Key.Substring(5).Replace('_', ' ');
            return w.Label;
        }

        static string Long(UsageWindow w)
        {
            if (w.Key == "session") return "session window (5 hours)";
            if (w.Key == "week") return "weekly limit";
            if (w.Key.StartsWith("week_"))
                return "weekly " + w.Key.Substring(5).Replace('_', ' ') + " limit";
            return w.Label;
        }

        static Color Fill(float pct)
        {
            if (pct >= Danger) return new Color(0.85f, 0.35f, 0.35f);
            if (pct >= Warn) return new Color(0.98f, 0.80f, 0.30f);
            return new Color(0.45f, 0.75f, 0.95f);
        }

        static Color Tint(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

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
