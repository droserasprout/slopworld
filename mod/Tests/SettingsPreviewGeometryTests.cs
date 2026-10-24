namespace SlopWorld.Tests
{
    static class SettingsPreviewGeometryTests
    {
        public static void PreviewStaysPinnedBesideScrollableForm()
        {
            AssertEx.False(
                SettingsPreviewGeometry.ShouldStack(300f, 30f, 120f, 24f, 4f, 16f),
                "room for a minimum form row keeps the preview pinned"
            );

            var geometry = SettingsPreviewGeometry.Arrange(
                400f,
                300f,
                false,
                900f,
                120f,
                24f,
                4f,
                16f
            );

            AssertEx.False(geometry.Stacked, "wide layout remains pinned");
            AssertEx.Equal(0f, geometry.Form.Y, "pinned form starts at the body top");
            AssertEx.Equal(
                136f,
                geometry.Form.Height,
                "pinned form fills space before the preview"
            );
            AssertEx.Equal(152f, geometry.PreviewCaption.Y, "preview follows the form gap");
            AssertEx.Equal(180f, geometry.Preview.Y, "preview follows its caption gap");
            AssertEx.Equal(400f, geometry.Preview.Width, "preview uses the body width");
        }

        public static void StackedPreviewFollowsFormInSharedScrollSpace()
        {
            AssertEx.True(
                SettingsPreviewGeometry.ShouldStack(190f, 30f, 120f, 24f, 4f, 16f),
                "short body stacks when the minimum form row and preview do not fit"
            );

            var geometry = SettingsPreviewGeometry.Arrange(
                280f,
                200f,
                true,
                500f,
                120f,
                24f,
                4f,
                16f
            );

            AssertEx.True(geometry.Stacked, "short layout is stacked");
            AssertEx.Equal(500f, geometry.Form.Height, "stacked form keeps its measured height");
            AssertEx.Equal(516f, geometry.PreviewCaption.Y, "stacked preview follows the form gap");
            AssertEx.Equal(544f, geometry.Preview.Y, "stacked preview follows its caption gap");
        }

        public static void PreviewGeometryClampsNegativeDimensionsAndSpacing()
        {
            var geometry = SettingsPreviewGeometry.Arrange(
                -5f,
                -10f,
                false,
                -20f,
                -30f,
                -40f,
                -50f,
                -60f
            );

            AssertEx.Equal(0f, geometry.Form.Width, "negative width clamps to zero");
            AssertEx.Equal(0f, geometry.Form.Height, "negative available height clamps to zero");
            AssertEx.Equal(
                0f,
                geometry.PreviewCaption.Height,
                "negative caption height clamps to zero"
            );
            AssertEx.Equal(0f, geometry.Preview.Height, "negative preview height clamps to zero");
            AssertEx.Equal(0f, geometry.Preview.Y, "negative gaps clamp to zero");
        }
    }
}
