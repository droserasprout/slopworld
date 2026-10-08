using Verse;

namespace SlopWorld.Tests
{
    static class UiMetricsTests
    {
        public static void Values()
        {
            var normal = UiMetricValues.Resolve(UiDensityPreset.Default, 11f, 13f, 15f, 4);
            AssertEx.Equal(4f, normal.GapXS, "default extra-small gap");
            AssertEx.Equal(30f, normal.ButtonH(13f), "default button minimum");
            AssertEx.Equal(22f, normal.CompactMinH, "default compact control minimum");

            var compact = UiMetricValues.Resolve(UiDensityPreset.Compact, 11f, 13f, 15f, 5);
            AssertEx.Equal(3f, compact.GapXS, "compact extra-small gap");
            AssertEx.Equal(26f, compact.ButtonH(13f), "compact button minimum");
            AssertEx.Equal(20f, compact.CompactMinH, "compact control minimum");

            // A large dynamic-font line wins over the nominal floor in both presets.
            AssertEx.Equal(46f, normal.ButtonH(40f), "font-derived default minimum");
            AssertEx.Equal(45f, compact.ButtonH(40f), "font-derived compact minimum");

            var negative = UiMetricValues.Resolve("unknown", -3f, -2f, -1f);
            AssertEx.Equal(UiDensityPreset.Default, negative.Density,
                "unknown density normalizes to default");
            AssertEx.Equal(0f, negative.LineH(GameFont.Small),
                "negative measured line is safe");
        }

        public static void Invalidation()
        {
            string density = Settings.S.uiDensity;
            float scale = Prefs.UIScale;
            int font = UiFont.RevisionValue, atlas = UiTheme.AtlasRevisionValue;
            try
            {
                ModEntry.Instance.settings.uiDensity = UiDensityPreset.Default;
                Verse.Prefs.UIScale = 1f;
                UiFont.RevisionValue = 0;
                UiTheme.AtlasRevisionValue = 0;
                UnityEngine.Time.frameCount = 100;

                int baseRevision = UiMetrics.Revision;
                int baseDensity = UiMetrics.DensityRevision;
                int baseTypography = UiMetrics.TypographyRevision;
                int baseScale = UiMetrics.ScaleRevision;

                ModEntry.Instance.settings.uiDensity = UiDensityPreset.Compact;
                UnityEngine.Time.frameCount++;
                AssertEx.True(UiMetrics.Revision != baseRevision &&
                    UiMetrics.DensityRevision != baseDensity,
                    "density transition invalidates layout");
                AssertEx.Equal(baseTypography, UiMetrics.TypographyRevision,
                    "density transition does not report typography change");
                AssertEx.Equal(baseScale, UiMetrics.ScaleRevision,
                    "density transition does not report scale change");

                Verse.Prefs.UIScale = 1.25f;
                UnityEngine.Time.frameCount++;
                AssertEx.True(UiMetrics.ScaleRevision != baseScale,
                    "scale transition has its own revision");

                UiFont.RevisionValue++;
                UiTheme.AtlasRevisionValue++;
                UnityEngine.Time.frameCount++;
                AssertEx.True(UiMetrics.TypographyRevision != baseTypography,
                    "font and atlas transitions invalidate typography");
            }
            finally
            {
                Settings.S.uiDensity = density;
                Prefs.UIScale = scale;
                UiFont.RevisionValue = font;
                UiTheme.AtlasRevisionValue = atlas;
                UnityEngine.Time.frameCount++;
                UiMetrics.BeginFrame();
            }
        }
    }
}
