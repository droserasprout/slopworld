using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class TerminalHistoryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("stitches skipped offsets from overlapping viewports", StitchesOverlap);
            yield return ("requires a bridge across non-overlapping viewports", RequiresBridge);
        }

        static ScreenBuf Frame(int off, params string[] lines) => new ScreenBuf
        {
            Seq = 7,
            Cols = 20,
            Rows = 3,
            Off = off,
            Lines = lines,
        };

        static void StitchesOverlap()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 2);

            AssertEx.True(history.TryView(1, true, out var one), "offset one is covered");
            AssertEx.Sequence(
                new[] { "old-1", "live-0", "live-1", "live-2" }, one.Lines,
                "fractional view has an overscan row");

            AssertEx.True(history.TryView(2, true, out var two), "offset two is covered");
            AssertEx.Sequence(
                new[] { "old-2", "old-1", "live-0", "live-1" }, two.Lines,
                "one distant response covers its intervening offsets");
        }

        static void RequiresBridge()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(4, "old-4", "old-3", "old-2"), live, 4);

            AssertEx.False(history.TryView(4, true, out _),
                "a missing row between snapshots is not invented");

            history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 4);
            AssertEx.True(history.TryView(4, true, out var bridged), "bridge covers the edge");
            AssertEx.Sequence(
                new[] { "old-4", "old-3", "old-2", "old-1" }, bridged.Lines,
                "bridge contributes the overscan row");
        }
    }
}
