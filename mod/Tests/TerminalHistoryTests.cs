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
            yield return ("rejects a frame from an older live sequence", RejectsOldSequence);
            yield return ("plans covered views without replacing the cache", CoversViews);
            yield return ("ignores empty frames and prunes distant history", IgnoresEmptyAndPrunes);
            yield return ("quantizes prefetch windows in both directions", QuantizesPrefetch);
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

        static void RejectsOldSequence()
        {
            var history = new TerminalHistory();
            var current = Frame(0, "current-0", "current-1", "current-2");
            current.Seq = 8;
            history.Reset(current);

            var old = Frame(2, "old-2", "old-1", "current-0");
            history.Add(old, current, 2);

            AssertEx.False(history.TryView(1, true, out _),
                "an old reply cannot bridge the current live sequence");
        }

        static void CoversViews()
        {
            var history = new TerminalHistory();
            history.Reset(Frame(0, "one", "two", "three"));

            AssertEx.True(history.Covers(0, false), "live viewport is covered");
            AssertEx.False(history.Covers(0, true), "an extra row needs history too");
            AssertEx.False(history.Covers(1, false), "missing history row is not covered");

            AssertEx.True(history.TryView(0, false, out var first), "live view is built");
            AssertEx.True(history.TryView(0, false, out var cached), "live view is cached");
            AssertEx.True(object.ReferenceEquals(first, cached), "cached view is reused");
        }

        static void IgnoresEmptyAndPrunes()
        {
            var history = new TerminalHistory();
            history.Reset();
            history.Add(null, null, 0);
            history.Add(new ScreenBuf { Seq = 1, Off = 1, Lines = Array.Empty<string>() },
                        null, 1);
            AssertEx.False(history.TryView(0, false, out _), "empty history cannot build a view");

            var live = Frame(0, "live-0", "live-1", "live-2");
            live.Seq = 3;
            history.Reset();
            var historical = Frame(2, "old-2", "old-1", "live-0");
            historical.Seq = 3;
            history.Add(historical, live, 2);
            AssertEx.True(history.Covers(1, false),
                          "a new sequence seeds its live snapshot before adding history");

            for (int off = 1; off <= 33; off++)
                history.Add(new ScreenBuf
                {
                    Seq = 2,
                    Off = off,
                    Rows = 1,
                    Lines = new[] { "row-before-" + off, "row-" + off },
                }, null, 1);

            AssertEx.True(history.Covers(1, false), "nearest history survives pruning");
        }

        static void QuantizesPrefetch()
        {
            AssertEx.Equal(20, TerminalHistory.PrefetchAnchor(1, 20, true, 10_000),
                "first upward gesture fetches one reusable window");
            AssertEx.Equal(20, TerminalHistory.PrefetchAnchor(10, 20, true, 10_000),
                "upward probe stays stable through half the window");
            AssertEx.Equal(40, TerminalHistory.PrefetchAnchor(11, 20, true, 10_000),
                "upward probe advances at the midpoint");
            AssertEx.Equal(1_980, TerminalHistory.PrefetchAnchor(2_000, 20, false, 10_000),
                "downward probe looks toward live output");
            AssertEx.Equal(1_980, TerminalHistory.PrefetchAnchor(1_990, 20, false, 10_000),
                "downward probe remains on its boundary");
            AssertEx.Equal(0, TerminalHistory.PrefetchAnchor(5, 20, false, 10_000),
                "downward probe joins the live viewport");
        }
    }
}
