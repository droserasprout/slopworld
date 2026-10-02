namespace SlopWorld.Tests
{
    static class SettingsLayoutTests
    {
        public static void Bounds()
        {
            foreach (float width in new[] { 0f, 3f, 300f, 900f })
                foreach (float height in new[] { 0f, 2f, 25f, 600f })
                {
                    var page = new UiLayoutRect(17f, 31f, width, height);
                    var body = SettingsLayout.Body(page, 16f, 32f, 8f);
                    var footer = SettingsLayout.Footer(page, 32f);
                    Contained(page, body);
                    Contained(page, footer);
                    AssertEx.True(body.YMax <= footer.Y, "body never overlaps footer");
                    Contained(page, SettingsLayout.Body(page, 16f, 0f, 8f));
                }
            var representative = new UiLayoutRect(17f, 31f, 300f, 600f);
            var expectedBody = SettingsLayout.Body(representative, 16f, 32f, 8f);
            var expectedFooter = SettingsLayout.Footer(representative, 32f);
            AssertEx.Equal(33f, expectedBody.X, "body inset X");
            AssertEx.Equal(47f, expectedBody.Y, "body inset Y");
            AssertEx.Equal(268f, expectedBody.Width, "body width after padding");
            AssertEx.Equal(528f, expectedBody.Height, "body height after footer gap and padding");
            AssertEx.Equal(599f, expectedFooter.Y, "footer bottom alignment");
            AssertEx.Equal(representative.YMax, expectedFooter.YMax, "footer ends at page bottom");
            var full = new UiLayoutRect(0f, 0f, 500f, 600f);
            AssertEx.Equal(40f, SettingsLayout.Body(full, 16f, 0f, 8f).Height -
                SettingsLayout.Body(full, 16f, 32f, 8f).Height,
                "pages without a footer recover its height and gap");
        }

        static void Contained(UiLayoutRect parent, UiLayoutRect child)
        {
            AssertEx.True(child.Width >= 0f && child.Height >= 0f &&
                child.X >= parent.X && child.Y >= parent.Y &&
                child.XMax <= parent.XMax && child.YMax <= parent.YMax,
                "page geometry stays bounded at nonzero origins");
        }

        public static void Measurement()
        {
            var height = new ContentHeight(400f);
            AssertEx.Equal(400f, height.BeginFrame(10), "first frame uses estimate");
            height.Measure(800f);
            AssertEx.Equal(400f, height.BeginFrame(10), "input cannot move repaint geometry");
            AssertEx.Equal(800f, height.BeginFrame(11), "next frame publishes measurement");
            height.Measure(100f);
            AssertEx.Equal(800f, height.BeginFrame(11), "shrinking form stays stable too");
            AssertEx.Equal(100f, height.BeginFrame(12), "shrinking form updates next frame");
            height.Measure(-10f);
            AssertEx.Equal(0f, height.BeginFrame(13), "negative measurement is bounded");
        }
    }
}
