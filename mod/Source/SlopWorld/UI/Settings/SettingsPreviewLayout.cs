using System;

namespace SlopWorld
{
    // Shared cached composition for settings with a form and live preview.
    public sealed class SettingsPreviewLayout
    {
        readonly UiLayoutItem[] _rootItems = new UiLayoutItem[2];
        readonly UiLayoutRect[] _root = new UiLayoutRect[2];
        readonly UiLayoutItem[] _previewItems = new UiLayoutItem[2];
        readonly UiLayoutRect[] _preview = new UiLayoutRect[2];

        bool _valid;
        float _width, _height, _formH, _previewH;
        bool _stacked;
        int _contentRevision, _metricsRevision, _typographyRevision, _scaleRevision;

        public UiLayoutRect Form => _root[0];
        public UiLayoutRect PreviewBlock => _root[1];
        public UiLayoutRect PreviewCaption =>
            new UiLayoutRect(PreviewBlock.X + _preview[0].X,
                PreviewBlock.Y + _preview[0].Y, _preview[0].Width, _preview[0].Height);
        public UiLayoutRect Preview =>
            new UiLayoutRect(PreviewBlock.X + _preview[1].X,
                PreviewBlock.Y + _preview[1].Y, _preview[1].Width, _preview[1].Height);

        public void Arrange(float width, float height, bool stacked, float formH,
                            float previewH, int contentRevision)
        {
            float safeWidth = Math.Max(0f, width);
            float safeHeight = Math.Max(0f, height);
            float safeForm = Math.Max(0f, formH);
            float safePreview = Math.Max(0f, previewH);
            int metricsRevision = UiMetrics.DensityRevision;
            int typographyRevision = UiMetrics.TypographyRevision;
            int scaleRevision = UiMetrics.ScaleRevision;
            if (_valid && _width == safeWidth && _height == safeHeight
                && _formH == safeForm && _previewH == safePreview
                && _stacked == stacked && _contentRevision == contentRevision
                && _metricsRevision == metricsRevision
                && _typographyRevision == typographyRevision
                && _scaleRevision == scaleRevision)
                return;

            _width = safeWidth;
            _height = safeHeight;
            _formH = safeForm;
            _previewH = safePreview;
            _stacked = stacked;
            _contentRevision = contentRevision;
            _metricsRevision = metricsRevision;
            _typographyRevision = typographyRevision;
            _scaleRevision = scaleRevision;
            _valid = true;

            float blockH = UiWidgets.RowH + UiWidgets.GapXS + safePreview;
            _rootItems[0] = new UiLayoutItem(
                stacked ? UiLayoutSize.Content(safeForm) :
                    UiLayoutSize.Flexible(),
                UiLayoutSize.Flexible(), stacked ? safeForm : 0f, 0f);
            _rootItems[1] = new UiLayoutItem(
                UiLayoutSize.Fixed(blockH), UiLayoutSize.Flexible(), blockH, 0f);

            float availableH = stacked
                ? safeForm + UiWidgets.GapM + blockH : safeHeight;
            UiComposition.Arrange(UiLayoutAxis.Column,
                new UiLayoutRect(0f, 0f, safeWidth, availableH),
                UiLayoutPadding.Zero, UiWidgets.GapM, _rootItems, _root);

            _previewItems[0] = new UiLayoutItem(UiLayoutSize.Fixed(UiWidgets.RowH),
                UiLayoutSize.Flexible(), UiWidgets.RowH, 0f);
            _previewItems[1] = new UiLayoutItem(UiLayoutSize.Fixed(safePreview),
                UiLayoutSize.Flexible(), safePreview, 0f);
            UiComposition.Arrange(UiLayoutAxis.Column,
                new UiLayoutRect(0f, 0f, _root[1].Width, blockH),
                UiLayoutPadding.Zero, UiWidgets.GapXS, _previewItems, _preview);
        }
    }
}
