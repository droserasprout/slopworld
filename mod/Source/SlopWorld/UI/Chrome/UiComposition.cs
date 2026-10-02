using System;

namespace SlopWorld
{
    // Pure row/column sizing primitives. The math uses no Unity rectangles or IMGUI state.
    // callers convert the resulting values to Rect only while drawing.
    public enum UiLayoutAxis
    {
        Row,
        Column,
    }

    public enum UiLayoutSizeKind
    {
        Fixed,
        Content,
        Flexible,
    }

    public enum UiLayoutAlignment
    {
        Start,
        Center,
        End,
        Stretch,
    }

    public readonly struct UiLayoutSize
    {
        public readonly UiLayoutSizeKind Kind;
        public readonly float Value;
        public readonly float Minimum;

        UiLayoutSize(UiLayoutSizeKind kind, float value, float minimum)
        {
            Kind = kind;
            Value = NonNegative(value);
            Minimum = NonNegative(minimum);
        }

        public static UiLayoutSize Fixed(float value, float minimum = 0f) =>
            new UiLayoutSize(UiLayoutSizeKind.Fixed, value, minimum);

        public static UiLayoutSize Content(float value, float minimum = 0f) =>
            new UiLayoutSize(UiLayoutSizeKind.Content, value, minimum);

        // Value is the flex weight. A zero weight keeps its base size but receives no surplus.
        public static UiLayoutSize Flexible(float weight = 1f, float minimum = 0f) =>
            new UiLayoutSize(UiLayoutSizeKind.Flexible, weight, minimum);

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }

    public readonly struct UiLayoutPadding
    {
        public readonly float Left, Top, Right, Bottom;

        public UiLayoutPadding(float left, float top, float right, float bottom)
        {
            Left = NonNegative(left);
            Top = NonNegative(top);
            Right = NonNegative(right);
            Bottom = NonNegative(bottom);
        }

        public static UiLayoutPadding Zero => new UiLayoutPadding(0f, 0f, 0f, 0f);

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }

    public readonly struct UiLayoutRect
    {
        public readonly float X, Y, Width, Height;

        public UiLayoutRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = NonNegative(width);
            Height = NonNegative(height);
        }

