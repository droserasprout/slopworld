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

            bool stale = !usage.Ok || usage.Age > StaleAfter;
            float alpha = stale ? 0.55f : 1f;

            var was = GUI.color;
            var anchor = Text.Anchor;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(1f, 1f, 1f, alpha);

            var rows = Rows(usage);

            float x = area.xMax;
            for (int i = rows.Count - 1; i >= 0; i--)
            {
                string key = rows[i];
                var w = Window(usage, key);
                string count = w != null ? Count(w) : Unsaid;

                float need = IconSize + 2f + SlopWidgets.Wide(count) + 2f;
                if (x - need < area.x) break;

                x -= need;
                var chip = new Rect(x, area.y, need, area.height);

                // A row holding a place is fainter than a stale one, whatever the rest of the
                // strip is doing: the icon is there to keep the line from re-flowing, and one
                // drawn as live would be a number nobody sent.
                float a = w != null ? alpha : 0.4f;

                var icon = IconFor(key);
                if (icon != null)
                {
                    var box = new Rect(chip.x, chip.y + (chip.height - IconSize) / 2f,
                        IconSize, IconSize);
                    Widgets.ThingIcon(box, icon, null, null, 1f, null, null, a);
                }

                // After the icon: ThingIcon leaves GUI.color on the def's own tint.
                GUI.color = new Color(1f, 1f, 1f, a);
                Widgets.Label(
                    new Rect(chip.x + IconSize + 2f, chip.y,
                        chip.width - IconSize - 2f, chip.height),
                    count);
                Tip(chip, usage, key, w);

                x -= ChipGap;
            }

            Text.Anchor = anchor;
            GUI.color = was;
        }

        const float ChipGap = 10f;

        // What a row draws where the daemon has sent no figure for it. Not "0" and not "-":
        // one reads as a spent window and the other as a row that has been switched off.
        const string Unsaid = "...";

        // Every row this strip should have, left to right: what the daemon reported, plus a
        // place held for anything a switched-on seller owes us and has not sent.
        //
        // Keyed off the sellers rather than off the windows, because the case this is for is
        // exactly the one where there are no windows: a login that has gone stale answers with
        // an error and nothing else, and a strip that drew only what arrived would take the
        // colony's resources off the top of the screen to say so. The icons stay, the numbers
        // go, and the tooltip says which seller is out.
        static List<string> Rows(UsageInfo usage)
        {
            var rows = new List<string>();
            foreach (var w in usage.Windows)
                if (!rows.Contains(w.Key)) rows.Add(w.Key);

            foreach (string seller in usage.Sources)
                foreach (string key in Owed(seller))
                    if (!rows.Contains(key)) Place(rows, key);

            // Pollers answer independently, so arrival order is not display order. Keep the
            // shared pools in one fixed left-to-right run even when a source comes back late.
            rows.Sort((a, b) => Rank(a).CompareTo(Rank(b)));
            return rows;
        }

        // The rows a seller is expected to answer with. Only the ones every account of that
        // kind has: a per-model weekly limit or an extra-usage budget that this plan has not
        // got is a row that would never fill in, and an icon that stays blank forever is worse
        // than no icon at all.
        static string[] Owed(string seller)
        {
            if (seller == "anthropic") return AnthropicRows;
            if (seller == "openrouter") return OpenRouterRows;
            if (seller == "openai") return OpenAiRows;
            return new string[0];
        }

        static readonly string[] AnthropicRows = { "claude_session", "claude_week" };
        static readonly string[] OpenRouterRows = { "openrouter_balance" };
        static readonly string[] OpenAiRows = { "openai_session", "openai_week" };

        // Slots a held place next to its own kind rather than on the end: a session window that
        // turned up after the weekly one would otherwise sit to the right of it, and the strip
        // would re-order itself the moment the numbers came back.
        static void Place(List<string> rows, string key)
        {
            int rank = Rank(key);
            for (int i = 0; i < rows.Count; i++)
            {
                if (Rank(rows[i]) <= rank) continue;
                rows.Insert(i, key);
                return;
            }
            rows.Add(key);
        }

        // The strip's fixed left-to-right order. Anything plan-specific comes after the common
        // pools, so it cannot shove OpenAI or the OpenRouter balance out of their usual place.
        static int Rank(string key)
        {
            if (key == "claude_session") return 0;
            if (key == "claude_week") return 1;
            if (key == "openai_session") return 2;
            if (key == "openai_week") return 3;
            if (key == "openrouter_balance") return 4;
            if (key == "claude_spend") return 5;
            return 6;
        }

        static UsageWindow Window(UsageInfo usage, string key)
        {
            foreach (var w in usage.Windows)
                if (w.Key == key) return w;
            return null;
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
        static void Tip(Rect row, UsageInfo usage, string key, UsageWindow w)
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
                // Named even with nothing to say about it: an icon and three dots is a
                // question, and the answer to "which one is this?" must not wait on a poll
                // that is failing.
                lines.Add(Long(key) + ": nothing heard yet");
            }

            if (!string.IsNullOrEmpty(usage.Plan)) lines.Add("plan: " + usage.Plan);

            // How old the numbers are, always - Heard follows the last *good* poll, so this
            // says the thing a failing readout is most often asked.
            if (usage.Heard > 0f && w != null)
                lines.Add("refreshed " + Span((long)usage.Age) + " ago");

            if (!usage.Ok && !string.IsNullOrEmpty(usage.Error))
            {
                lines.Add(usage.Any
                    ? $"last poll failed: {usage.Error}"
                    : usage.Error);
            }

            // Keyed off the row so two of them don't share one tooltip.
            TooltipHandler.TipRegion(row, new TipSignal(
                string.Join("\n", lines.ToArray()),
                0x51_0F_0000 ^ (key?.GetHashCode() ?? 0)));
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
            if (key == "claude_session") return "Claude session window (5 hours)";
            if (key == "claude_week") return "Claude weekly limit";
            if (key == "openai_session") return "OpenAI primary window";
            if (key == "openai_week") return "OpenAI secondary window";
            if (key == "claude_spend") return "Claude extra usage";
            if (key == "openrouter_balance") return "OpenRouter balance";
            if (key.StartsWith("claude_week_"))
                return "Claude weekly " + key.Substring(12).Replace('_', ' ') + " limit";
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
                case "claude_session": return ThingDefOf.Chemfuel;
                case "claude_week": return ThingDefOf.Steel;
                case "claude_week_opus": return ThingDefOf.Plasteel;
                case "claude_week_sonnet": return ThingDefOf.ComponentIndustrial;
                case "claude_week_cowork": return ThingDefOf.Jade;
                case "claude_spend": return ThingDefOf.Silver;
                // Money like the row above it, and the two are never the same coin: what is
                // left of a budget and what is left of a wallet are different questions.
                case "openrouter_balance": return ThingDefOf.Gold;
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
            if (secs > 86400)
                return $"{secs / 86400}d {(secs % 86400) / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 3600) return $"{secs / 3600}h {(secs % 3600) / 60}m";
            if (secs >= 60) return $"{secs / 60}m";
            return $"{secs}s";
        }
    }
}
