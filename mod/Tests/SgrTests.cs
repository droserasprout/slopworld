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
            // "1;31" = bold + red in a single escape.
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
            // "38;5;196;1" — 256-color then bold in one sequence.
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
    }
}
