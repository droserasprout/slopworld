using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class SgrTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("plain text is one run", PlainText);
            yield return ("empty line gives no runs", EmptyLine);
            yield return ("bold flag is set", BoldFlag);
            yield return ("basic 16 foreground colors", Basic16Fg);
            yield return ("basic 16 background colors", Basic16Bg);
            yield return ("bright foreground colors", BrightFg);
            yield return ("256-color foreground", Color256Fg);
            yield return ("truecolor foreground", TruecolorFg);
            yield return ("truecolor background", TruecolorBg);
            yield return ("reset clears attributes", ResetClears);
            yield return ("adjacent same-attr runs merge", AdjacentMerge);
            yield return ("different-attr runs stay separate", DifferentAttrSeparate);
            yield return ("CHA column jump", ChaColumnJump);
            yield return ("OSC 8 hyperlink", Osc8Hyperlink);
            yield return ("combined attributes in one escape", CombinedAttrs);
            yield return ("256-color then bold in one sequence", Color256ThenBold);
            yield return ("faint dims foreground toward background", FaintDims);
            yield return ("reverse swaps foreground and background", ReverseSwaps);
            yield return ("supplementary glyph remains one run", SupplementaryGlyph);
            yield return ("emoji sequences keep daemon cells and copy text", EmojiSequences);
            yield return ("long combining clusters keep cell geometry", LongCombiningClusters);
            yield return ("incomplete clusters preserve payload geometry", IncompleteClusters);
            yield return ("clears individual attributes", ClearsIndividualAttributes);
            yield return ("handles bright backgrounds and grey colors", BrightBackgroundAndGreyColors);
        }

        static Color DefaultFg => TerminalTheme.Current.Fg;
        static Color DefaultBg => TerminalTheme.Current.Bg;

        static void PlainText()
        {
            var runs = Sgr.ParseLine("hello");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal("hello", runs[0].Text, "text");
            AssertEx.Equal(0, runs[0].Col, "column");
            AssertEx.Equal(DefaultFg, runs[0].Fg, "fg is default");
        }

        static void EmptyLine()
        {
            AssertEx.Equal(0, Sgr.ParseLine("").Count, "empty");
            AssertEx.Equal(0, Sgr.ParseLine(null).Count, "null");
        }

        static void BoldFlag()
        {
            var runs = Sgr.ParseLine("\x1b[1mhello");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.True(runs[0].Bold, "bold set");
        }

        static void Basic16Fg()
        {
            var runs = Sgr.ParseLine("\x1b[31mred");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], runs[0].Fg, "red foreground");
        }

        static void Basic16Bg()
        {
            var runs = Sgr.ParseLine("\x1b[42mgreen");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(TerminalTheme.Current.Ansi[2], runs[0].Bg, "green background");
            AssertEx.True(runs[0].HasBg, "has background");
        }

        static void BrightFg()
        {
            var runs = Sgr.ParseLine("\x1b[91mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(TerminalTheme.Current.Ansi[9], runs[0].Fg, "bright red");
        }

        static void Color256Fg()
        {
            var runs = Sgr.ParseLine("\x1b[38;5;196mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(Sgr.Xterm256(196), runs[0].Fg, "xterm 196");
        }

        static void TruecolorFg()
        {
            var runs = Sgr.ParseLine("\x1b[38;2;100;200;50mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(new Color(100 / 255f, 200 / 255f, 50 / 255f), runs[0].Fg, "truecolor fg");
        }

        static void TruecolorBg()
        {
            var runs = Sgr.ParseLine("\x1b[48;2;10;20;30mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(new Color(10 / 255f, 20 / 255f, 30 / 255f), runs[0].Bg, "truecolor bg");
            AssertEx.True(runs[0].HasBg, "has bg");
        }

        static void ResetClears()
        {
            var runs = Sgr.ParseLine("\x1b[31mred\x1b[0mplain");
            AssertEx.Equal(2, runs.Count, "run count");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], runs[0].Fg, "first is red");
            AssertEx.Equal(DefaultFg, runs[1].Fg, "second is default after reset");
        }

        static void AdjacentMerge()
        {
            // Two SGR escapes that resolve to the same color should merge.
            var runs = Sgr.ParseLine("\x1b[31mhel\x1b[31mlo");
            AssertEx.Equal(1, runs.Count, "merged into one run");
            AssertEx.Equal("hello", runs[0].Text, "merged text");
        }

        static void DifferentAttrSeparate()
        {
            var runs = Sgr.ParseLine("\x1b[31mred\x1b[32mgreen");
            AssertEx.Equal(2, runs.Count, "two runs");
            AssertEx.Equal("red", runs[0].Text, "first text");
            AssertEx.Equal("green", runs[1].Text, "second text");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], runs[0].Fg, "first fg");
            AssertEx.Equal(TerminalTheme.Current.Ansi[2], runs[1].Fg, "second fg");
        }

        static void ChaColumnJump()
        {
            var runs = Sgr.ParseLine("\x1b[0mab\x1b[5Gc");
            AssertEx.Equal(2, runs.Count, "two runs after CHA");
            AssertEx.Equal(0, runs[0].Col, "first at 0");
            AssertEx.Equal("ab", runs[0].Text, "first text");
            AssertEx.Equal(4, runs[1].Col, "CHA 5 is column 4");
            AssertEx.Equal("c", runs[1].Text, "second text");
        }

        static void Osc8Hyperlink()
        {
            var runs = Sgr.ParseLine("\x1b]8;;https://example.com\x07link\x1b]8;;\x07");
            AssertEx.Equal(1, runs.Count, "one run");
            AssertEx.Equal("link", runs[0].Text, "text");
            AssertEx.Equal("https://example.com", runs[0].Url, "url");
        }

        static void CombinedAttrs()
        {
            // This escape sets bold text and the red color together.
            var runs = Sgr.ParseLine("\x1b[1;31mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.True(runs[0].Bold, "bold");
            // Bold brightens the color, so compare against the brightened red.
            var red = TerminalTheme.Current.Ansi[1];
            var expected = new Color(
                Math.Min(1f, red.r * 1.25f),
                Math.Min(1f, red.g * 1.25f),
                Math.Min(1f, red.b * 1.25f));
            AssertEx.Equal(expected, runs[0].Fg, "bold-brightened red");
        }

        static void Color256ThenBold()
        {
            // This escape sets a 256-color value and then sets bold text.
            var runs = Sgr.ParseLine("\x1b[38;5;196;1mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.True(runs[0].Bold, "bold set after 256-color");
            var raw = Sgr.Xterm256(196);
            var expected = new Color(
                Math.Min(1f, raw.r * 1.25f),
                Math.Min(1f, raw.g * 1.25f),
                Math.Min(1f, raw.b * 1.25f));
            AssertEx.Equal(expected, runs[0].Fg, "bold-brightened xterm 196");
        }

        static void FaintDims()
        {
            var runs = Sgr.ParseLine("\x1b[2mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            var expected = Color.Lerp(DefaultBg, DefaultFg, 0.55f);
            AssertEx.Equal(expected, runs[0].Fg, "faint dims toward bg");
        }

        static void ReverseSwaps()
        {
            var runs = Sgr.ParseLine("\x1b[7mhi");
            AssertEx.Equal(1, runs.Count, "run count");
            AssertEx.Equal(DefaultBg, runs[0].Fg, "fg becomes bg");
            AssertEx.Equal(DefaultFg, runs[0].Bg, "bg becomes fg");
            AssertEx.True(runs[0].HasBg, "has bg under reverse");
        }

        static void SupplementaryGlyph()
        {
            // The daemon's wide emoji occupies columns 0 and 1, then re-anchors the next
            // cell at column 2. Keep the UTF-16 pair intact while retaining terminal columns.
            var runs = Sgr.ParseLine("\U0001F916\x1b[3G\x1b[31mX");
            AssertEx.Equal(2, runs.Count, "run count");
            AssertEx.Equal("\U0001F916", runs[0].Text, "emoji pair is intact");
            AssertEx.Equal(0, runs[0].Col, "emoji starts at column zero");
            AssertEx.Equal("X", runs[1].Text, "tail text");
            AssertEx.Equal(2, runs[1].Col, "tail starts after the wide emoji");
        }

        static void EmojiSequences()
        {
            var pager = Sgr.ParseLine("\x1b[0m# \x1b[2;2z👋🏻\x1b[5G E1.0 waving hand");
            AssertEx.True(pager.Exists(r => r.Text == "👋🏻" && r.IsCluster && r.Columns == 2),
                "pager skin tone remains one sprite cluster");
            AssertEx.True(pager.Exists(r => r.Text == " E1.0 waving hand" && !r.IsCluster),
                "pager description remains ordinary text");
            var heart = Sgr.ParseLine("\x1b[0m# \x1b[4;2z❤️‍🔥\x1b[5G E13.1 heart on fire");
            AssertEx.True(heart.Exists(r => r.Text == "❤️‍🔥" && r.IsCluster && r.Columns == 2),
                "pager joined heart remains one sprite cluster");
            var redHeart = Sgr.ParseLine("\x1b[0m# \x1b[2;2z❤️\x1b[5G E0.6 red heart");
            AssertEx.True(redHeart.Exists(r => r.Text == "❤️" && r.IsCluster && r.Columns == 2),
                "two-scalar red heart remains separate from pager description");
            var pagerHeart = Sgr.ParseLine("\x1b[0m# \x1b[3;1z❤️‍🔥\x1b[6G E13.1 heart on fire");
            AssertEx.True(pagerHeart.Exists(r => r.Text == "❤️‍🔥" && r.IsCluster),
                "joined heart stays separate from pager description");
            var cases = new[]
            {
                (Wire: "\x1b[0m\x1b[2;2z👩‍\x1b[3G💻\x1b[5Gx", Key: "👩‍💻", Width: 4),
                (Wire: "\x1b[0m👍\x1b[3G🏽\x1b[5Gx", Key: "👍🏽", Width: 4),
                (Wire: "\x1b[0m🇺\x1b[3G🇾\x1b[5Gx", Key: "🇺🇾", Width: 4),
                (Wire: "\x1b[0m\x1b[3;1z1️⃣x", Key: "1️⃣", Width: 1),
                (Wire: "\x1b[0m👩\x1b[3G\x1b[2;2z🏽‍\x1b[5G💻\x1b[7Gx",
                    Key: "👩🏽‍💻", Width: 6),
                (Wire: "\x1b[0m\x1b[3;2z🏳️‍\x1b[3G🌈\x1b[5Gx",
                    Key: "🏳️‍🌈", Width: 4),
                (Wire: "\x1b[0m\x1b[7;2z🏴\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007Fx",
                    Key: "🏴\U000E0067\U000E0062\U000E0065\U000E006E\U000E0067\U000E007F", Width: 2),
            };
            foreach (var sample in cases)
            {
                var runs = Sgr.ParseLine(sample.Wire);
                AssertEx.Equal(sample.Key, runs[0].Text, "complete sequence reaches renderer" + ": " + sample.Key);
                AssertEx.Equal(sample.Width, runs[0].Columns, "daemon width survives sequence" + ": " + sample.Key);
                AssertEx.True(runs[0].IsCluster, "sequence is one display cluster" + ": " + sample.Key);
                AssertEx.Equal(sample.Key + "x", TerminalColumns.Slice(TerminalColumns.Cells(runs),
                    0, sample.Width), "copy keeps sequence once" + ": " + sample.Key);
                AssertEx.True(TextSpriteCatalog.Shared.Match(sample.Key, 0, out int length, out _)
                    && length == sample.Key.Length, "sequence has Noto artwork" + ": " + sample.Key);
                var layout = InlineTextLayout.Cells(runs[0].Text, runs[0].Columns, 7f,
                    TextSpriteCatalog.Shared, _ => true);
                AssertEx.True(layout.Spans[0].Sprite >= 0, "sequence selects atlas sprite" + ": " + sample.Key);
                AssertEx.Equal(sample.Width * 7f, layout.Spans[0].Width, "sprite uses daemon cells" + ": " + sample.Key);
            }
            var combining = Sgr.ParseLine("\x1b[0m\x1b[2;1zéx");
            AssertEx.Equal("é", combining[0].Text, "non-emoji combining text is preserved");
            AssertEx.Equal("éx", TerminalColumns.Slice(TerminalColumns.Cells(combining), 0, 1),
                "non-emoji combining text copies as one cell");
        }

        static void LongCombiningClusters()
        {
            foreach (int marks in new[] { 32, 128 })
            {
                string cluster = "e" + new string('\u0301', marks);
                var runs = Sgr.ParseLine("\x1b[0m\x1b[" + (marks + 1) + ";1z" + cluster + "x");
                AssertEx.Equal(2, runs.Count, "cluster stays separate from following text");
                AssertEx.True(runs[0].IsCluster, "long cluster is recognized");
                AssertEx.Equal(1, runs[0].Columns, "combining marks do not advance the pen");
                AssertEx.Equal(1, runs[1].Col, "following text starts in the next cell");
                AssertEx.Equal(cluster + "x", TerminalColumns.Slice(TerminalColumns.Cells(runs),
                    0, 1), "copy preserves every combining mark");
            }
            var truncated = Sgr.ParseLine("\x1b[2147483647;1zex");
            AssertEx.Equal("ex", truncated[0].Text, "impossible count leaves payload as text");
        }

        static void IncompleteClusters()
        {
            foreach (string wire in new[] { "\x1b[3;1z😀x", "\x1b[3;1ze\x1b[0mx" })
            {
                var runs = Sgr.ParseLine(wire);
                AssertEx.True(!runs[0].IsCluster, "incomplete payload is not consumed as a cluster");
                AssertEx.Equal(2, runs[runs.Count - 1].Col + runs[runs.Count - 1].Columns,
                    "fallback preserves ordinary scalar geometry");
            }
        }


        static void ClearsIndividualAttributes()
        {
            var runs = Sgr.ParseLine(
                "\x1b[1;2;7;31;42mA\x1b[22;27;39;49mB");

            AssertEx.Equal(2, runs.Count, "attribute reset splits runs");
            AssertEx.True(runs[0].Bold, "bold is set before its reset");
            AssertEx.True(runs[0].HasBg, "background is set before its reset");
            AssertEx.Equal(TerminalTheme.Current.Ansi[1], runs[0].Bg,
                           "reverse moves foreground into the background");
            AssertEx.Equal(TerminalTheme.Current.Ansi[2], runs[0].Fg,
                           "reverse moves background into the foreground");
            AssertEx.Equal(DefaultFg, runs[1].Fg, "foreground reset");
            AssertEx.False(runs[1].HasBg, "background reset");
            AssertEx.False(runs[1].Bold, "bold reset");
            AssertEx.Equal(DefaultBg, runs[1].Bg, "background value after reset");
        }

        static void BrightBackgroundAndGreyColors()
        {
            var runs = Sgr.ParseLine("\x1b[100mbright background");
            AssertEx.Equal(TerminalTheme.Current.Ansi[8], runs[0].Bg,
                           "bright black background");
            AssertEx.True(runs[0].HasBg, "bright background is present");

            var firstGrey = Sgr.Xterm256(232);
            var lastGrey = Sgr.Xterm256(255);
            AssertEx.Equal(new Color(8 / 255f, 8 / 255f, 8 / 255f), firstGrey,
                           "first xterm grey");
            AssertEx.Equal(new Color(238 / 255f, 238 / 255f, 238 / 255f), lastGrey,
                           "last xterm grey");
            AssertEx.Equal(TerminalTheme.Current.Ansi[0], Sgr.Xterm256(-1),
                           "negative xterm index clamps");
        }


    }
}
