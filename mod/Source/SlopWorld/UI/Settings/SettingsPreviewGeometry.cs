namespace SlopWorld
{
    // Pure placement for settings pages that keep a preview beside a scrollable form.
    public readonly struct SettingsPreviewGeometry
    {
        public readonly UiLayoutRect Form;
        public readonly UiLayoutRect PreviewCaption;
        public readonly UiLayoutRect Preview;
        public readonly bool Stacked;

        SettingsPreviewGeometry(
            UiLayoutRect form,
            UiLayoutRect previewCaption,
            UiLayoutRect preview,
            bool stacked
        )
        {
            Form = form;
            PreviewCaption = previewCaption;
            Preview = preview;
            Stacked = stacked;
        }

        public static bool ShouldStack(
            float availableHeight,
            float minimumFormHeight,
            float previewHeight,
            float captionHeight,
            float previewGap,
            float formGap
        )
        {
            float blockHeight =
                NonNegative(captionHeight) + NonNegative(previewGap) + NonNegative(previewHeight);
            return NonNegative(availableHeight)
                < NonNegative(minimumFormHeight) + NonNegative(formGap) + blockHeight;
        }

        public static SettingsPreviewGeometry Arrange(
            float width,
            float height,
            bool stacked,
            float formHeight,
            float previewHeight,
            float captionHeight,
            float previewGap,
            float formGap
        )
        {
            float safeWidth = NonNegative(width);
            float safeHeight = NonNegative(height);
            float safeFormHeight = NonNegative(formHeight);
            float safePreviewHeight = NonNegative(previewHeight);
            float safeCaptionHeight = NonNegative(captionHeight);
            float safePreviewGap = NonNegative(previewGap);
            float safeFormGap = NonNegative(formGap);
            float blockHeight = safeCaptionHeight + safePreviewGap + safePreviewHeight;
            float arrangedFormHeight = stacked
                ? safeFormHeight
                : NonNegative(safeHeight - safeFormGap - blockHeight);
            float blockY = arrangedFormHeight + safeFormGap;

            return new SettingsPreviewGeometry(
                new UiLayoutRect(0f, 0f, safeWidth, arrangedFormHeight),
                new UiLayoutRect(0f, blockY, safeWidth, safeCaptionHeight),
                new UiLayoutRect(
                    0f,
                    blockY + safeCaptionHeight + safePreviewGap,
                    safeWidth,
                    safePreviewHeight
                ),
                stacked
            );
        }

        static float NonNegative(float value) => value < 0f ? 0f : value;
    }
}
