using System;

namespace SlopWorld
{
    public readonly struct TabbedFormGeometry
    {
        public readonly UiLayoutRect Rail;
        public readonly UiLayoutRect Body;
        public readonly UiLayoutRect Footer;

        public TabbedFormGeometry(UiLayoutRect rail, UiLayoutRect body,
                                  UiLayoutRect footer)
        {
            Rail = rail;
            Body = body;
            Footer = footer;
        }
    }

    // Pure geometry for dialogs with a title lane, left tab rail, body and footer. Short or
    // narrow windows collapse the content area to zero instead of producing negative rects.
    public static class TabbedFormLayout
    {
        public static TabbedFormGeometry Arrange(UiLayoutRect form, float railWidth,
                                                  float headerHeight, float footerHeight,
                                                  float edgeGap, float bodyGap)
        {
            float width = form.Width;
            float height = form.Height;
            float footer = Math.Min(height, Math.Max(0f, footerHeight));
            float safeEdgeGap = Math.Max(0f, edgeGap);
            float top = form.Y + Math.Min(height,
                Math.Max(0f, headerHeight) + safeEdgeGap);
            float footerTop = form.Y + height - footer;
            float bottom = Math.Max(form.Y, footerTop - safeEdgeGap);
            float bodyHeight = Math.Max(0f, bottom - top);

            float rail = Math.Min(width, Math.Max(0f, railWidth));
            float gap = Math.Min(Math.Max(0f, bodyGap), Math.Max(0f, width - rail));
            var railRect = new UiLayoutRect(form.X, top, rail, bodyHeight);
            var bodyRect = new UiLayoutRect(form.X + rail + gap, top,
                Math.Max(0f, width - rail - gap), bodyHeight);
            var footerRect = new UiLayoutRect(form.X, footerTop, width, footer);
            return new TabbedFormGeometry(railRect, bodyRect, footerRect);
        }
    }
}
