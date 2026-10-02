using System;

namespace SlopWorld
{
    // Pure vertical split geometry for the Files tab. The pane floors are allowed to
    // compress together on a tiny viewport, but the result never extends beyond the body.
    public readonly struct SidebarFilesSplitGeometry
    {
        public readonly UiLayoutRect Upper;
        public readonly UiLayoutRect Divider;
        public readonly UiLayoutRect Lower;
        public readonly bool HasUpper;
        public readonly float Fraction;

        public SidebarFilesSplitGeometry(UiLayoutRect upper, UiLayoutRect divider,
                                         UiLayoutRect lower, bool hasUpper, float fraction)
        {
            Upper = upper;
            Divider = divider;
            Lower = lower;
            HasUpper = hasUpper;
            Fraction = fraction;
        }

        public static SidebarFilesSplitGeometry Arrange(UiLayoutRect body, bool hasUpper,
                                                        float fraction, float upperMinimum,
                                                        float lowerMinimum, float dividerHeight)
        {
            if (!hasUpper || body.Height <= 0f)
                return new SidebarFilesSplitGeometry(
                    new UiLayoutRect(body.X, body.Y, body.Width, 0f),
                    new UiLayoutRect(body.X, body.Y, body.Width, 0f), body, false, Clamp01(fraction));

            float available = AvailableHeight(body.Height, dividerHeight, out float divider);
            float normalized = ClampFraction(fraction, available, upperMinimum, lowerMinimum);
            float upperHeight = available * normalized;
            float lowerHeight = available - upperHeight;

            var upper = new UiLayoutRect(body.X, body.Y, body.Width, upperHeight);
            var split = new UiLayoutRect(body.X, body.Y + upperHeight,
                body.Width, divider);
            var lower = new UiLayoutRect(split.X, split.YMax, body.Width, lowerHeight);
            return new SidebarFilesSplitGeometry(upper, split, lower, true, normalized);
        }

        public static float ClampFraction(float fraction, float availableHeight,
                                          float upperMinimum, float lowerMinimum)
        {
            float available = SafeNonNegative(availableHeight);
            if (available <= 0f) return Clamp01(fraction);

            // Capping each floor at half the available room makes the fallback useful even
            // when the two requested floors cannot both fit in a tiny sidebar.
            float upper = Math.Min(available / 2f, SafeNonNegative(upperMinimum));
            float lower = Math.Min(available / 2f, SafeNonNegative(lowerMinimum));
            float min = upper / available;
            float max = 1f - lower / available;
            return Math.Max(min, Math.Min(max, Clamp01(fraction)));
        }

        public static float FractionAt(float pointerY, UiLayoutRect body,
                                       float upperMinimum, float lowerMinimum,
                                       float dividerHeight)
        {
            if (body.Height <= 0f) return Clamp01(0f);
            float available = AvailableHeight(body.Height, dividerHeight, out float divider);
            float raw = available <= 0f ? 0f
                : (pointerY - body.Y - divider / 2f) / available;
            return ClampFraction(raw, available, upperMinimum, lowerMinimum);
        }

        static float AvailableHeight(float height, float dividerHeight, out float divider)
        {
            divider = SafeNonNegative(dividerHeight);
            // An oversized divider must not consume both panes.
            if (divider >= height) divider = 0f;
            return Math.Max(0f, height - divider);
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0.25f;
            return Math.Max(0f, Math.Min(1f, value));
        }

        static float SafeNonNegative(float value) =>
            float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
    }
}
