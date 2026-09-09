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
        const float WordsPerSecond = 60f;
        const int MaxWordsPerFrame = 48;

        // Text sits inside a full-height panel. Keep the width narrow enough to read as a
        // terminal instead of turning the loading screen into a wall of tiny type.
        const float WidthRatio = 0.45f;
        const float MaxWidth = 540f;
        const float MinWidth = 280f;
        internal const float LoadingSideMargin = 0f;
        const int LoadingFontBump = 10;

        // This is the same padding on both sides of the panel: the stream begins at its
        // top-left inner corner, rather than inheriting GameplayTipWindow's centred label.
        internal static readonly Vector2 Margin = new Vector2(16f, 16f);
        internal static readonly Color ContainerBackground = Color.black;
        internal static readonly Color StreamText = new Color(0.82f, 0.88f, 0.93f);
        // Keep the typewriter tail visible: word n-1 is muted and word n is deepest.
        internal static readonly Color PreviousWordText = ScaleRgb(StreamText, 0.75f);
        internal static readonly Color LatestWordText = ScaleRgb(StreamText, 0.55f);
        // The package name is the first lookup key; the embedded family name keeps the same
        // font working on platforms whose font APIs ignore the filename.
        static readonly string[] LoadingFontNames =
        {
            "slopworld_clacon2",
            "Classic Console Neue",
            "Classic Console",
            "Courier New",
        };

        // Not Verse.Rand: this screen is up during map generation, so drawing a tip must not
        // consume the game's deterministic sequence for terrain and pawns.
        static readonly System.Random Dice = new System.Random();

        static Color ScaleRgb(Color color, float scale)
            => new Color(color.r * scale, color.g * scale, color.b * scale, color.a);

        // The box is measured against the current screen because both the wrapped stream and
        // GameplayTipWindow's immediate window need the same dimensions.
        static int _measuredW, _measuredH;
        static int _measuredFontSize = -1;
        static Vector2 _box;
        static int _lineCount;
        static GUIStyle _loadingStyle;
        static Font _loadingFont;
        static int _loadingFontSize = -1;

        // Bumped per re-measure. A geometry change rewraps the existing stream rather than
        // discarding it; only a new load or mode change starts a fresh stream.
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

            return LoadingStyle.CalcHeight(new GUIContent(probe.ToString()), width);
        }

        internal static GUIStyle LoadingStyle
        {
            get
            {
                int size = Mathf.Clamp(Settings.FontSize + LoadingFontBump, 8, 32);
                if (_loadingStyle != null && _loadingFont != null && _loadingFontSize == size)
                    return _loadingStyle;

                Font old = _loadingFont;
                _loadingFont = CreateLoadingFont(size);
                if (_loadingFont == null)
                {
                    Log.Warning("[SlopWorld] loading screen font unavailable: " +
                        string.Join(", ", LoadingFontNames));
                    _loadingFont = Font.CreateDynamicFontFromOSFont("Courier New", size);
                }

                if (_loadingFont != null)
                    _loadingFont.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _loadingFontSize = size;
                _loadingStyle = new GUIStyle
                {
                    font = _loadingFont,
                    fontSize = size,
                    fontStyle = FontStyle.Normal,
                    richText = false,
                    wordWrap = false,
                    clipping = TextClipping.Overflow,
                    alignment = TextAnchor.UpperLeft,
                    normal = new GUIStyleState { textColor = Color.white },
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                };

                if (old != null && old != _loadingFont) UnityEngine.Object.Destroy(old);
                return _loadingStyle;
            }
        }

        static Font CreateLoadingFont(int size)
            => Font.CreateDynamicFontFromOSFont(LoadingFontNames, size);

        internal static Vector2 Box
        {
            get
            {
                int fontSize = Mathf.Clamp(Settings.FontSize + LoadingFontBump, 8, 32);
                if (_measuredW == UI.screenWidth && _measuredH == UI.screenHeight
                    && _measuredFontSize == fontSize)
                    return _box;

                _measuredW = UI.screenWidth;
                _measuredH = UI.screenHeight;
                _measuredFontSize = fontSize;
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

                return _box;
            }
        }

        // The visible stream is a line buffer, not a pre-wrapped wall. Keeping lines explicitly
        // lets a full panel scroll one row at a time while the newly exposed row is populated.
        // Advance runs after the panel background is issued during Repaint, so a new stream
        // starts only with a visible container and fills naturally before scrolling begins.
        static readonly List<string> Stream = new List<string>();
        static readonly List<string> Tokens = new List<string>();
        static readonly System.Text.StringBuilder PaintedBuilder = new System.Text.StringBuilder();
        static int _tokenAt;
        static string _painted = string.Empty;
        static float _wordBudget;
        static float _lastAdvancedAt = -1f;
        static bool _modeKnown;
        static bool _grandma;
        static bool _loadingSession;
        static bool _newLoadingSession;
        static int _streamGeneration = -1;

        internal static string Painted => _painted;

        static int WordCount()
        {
            int count = 0;
            foreach (string line in Stream)
            {
                if (line.Length == 0) continue;
                count += line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            return count;
        }

        internal static void DrawStream(Rect rect)
        {
            GUIStyle style = LoadingStyle;
            int wordCount = WordCount();
            if (wordCount == 0) return;

            float lineHeight = Mathf.Max(1f, style.lineHeight);
            int wordAt = 0;
            Color oldGuiColor = GUI.color;

            try
            {
                for (int lineAt = 0; lineAt < Stream.Count; lineAt++)
                {
                    string line = Stream[lineAt];
                    if (line.Length == 0) continue;

                    string[] words = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    float x = 0f;
                    float y = lineAt * lineHeight;
                    for (int i = 0; i < words.Length; i++)
                    {
                        string word = words[i];
                        string segment = i + 1 < words.Length ? word + " " : word;
                        float width = style.CalcSize(new GUIContent(segment)).x;
                        GUI.color = wordAt == wordCount - 1
                            ? LatestWordText
                            : wordAt == wordCount - 2 ? PreviousWordText : StreamText;
                        GUI.Label(new Rect(x, y, Mathf.Max(1f, width + 2f), lineHeight),
                            segment, style);
                        x += width;
                        wordAt++;
                    }
                }
            }
            finally
            {
                GUI.color = oldGuiColor;
            }
        }

        static void ResetStream()
        {
            Stream.Clear();
            Tokens.Clear();
            _tokenAt = 0;
            _painted = string.Empty;
            _wordBudget = 0f;
            _lastAdvancedAt = -1f;
        }

        // One loading screen can move through several QueuedLongEvent objects. Keep one stream
        // across those queue handoffs; null currentEvent is the boundary between load sessions.
        internal static void Begin(object loadingEvent)
        {
            if (loadingEvent == null)
            {
                _loadingSession = false;
                return;
            }

            if (_loadingSession) return;
            _loadingSession = true;
            _newLoadingSession = true;
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

            Stream.RemoveAt(0);
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
            if (LoadingStyle.CalcSize(new GUIContent(candidate)).x <= width)
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

        static void ReflowStream(float width)
        {
            var reflowed = new List<string>();
            foreach (string oldLine in Stream)
            {
                if (oldLine.Length == 0)
                {
                    reflowed.Add(string.Empty);
                    continue;
                }

                string line = string.Empty;
                string[] words = oldLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                {
                    if (line.Length == 0)
                    {
                        line = word;
                        continue;
                    }

                    string candidate = line + " " + word;
                    if (LoadingStyle.CalcSize(new GUIContent(candidate)).x <= width)
                    {
                        line = candidate;
                        continue;
                    }

                    reflowed.Add(line);
                    line = word;
                }

                if (line.Length > 0) reflowed.Add(line);
            }

            while (reflowed.Count > Lines) reflowed.RemoveAt(0);
            Stream.Clear();
            Stream.AddRange(reflowed);
            _painted = JoinStream();
        }

        internal static void Advance()
        {
            Vector2 box = Box;
            int generation = Generation;
            bool grandma = Settings.GrandmaMode;
            bool fresh = _newLoadingSession || !_modeKnown || _grandma != grandma;
            bool resized = _streamGeneration != generation;
            if (fresh)
            {
                _newLoadingSession = false;
                _modeKnown = true;
                _grandma = grandma;
                _streamGeneration = generation;
                ResetStream();
            }

            if (Event.current != null && Event.current.type != EventType.Repaint)
            {
                return;
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

            float width = Mathf.Max(1f, box.x - Margin.x * 2f);
            if (resized)
            {
                ReflowStream(width);
                _streamGeneration = generation;
            }

            for (int i = 0; i < count; i++)
            {
                if (_tokenAt >= Tokens.Count) LoadTip();
                AppendToken(Tokens[_tokenAt++], width);
            }
            _painted = JoinStream();
        }

        static readonly FieldInfo LastRotated =
            AccessTools.Field(typeof(GameplayTipWindow), "lastTimeUpdatedTooltip");

        static void Prefix()
        {
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
            Patch_LoadingTips.Advance();

            Widgets.BeginGroup(inner);
            try
            {
                Patch_LoadingTips.DrawStream(inner);
            }
            finally
            {
                Widgets.EndGroup();
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
                new Vector2(
                    Mathf.Max(0f, UI.screenWidth - size.x - Patch_LoadingTips.LoadingSideMargin),
                    (UI.screenHeight - size.y) / 2f), false);
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
