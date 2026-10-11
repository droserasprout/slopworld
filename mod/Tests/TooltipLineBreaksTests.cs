using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TooltipLineBreaksTests
    {
        public static void NativeBreaksBecomeExplicitWithoutLosingText()
        {
            const string text = "title\n/home/project/long-component";
            int start = text.IndexOf("long", StringComparison.Ordinal);
            string frozen = TooltipLineBreaks.Freeze(text, i => i < 6 ? 0f : i < start ? 20f : 40f, false);
            AssertEx.Equal("title\n/home/project/\nlong-component", frozen, "native soft break becomes explicit");
            AssertEx.Equal(text.Replace("\n", ""), frozen.Replace("\n", ""), "all original characters survive");
        }

        public static void ExistingBlankLinesAndCarriageReturnsAreNotDuplicated()
        {
            const string text = "a\r\n\r\nb\n";
            AssertEx.Equal(text, TooltipLineBreaks.Freeze(text, i => i == 0 ? 0f : 40f, false),
                "hard breaks remain unchanged");
        }

        public static void RichTagsSurviveSoftBreaksAndAreNotMeasuredAsCharacters()
        {
            const string text = "<color=#ff00ff><b>ab</b>cd</color>";
            int a = text.IndexOf("ab", StringComparison.Ordinal);
            int c = text.IndexOf("cd", StringComparison.Ordinal);
            var sampled = new List<int>();
            string frozen = TooltipLineBreaks.Freeze(text, i =>
            {
                sampled.Add(i);
                return i < c ? 0f : 20f;
            }, true);
            AssertEx.Equal("<color=#ff00ff><b>ab</b>\ncd</color>", frozen, "formatting spans the break");
            AssertEx.Sequence(new[] { a, a + 1, c, c + 1 }, sampled, "tag contents are not cursor boundaries");
        }

        public static void UnicodeElementsAndInlineQuadsRemainAtomic()
        {
            const string text = "e\u0301\U0001f33b<quad size=16 />x";
            var offsets = new List<int>();
            string frozen = TooltipLineBreaks.Freeze(text, i =>
            {
                offsets.Add(i);
                return i < 4 ? 0f : 20f;
            }, true);
            AssertEx.Equal("e\u0301\U0001f33b\n<quad size=16 />x", frozen, "quad and text elements are indivisible");
            AssertEx.Sequence(new[] { 0, 2, 4, text.Length - 1 }, offsets, "only visible element starts sampled");
        }

        public static void EmptyTextDoesNotInvokeNativeMeasurement()
        {
            AssertEx.Equal("", TooltipLineBreaks.Freeze(null, _ => throw new Exception("unexpected"), false),
                "empty tooltip needs no cursor layout");
        }
    }
}
