namespace SlopWorld.Tests
{
    static class ScrollableGeometryTests
    {
        public static void Policies()
        {
            var frame = new UiLayoutRect(20f, 30f, 400f, 200f);
            var atViewport = ScrollableGeometry.Measure(frame, 200f,
                UiScrollbarReservation.WhenNeeded, 18f);
            AssertEx.False(atViewport.ReservesScrollbar,
                "when-needed lists keep the full width at the viewport boundary");
            AssertEx.Equal(400f, atViewport.ContentWidth, "when-needed width at viewport");
            AssertEx.Equal(200f, atViewport.ContentHeight, "content height at viewport");

            var aboveViewport = ScrollableGeometry.Measure(frame, 240f,
                UiScrollbarReservation.WhenNeeded, 18f);
            AssertEx.True(aboveViewport.ReservesScrollbar,
                "when-needed lists reserve a gutter above the viewport");
            AssertEx.Equal(382f, aboveViewport.ContentWidth, "when-needed overflow width");
            AssertEx.Equal(240f, aboveViewport.ContentHeight, "overflow content height");

            var always = ScrollableGeometry.Measure(frame, 20f,
                UiScrollbarReservation.Always, 18f);
            AssertEx.True(always.ReservesScrollbar,
                "always-reserved forms keep their stable gutter below the viewport");
            AssertEx.Equal(382f, always.ContentWidth, "always-reserved width");
            AssertEx.Equal(200f, always.View.Height, "viewport clamps short content height");
        }
    }
}
