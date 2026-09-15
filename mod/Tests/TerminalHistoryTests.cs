using System;
using System.Collections.Generic;
using System.Linq;

namespace SlopWorld.Tests
{
    static class TerminalHistoryTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("freezes the entire displayed scrollback during streaming", FreezesDisplayedView);
            yield return ("copies a selection across viewports in either direction", CopiesSelection);
            yield return ("refuses selection gaps and preserves displayed text", SelectionGaps);
            yield return ("stitches skipped offsets from overlapping viewports", StitchesOverlap);
            yield return ("requires a bridge across non-overlapping viewports", RequiresBridge);
            yield return ("rejects a frame from an older live sequence", RejectsOldSequence);
            yield return ("retains history across an in-place live refresh", RetainsLiveRefresh);
            yield return ("preserves a deep assembled view across unrelated live refreshes",
                PreservesDeepViewAcrossLiveRefresh);
            yield return ("refreshes indexed live rows behind a shallow history view",
                RefreshesShallowViewAcrossLiveRefresh);
            yield return ("refreshes the live overscan row at the history boundary",
                RefreshesLiveOverscanRow);
            yield return ("tracks the live cursor in cached scrollback views", TracksLiveCursor);
            yield return ("translates history when live output scrolls", TranslatesLiveScroll);
            yield return ("keeps history anchored when a repeated live row scrolls",
                KeepsHistoryAnchoredWhenTopRowRepeats);
            yield return ("keeps bounded history across repeated live shifts", RepeatedLiveShifts);
            yield return ("plans covered views without replacing the cache", CoversViews);
            yield return ("ignores empty frames and retains indexed history", IgnoresEmptyAndRetainsRows);
            yield return ("quantizes prefetch windows in both directions", QuantizesPrefetch);
            yield return ("warms a shallow viewport before the first gesture", WarmsFirstGesture);
            yield return ("warmup skips empty and application-owned history", WarmupEligibility);
            yield return ("live history bounds scrolling before warmup and after clears", LiveScrollLimit);
            yield return ("capture after output does not translate that output twice", CaptureAfterOutput);
            yield return ("delayed capture translates only output after capture", DelayedCapture);
            yield return ("rewritten live prompts are not promoted to history", RewrittenPrompt);
            yield return ("history follows skipped frames and clears", SkippedFramesAndClear);
            yield return ("warmup retains coverage across redraws and resets", WarmupInvalidation);
            yield return ("aggressive prefetch covers fast gestures without gaps", AggressivePrefetch);
        }

        static void FreezesDisplayedView()
        {
            foreach (bool extra in new[] { false, true })
            {
                var live = Frame(0, "live-0", "live-1", "live-2");
                var history = new TerminalHistory();
                history.Reset(live);
                history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 2);
                AssertEx.True(history.TryView(1, extra, out var before, freeze: true), "view ready");
                var refresh = Frame(0, "new-0", "new-1", "new-2");
                refresh.Seq++;
                history.UpdateLive(refresh, 0);
                var reply = Frame(2, "corrected-2", "old-1", "new-0");
                reply.Seq = refresh.Seq;
                history.Add(reply, refresh, 2);
                AssertEx.True(history.TryView(1, extra, out var after, freeze: true), "view retained");
                AssertEx.True(object.ReferenceEquals(before, after), "all displayed rows stay frozen");
                history.UpdateLive(Frame(0, "new-1", "new-2", "new-3"), 1);
                AssertEx.True(history.TryView(2, extra, out var shifted, freeze: true), "translated view retained");
                AssertEx.True(object.ReferenceEquals(before, shifted), "scroll preserves the snapshot");
                history.ReleaseView();
                AssertEx.True(history.TryView(0, false, out var current), "live edge ready");
                AssertEx.Equal("new-3", current.Lines[2], "returning live shows latest output");
            }
        }

        static ScreenBuf Frame(int off, params string[] lines) => new ScreenBuf
        {
            Seq = 7,
            Cols = 20,
            Rows = 3,
            Off = off,
            Lines = lines,
        };

        static void LiveScrollLimit()
        {
            var live = Frame(0, "codex", "", "prompt");
            live.History = 0;
            AssertEx.Equal(0, TerminalHistory.ScrollLimit(live, -1),
                "fresh pane cannot move while waiting for its first capture");
            AssertEx.Equal(0, TerminalHistory.ScrollLimit(live, 1),
                "a stale one-row extent cannot override an empty live frame");
            live.History = 12;
            AssertEx.Equal(12, TerminalHistory.ScrollLimit(live, 0),
                "new output can extend a previously empty history");
            live.History = 0;
            AssertEx.Equal(0, TerminalHistory.ScrollLimit(live, 12),
                "clearing history immediately removes the old scroll range");
            live.History = -1;
            AssertEx.Equal(5, TerminalHistory.ScrollLimit(live, 5),
                "legacy frames use the last capture extent");
            AssertEx.Equal(SharedDefaults.ScrollbackLines, TerminalHistory.ScrollLimit(live, -1),
                "unknown legacy history retains discovery through capture");
        }

        static void CaptureAfterOutput()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "input", "answer-0", "answer-1");
            live.History = 10;
            history.Reset(live);
            // A request is sent now, but the daemon captures after two more rows arrive.
            var next = Frame(0, "answer-1", "answer-2", "prompt");
            next.Seq++;
            next.History = 12;
            history.UpdateLive(next, 2);
            var reply = Frame(2, "input", "answer-0", "answer-1");
            reply.Seq = next.Seq;
            reply.History = next.History;
            history.Add(reply, next, 2, coordinateShift: 2);

            AssertEx.True(history.TryView(2, false, out var view), "requested rows are covered");
            AssertEx.Sequence(reply.Lines, view.Lines,
                "a current capture is already in the current coordinate space");
        }

        static void DelayedCapture()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "answer-2", "answer-3", "prompt");
            live.Seq = 10;
            live.History = 13;
            history.Reset(live);
            var reply = Frame(2, "input", "answer-0", "answer-1");
            reply.Seq = 9;
            reply.History = 12;
            // Three rows since request, but only one since this reply was captured.
            history.Add(reply, live, 2, coordinateShift: 3, allowStale: true);
            AssertEx.True(history.TryLine(-3, out var input), "captured input has a known coordinate");
            AssertEx.Equal("input", input, "translation starts at capture, not request");
        }

        static void SkippedFramesAndClear()
        {
            var live = Frame(0, "a", "b", "c");
            live.History = 10;
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(3, "old-a", "old-b", "old-c"), live, 3);
            var next = Frame(0, "c", "d", "e");
            next.Seq += 2;
            next.History = 12;
            next.LiveShift = 1;
            int shift = TerminalHistory.ShiftSince(live.History, next);
            AssertEx.Equal(2, shift, "cache translation includes both streamed frames");
            history.UpdateLive(next, shift);
            AssertEx.True(history.TryLine(-5, out var old), "confirmed history advances by both frames");
            AssertEx.Equal("old-a", old, "row coordinates follow the cache observation");
            next = next.Snapshot();
            next.Seq++;
            next.History = 1;
            AssertEx.False(history.UpdateLive(next, 0), "a history clear starts a new cache epoch");
            AssertEx.False(history.TryLine(-5, out _), "cleared history cannot be reused");
        }

        static void RewrittenPrompt()
        {
            var history = new TerminalHistory();
            var live = Frame(0, "Ask Codex to do anything", "", "");
            live.History = 10;
            history.Reset(live);
            history.Add(Frame(3, "diff-a", "diff-b", "diff-c"), live, 3);
            // The application clears its prompt, draws a diff and scrolls three rows
            // before the next streamed frame. The previous prompt never entered history.
            var next = Frame(0, "diff-g", "diff-h", "prompt");
            next.Seq++;
            next.History = 13;
            history.UpdateLive(next, 3);
            AssertEx.False(history.TryView(3, false, out _),
                "old live prompt and blank rows cannot satisfy history coverage");
            AssertEx.True(history.TryLine(-6, out var old), "confirmed history is retained");
            AssertEx.Equal("diff-a", old, "confirmed rows retain their translated coordinates");

            var delayed = Frame(1, "diff-c", "Ask Codex to do anything", "");
            delayed.History = 10;
            history.Add(delayed, next, 1, allowStale: true);
            AssertEx.False(history.TryLine(-3, out _),
                "a delayed capture cannot reintroduce its live prompt as history");

            var capture = Frame(3, "diff-d", "diff-e", "diff-f");
            capture.Seq = next.Seq;
            capture.History = next.History;
            history.Add(capture, next, 3);
            AssertEx.True(history.TryView(3, false, out var view), "fresh capture fills the gap");
            AssertEx.Sequence(capture.Lines, view.Lines, "only captured diff rows enter history");
        }

        static void CopiesSelection()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(3, "old-3", "old-2", "old-1"), live, 3);
            history.Add(Frame(6, "old-6", "old-5", "old-4"), live, 6);
            var middle = Frame(3, "old-3", "old-2", "old-1");
            const string expected = "d-5\nold-4\nold-3\nold-2\nold-1\nlive-0\nliv";
            AssertEx.Equal(expected, history.SelectionText(middle, 2, -2, 2, 4),
                "both offscreen ends and their partial columns are copied");
            AssertEx.Equal(expected, history.SelectionText(middle, 2, 4, 2, -2),
                "backwards drag copies in reading order");
            var next = Frame(0, "live-1", "live-2", "live-3");
            history.UpdateLive(next, 1);
            middle.Off++;
            AssertEx.Equal(expected, history.SelectionText(middle, 2, -2, 2, 4),
                "history translation preserves the selected rows");
        }

        static void SelectionGaps()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            AssertEx.Equal("", history.SelectionText(live, 0, -1, 2, 1),
                "a missing row never produces a silently truncated copy");
            var frozen = Frame(0, "\u001b[31mfrozen\u001b[0m", "", "tail");
            AssertEx.Equal("frozen\n\nta", history.SelectionText(frozen, 0, 0, 1, 2),
                "displayed text wins over cache, strips SGR, and keeps blank lines");
        }

        static void WarmsFirstGesture()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            live.History = 50;
            var history = new TerminalHistory();
            int offset = history.WarmupOffset(live, -1);
            AssertEx.Equal(1, offset, "half-viewport capture");
            history.Reset(live);
            history.Add(Frame(offset, "old", "live-0", "live-1"), live, offset);
            AssertEx.True(history.TryView(1, true, out var view), "first fractional gesture is local");
            AssertEx.Sequence(new[] { "old", "live-0", "live-1", "live-2" }, view.Lines,
                "history overlaps live rows");
            AssertEx.Equal(0, live.Off, "warmup does not move live pane");
            AssertEx.Equal(2, history.WarmupOffset(live, -1), "next overlapping window warms deeper");
        }

        static void WarmupEligibility()
        {
            var history = new TerminalHistory();
            AssertEx.Equal(0, history.WarmupOffset(null, -1), "no frame");
            var live = Frame(0, "a", "b", "c");
            live.History = 0;
            AssertEx.Equal(0, history.WarmupOffset(live, -1), "known empty history");
            live.History = -1;
            AssertEx.Equal(0, history.WarmupOffset(live, 0), "empty extent learned from reply");
            AssertEx.Equal(1, history.WarmupOffset(live, -1), "unknown extent can be probed");
            live.History = 1;
            live.Rows = 24;
            AssertEx.Equal(1, history.WarmupOffset(live, -1), "short history caps capture");
            live.AltScreen = true;
            AssertEx.Equal(0, history.WarmupOffset(live, -1), "alternate screen");
            live.AltScreen = false;
            live.AppMouse = true;
            AssertEx.Equal(0, history.WarmupOffset(live, -1), "mouse reporting");
            live.AppMouse = false;
            live.Off = 1;
            AssertEx.Equal(0, history.WarmupOffset(live, -1), "not live bottom");
        }

        static void WarmupInvalidation()
        {
            var live = Frame(0, "a", "b", "c");
            live.History = 10;
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(1, "old", "a", "b"), live, 1);
            live.Seq++;
            history.UpdateLive(live, 0);
            AssertEx.Equal(2, history.WarmupOffset(live, -1), "live redraw retains first warm window");
            history.Reset(live);
            AssertEx.Equal(1, history.WarmupOffset(live, -1), "discarded epoch needs new capture");
        }

        static void AggressivePrefetch()
        {
            var live = new ScreenBuf
            {
                Seq = 1, Cols = 80, Rows = 24, History = 1000,
                Lines = System.Linq.Enumerable.Range(0, 24).Select(i => "row " + i).ToArray(),
            };
            var history = new TerminalHistory();
            history.Reset(live);
            int requests = 0;
            for (int offset = history.WarmupOffset(live, -1); offset > 0;
                 offset = history.WarmupOffset(live, -1))
            {
                requests++;
                AssertEx.True(requests <= 16, "warmup is bounded");
                AssertEx.Equal(requests * 12, offset, "nearest missing window first");
                history.Add(new ScreenBuf
                {
                    Seq = 1, Cols = 80, Rows = 24, Off = offset,
                    Lines = System.Linq.Enumerable.Range(-offset, 24).Select(i => "row " + i).ToArray(),
                }, live, offset);
            }
            AssertEx.Equal(16, requests, "eight screens warmed");
            for (int offset = 1; offset <= 192; offset++)
                AssertEx.True(history.Covers(offset, true), "no gaps in fast-scroll range");
            AssertEx.Equal(204, history.PrefetchOffset(96, 24, true, 1000), "extend ahead of gesture");
            AssertEx.Equal(-1, history.PrefetchOffset(96, 24, false, 1000), "reverse uses cached bridge");
            AssertEx.Equal(-1, history.PrefetchOffset(0, 24, true, 100), "short history is complete");
            AssertEx.Equal(-1, history.PrefetchOffset(0, 24, true, 0), "empty history");
            history.Reset(live);
            AssertEx.Equal(5, history.PrefetchOffset(0, 24, true, 5), "request is capped to real top");
            AssertEx.Equal(108, history.PrefetchOffset(120, 24, false, 1000), "reverse fills nearest first");
        }

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

        static void RefreshesShallowViewAcrossLiveRefresh()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(2, "old-2", "old-1", "live-0"), live, 2);

            AssertEx.True(history.TryView(1, false, out var before),
                "shallow history view is assembled");
            AssertEx.Sequence(
                new[] { "old-1", "live-0", "live-1" }, before.Lines,
                "view overlaps cached history and live rows");

            var refresh = Frame(0, "new-0", "new-1", "new-2");
            refresh.Seq = 8;
            AssertEx.True(history.UpdateLive(refresh, 0),
                "in-place live refresh is compatible");
            AssertEx.True(history.TryView(1, false, out var after),
                "shallow view remains assembled");
            AssertEx.Sequence(
                new[] { "old-1", "new-0", "new-1" }, after.Lines,
                "historical rows stay anchored while visible live rows refresh");
            AssertEx.False(object.ReferenceEquals(before, after),
                "the changed view invalidates the rendering cache");

            AssertEx.True(history.TryView(0, false, out var liveView),
                "moving to the live edge rebuilds the view");
            AssertEx.Sequence(
                new[] { "new-0", "new-1", "new-2" }, liveView.Lines,
                "the live edge sees the latest in-place refresh");
        }

        static void RefreshesLiveOverscanRow()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(3, "old-3", "old-2", "old-1"), live, 3);
            AssertEx.True(history.TryView(3, true, out var before),
                "fractional view includes the first live row as overscan");

            var refresh = Frame(0, "timer-1", "live-1", "live-2");
            refresh.Seq = 8;
            history.UpdateLive(refresh, 0);
            AssertEx.True(history.TryView(3, true, out var after),
                "fractional view stays covered");
            AssertEx.Sequence(new[] { "old-3", "old-2", "old-1", "timer-1" },
                after.Lines, "the overscan row refreshes without moving history");

            var next = Frame(0, "live-1", "live-2", "live-3");
            next.Seq = 9;
            history.UpdateLive(next, 1);
            AssertEx.True(history.TryView(4, true, out var shifted),
                "fractional view remains covered after a real scroll");
            AssertEx.Sequence(after.Lines, shifted.Lines,
                "a real scroll keeps the refreshed rows anchored");
        }

        static void TracksLiveCursor()
        {
            var live = Frame(0, "live-0", "live-1", "live-2");
            live.Cx = 2;
            live.Cy = 0;
            live.CursorBlink = true;
            var history = new TerminalHistory();
            history.Reset(live);
            history.Add(Frame(3, "old-3", "old-2", "old-1"), live, 3);
            AssertEx.True(history.TryView(1, false, out var view), "shallow view is ready");
            AssertEx.Equal(2, view.Cx, "cursor column stays in live coordinates");
            AssertEx.Equal(1, view.Cy, "cursor row includes the history offset");
            AssertEx.True(view.CursorBlink, "live cursor blink remains enabled");

            live.Seq++;
            live.Cx = 4;
            live.Cy = 1;
            live.CursorShape = 2;
            live.CursorBlink = false;
            history.UpdateLive(live, 0);
            AssertEx.True(history.TryView(1, false, out var moved), "cached view is ready");
            AssertEx.True(object.ReferenceEquals(view, moved),
                "cursor-only updates keep the row rendering cache");
            AssertEx.Equal(4, moved.Cx, "cursor-only movement updates the column");
            AssertEx.Equal(2, moved.Cy, "cursor-only movement updates the translated row");
            AssertEx.Equal(2, moved.CursorShape, "cursor shape follows the live frame");
            AssertEx.False(moved.CursorBlink, "steady cursor follows the live frame");

            live.Cy = 0;
            history.UpdateLive(live, 0);
            AssertEx.True(history.TryView(3, true, out var edge), "overscan view is ready");
            AssertEx.Equal(3, edge.Cy, "cursor can occupy the partially visible overscan row");
            live.Cy = live.Rows;
            history.UpdateLive(live, 0);
            AssertEx.True(history.TryView(3, true, out var hidden), "hidden cursor view is ready");
            AssertEx.True(hidden.Cy >= hidden.Lines.Length,
                "daemon-hidden cursor stays outside even the overscan row");

            live.Cy = 0;
            history.UpdateLive(live, 0);
            AssertEx.True(history.TryView(3, false, out var deep), "deep view is ready");
            AssertEx.True(deep.Cy >= deep.Lines.Length,
                "history-only views keep the live cursor outside the pane");
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
