using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ScreenBufTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("scroll overlap matches exhaustive reference", OverlapReference);
            yield return ("hydrates a screen frame", HydratesScreenFrame);
            yield return ("uses wire defaults", UsesWireDefaults);
            yield return ("detects a live row shift", DetectsLiveRowShift);
            yield return ("detects a live row shift with a repeated outgoing row",
                DetectsRepeatedRowShift);
            yield return ("uses live history growth for a large row shift",
                UsesLiveHistoryGrowth);
            yield return ("known history rejects a prompt overlap during redraw", PromptOverlap);
            yield return ("does not treat unknown history as one row",
                UnknownHistoryIsNotGrowth);
            yield return ("does not treat a replayed frame as a row shift",
                ReplayIsNotGrowth);
            yield return ("does not call a bottom edit a row shift", IgnoresBottomEdit);
            yield return ("does not call an ambiguous repeated-content edit a row shift",
                IgnoresAmbiguousRepeatedEdit);
            yield return ("does not shift across a changed viewport", IgnoresChangedViewport);
            yield return ("retains unchanged row metadata", RetainsUnchangedRows);
            yield return ("cursor-only frames retain the content revision", CursorOnlyRevision);
            yield return ("incremental links match full parsing", IncrementalLinksMatchFullParse);
            yield return ("link edits survive skipped renders and snapshots", LinksAcrossSkippedRenders);
        }

        static void OverlapReference()
        {
            var screen = new ScreenBuf();
            for (int rows = 2; rows <= 6; rows++)
                for (int a = 0; a < (1 << rows); a++)
                    for (int b = 0; b < (1 << rows); b++)
                    {
                        var before = new string[rows];
                        var after = new string[rows];
                        bool changed = false;
                        for (int i = 0; i < rows; i++)
                        {
                            before[i] = (a & (1 << i)) == 0 ? "" : "repeat";
                            after[i] = (b & (1 << i)) == 0 ? "" : "repeat";
                            if (i < rows - 1 && before[i] != after[i]) changed = true;
                        }
                        int expected = 0;
                        if (changed && before[rows - 1] != after[rows - 1])
                            for (int shift = 1; shift < rows; shift++)
                            {
                                bool overlap = true;
                                for (int i = 0; i < rows - shift; i++)
                                    if (before[i + shift] != after[i]) { overlap = false; break; }
                                if (overlap) { expected = shift; break; }
                            }
                        AssertEx.Equal(expected, screen.VerticalShift(before, after, rows, rows, rows - 1),
                            "reference overlap for repeated rows");
                    }
        }

        static void HydratesScreenFrame()
        {
            var screen = new ScreenBuf
            {
                Runs = new List<SgrRun>[1],
                RunsRev = 12,
            };
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":7,\"cols\":120,\"rows\":40,\"cx\":3,\"cy\":4," +
                "\"off\":5,\"history\":91,\"request_id\":42,\"cursor_shape\":2," +
                "\"cursor_blink\":false,\"app_mouse\":true,\"app_drag\":true," +
                "\"alt_screen\":true,\"title\":\"vim\",\"lines\":[\"one\",\"two\"]}")));

            AssertEx.Equal(7, screen.Seq, "sequence");
            AssertEx.Equal(120, screen.Cols, "columns");
            AssertEx.Equal(40, screen.Rows, "rows");
            AssertEx.Equal(3, screen.Cx, "cursor x");
            AssertEx.Equal(4, screen.Cy, "cursor y");
            AssertEx.Equal(5, screen.Off, "scroll offset");
            AssertEx.Equal(91, screen.History, "history extent");
            AssertEx.Equal(42UL, screen.ScrollRequestId, "scroll request id");
            AssertEx.Equal(2, screen.CursorShape, "cursor shape");
            AssertEx.False(screen.CursorBlink, "cursor blink");
            AssertEx.True(screen.AppMouse, "application mouse");
            AssertEx.True(screen.AppDrag, "application drag");
            AssertEx.True(screen.AltScreen, "alternate screen");
            AssertEx.Equal("vim", screen.Title, "screen title");
            AssertEx.Sequence(new[] { "one", "two" }, screen.Lines, "screen lines");
            AssertEx.True(screen.Runs == null, "hydration clears parsed runs");
        }

        static void UsesWireDefaults()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse("{}")));

            AssertEx.Equal(0, screen.Seq, "sequence default");
            AssertEx.Equal(80, screen.Cols, "columns default");
            AssertEx.Equal(24, screen.Rows, "rows default");
            AssertEx.Equal(0, screen.Cx, "cursor x default");
            AssertEx.Equal(0, screen.Cy, "cursor y default");
            AssertEx.Equal(0, screen.Off, "scroll offset default");
            AssertEx.Equal(0, screen.History, "history extent default");
            AssertEx.Equal(0UL, screen.ScrollRequestId, "request id default");
            AssertEx.Equal(0, screen.CursorShape, "cursor shape default");
            AssertEx.True(screen.CursorBlink, "cursor blink default");
            AssertEx.False(screen.AppMouse, "application mouse default");
            AssertEx.False(screen.AppDrag, "application drag default");
            AssertEx.False(screen.AltScreen, "alternate screen default");
            AssertEx.Equal("", screen.Title, "title default");
            AssertEx.Sequence(Array.Empty<string>(), screen.Lines, "lines default");
        }

        static void DetectsLiveRowShift()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":5," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":6," +
                "\"off\":0,\"lines\":[\"two\",\"three\",\"four\"]}")));

            AssertEx.Equal(1, screen.LiveShift, "live row shift");
        }

        static void DetectsRepeatedRowShift()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":4,\"cy\":3,\"history\":5," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"line-2\",\"line-3\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3,\"history\":6," +
                "\"off\":0,\"lines\":[\"same\",\"line-2\",\"line-3\",\"line-4\"]}")));

            AssertEx.Equal(1, screen.LiveShift,
                "a scroll remains detectable when the outgoing top rows repeat");
        }

        static void UsesLiveHistoryGrowth()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":4," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":8," +
                "\"off\":0,\"lines\":[\"new-a\",\"new-b\",\"new-c\"]}")));

            AssertEx.Equal(4, screen.LiveShift,
                "history growth reports a shift when no visible rows overlap");
        }

        static void PromptOverlap()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":10," +
                "\"lines\":[\"Ask Codex to do anything\",\"\",\"\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":10," +
                "\"lines\":[\"\",\"\",\"diff\"]}")));
            AssertEx.Equal(0, screen.LiveShift,
                "overlapping blank rows cannot override an unchanged daemon history extent");
        }

        static void UnknownHistoryIsNotGrowth()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":0," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));

            AssertEx.Equal(0, screen.LiveShift,
                "hydrating an unknown zero history extent does not add a row");
        }

        static void ReplayIsNotGrowth()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":7,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"history\":0,\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":7,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"history\":1,\"off\":0,\"lines\":[\"two\",\"three\",\"four\"]}")));

            AssertEx.Equal(0, screen.LiveShift,
                "a same-sequence resubscription replay does not add a row");
        }

        static void IgnoresBottomEdit()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"four\"]}")));

            AssertEx.Equal(0, screen.LiveShift, "bottom edit shift");
        }

        static void IgnoresAmbiguousRepeatedEdit()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"same\",\"same\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"same\",\"new\"]}")));

            AssertEx.Equal(0, screen.LiveShift,
                "a bottom edit among repeated rows remains ambiguous");
        }

        static void IgnoresChangedViewport()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}")));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"two\",\"three\",\"four\",\"five\"]}")));

            AssertEx.Equal(0, screen.LiveShift, "a resize does not look like a scroll");
        }

        static void RetainsUnchangedRows()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3," +
                "\"lines\":[\"one\",\"two\",\"three\"]}")));
            var first = new List<SgrRun>();
            var second = new List<SgrRun>();
            var third = new List<SgrRun>();
            screen.Runs = new[] { first, second, third };
            screen.RunsRev = 1;
            screen.RunsComplete = true;
            var snapshot = screen.Snapshot();
            int revision = screen.ContentRevision;

            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3," +
                "\"lines\":[\"one\",\"changed\",\"three\"]}")));

            AssertEx.Equal(revision + 1, screen.ContentRevision, "content revision");
            AssertEx.Equal(1, screen.ChangedRows.Length, "changed row count");
            AssertEx.Equal(1, screen.ChangedRows[0], "changed row index");
            AssertEx.True(object.ReferenceEquals(first, screen.Runs[0]),
                "unchanged row runs are retained");
            AssertEx.True(screen.Runs[1] == null, "changed row runs are invalidated");
            AssertEx.True(object.ReferenceEquals(second, snapshot.Runs[1]),
                "invalidation preserves snapshot rows");
            AssertEx.False(screen.RunsComplete, "partial runs are not complete");
        }

        static void CursorOnlyRevision()
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":2,\"cx\":1," +
                "\"lines\":[\"one\",\"two\"]}")));
            int revision = screen.ContentRevision;
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":2,\"cx\":2," +
                "\"lines\":[\"one\",\"two\"]}")));

            AssertEx.Equal(revision, screen.ContentRevision, "content revision is stable");
            AssertEx.Equal(0, screen.ChangedRows.Length, "no changed rows");
        }

        static void IncrementalLinksMatchFullParse()
        {
            const int cols = 12;
            var initial = new[]
            {
                "https://exam",
                "ple.com/path",
                "/very/long  ",
                "\x1b]8;;https://named.example\x07wide\x1b]8;;\x07",
                "\U0001F916\x1b[3Gtail",
            };
            var edited = new[]
            {
                "https://exam",
                "progress ",
                "/very/long  ",
                initial[3],
                initial[4],
            };
            var cache = new TerminalRunCache();
            var screen = Hydrate(1, cols, initial, 0);
            var first = cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            AssertRunsEqual(Sgr.ParseLines(initial, cols), first, "initial full parse");
            var retained = screen.Snapshot();
            var unchanged = screen.Runs[3];

            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(2, cols, edited, 0)));
            var second = cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            AssertRunsEqual(Sgr.ParseLines(edited, cols), second, "removed long link");
            AssertRunsEqual(Sgr.ParseLines(initial, cols), retained.Runs, "snapshot stays old");
            AssertEx.True(object.ReferenceEquals(unchanged, second[3]),
                           "unrelated OSC 8 row is retained");
            AssertEx.True(second[0] != first[0] && second[2] != first[2],
                           "the whole removed URL span is rebuilt");

            var joined = new[]
            {
                "https://exam",
                "ple.com/path",
                "/very/long  ",
                initial[3],
                initial[4],
            };
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(3, cols, joined, 17)));
            var third = cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            AssertRunsEqual(Sgr.ParseLines(joined, cols), third, "joined link after scroll");

            var resized = new[] { "\x1b[31mhttps://exam", "ple.com" };
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(4, 16, resized, 0)));
            var fourth = cache.Parse(screen, 1, 1, out _, out _);
            AssertRunsEqual(Sgr.ParseLines(resized, 16), fourth, "resize full parse");
        }

        static void LinksAcrossSkippedRenders()
        {
            const int cols = 12;
            var initial = new[] { "https://exam", "ple.com/path", "/very/long  ", "plain" };
            var edited = new[] { initial[0], "progress ", initial[2], initial[3] };
            var cache = new TerminalRunCache();
            var screen = Hydrate(1, cols, initial, 0);
            cache.Parse(screen, 1, 1, out _, out _);
            screen.RunsRev = 1;
            var original = screen.Snapshot();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(2, cols, edited, 0)));
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(3, cols, edited, 0)));
            var pending = screen.Snapshot();
            AssertRunsEqual(Sgr.ParseLines(edited, cols),
                cache.Parse(screen, 1, 1, out _, out _), "edit then replay before render");
            AssertRunsEqual(Sgr.ParseLines(edited, cols),
                cache.Parse(pending, 1, 1, out _, out _), "snapshot of pending edits");
            AssertRunsEqual(Sgr.ParseLines(initial, cols), original.Runs, "retained old snapshot");

            // Exercise multiple accumulated edits, reversals, resize, history offsets and
            // palette changes. Every parse must agree with the uncached whole-screen parser.
            var random = new Random(42);
            var lines = (string[])initial.Clone();
            var choices = new[] { "https://exam", "ple.com/path", "/very/long  ", " ",
                "progress", "\x1b[31mred", "\U0001F916\x1b[3Gtail",
                "\x1b]8;;https://named.example\x07wide\x1b]8;;\x07" };
            int seq = 3;
            for (int round = 0; round < 80; round++)
            {
                int width = round % 3 == 0 ? 16 : cols;
                for (int edit = 0; edit < 3; edit++)
                {
                    lines[random.Next(lines.Length)] = choices[random.Next(choices.Length)];
                    screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(++seq, width, lines, round % 2 == 0 ? 0 : 17)));
                }
                var expected = Sgr.ParseLines(lines, width);
                var retained = screen.Snapshot();
                AssertRunsEqual(expected, cache.Parse(screen, round / 10, 1, out _, out _),
                                "accumulated edits " + round);
                screen.RunsRev = round / 10;
                if (retained.Runs != null)
                    foreach (int row in screen.ChangedRows)
                        AssertEx.True(retained.Runs[row] == null,
                                      "source parsing leaves pending snapshot slots untouched");
                AssertRunsEqual(expected, cache.Parse(retained, round / 10, 1, out _, out _),
                                "pending snapshot " + round);
            }
        }

        static ScreenBuf Hydrate(int seq, int cols, string[] lines, int off)
        {
            var screen = new ScreenBuf();
            screen.FromWire(ProtobufFixtures.Read<Wire.ScreenView>(Wire(seq, cols, lines, off)));
            return screen;
        }

        static JVal Wire(int seq, int cols, string[] lines, int off)
        {
            return JVal.Parse("{\"seq\":" + seq + ",\"cols\":" + cols +
                ",\"rows\":" + lines.Length + ",\"off\":" + off +
                ",\"lines\":[" + string.Join(",", System.Linq.Enumerable.Select(lines, JVal.Q)) + "]}");
        }

        static void AssertRunsEqual(List<SgrRun>[] expected, List<SgrRun>[] actual, string message)
        {
            AssertEx.Equal(expected.Length, actual.Length, message + " row count");
            for (int row = 0; row < expected.Length; row++)
            {
                AssertEx.Equal(expected[row].Count, actual[row].Count, message + " run count " + row);
                for (int i = 0; i < expected[row].Count; i++)
                {
                    var left = expected[row][i];
                    var right = actual[row][i];
                    AssertEx.Equal(left.Text, right.Text, message + " text " + row + ":" + i);
                    AssertEx.Equal(left.Col, right.Col, message + " col " + row + ":" + i);
                    AssertEx.Equal(left.Fg, right.Fg, message + " fg " + row + ":" + i);
                    AssertEx.Equal(left.Bg, right.Bg, message + " bg " + row + ":" + i);
                    AssertEx.Equal(left.HasBg, right.HasBg, message + " bg flag " + row + ":" + i);
                    AssertEx.Equal(left.Bold, right.Bold, message + " bold " + row + ":" + i);
                    AssertEx.Equal(left.Url, right.Url, message + " url " + row + ":" + i);
                }
            }
        }
    }
}
