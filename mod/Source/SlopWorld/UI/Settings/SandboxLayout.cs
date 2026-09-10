using System;

namespace SlopWorld
{
    public readonly struct SandboxSplit
    {
        public readonly UiLayoutRect List;
        public readonly UiLayoutRect Editor;
        public readonly bool Stacked;

        public SandboxSplit(UiLayoutRect list, UiLayoutRect editor, bool stacked)
        {
            List = list;
            Editor = editor;
            Stacked = stacked;
        }
    }

    // Pure master/detail policy for both Sandbox settings pages.
    public static class SandboxLayout
    {
        public const float ListMinimumWidth = 220f;
        public const float EditorMinimumWidth = 320f;
        public const float EditorPreferredWidth = 590f;

        public static float Breakpoint(float gap) =>
            ListMinimumWidth + Math.Max(0f, gap) + EditorMinimumWidth;

        public static SandboxSplit Arrange(UiLayoutRect content, float gap)
        {
            float requestedGap = Math.Max(0f, gap);
            bool stacked = content.Width < Breakpoint(requestedGap);
            float safeGap = Math.Min(requestedGap,
                stacked ? content.Height : content.Width);
            if (stacked)
            {
                float paneHeight = Math.Max(0f, content.Height - safeGap) / 2f;
                var list = new UiLayoutRect(content.X, content.Y,
                    content.Width, paneHeight);
                var editor = new UiLayoutRect(content.X,
                    content.Y + paneHeight + safeGap, content.Width,
                    content.Height - paneHeight - safeGap);
                return new SandboxSplit(list, editor, true);
            }

            float editorWidth = Math.Min(EditorPreferredWidth,
                Math.Max(EditorMinimumWidth, content.Width * 0.60f));
            editorWidth = Math.Min(editorWidth,
                content.Width - safeGap - ListMinimumWidth);
            float listWidth = content.Width - safeGap - editorWidth;
            return new SandboxSplit(
                new UiLayoutRect(content.X, content.Y, listWidth, content.Height),
                new UiLayoutRect(content.X + listWidth + safeGap, content.Y,
                    editorWidth, content.Height), false);
        }
    }
}
