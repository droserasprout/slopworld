using System;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The values used by layout are captured once per IMGUI frame. A settings action may change
    // density, a font, or scale while the page is being drawn. However, All of the passes belonging
    // to that frame must continue to use one coherent set of measurements.
    public readonly struct UiMetricValues
    {
        public readonly string Density;
        public readonly float TinyLine;
        public readonly float SmallLine;
        public readonly float MediumLine;
        public readonly int Revision;

        public UiMetricValues(string density, float tinyLine, float smallLine, float mediumLine,
                              int revision)
        {
            Density = UiDensityPreset.Normalize(density);
            TinyLine = NonNegative(tinyLine);
            SmallLine = NonNegative(smallLine);
            MediumLine = NonNegative(mediumLine);
            Revision = revision;
        }

        public bool Compact => Density == UiDensityPreset.Compact;
        public float GapXS => Compact ? 3f : 4f;
        public float GapS => Compact ? 6f : 8f;
        public float GapM => Compact ? 12f : 16f;
        public float GapL => Compact ? 18f : 24f;

        public float LineH(GameFont font)
        {
            switch (font)
            {
                case GameFont.Tiny: return TinyLine;
                case GameFont.Medium: return MediumLine;
                default: return SmallLine;
            }
        }

        public float ButtonH(float lineH) =>
            Mathf.Max(NonNegative(lineH) + GapXS + 2f, Compact ? 26f : 30f);

        public float CompactMinH => Compact ? 20f : 22f;
        public float PaletteMinH => Compact ? 24f : 26f;

        // These pure helpers keep the font-derived floors testable without Unity's text
        // generator. They are also useful to callers that already measured a specific tier.
        public static UiMetricValues Resolve(string density, float tinyLine, float smallLine,
                                              float mediumLine, int revision = 0) =>
            new UiMetricValues(density, tinyLine, smallLine, mediumLine, revision);

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }

    public static class UiMetrics
    {
        static int _frame = -1;
        static UiMetricValues _current;
        static string _density;
        static float _scale = float.NaN;
        static int _fontRevision = -1;
        static int _atlasRevision = -1;
        static int _revision;
        static int _densityRevision;
        static int _typographyRevision;
        static int _scaleRevision;

        public static UiMetricValues Current
        {
            get
            {
                BeginFrame();
                return _current;
            }
        }

        public static string Density => Current.Density;
        public static bool Compact => Current.Compact;
        public static int Revision => Current.Revision;

        // Kept separate so caches can distinguish geometry invalidation from text-only
        // measurement changes. Color/scheme changes deliberately do not touch any of these.
        public static int DensityRevision { get { BeginFrame(); return _densityRevision; } }
        public static int TypographyRevision
        {
            get { BeginFrame(); return _typographyRevision; }
        }
        public static int ScaleRevision { get { BeginFrame(); return _scaleRevision; } }

        public static float GapXS => Current.GapXS;
        public static float GapS => Current.GapS;
        public static float GapM => Current.GapM;
        public static float GapL => Current.GapL;

        public static float ButtonH(float lineH) => Current.ButtonH(lineH);
        public static float CompactMinH => Current.CompactMinH;
        public static float PaletteMinH => Current.PaletteMinH;

        // WorkspaceLayout calls this before reading screen bounds. Other chrome can call it
        // first. The frame number makes both paths resolve to the same immutable snapshot.
        public static void BeginFrame()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;

            string density = UiDensityPreset.Normalize(Settings.UiDensity);
            float scale = Prefs.UIScale;
            int fontRevision = UiFont.Revision;
            int atlasRevision = UiTheme.AtlasRevision;

            bool first = _density == null;
            bool densityChanged = first || !string.Equals(_density, density,
                StringComparison.Ordinal);
            bool typographyChanged = first || _fontRevision != fontRevision
                || _atlasRevision != atlasRevision;
            bool scaleChanged = first || _scale != scale;

            if (densityChanged) unchecked { _densityRevision++; }
            if (typographyChanged) unchecked { _typographyRevision++; }
            if (scaleChanged) unchecked { _scaleRevision++; }
            if (densityChanged || typographyChanged || scaleChanged)
            {
                unchecked { _revision++; }
                if (_revision == 0) _revision = 1;
            }

            _density = density;
            _scale = scale;
            _fontRevision = fontRevision;
            _atlasRevision = atlasRevision;

            _current = UiMetricValues.Resolve(density,
                LineHeight(GameFont.Tiny), LineHeight(GameFont.Small),
                LineHeight(GameFont.Medium), _revision);
        }

        static float LineHeight(GameFont font)
        {
            if (font == GameFont.Tiny && !Text.TinyFontSupported)
                font = GameFont.Small;
            return Mathf.Ceil(Mathf.Max(0f, Text.LineHeightOf(font)));
        }
    }
}
