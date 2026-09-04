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
            yield return ("retains history across an in-place live refresh", RetainsLiveRefresh);
            yield return ("preserves a deep assembled view across unrelated live refreshes",
                PreservesDeepViewAcrossLiveRefresh);
            yield return ("translates history when live output scrolls", TranslatesLiveScroll);
            yield return ("keeps history anchored when a repeated live row scrolls",
                KeepsHistoryAnchoredWhenTopRowRepeats);
            yield return ("keeps bounded history across repeated live shifts", RepeatedLiveShifts);
            yield return ("plans covered views without replacing the cache", CoversViews);
            yield return ("ignores empty frames and retains indexed history", IgnoresEmptyAndRetainsRows);
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

        static void RetainsLiveRefresh()
        {
            var history = new TerminalHistory();
            var current = Frame(0, "current-0", "current-1", "current-2");
            current.Seq = 8;
            history.Reset(current);

            var old = Frame(2, "old-2", "old-1", "current-0");
            old.Seq = 7;
            history.Add(old, current, 2, allowStale: true);

            AssertEx.True(history.TryView(1, true, out var view),
                "an in-flight response still supplies history after a live redraw");
            AssertEx.Sequence(
                new[] { "old-1", "current-0", "current-1", "current-2" }, view.Lines,
                "a stale response does not overwrite refreshed live rows");
        }

        static void TranslatesLiveScroll()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "live-0", "live-1", "live-2");
            history.Reset(live);
            history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 2);

            var next = Frame(0, "live-1", "live-2", "live-3");
            next.Seq = 8;
            next.LiveShift = 1;
            AssertEx.True(history.UpdateLive(next, 1),
                "a compatible live scroll keeps the history cache");
            AssertEx.True(history.TryView(2, true, out var view),
                "translated history remains readable after live output scrolls");
            AssertEx.Sequence(
                new[] { "old-1", "live-0", "live-1", "live-2" }, view.Lines,
                "cached rows move with the live bottom");
        }

        static void PreservesDeepViewAcrossLiveRefresh()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "live-0", "live-1", "live-2");
            history.Reset(live);
            history.Add(Frame(4, "old-4", "old-3", "old-2"), live, 4);
            history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 2);
            AssertEx.True(history.TryView(3, false, out var before), "deep view is assembled");

            var redrawn = Frame(0, "new-0", "new-1", "new-2");
            redrawn.Seq = 8;
            AssertEx.True(history.UpdateLive(redrawn, 0), "in-place refresh is compatible");
            AssertEx.True(history.TryView(3, false, out var after), "deep view stays covered");
            AssertEx.True(object.ReferenceEquals(before, after),
                "unrelated live rows preserve the assembled ScreenBuf and parsed runs");

            var shifted = Frame(0, "new-1", "new-2", "new-3");
            shifted.Seq = 9;
            AssertEx.True(history.UpdateLive(shifted, 1), "live scroll is compatible");
            AssertEx.True(history.TryView(4, false, out var translated),
                "translated deep view stays covered");
            AssertEx.True(object.ReferenceEquals(before, translated),
                "logical coordinate maintenance preserves the assembled view");
            AssertEx.Equal(4, translated.Off, "cached view metadata follows the live bottom");
        }

        static void KeepsHistoryAnchoredWhenTopRowRepeats()
        {
            var live = new ScreenBuf
            {
                Seq = 1,
                Cols = 20,
                Rows = 4,
                Cy = 3,
                Lines = new[] { "same", "same", "line-2", "line-3" },
            };
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(new ScreenBuf
            {
                Seq = 1,
                Cols = 20,
                Rows = 4,
                Off = 2,
                Lines = new[] { "old-2", "old-1", "same", "same" },
            }, live, 2);

            var next = new ScreenBuf();
            next.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"line-2\",\"line-3\"]}"));
            next.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"line-2\",\"line-3\",\"line-4\"]}"));

            AssertEx.Equal(1, next.LiveShift, "streaming frame reports its row scroll");
            AssertEx.True(history.UpdateLive(next, next.LiveShift),
                "the compatible stream keeps the history cache");
            AssertEx.True(history.TryView(3, true, out var view),
                "the translated history view remains complete");
            AssertEx.Sequence(
                new[] { "old-2", "old-1", "same", "same", "line-2" }, view.Lines,
                "all visible rows stay anchored while the live pane advances");
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

        static void RepeatedLiveShifts()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "live-0", "live-1", "live-2");
            history.Reset(live);
            for (int off = 3; off <= 9_999; off += 3)
                history.Add(Frame(off, "old-" + off, "old-" + (off - 1),
                                  "old-" + (off - 2)), live, off);

            for (int n = 1; n <= 250; n++)
            {
                var next = Frame(0, "live-" + n, "live-" + (n + 1), "live-" + (n + 2));
                next.Seq = 7 + n;
                AssertEx.True(history.UpdateLive(next, 1), "compatible shift is retained");
            }

            AssertEx.True(history.Count <= 10_200, "cache remains within its coordinate bounds");
            AssertEx.True(history.TryView(252, false, out var view),
                "rows remain addressable after many logical shifts");
            AssertEx.Sequence(new[] { "old-2", "old-1", "live-0" }, view.Lines,
                "logical shifts preserve row order without dictionary copies");
        }

        static void IgnoresEmptyAndRetainsRows()
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

            history.Reset(live);
            for (int off = 2; off <= 80; off += 2)
                history.Add(new ScreenBuf
                {
                    Seq = 3,
                    Off = off,
                    Rows = 3,
                    Lines = new[]
                    {
                        "row-" + off,
                        "row-" + (off - 1),
                        "row-" + (off - 2),
                    },
                }, live, off);

            AssertEx.True(history.Covers(2, false),
                "early rows remain after more than the old frame limit");
            AssertEx.True(history.Covers(80, false), "latest indexed rows are covered");
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
