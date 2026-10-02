using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TextSelectionTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("word range includes Unicode letters", UnicodeWord);
            yield return ("word range never splits a surrogate pair", SurrogateWord);
            yield return ("non-word range groups identical code points", PunctuationRun);
            yield return ("line range is half-open and includes newline", Lines);
            yield return ("word drag right keeps the clicked word", WordDragRight);
            yield return ("word drag left keeps the clicked word", WordDragLeft);
            yield return ("range exposes ordered endpoints", OrderedRange);
        }

        public static void IsolatedSurrogatesRemainSelectable()
        {
            foreach (string scalar in new[] { "\ud800", "\udc00" })
            {
                var range = TextSelectionRules.WordRange("a" + scalar + "b", 1);
                AssertEx.Equal(1, range.Start, "isolated surrogate starts at its code unit");
                AssertEx.Equal(2, range.End, "isolated surrogate ends after its code unit");
                AssertEx.Equal(1, TextSelectionRules.WordRange(scalar, 0).End, "trailing isolated surrogate does not throw");
            }
        }

        static void UnicodeWord()
        {
            var range = TextSelectionRules.WordRange("αβ 42", 1);
            AssertEx.Equal(0, range.Start, "word starts at first Greek letter");
            AssertEx.Equal(2, range.End, "word ends after second Greek letter");
        }

        static void SurrogateWord()
        {
            string text = "go😀now";
            var range = TextSelectionRules.WordRange(text, 3);
            AssertEx.Equal(2, range.Start, "emoji starts at its high surrogate");
            AssertEx.Equal(4, range.End, "emoji ends after its low surrogate");
        }

        static void PunctuationRun()
        {
            var range = TextSelectionRules.WordRange("a---b", 2);
            AssertEx.Equal(1, range.Start, "punctuation run starts at first dash");
            AssertEx.Equal(4, range.End, "punctuation run ends after last dash");
        }

        static void Lines()
        {
            var first = TextSelectionRules.LineRange("one\ntwo", 1);
            var second = TextSelectionRules.LineRange("one\ntwo", 5);
            AssertEx.Equal(0, first.Start, "first line starts at zero");
            AssertEx.Equal(4, first.End, "first line includes its newline");
            AssertEx.Equal(4, second.Start, "second line starts after newline");
            AssertEx.Equal(7, second.End, "last line ends at text length");
        }

        static void OrderedRange()
        {
            var range = new TextSelectionRange(9, 3);
            AssertEx.Equal(9, range.Anchor, "anchor is preserved");
            AssertEx.Equal(3, range.Focus, "focus is preserved");
            AssertEx.Equal(3, range.Start, "start is ordered");
            AssertEx.Equal(9, range.End, "end is ordered");
            AssertEx.True(range.HasSelection, "reversed range is non-empty");
        }

        static void WordDragRight()
        {
            var original = new TextSelectionRange(2, 8);
            var destination = new TextSelectionRange(6, 7);
            var range = TextSelectionRules.ExpandWordSelection(original, destination);
            AssertEx.Equal(2, range.Start, "right drag starts at clicked word");
            AssertEx.Equal(8, range.End, "right drag keeps the clicked word intact");
        }

        static void WordDragLeft()
        {
            var original = new TextSelectionRange(6, 9);
            var destination = new TextSelectionRange(2, 5);
            var range = TextSelectionRules.ExpandWordSelection(original, destination);
            AssertEx.Equal(2, range.Start, "left drag reaches destination word");
            AssertEx.Equal(9, range.End, "left drag keeps clicked word");
        }
    }
}
