using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static partial class Patch_LoadingTips
    {
        const float Tick = 0.07f;
        const double ScrollChance = 0.25;
        const int Passes = 3;

        // Rows that did not move whose noise is rolled again per tick. Seasoning the whole block
        // instead was StringBuilder churn fourteen times a second during map generation.
        const int Flicker = 2;

        // ZALGO
        const string Marks =
            "\u0300\u0301\u0302\u0303\u0304\u0306\u0307\u0308\u030A\u030B\u030C" +   // above
            "\u0327\u0323\u0324\u0325\u0326\u0330\u0331";    // below
        const double MarkChance = 0.4;
        const double DoubleChance = 0.4;
        const string Overlays = "\u0334\u0335\u0336\u0337\u0338";
        const double OverlayChance = 0.35;

        // Wobbly words
        const string Gaps = "\u200a";
        const double GapChance = 0.2;


        // Not Verse.Rand: this screen is up *during* map generation, so a draw off the global
        // sequence once a frame is a loading screen deciding where the rivers go.
        static readonly System.Random Dice = new System.Random();

        // Vanilla's own TextMargin, which is private.
        internal static readonly Vector2 Margin = new Vector2(15f, 8f);

        // Layout and wrapping share the measured box. Width is capped in scaled UI
        // coordinates; height comes from wrapped text rather than Text.LineHeight.
        const float MaxWidth = 500f;
        const float MinWidth = 320f;
        const float Ratio = 0.3f;
        const int MinLines = 6;

        // The screen the box was measured against; latching it forever left the wall wrapped to
        // the old width inside a window sized for it after any resolution or UI-scale change.
        static int _measuredW, _measuredH;
        static Vector2 _box;
        static int _lines;

        // Bumped per re-measure: tells the layout patch to rewrite vanilla's size field and the
        // wall to re-wrap.
        internal static int Generation;

        internal static int Lines
        {
            get { var _ = Box; return _lines; }
        }

        // Asked the same way the block will be drawn.
        static float ProbeHeight(int lines, float width)
        {
            var probe = new System.Text.StringBuilder();
            for (int i = 0; i < lines; i++)
            {
                if (i > 0) probe.Append('\n');
                probe.Append('A');
            }

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            float h = Text.CalcHeight(probe.ToString(), width);
            Text.Font = font;
            Text.WordWrap = wrap;
            return h;
        }

        internal static Vector2 Box
        {
            get
            {
                if (_measuredW != UI.screenWidth || _measuredH != UI.screenHeight)
                {
                    _measuredW = UI.screenWidth;
                    _measuredH = UI.screenHeight;
                    Generation++;
                    float w = Mathf.Clamp(UI.screenWidth - 80f, MinWidth, MaxWidth);
                    float text = w - Margin.x * 2f;

                    // A box taller than the screen is centred into losing its top and bottom rows.
                    float room = Mathf.Min(w * Ratio, UI.screenHeight - 80f) - Margin.y * 2f;

                    // CalcHeight is linear in the count but does not pass through zero: the first
                    // line carries the font's own slack.
                    float one = ProbeHeight(1, text);
                    float step = Mathf.Max(1f, ProbeHeight(2, text) - one);
                    _lines = Mathf.Max(MinLines, Mathf.FloorToInt((room - one) / step) + 1);

                    _box = new Vector2(w, ProbeHeight(_lines, text) + Margin.y * 2f);
                }
                return _box;
            }
        }

        // The tips as one wrapped stream, clean; zalgo is rolled onto the rows on screen. Built
        // lazily, the wrap needing a font and so an OnGUI; a build that throws leaves an empty
        // list, which stands both patches down. A scroll is an index into this, where it used to
        // be one precomputed block per position.
        static List<string> _wall;

        // Which mode the wall was filtered for and which box it was wrapped to; both run once,
        // at the build, and the wall then lives as long as the process.
        static bool _wallGrandma;
        static int _wallAt = -1;

        internal static List<string> Wall
        {
            get
            {
                var _ = Box;                // so a resize bumps the generation before it is read
                bool grandma = Settings.GrandmaMode;
                if (_wall != null && _wallGrandma == grandma && _wallAt == Generation) return _wall;
                _wallGrandma = grandma;
                _wallAt = Generation;

                // All three point into the wall going away. Painted especially: it is what
                // DrawContents draws, so a stale one keeps the dropped tips on screen.
                _top = 0;
                _rows = null;
                _painted = null;

                try
                {
                    _wall = BuildWall();
                }
                catch (Exception e)
                {
                    _wall = new List<string>();
                    Log.Warning($"[SlopWorld] loading tips left to vanilla: {e}");
                }
                return _wall;
            }
        }

        static List<string> BuildWall()
        {
            var rng = Dice;
            var stream = new System.Text.StringBuilder();

            // Whichever half of the marked tips this mode is not for goes here, once, and the
            // wall is built from what is left.
            bool grandma = Settings.GrandmaMode;
            var order = new List<string>();
            for (int i = 0; i < Tips.Count; i++)
            {
                string tip = Tips[i];
                if (Shown(tip, grandma)) order.Add(Strip(tip));
            }
            for (int p = 0; p < Passes; p++)
            {
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    string t = order[i];
                    order[i] = order[j];
                    order[j] = t;
                }
                foreach (string tip in order)
                {
                    if (stream.Length > 0) stream.Append(' ');
                    stream.Append(tip);
                }
            }

            List<string> lines;
            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;      // or CalcSize answers about a wrapped block
            try
            {
                lines = Wrap(stream.ToString(), Box.x - Margin.x * 2f);
            }
            finally
            {
                Text.Font = font;
                Text.WordWrap = wrap;
            }

            return lines;
        }

        // Measured rather than counted, the font being proportional. A word wider than the box
        // is left on its own line and clipped by the group.
        static List<string> Wrap(string text, float width)
        {
            var lines = new List<string>();
            var line = new System.Text.StringBuilder();
            foreach (string word in text.Split(' '))
            {
                if (word.Length == 0) continue;
                int end = line.Length;
                if (end > 0) line.Append(' ');
                line.Append(word);
                if (end == 0 || Text.CalcSize(line.ToString()).x <= width) continue;
                lines.Add(line.ToString(0, end));
                line.Length = 0;
                line.Append(word);
            }
            if (line.Length > 0) lines.Add(line.ToString());
            return lines;
        }

        // Whitespace takes gaps rather than marks, a mark on a space having nothing to sit on.
        // Overlay first and marks after: canonical order (class 1 before 220 and 230), and the
        // only order that draws right, a stroke being positioned against the letter.
        static string Season(string s, System.Random rng)
        {
            var sb = new System.Text.StringBuilder(s.Length * 3);
            foreach (char c in s)
            {
                sb.Append(c);
                if (c == ' ' && rng.NextDouble() < GapChance) sb.Append(Gaps[rng.Next(Gaps.Length)]);
                if (char.IsWhiteSpace(c)) continue;
                if (rng.NextDouble() < OverlayChance) sb.Append(Overlays[rng.Next(Overlays.Length)]);
                if (rng.NextDouble() >= MarkChance) continue;
                sb.Append(Marks[rng.Next(Marks.Length)]);
                if (rng.NextDouble() < DoubleChance) sb.Append(Marks[rng.Next(Marks.Length)]);
            }
            return sb.ToString();
        }

        static readonly FieldInfo AllTips =
            AccessTools.Field(typeof(GameplayTipWindow), "allTipsCached");
        static readonly FieldInfo CurrentTip =
            AccessTools.Field(typeof(GameplayTipWindow), "currentTipIndex");
        static readonly FieldInfo LastRotated =
            AccessTools.Field(typeof(GameplayTipWindow), "lastTimeUpdatedTooltip");

        static float _shown;

        // Ours rather than vanilla's field; the field is written anyway, for the build where
        // Patch_LoadingTipBlock did not bind. Which row of the wall the top of the block holds.
        static int _top;
        internal static int Top => _top;

        // The rows on screen, seasoned, top to bottom - the ring the scroll turns. A row keeps
        // its noise until something rolls it again.
        static string[] _rows;

        // The rows joined as of the last tick, so the noise runs on the tick's clock rather than
        // the frame rate's and DrawContents does no work of its own.
        static string _painted;
        internal static string Painted => _painted;

        static readonly System.Text.StringBuilder _block = new System.Text.StringBuilder();

        // Grandma's wall is clean, so a row is itself.
        static string Row(List<string> wall, int n) =>
            Settings.GrandmaMode
                ? wall[(_top + n) % wall.Count]
                : Season(wall[(_top + n) % wall.Count], Dice);

        static string Join()
        {
            _block.Length = 0;
            for (int n = 0; n < _rows.Length; n++)
            {
                if (n > 0) _block.Append('\n');
                _block.Append(_rows[n]);
            }
            return _block.ToString();
        }

        static void Prefix()
        {
            var wall = Wall;
            if (wall.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now - _shown >= Tick || _painted == null)
            {
                _shown = now;
                bool grandma = Settings.GrandmaMode;
                bool fresh = _rows == null || _rows.Length != Lines;

                if (fresh)
                {
                    _rows = new string[Lines];
                    for (int n = 0; n < _rows.Length; n++) _rows[n] = Row(wall, n);
                }

                bool scrolled = Dice.NextDouble() < ScrollChance;
                if (scrolled && !fresh)
                {
                    // Only the row arriving at the bottom is new; the rest were on screen a tick
                    // ago, one row higher.
                    _top = (_top + 1) % wall.Count;
                    Array.Copy(_rows, 1, _rows, 0, _rows.Length - 1);
                    _rows[_rows.Length - 1] = Row(wall, _rows.Length - 1);
                }

                if (!grandma)
                    for (int i = 0; i < Flicker; i++)
                    {
                        int n = Dice.Next(_rows.Length);
                        _rows[n] = Row(wall, n);
                    }

                // A clean wall that did not scroll would rebuild the block already up.
                if (fresh || scrolled || !grandma) _painted = Join();
            }

            // A field this build has never heard of leaves vanilla's list in the cache, which
            // only matters if the draw patch missed too - then the wall degrades to vanilla's
            // own one-row-at-a-time draw off the same stream.
            if (AllTips != null)
            {
                if (!ReferenceEquals(AllTips.GetValue(null), wall)) AllTips.SetValue(null, wall);
                if (CurrentTip != null) CurrentTip.SetValue(null, _top);
            }

            // Holding the timer at now keeps vanilla from rolling the index on its own.
            if (LastRotated != null) LastRotated.SetValue(null, now);
        }
    }

    // Vanilla sets MiddleCenter, which for a wall means every scroll shuffles every line
    // sideways. Word wrap is off, the pair to measuring the wrap ourselves: a combining mark
    // the font gives an advance width to would push a line over the edge and let Unity re-wrap
    // it, costing the bottom line. Stands down when the wall could not be built.
    [HarmonyPatch(typeof(GameplayTipWindow), "DrawContents")]
    public static class Patch_LoadingTipBlock
    {
        static bool Prefix(Rect rect)
        {
            List<string> wall = Patch_LoadingTips.Wall;
            if (wall.Count == 0) return true;

            // A single row if the tick has not run yet; it has, DrawWindow being what called us.
            string block = Patch_LoadingTips.Painted ?? wall[Patch_LoadingTips.Top % wall.Count];

            Vector2 margin = Patch_LoadingTips.Margin;
            Rect inner = new Rect(
                rect.x + margin.x, rect.y + margin.y,
                rect.width - margin.x * 2f, rect.height - margin.y * 2f);

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;

            Widgets.BeginGroup(inner);
            Widgets.Label(new Rect(0f, 0f, inner.width, inner.height), block);
            Widgets.EndGroup();

            Text.WordWrap = wrap;
            Text.Font = font;
            Text.Anchor = TextAnchor.UpperLeft;
            return false;
        }
    }

    // The wall of tips and nothing else; the status box above them goes. A re-layout rather
    // than a hidden box, because LongEventsOnGUI centres the stack on the sum of the heights
    // it is about to draw. Everything it was not asked about falls through to vanilla.
    [HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.LongEventsOnGUI))]
    public static class Patch_LoadingLayout
    {
        static readonly FieldInfo CurrentEvent =
            AccessTools.Field(typeof(LongEventHandler), "currentEvent");
        static readonly Type EventType = CurrentEvent?.FieldType;
        static readonly FieldInfo ForceHideUI =
            EventType == null ? null : AccessTools.Field(EventType, "forceHideUI");
        static readonly FieldInfo ShowExtraUIInfo =
            EventType == null ? null : AccessTools.Field(EventType, "showExtraUIInfo");
        static readonly MethodInfo UseStandardWindow =
            EventType == null ? null : AccessTools.PropertyGetter(EventType, "UseStandardWindow");

        static readonly FieldInfo WindowSizeField =
            AccessTools.Field(typeof(GameplayTipWindow), nameof(GameplayTipWindow.WindowSize));

        // Box rather than a number, the same figure being what the text was wrapped to. The
        // field is `static initonly`, which this runtime may or may not let reflection write;
        // a refusal leaves vanilla's box with the left of the wall in it. Keyed on generation,
        // not a bool, so a screen that changed size mid-session is written again.
        static int _sizedAt = -1;

        static void EnsureSize()
        {
            Vector2 box = Patch_LoadingTips.Box;    // may re-measure, and so bump the generation
            if (_sizedAt == Patch_LoadingTips.Generation) return;
            _sizedAt = Patch_LoadingTips.Generation;
            if (WindowSizeField == null) return;
            try
            {
                WindowSizeField.SetValue(null, box);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] tip box left at vanilla's size: {e.Message}");
            }
        }

        static bool Prefix()
        {
            if (ForceHideUI == null || ShowExtraUIInfo == null || UseStandardWindow == null) return true;

            object ev = CurrentEvent.GetValue(null);
            if (ev == null) return true;                                // vanilla resets the tip timer
            if ((bool)ForceHideUI.GetValue(ev)) return true;            // vanilla draws nothing
            if ((bool)UseStandardWindow.Invoke(ev, null)) return true;  // the in-game box, not this screen
            if (Find.UIRoot == null) return true;
            if (!(bool)ShowExtraUIInfo.GetValue(ev)) return true;

            if (UIMenuBackgroundManager.background == null)
                UIMenuBackgroundManager.background = new UI_BackgroundMain();
            // Unconditional: grandma mode does not take the background away, it swaps the frames
            // behind it for the sparkling set - see MenuBackground.
            UIMenuBackgroundManager.background.BackgroundOnGUI();

            // Before the size is read: DrawWindow lays its rect out from the same field.
            EnsureSize();
            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
        }
    }

    // Both halves, because LongEventHandler asks the window how tall it is and centres the
    // stack on the total - skipping only the draw leaves the hole.
    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawWindow))]
    public static class Patch_NoModSummary
    {
        static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.GetEffectiveSize))]
    public static class Patch_NoModSummarySize
    {
        static void Postfix(ref Vector2 __result) => __result = Vector2.zero;
    }
}
