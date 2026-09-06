using System;
using System.Collections.Generic;

namespace SlopWorld.Tests
{
    static class ScreenBufTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("hydrates a screen frame", HydratesScreenFrame);
            yield return ("uses wire defaults", UsesWireDefaults);
            yield return ("detects a live row shift", DetectsLiveRowShift);
            yield return ("detects a live row shift with a repeated outgoing row",
                DetectsRepeatedRowShift);
            yield return ("uses live history growth for a large row shift",
                UsesLiveHistoryGrowth);
            yield return ("does not call a bottom edit a row shift", IgnoresBottomEdit);
            yield return ("does not call an ambiguous repeated-content edit a row shift",
                IgnoresAmbiguousRepeatedEdit);
            yield return ("does not shift across a changed viewport", IgnoresChangedViewport);
        }

        static void HydratesScreenFrame()
        {
            var screen = new ScreenBuf
            {
                Runs = new List<SgrRun>[1],
                RunsRev = 12,
            };
            screen.FromJson(JVal.Parse(
                "{\"seq\":7,\"cols\":120,\"rows\":40,\"cx\":3,\"cy\":4," +
                "\"off\":5,\"history\":91,\"request_id\":42,\"cursor_shape\":2," +
                "\"cursor_blink\":false,\"app_mouse\":true,\"app_drag\":true," +
                "\"alt_screen\":true,\"title\":\"vim\",\"lines\":[\"one\",\"two\"]}"));

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
            screen.FromJson(JVal.Parse("{}"));

            AssertEx.Equal(0, screen.Seq, "sequence default");
            AssertEx.Equal(80, screen.Cols, "columns default");
            AssertEx.Equal(24, screen.Rows, "rows default");
            AssertEx.Equal(0, screen.Cx, "cursor x default");
            AssertEx.Equal(0, screen.Cy, "cursor y default");
            AssertEx.Equal(0, screen.Off, "scroll offset default");
            AssertEx.Equal(-1, screen.History, "history extent default");
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
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"two\",\"three\",\"four\"]}"));

            AssertEx.Equal(1, screen.LiveShift, "live row shift");
        }

        static void DetectsRepeatedRowShift()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"line-2\",\"line-3\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"line-2\",\"line-3\",\"line-4\"]}"));

            AssertEx.Equal(1, screen.LiveShift,
                "a scroll remains detectable when the outgoing top rows repeat");
        }

        static void UsesLiveHistoryGrowth()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":4," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2,\"history\":8," +
                "\"off\":0,\"lines\":[\"new-a\",\"new-b\",\"new-c\"]}"));

            AssertEx.Equal(4, screen.LiveShift,
                "history growth reports a shift when no visible rows overlap");
        }

        static void IgnoresBottomEdit()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"four\"]}"));

            AssertEx.Equal(0, screen.LiveShift, "bottom edit shift");
        }

        static void IgnoresAmbiguousRepeatedEdit()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"same\",\"same\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"same\",\"same\",\"same\",\"new\"]}"));

            AssertEx.Equal(0, screen.LiveShift,
                "a bottom edit among repeated rows remains ambiguous");
        }

        static void IgnoresChangedViewport()
        {
            var screen = new ScreenBuf();
            screen.FromJson(JVal.Parse(
                "{\"seq\":1,\"cols\":20,\"rows\":3,\"cy\":2," +
                "\"off\":0,\"lines\":[\"one\",\"two\",\"three\"]}"));
            screen.FromJson(JVal.Parse(
                "{\"seq\":2,\"cols\":20,\"rows\":4,\"cy\":3," +
                "\"off\":0,\"lines\":[\"two\",\"three\",\"four\",\"five\"]}"));

            AssertEx.Equal(0, screen.LiveShift, "a resize does not look like a scroll");
        }
    }
}