        public float XMax => X + Width;
        public float YMax => Y + Height;

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }

    public readonly struct UiLayoutItem
    {
        public readonly UiLayoutSize Main;
        public readonly UiLayoutSize Cross;
        public readonly float ContentMain;
        public readonly float ContentCross;
        public readonly UiLayoutAlignment Alignment;

        public UiLayoutItem(UiLayoutSize main, UiLayoutSize cross, float contentMain,
                            float contentCross, UiLayoutAlignment alignment = UiLayoutAlignment.Stretch)
        {
            Main = main;
            Cross = cross;
            ContentMain = NonNegative(contentMain);
            ContentCross = NonNegative(contentCross);
            Alignment = alignment;
        }

        public static UiLayoutItem Column(UiLayoutSize height, float contentHeight,
                                          float minWidth = 0f) =>
            new UiLayoutItem(height, UiLayoutSize.Flexible(1f, minWidth), contentHeight, 0f);

        public static UiLayoutItem Row(UiLayoutSize width, float contentWidth,
                                       float minHeight = 0f) =>
            new UiLayoutItem(width, UiLayoutSize.Flexible(1f, minHeight), contentWidth, 0f);

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }

    public static class UiComposition
    {
        // Return the minimum content size that the items require. Do not limit it to the available space.
        // Callers need the overflow amount to create a scroll body.
        public static float Measure(UiLayoutAxis axis, UiLayoutPadding padding, float gap,
                                    UiLayoutItem[] items)
        {
            if (items == null || items.Length == 0)
                return axis == UiLayoutAxis.Row
                    ? padding.Left + padding.Right : padding.Top + padding.Bottom;

            float total = axis == UiLayoutAxis.Row
                ? padding.Left + padding.Right : padding.Top + padding.Bottom;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                total += MainBase(item);
                if (i != 0) total += NonNegative(gap);
            }
            return NonNegative(total);
        }

        // Arrange a flat row or column into caller-owned output storage. Fixed and content items
        // keep their preferred sizes. Flexible items divide remaining space by weight. If minimums
        // do not fit, later items may extend beyond the available edge. Every rectangle remains
        // nonnegative, and no item receives a negative size.
        public static float Arrange(UiLayoutAxis axis, UiLayoutRect available,
                                    UiLayoutPadding padding, float gap, UiLayoutItem[] items,
                                    UiLayoutRect[] output)
        {
            if (output == null || items == null || output.Length < items.Length)
                throw new ArgumentException("output must hold one rectangle per layout item");

            float width = NonNegative(available.Width);
            float height = NonNegative(available.Height);
            float left = Math.Min(padding.Left, width);
            float right = Math.Min(padding.Right, Math.Max(0f, width - left));
            float top = Math.Min(padding.Top, height);
            float bottom = Math.Min(padding.Bottom, Math.Max(0f, height - top));
            float innerMain = axis == UiLayoutAxis.Row
                ? Math.Max(0f, width - left - right)
                : Math.Max(0f, height - top - bottom);
            float innerCross = axis == UiLayoutAxis.Row
                ? Math.Max(0f, height - top - bottom)
                : Math.Max(0f, width - left - right);
            float safeGap = NonNegative(gap);
            float gaps = items.Length <= 1 ? 0f : safeGap * (items.Length - 1);
            float remaining = innerMain - gaps;
            float baseTotal = 0f;
            float flexWeight = 0f;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                baseTotal += MainBase(item);
                if (item.Main.Kind == UiLayoutSizeKind.Flexible)
                    flexWeight += item.Main.Value;
            }

            // Every item receives its fixed/content/minimum base first. Only genuine
            // surplus is divided by flex weight, so satisfying one flex minimum cannot
            // manufacture overflow by leaving the other flex shares unchanged.
            float flexSpace = Math.Max(0f, remaining - baseTotal);
            float cursor = axis == UiLayoutAxis.Row
                ? available.X + left : available.Y + top;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                float main = MainBase(item);
                if (item.Main.Kind == UiLayoutSizeKind.Flexible)
                    main += flexWeight <= 0f ? 0f : flexSpace *
                        (item.Main.Value) / flexWeight;

                float cross = CrossPreferred(item, innerCross);
                float crossStart = CrossOffset(item.Alignment, innerCross, cross);
                float x = axis == UiLayoutAxis.Row
                    ? cursor : available.X + left + crossStart;
                float y = axis == UiLayoutAxis.Row
                    ? available.Y + top + crossStart : cursor;
                output[i] = axis == UiLayoutAxis.Row
                    ? new UiLayoutRect(x, y, main, cross)
                    : new UiLayoutRect(x, y, cross, main);
                cursor += main;
                if (i + 1 < items.Length) cursor += safeGap;
            }

            return Measure(axis, padding, safeGap, items);
        }

        static float MainBase(UiLayoutItem item)
        {
            float preferred = item.Main.Kind == UiLayoutSizeKind.Fixed
                ? item.Main.Value : item.ContentMain;
            return Math.Max(item.Main.Minimum, Math.Max(0f, preferred));
        }

        static float CrossPreferred(UiLayoutItem item, float available)
        {
            float value;
            if (item.Cross.Kind == UiLayoutSizeKind.Fixed)
                value = item.Cross.Value;
            else if (item.Cross.Kind == UiLayoutSizeKind.Content)
                value = Math.Max(item.ContentCross, item.Cross.Minimum);
            else value = available;

            if (item.Alignment == UiLayoutAlignment.Stretch &&
                item.Cross.Kind != UiLayoutSizeKind.Fixed)
                value = Math.Max(value, available);
            return Math.Min(available, Math.Max(0f, Math.Max(value, item.Cross.Minimum)));
        }

        static float CrossOffset(UiLayoutAlignment alignment, float available, float size)
        {
            switch (alignment)
            {
                case UiLayoutAlignment.Center: return Math.Max(0f, (available - size) / 2f);
                case UiLayoutAlignment.End: return Math.Max(0f, available - size);
                default: return 0f;
            }
        }

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }
}
