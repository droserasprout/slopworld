namespace SlopWorld.Tests
{
    static class BandedNoiseRowTests
    {
        public static void InterpolatesBothAxesBeforeShapingTheBand()
        {
            var row = new BandedNoiseRow(new[] { 0f, 0.5f, 0.5f, 1f }, 2, 2, 0.5f);
            AssertEx.Equal(0.15625f, row.Sample(0f, 0f, 1f), "left midpoint");
            AssertEx.Equal(0.5f, row.Sample(0.5f, 0f, 1f), "cell center");
            AssertEx.Equal(0.84375f, row.Sample(1f, 0f, 1f), "right midpoint");
            AssertEx.Equal(0f, row.Sample(0f, 0.25f, 2f), "lower band edge");
            AssertEx.Equal(0.5f, row.Sample(0.5f, 0.25f, 2f), "band center");
            AssertEx.Equal(1f, row.Sample(1f, 0.25f, 2f), "upper band edge");
        }

        public static void ClampsTheBandAndSamplesTheLastRowAndColumn()
        {
            var noise = new[] { -1f, 2f, 0.25f, 0.75f };
            var top = new BandedNoiseRow(noise, 2, 2, 0f);
            var bottom = new BandedNoiseRow(noise, 2, 2, 1f);
            AssertEx.Equal(0f, top.Sample(0f, 0f, 1f), "below band");
            AssertEx.Equal(1f, top.Sample(1f, 0f, 1f), "above band");
            AssertEx.Equal(0.84375f, bottom.Sample(1f, 0f, 1f), "last corner");
            AssertEx.Equal(0.5f, bottom.Sample(0.5f, 0f, 1f), "last row interpolation");
        }
    }
}
