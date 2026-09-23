using System;

namespace SlopWorld
{
    // Page geometry is independent of Unity. Shrinking the viewport never produces a
    // negative body or places the footer outside the page.
    public static class SettingsLayout
    {
        public static UiLayoutRect Inset(UiLayoutRect rect, float padding)
        {
            float x = Math.Min(Math.Max(0f, padding), rect.Width / 2f);
            float y = Math.Min(Math.Max(0f, padding), rect.Height / 2f);
            return new UiLayoutRect(rect.X + x, rect.Y + y,
                rect.Width - x * 2f, rect.Height - y * 2f);
        }

        public static UiLayoutRect Body(UiLayoutRect page, float padding,
                                        float footerHeight, float gap)
        {
            float footer = Math.Min(page.Height, Math.Max(0f, footerHeight));
            float remaining = Math.Max(0f, page.Height - footer);
            float body = Math.Max(0f, remaining - (footer > 0f ? Math.Max(0f, gap) : 0f));
            return Inset(new UiLayoutRect(page.X, page.Y, page.Width, body), padding);
        }

        public static UiLayoutRect Footer(UiLayoutRect page, float height) =>
            new UiLayoutRect(page.X, page.YMax - Math.Min(page.Height, Math.Max(0f, height)),
                page.Width, Math.Min(page.Height, Math.Max(0f, height)));
    }

    // Drawing measures IMGUI content. Publish the result on the next frame so input
    // and repaint within a frame see the same scroll extent.
    public sealed class SettingsContentHeight
    {
        int _frame = -1;
        float _height, _pending;

        public SettingsContentHeight(float estimate = 900f)
        {
            _height = _pending = Math.Max(0f, estimate);
        }

        public float BeginFrame(int frame)
        {
            if (_frame != frame)
            {
                _height = _pending;
                _frame = frame;
            }
            return _height;
        }

        public void Measure(float height) => _pending = Math.Max(0f, height);
    }
}
