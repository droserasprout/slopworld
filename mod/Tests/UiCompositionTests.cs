namespace SlopWorld.Tests
{
    static class UiCompositionTests
    {
        public static void Arrange()
        {
            var items = new[]
            {
                new UiLayoutItem(UiLayoutSize.Fixed(20f), UiLayoutSize.Fixed(8f), 20f, 8f,
                    UiLayoutAlignment.Start),
                new UiLayoutItem(UiLayoutSize.Content(12f, 10f), UiLayoutSize.Content(6f),
                    12f, 6f, UiLayoutAlignment.Center),
                new UiLayoutItem(UiLayoutSize.Flexible(1f, 5f), UiLayoutSize.Flexible(),
                    0f, 0f),
            };
            var output = new UiLayoutRect[items.Length];
            float measured = UiComposition.Arrange(UiLayoutAxis.Column,
                new UiLayoutRect(0f, 0f, 100f, 100f),
                new UiLayoutPadding(2f, 3f, 4f, 5f), 4f, items, output);

            AssertEx.Equal(20f, output[0].Height, "fixed column height");
            AssertEx.Equal(12f, output[1].Height, "content column height");
            AssertEx.Equal(52f, output[2].Height, "flexible column fills remainder");
            AssertEx.Equal(53f, measured, "column minimum measured height");
            AssertEx.True(output[0].Y < output[1].Y && output[1].Y < output[2].Y,
                "column children are ordered");

            var offset = new UiLayoutRect[1];
            UiComposition.Arrange(UiLayoutAxis.Row, new UiLayoutRect(7f, 9f, 40f, 20f),
                new UiLayoutPadding(2f, 3f, 4f, 5f), 0f,
                new[] { new UiLayoutItem(UiLayoutSize.Fixed(10f), UiLayoutSize.Flexible(),
                    10f, 0f) }, offset);
            AssertEx.Equal(9f, offset[0].X, "arrangement preserves available x origin");
            AssertEx.Equal(12f, offset[0].Y, "arrangement preserves available y origin");

            var overflow = new[]
            {
                new UiLayoutItem(UiLayoutSize.Fixed(80f, 120f), UiLayoutSize.Flexible(),
                    80f, 0f),
                new UiLayoutItem(UiLayoutSize.Content(40f, 20f), UiLayoutSize.Flexible(),
                    40f, 0f),
            };
            var tiny = new UiLayoutRect[overflow.Length];
            UiComposition.Arrange(UiLayoutAxis.Row, new UiLayoutRect(0f, 0f, 4f, 3f),
                new UiLayoutPadding(10f, 10f, 10f, 10f), -2f, overflow, tiny);
            for (int i = 0; i < tiny.Length; i++)
            {
                AssertEx.True(tiny[i].Width >= 0f && tiny[i].Height >= 0f,
                    "overflow rectangles are nonnegative");
                AssertEx.True(tiny[i].Width == tiny[i].Width && tiny[i].Height == tiny[i].Height,
                    "overflow rectangles are finite");
            }

            var minimums = new[]
            {
                new UiLayoutItem(UiLayoutSize.Flexible(1f, 40f),
                    UiLayoutSize.Flexible(), 0f, 0f),
                new UiLayoutItem(UiLayoutSize.Flexible(9f),
                    UiLayoutSize.Flexible(), 0f, 0f),
            };
            var fitted = new UiLayoutRect[minimums.Length];
            UiComposition.Arrange(UiLayoutAxis.Row,
                new UiLayoutRect(0f, 0f, 100f, 10f), UiLayoutPadding.Zero, 0f,
                minimums, fitted);
            AssertEx.Equal(46f, fitted[0].Width,
                "flex distribution reserves the first minimum");
            AssertEx.Equal(54f, fitted[1].Width,
                "flex distribution uses only surplus space");
            AssertEx.Equal(100f, fitted[1].XMax,
                "fitting flex minimums do not create overflow");

            var fixedMinimum = new[]
            {
                new UiLayoutItem(UiLayoutSize.Fixed(10f, 25f),
                    UiLayoutSize.Flexible(), 10f, 0f),
            };
            AssertEx.Equal(25f, UiComposition.Measure(UiLayoutAxis.Row,
                UiLayoutPadding.Zero, 0f, fixedMinimum),
                "fixed measurement honors its minimum");
        }
    }
}
