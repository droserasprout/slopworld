using UnityEngine;

namespace SlopWorld.Tests
{
    static class UiBubbleTests
    {
        public static void AdviceExpiresAtThreeSecondsAndRepeatedCleanupIsSafe()
        {
            var bubble = new UiBubbleSequence();
            bubble.Show("answer", new Vector2(10f, 20f), 7f);
            AssertEx.True(bubble.Advance(9.999f), "full three seconds");
            AssertEx.False(bubble.Advance(10f), "expires at boundary");
            bubble.Clear();
            bubble.Clear();
            AssertEx.False(bubble.Advance(100f), "cleanup cannot revive a bubble");
            AssertEx.Equal<string>(null, bubble.Text, "retire displayed text");
        }

        public static void HintSequencesKeepTheirAnchorAndGiveEveryHintThreeSeconds()
        {
            for (int total = 2; total <= 4; total++)
            {
                var bubble = new UiBubbleSequence();
                var anchor = new Vector2(200f, 50f);
                int generated = 0;
                bubble.Show("first", anchor, 0f, total - 1, () => "hint " + ++generated);
                for (int i = 1; i < total; i++)
                {
                    // A late repaint starts the next full interval instead of skipping text.
                    float at = i * 5f;
                    AssertEx.True(bubble.Advance(at), "next hint");
                    AssertEx.Equal(i, generated, "one callback per displayed hint");
                    AssertEx.Equal("hint " + i, bubble.Text, "new content");
                    AssertEx.True(bubble.Advance(at + 2.99f), "not yet expired");
                    AssertEx.Equal(anchor, bubble.Anchor, "sequence anchor is stable");
                }
                AssertEx.False(bubble.Advance((total - 1) * 5f + 3f), "sequence ends");
            }
        }

        public static void ReplacementAndCancellationDiscardUnfinishedSequence()
        {
            var bubble = new UiBubbleSequence();
            bubble.Show(
                "hint",
                new Vector2(1f, 2f),
                0f,
                3,
                () => throw new System.Exception("stale sequence")
            );
            bubble.Show("advice", new Vector2(3f, 4f), 1f);
            AssertEx.Equal("advice", bubble.Text, "fresh request replaces content");
            AssertEx.Equal(new Vector2(3f, 4f), bubble.Anchor, "fresh request replaces anchor");
            AssertEx.False(bubble.Advance(4f), "old hints discarded");
            bubble.Show("hint", default, 5f, 3, () => throw new System.Exception("retired owner"));
            bubble.Clear();
            AssertEx.False(bubble.Advance(8f), "cancel discards sequence");
        }

        public static void RetiredOwnerCancelsBeforeGeneratingTheNextHint()
        {
            var bubble = new UiBubbleSequence();
            bool alive = true;
            int guardCalls = 0;
            bubble.Show(
                "hint",
                default,
                0f,
                3,
                () => throw new System.Exception("retired request"),
                () =>
                {
                    guardCalls++;
                    return alive;
                }
            );
            AssertEx.True(bubble.Advance(1f), "live owner displays");
            alive = false;
            AssertEx.False(bubble.Advance(3f), "retired owner cancels");
            AssertEx.Equal<string>(null, bubble.Text, "retire content");
            AssertEx.False(bubble.Advance(6f), "cannot revive");
            AssertEx.Equal(2, guardCalls, "retired callbacks are no longer invoked");
        }

        public static void MissingContentEndsTheSequenceWithoutRetainingCallbacks()
        {
            var bubble = new UiBubbleSequence();
            bubble.Show("first", default, 0f, 2);
            AssertEx.False(bubble.Advance(3f), "no provider ends the sequence");
            bubble.Show("first", default, 4f, 2, () => null);
            AssertEx.False(bubble.Advance(7f), "empty provider retires the sequence");
            bubble.Show("first", default, 8f, 2);
            bubble.Show(null, default, 9f, 2, () => throw new System.Exception("empty request"));
            AssertEx.False(bubble.Advance(12f), "empty request cancels previous content");
        }

        public static void PlacementPrefersAboveAndClampsAllScreenEdges()
        {
            var screen = new Vector2(800f, 600f);
            var size = new Vector2(320f, 100f);
            var above = UiBubblePlacement.Place(new Vector2(400f, 300f), size, screen);
            AssertEx.Equal(188f, above.y, "above opening anchor");
            var below = UiBubblePlacement.Place(new Vector2(0f, 0f), size, screen);
            AssertEx.Equal(8f, below.x, "left clamp");
            AssertEx.Equal(12f, below.y, "below when above hits top");
            var corner = UiBubblePlacement.Place(new Vector2(900f, 900f), size, screen);
            AssertEx.Equal(792f, corner.xMax, "right margin");
            AssertEx.Equal(592f, corner.yMax, "bottom margin");
            var tall = UiBubblePlacement.Place(
                new Vector2(400f, 300f),
                new Vector2(320f, 900f),
                screen
            );
            AssertEx.Equal(8f, tall.y, "oversized content keeps top margin");
        }
    }
}
