using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    [HarmonyPatch(typeof(GameplayTipWindow), nameof(GameplayTipWindow.DrawWindow))]
    public static partial class Patch_LoadingTips
    {
        const float WordsPerSecond = 52.5f;
        const int MaxWordsPerFrame = 48;
        const float ScrollHalfStepSeconds = 0.06f;

        const int LoadingFontBump = 10;

        // This is the same padding on both sides of the panel: the stream begins at its
        // top-left inner corner, rather than inheriting GameplayTipWindow's centred label.
        internal static readonly Vector2 Margin = new Vector2(16f, 16f);
        internal static readonly Color StreamText = new Color(0.82f, 0.88f, 0.93f);

        // Keep the typewriter tail visible: word n-1 is muted and word n is deepest.
        internal static readonly Color PreviousWordText = ScaleRgb(StreamText, 0.75f);
        internal static readonly Color LatestWordText = ScaleRgb(StreamText, 0.55f);

        // Not Verse.Rand: this screen is up during map generation, so drawing a tip must not
        // consume the game's deterministic sequence for terrain and pawns.
        static readonly System.Random Dice = new System.Random();

        static Color ScaleRgb(Color color, float scale) =>
            new Color(color.r * scale, color.g * scale, color.b * scale, color.a);

        // The stream wraps against the same screen-space panel that owns its drawing clip.
        static int _measuredW,
            _measuredH;
        static int _measuredFontSize = -1;
        static Vector2 _box;
        static int _lineCount;
        static int FontSize => Mathf.Clamp(Settings.FontSize + LoadingFontBump, 8, 32);

        // Keep this grid in sync with tools/assets/loading_font_atlas.py.
        const int GlyphFirst = 32,
            GlyphLast = 126;
        const int GlyphSource = 64,
            GlyphWidth = 32,
            GlyphHeight = 64;
        const int GlyphLineHeight = 60;
        const int GlyphColumns = 16,
            GlyphRows = 6;
        const int GlyphGutter = 1;
        const int GlyphPitchWidth = GlyphWidth + GlyphGutter * 2;
        const int GlyphPitchHeight = GlyphHeight + GlyphGutter * 2;
        const int GlyphAtlasWidth = GlyphColumns * GlyphPitchWidth;
        const int GlyphAtlasHeight = GlyphRows * GlyphPitchHeight;
        const float GlyphScale = 0.80f;
        static Texture2D GlyphAtlas => LoadingFontAtlas.Get();

        static float TextWidth(string text, int fontSize) =>
            text.Length * fontSize * GlyphScale * GlyphWidth / GlyphSource;

        static void DrawGlyphs(string text, float x, float y, int fontSize)
        {
            Texture2D atlas = GlyphAtlas;
            if (atlas == null || string.IsNullOrEmpty(text))
                return;

            float scale = fontSize * GlyphScale / GlyphSource;
            float width = GlyphWidth * scale,
                height = GlyphHeight * scale;
            for (int i = 0; i < text.Length; i++)
            {
                int code = text[i];
                if (code < GlyphFirst || code > GlyphLast)
                    code = '?';
                if (code == ' ')
                    continue;

                int slot = code - GlyphFirst;
                int row = slot / GlyphColumns,
                    column = slot % GlyphColumns;
                var uv = new Rect(
                    (column * GlyphPitchWidth + GlyphGutter) / (float)GlyphAtlasWidth,
                    ((GlyphRows - row - 1) * GlyphPitchHeight + GlyphGutter)
                        / (float)GlyphAtlasHeight,
                    GlyphWidth / (float)GlyphAtlasWidth,
                    GlyphHeight / (float)GlyphAtlasHeight
                );
                GUI.DrawTextureWithTexCoords(new Rect(x + i * width, y, width, height), atlas, uv);
            }
        }

        // Bumped per re-measure. A geometry change rewraps the existing stream rather than
        // discarding it. Only a new load or mode change starts a fresh stream.
        internal static int Generation;

        internal static int Lines => _lineCount;
        internal static Vector2 Box => _box;

        // Call before reading geometry or checking Generation; getters never trigger a reflow.
        internal static void Measure()
        {
            int fontSize = FontSize;
            int width = UI.screenWidth,
                height = UI.screenHeight;
            if (_measuredW == width && _measuredH == height && _measuredFontSize == fontSize)
                return;

            _measuredW = width;
            _measuredH = height;
            _measuredFontSize = fontSize;
            Generation++;

            Rect frame = LoadingTipPanel.Frame(width, height);
            float textHeight = Mathf.Max(1f, frame.height - Margin.y * 2f);
            float scale = fontSize * GlyphScale / GlyphSource;
            _lineCount = LoadingTipPanel.VisibleLines(
                textHeight,
                GlyphHeight * scale,
                GlyphLineHeight * scale
            );
            _box = new Vector2(frame.width, frame.height);
        }

        // The visible stream is a line buffer, not a pre-wrapped wall. Keeping lines explicitly
        // lets a full panel scroll one row at a time while the newly exposed row is populated.
        // Advance runs during Repaint, so a new stream fills naturally before scrolling begins.
        static readonly List<string> Stream = new List<string>();
        static readonly List<string> Tokens = new List<string>();
        static readonly System.Text.StringBuilder PaintedBuilder = new System.Text.StringBuilder();
        static int _tokenAt;
        static string _painted = string.Empty;
        static float _wordBudget;
        static float _lastAdvancedAt = -1f;
        static float _scrollStartedAt = -1f;
        static bool _modeKnown;
        static bool _grandma;
        static readonly LoadingStreamSession LoadingSession = new LoadingStreamSession();
        static bool _newLoadingSession;
        static int _streamGeneration = -1;

        internal static string Painted => _painted;

        static int WordCount()
        {
            int count = 0;
            foreach (string line in Stream)
            {
                if (line.Length == 0)
                    continue;
                count += line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            return count;
        }

        internal static void DrawStream()
        {
            int wordCount = WordCount();
            if (wordCount == 0)
                return;

            int fontSize = FontSize;
            float lineHeight = fontSize * GlyphScale * GlyphLineHeight / GlyphSource;
            int wordAt = 0;
            Color oldGuiColor = GUI.color;

            try
            {
                for (int lineAt = 0; lineAt < Stream.Count; lineAt++)
                {
                    string line = Stream[lineAt];
                    if (line.Length == 0)
                        continue;

                    string[] words = line.Split(
                        new[] { ' ' },
                        StringSplitOptions.RemoveEmptyEntries
                    );
                    float x = 0f;
                    float y = (lineAt - (_scrollStartedAt >= 0f ? 0.5f : 0f)) * lineHeight;
                    for (int i = 0; i < words.Length; i++)
                    {
                        string word = words[i];
                        string segment = i + 1 < words.Length ? word + " " : word;
                        float width = TextWidth(segment, fontSize);
                        GUI.color =
                            wordAt == wordCount - 1 ? LatestWordText
                            : wordAt == wordCount - 2 ? PreviousWordText
                            : StreamText;
                        DrawGlyphs(segment, x, y, fontSize);
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
            StreamSelection.Clear();
            Stream.Clear();
            Tokens.Clear();
            _tokenAt = 0;
            _painted = string.Empty;
            _wordBudget = 0f;
            _lastAdvancedAt = -1f;
            _scrollStartedAt = -1f;
        }

        // Null currentEvent can be a scene/queue handoff, including the menu frame that
        // schedules auto-resume. Only the host's idle interactive-frame observer ends a load.
        internal static void Begin(object loadingEvent)
        {
            if (LoadingSession.Begin(loadingEvent != null))
            {
                _newLoadingSession = true;
                Log.Message("[SlopWorld] loading stream started");
            }
        }

        internal static void InteractiveFrame(bool eventsPending) =>
            LoadingSession.InteractiveFrame(eventsPending);

        internal static bool DrawFullScreen(bool canUseGameWindow) =>
            LoadingSession.DrawFullScreen(canUseGameWindow);

        internal static bool IsFullScreen => LoadingSession.FullScreen;

        // Keep the separators deliberately plain: tips are split on spaces, while a null token
        // represents an explicit newline. No parser or word-pool machinery is needed here.
        static void LoadTip(float width)
        {
            Tokens.Clear();
            _tokenAt = 0;
            int columns = Mathf.Max(1, Mathf.FloorToInt(width / TextWidth(" ", FontSize)));
            int occupied = Stream.Count == 0 ? 0 : Stream[Stream.Count - 1].Length;
            string tip = NextStreamTip(columns, occupied);
            string[] paragraphs = tip.Split(new[] { '\n' }, StringSplitOptions.None);
            for (int i = 0; i < paragraphs.Length; i++)
            {
                if (i > 0)
                    Tokens.Add(null);
                string[] words = paragraphs[i]
                    .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string word in words)
                    Tokens.Add(word);
            }

            if (Tokens.Count == 0)
                Tokens.Add("Loading.");
        }

        static void MakeRoomForLine()
        {
            if (Stream.Count < Lines)
                return;

            // Keep the outgoing row for the clipped half-line step. Advance removes it
            // after the hold, completing the second half without losing visible glyphs.
            _scrollStartedAt = Time.realtimeSinceStartup;
        }

        static void AppendToken(string token, float width)
        {
            if (Stream.Count == 0)
                Stream.Add(string.Empty);

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
            if (TextWidth(candidate, FontSize) <= width)
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
                if (i > 0)
                    PaintedBuilder.Append('\n');
                PaintedBuilder.Append(Stream[i]);
            }
            return PaintedBuilder.ToString();
        }

        static void ReflowStream(float width)
        {
            _scrollStartedAt = -1f;
            var reflowed = new List<string>();
            foreach (string oldLine in Stream)
            {
                if (oldLine.Length == 0)
                {
                    reflowed.Add(string.Empty);
                    continue;
                }

                string line = string.Empty;
                string[] words = oldLine.Split(
                    new[] { ' ' },
                    StringSplitOptions.RemoveEmptyEntries
                );
                foreach (string word in words)
                {
                    if (line.Length == 0)
                    {
                        line = word;
                        continue;
                    }

                    string candidate = line + " " + word;
                    if (TextWidth(candidate, FontSize) <= width)
                    {
                        line = candidate;
                        continue;
                    }

                    reflowed.Add(line);
                    line = word;
                }

                if (line.Length > 0)
                    reflowed.Add(line);
            }

            while (reflowed.Count > Lines)
                reflowed.RemoveAt(0);
            Stream.Clear();
            Stream.AddRange(reflowed);
            _painted = JoinStream();
        }

        internal static void Advance()
        {
            Measure();
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

            float width = Mathf.Max(1f, box.x - Margin.x * 2f);
            if (resized)
            {
                ReflowStream(width);
                _streamGeneration = generation;
            }

            float now = Time.realtimeSinceStartup;
            // The baseline targets filling the panel during a typical load at 2x scale.
            // Account for both dimensions of the text area:
            // halving UI scale fits roughly four times as many words on the same display.
            float scaleRatio = 2f / Mathf.Max(0.5f, Prefs.UIScale);
            float capacityRatio = scaleRatio * scaleRatio;
            int frameLimit = Mathf.Max(1, Mathf.CeilToInt(MaxWordsPerFrame * capacityRatio));
            if (_lastAdvancedAt < 0f)
            {
                _lastAdvancedAt = now;
                _wordBudget = frameLimit;
            }
            else
            {
                _wordBudget +=
                    Mathf.Max(0f, now - _lastAdvancedAt) * WordsPerSecond * capacityRatio;
                _lastAdvancedAt = now;
            }

            // Bound catch-up during the half-step hold or a stalled loading frame.
            _wordBudget = Mathf.Min(_wordBudget, frameLimit);
            if (_scrollStartedAt >= 0f)
            {
                if (now - _scrollStartedAt < ScrollHalfStepSeconds)
                    return;
                Stream.RemoveAt(0);
                _scrollStartedAt = -1f;
                _painted = JoinStream();
                // Paint the completed step before another overflowing row can start.
                return;
            }

            int count = Mathf.Min(frameLimit, Mathf.FloorToInt(_wordBudget));
            if (count <= 0)
                return;

            for (int i = 0; i < count; i++)
            {
                if (_tokenAt >= Tokens.Count)
                    LoadTip(width);
                AppendToken(Tokens[_tokenAt++], width);
                _wordBudget -= 1f;
                if (_scrollStartedAt >= 0f)
                    break;
            }
            _painted = JoinStream();
        }

        internal static void DrawPanel()
        {
            Advance();
            LoadingTipPanel.Draw(
                LoadingTipPanel.Frame(UI.screenWidth, UI.screenHeight),
                Margin,
                DrawStream
            );
        }

        // All callers use our measured panel, including the window-stack loading path.
        static bool Prefix()
        {
            DrawPanel();
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
