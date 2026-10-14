using System;

namespace UnityEngine
{
    // Text geometry is supplied by the test; this does not model native font layout.
    public class TextEditor
    {
        public string text;
        public int cursorIndex,
            selectIndex;
        public bool multiline;
        public GUIStyle style;
        public Rect position;
        public bool hasSelection => cursorIndex != selectIndex;
        public Action<Vector2> HitTest;

        public void MoveCursorToPosition(Vector2 point) => HitTest(point);
    }

    static partial class GUI
    {
        internal static Rect TextFieldTextRect(Rect rect, bool multiline, GUIStyle style) =>
            new Rect(
                rect.x + style.padding.left,
                rect.y + style.padding.top,
                rect.width - style.padding.horizontal,
                rect.height
            );
    }
}

namespace UnityStubs.Gui
{
    static class GuiBackend
    {
        public static TextBackend Current = new TextBackend();
    }

    sealed class TextBackend
    {
        public Func<string, byte[], float, int, int> Register;

        public int RegisterFont(string name, byte[] bytes, float size, int mode) =>
            Register(name, bytes, size, mode);

        public Func<string, float, int, UnityEngine.Vector2> Cursor;

        public UnityEngine.Vector2 TextCursorPos(
            int font,
            float size,
            UnityEngine.FontStyle style,
            string text,
            float width,
            int index,
            out float lineHeight
        )
        {
            lineHeight = 16f;
            return Cursor(text, width, index);
        }
    }
}
