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
        const float WordsPerSecond = 100f;
        const int MaxWordsPerFrame = 48;
        const int ScrollLines = 10;

        // Text sits inside a full-height panel. Keep the width narrow enough to read as a
        // terminal instead of turning the loading screen into a wall of tiny type.
        const float WidthRatio = 0.6f;
        const float MaxWidth = 720f;
        const float MinWidth = 360f;

        // This is the same padding on both sides of the panel: the stream begins at its
        // top-left inner corner, rather than inheriting GameplayTipWindow's centred label.
        internal static readonly Vector2 Margin = new Vector2(16f, 16f);
        internal static readonly Color ContainerBackground = new Color(0.13f, 0.14f, 0.15f);
        internal static readonly Color StreamText = new Color(0.86f, 0.87f, 0.88f);

        // Not Verse.Rand: this screen is up during map generation, so drawing a tip must not
        // consume the game's deterministic sequence for terrain and pawns.
        static readonly System.Random Dice = new System.Random();

        // The box is measured against the current screen because both the wrapped stream and
        // GameplayTipWindow's immediate window need the same dimensions.
        static int _measuredW, _measuredH;
        static Vector2 _box;
        static int _lineCount;

        // Bumped per re-measure. The loading stream is discarded when its wrapping width or
        // available height changes.
        internal static int Generation;

        internal static int Lines
        {
            get { var _ = Box; return _lineCount; }
        }

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
            try
            {
                return Text.CalcHeight(probe.ToString(), width);
            }
            finally
            {
                Text.Font = font;
                Text.WordWrap = wrap;
            }
        }

        internal static Vector2 Box
        {
            get
            {
                if (_measuredW == UI.screenWidth && _measuredH == UI.screenHeight) return _box;

                _measuredW = UI.screenWidth;
                _measuredH = UI.screenHeight;
                Generation++;

                float w = Mathf.Min(MaxWidth, Mathf.Max(MinWidth, UI.screenWidth * WidthRatio));
                w = Mathf.Min(w, Mathf.Max(120f, UI.screenWidth - 40f));
                float h = Mathf.Max(1f, UI.screenHeight);
                float textWidth = Mathf.Max(1f, w - Margin.x * 2f);
                float textHeight = Mathf.Max(1f, h - Margin.y * 2f);

                // CalcHeight has a little font slack before the first line, so measure one line
                // and the increment for subsequent lines instead of dividing by Text.LineHeight.
                float one = ProbeHeight(1, textWidth);
                float step = Mathf.Max(1f, ProbeHeight(2, textWidth) - one);
                _lineCount = Mathf.Max(1, Mathf.FloorToInt((textHeight - one) / step) + 1);
                _box = new Vector2(w, h);

                ResetStream();
                return _box;
            }
        }

        // The visible stream is a line buffer, not a pre-wrapped wall. Keeping lines explicitly
        // lets a full panel jump ten rows at once and leaves the newly exposed rows empty.
        static readonly List<string> Stream = new List<string>();
        static readonly List<string> Tokens = new List<string>();
        static readonly System.Text.StringBuilder PaintedBuilder = new System.Text.StringBuilder();
        static int _tokenAt;
        static string _painted = string.Empty;
        static float _wordBudget;
        static float _lastAdvancedAt = -1f;
        static bool _modeKnown;
        static bool _grandma;
        static object _loadingEvent;

        internal static string Painted => _painted;

        static void ResetStream()
        {
            Stream.Clear();
            Tokens.Clear();
            _tokenAt = 0;
            _painted = string.Empty;
            _wordBudget = 0f;
            _lastAdvancedAt = -1f;
        }

        // LongEventHandler owns one event object for a loading operation. A new object means a
        // fresh stream, even when RimWorld loads several maps during one process.
        internal static void Begin(object loadingEvent)
        {
            if (ReferenceEquals(_loadingEvent, loadingEvent)) return;
            _loadingEvent = loadingEvent;
            _modeKnown = false;
            ResetStream();
        }

        static string NextTip()
        {
            bool grandma = Settings.GrandmaMode;
            for (int i = 0; i < Tips.Count; i++)
            {
                string tip = Tips[Dice.Next(Tips.Count)];
                if (Shown(tip, grandma)) return Strip(tip);
            }

            return "Loading.";
        }

        // Keep the separators deliberately plain: tips are split on spaces, while a null token
        // represents an explicit newline. No parser or word-pool machinery is needed here.
        static void LoadTip()
        {
            Tokens.Clear();
            _tokenAt = 0;
            string tip = NextTip();
            string[] paragraphs = tip.Split(new[] { '\n' }, StringSplitOptions.None);
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0) Tokens.Add(null);
                string[] words = paragraphs[i].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words) Tokens.Add(word);
            }

            if (Tokens.Count == 0) Tokens.Add("Loading.");
        }

        static void MakeRoomForLine()
        {
            if (Stream.Count < Lines) return;

            int remove = Mathf.Min(ScrollLines, Stream.Count);
            Stream.RemoveRange(0, remove);
        }

        static void AppendToken(string token, float width)
        {
            if (Stream.Count == 0) Stream.Add(string.Empty);

            if (token == null)
            {
                MakeRoomForLine();
                Stream.Add(string.Empty);
                return;
            }

            string current = Stream[Stream.Count - 1];
            if (current.Length == 0)
            {
                Stream[Stream.Count - 1] = token;
                return;
            }

            string candidate = current + " " + token;
            if (Text.CalcSize(candidate).x <= width)
            {
                Stream[Stream.Count - 1] = candidate;
                return;
            }

            MakeRoomForLine();
            Stream.Add(token);
        }

        static string JoinStream()
        {
            PaintedBuilder.Length = 0;
            for (int i = 0; i < Stream.Count; i++)
            {
                if (i > 0) PaintedBuilder.Append('\n');
                PaintedBuilder.Append(Stream[i]);
            }
            return PaintedBuilder.ToString();
        }

        static void Advance()
        {
            bool grandma = Settings.GrandmaMode;
            if (!_modeKnown || _grandma != grandma)
            {
                _modeKnown = true;
                _grandma = grandma;
                ResetStream();
            }

            float now = Time.realtimeSinceStartup;
            if (_lastAdvancedAt < 0f)
            {
                _lastAdvancedAt = now;
                _wordBudget = MaxWordsPerFrame;
            }
            else
            {
                _wordBudget += Mathf.Max(0f, now - _lastAdvancedAt) * WordsPerSecond;
                _lastAdvancedAt = now;
            }

            int count = Mathf.Min(MaxWordsPerFrame, Mathf.FloorToInt(_wordBudget));
            if (count <= 0) return;
            _wordBudget -= count;

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            Text.Font = GameFont.Small;
            Text.WordWrap = false;
            try
            {
                float width = Mathf.Max(1f, Box.x - Margin.x * 2f);
                for (int i = 0; i < count; i++)
                {
                    if (_tokenAt >= Tokens.Count) LoadTip();
                    AppendToken(Tokens[_tokenAt++], width);
                }
                _painted = JoinStream();
            }
            finally
            {
                Text.Font = font;
                Text.WordWrap = wrap;
            }
        }

        static readonly FieldInfo LastRotated =
            AccessTools.Field(typeof(GameplayTipWindow), "lastTimeUpdatedTooltip");

        static void Prefix()
        {
            Advance();

            // Holding the vanilla timer at now keeps it from changing its own one-line tip while
            // our word stream is being painted.
            if (LastRotated != null) LastRotated.SetValue(null, Time.realtimeSinceStartup);
        }
    }

    [HarmonyPatch(typeof(GameplayTipWindow), "DrawContents")]
    public static class Patch_LoadingTipBlock
    {
        static bool Prefix(Rect rect)
        {
            Vector2 margin = Patch_LoadingTips.Margin;
            Rect inner = new Rect(
                rect.x + margin.x, rect.y + margin.y,
                Mathf.Max(1f, rect.width - margin.x * 2f),
                Mathf.Max(1f, rect.height - margin.y * 2f));

            Widgets.DrawBoxSolid(rect, Patch_LoadingTips.ContainerBackground);

            GameFont font = Text.Font;
            bool wrap = Text.WordWrap;
            TextAnchor anchor = Text.Anchor;
            Color guiColor = GUI.color;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = false;
            GUI.color = Patch_LoadingTips.StreamText;

            try
            {
                Widgets.BeginGroup(inner);
                Widgets.Label(new Rect(0f, 0f, inner.width, inner.height),
                    Patch_LoadingTips.Painted);
                Widgets.EndGroup();
            }
            finally
            {
                Text.WordWrap = wrap;
                Text.Font = font;
                Text.Anchor = anchor;
                GUI.color = guiColor;
            }

            return false;
        }
    }

    // Draw the stream and nothing else; the status box and mod summary are not useful while a
    // map is being generated. LongEventsOnGUI centres this full-height window horizontally.
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

        static int _sizedAt = -1;

        static void EnsureSize()
        {
            Vector2 box = Patch_LoadingTips.Box;
            if (_sizedAt == Patch_LoadingTips.Generation) return;
            _sizedAt = Patch_LoadingTips.Generation;
            if (WindowSizeField == null) return;
            try
            {
                WindowSizeField.SetValue(null, box);
            }
            catch (Exception e)
            {
                Log.Warning($"[SlopWorld] loading screen size left at vanilla's value: {e.Message}");
            }
        }

        static bool Prefix()
        {
            if (ForceHideUI == null || ShowExtraUIInfo == null || UseStandardWindow == null) return true;

            object ev = CurrentEvent.GetValue(null);
            if (ev == null)
            {
                Patch_LoadingTips.Begin(null);
                return true;
            }
            if ((bool)ForceHideUI.GetValue(ev)) return true;
            if ((bool)UseStandardWindow.Invoke(ev, null)) return true;
            if (Find.UIRoot == null) return true;
            if (!(bool)ShowExtraUIInfo.GetValue(ev)) return true;

            Patch_LoadingTips.Begin(ev);

            if (UIMenuBackgroundManager.background == null)
                UIMenuBackgroundManager.background = new UI_BackgroundMain();
            UIMenuBackgroundManager.background.BackgroundOnGUI();

            EnsureSize();
            Vector2 size = GameplayTipWindow.WindowSize;
            GameplayTipWindow.DrawWindow(
                new Vector2((UI.screenWidth - size.x) / 2f, (UI.screenHeight - size.y) / 2f), false);
            return false;
        }
    }

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
