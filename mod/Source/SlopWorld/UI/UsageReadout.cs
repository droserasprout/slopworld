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
            // the only figure that can say "left" when the size is unsaid. Unsaid rather than
            // zero: a wallet with nothing in it has $0 left, and rounding that to a percentage
            // would draw an empty account as a full bar.
            if (!w.IsMoney || w.Limit < 0f) return Mathf.RoundToInt(Left(w)) + "%";

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

            return w.Limit >= 0f
                ? $"{Long(w)}: ${Mathf.Max(0f, w.Limit - w.Amount):0.00} left of ${w.Limit:0.##} (${w.Amount:0.00} spent, {w.Pct:0.#}%)"
                : $"{Long(w)}: ${w.Amount:0.00} spent ({Left(w):0.#}% left)";
        }

        static string Long(UsageWindow w) => Long(w.Key, w.Label);

        // Keyed rather than windowed, so the settings page can name a row the daemon is not
        // currently reporting - the whole point of choosing an icon for it in advance.
        public static string Long(string key, string fallback = null)
        {
            if (key == "session") return "session window (5 hours)";
            if (key == "week") return "weekly limit";
            if (key == "spend") return "extra usage";
            if (key == "balance") return "OpenRouter balance";
            if (key.StartsWith("week_"))
                return "weekly " + key.Substring(5).Replace('_', ' ') + " limit";
            return string.IsNullOrEmpty(fallback) ? key : fallback;
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

        // One `key=defName` per line, the way the folded projects are. A key with no line and
        // a line naming a def this build has not got both read as "no choice made", which
        // hands the question back to Known and the pool.
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
            // Written on the click rather than on the way out of a window, the way the
            // column's own width and folds are: nothing here closes to save it.
            Settings.S.Write();
            Invalidate();
        }

        static ThingDef Known(string key)
        {
            switch (key)
            {
                case "session": return ThingDefOf.Chemfuel;
                case "week": return ThingDefOf.Steel;
                case "week_opus": return ThingDefOf.Plasteel;
                case "week_sonnet": return ThingDefOf.ComponentIndustrial;
                case "week_cowork": return ThingDefOf.Jade;
                case "spend": return ThingDefOf.Silver;
                // Money like the row above it, and the two are never the same coin: what is
                // left of a budget and what is left of a wallet are different questions.
                case "balance": return ThingDefOf.Gold;
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
