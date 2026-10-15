using System;
using HarmonyLib;
using UnityEngine;
using UnityStubs.Gui;

namespace SlopWorld
{
    // Share the game's text geometry with fields and frozen tooltip wrapping.
    internal static class NativeTextLayout
    {
        static readonly Func<Rect, bool, GUIStyle, Rect> FieldTextRect = AccessTools.MethodDelegate<
            Func<Rect, bool, GUIStyle, Rect>
        >(AccessTools.Method(typeof(GUI), "TextFieldTextRect"));

        internal static Vector2 Cursor(GUIStyle style, string text, float width, int index)
        {
            using var scope = new NativeTextScope(style.richText);
            return GuiBackend.Current.TextCursorPos(
                style.FontIndex,
                style.fontSize,
                style.fontStyle,
                text,
                style.wordWrap ? Mathf.Max(1f, width - style.padding.horizontal) : -1f,
                index,
                out _
            );
        }

        internal static float CaretY(TextEditor editor)
        {
            using var scope = new NativeTextScope(false);
            var rect = FieldTextRect(editor.position, editor.multiline, editor.style);
            var style = editor.style;
            return rect.y
                + GuiBackend
                    .Current.TextCursorPos(
                        style.FontIndex,
                        style.fontSize,
                        style.fontStyle,
                        editor.text,
                        style.wordWrap ? rect.width : -1f,
                        editor.cursorIndex,
                        out _
                    )
                    .y;
        }

        internal static bool IsOverSelection(TextEditor editor, Vector2 point)
        {
            if (!editor.hasSelection || !editor.position.Contains(point))
                return false;
            int cursor = editor.cursorIndex;
            int select = editor.selectIndex;
            try
            {
                editor.MoveCursorToPosition(point);
                return editor.cursorIndex >= Math.Min(cursor, select)
                    && editor.cursorIndex < Math.Max(cursor, select);
            }
            finally
            {
                editor.cursorIndex = cursor;
                editor.selectIndex = select;
            }
        }

        internal static void ReplaceSelection(TextEditor editor, string insert)
        {
            int start = Math.Min(editor.cursorIndex, editor.selectIndex);
            int end = Math.Max(editor.cursorIndex, editor.selectIndex);
            editor.text = editor.text.Substring(0, start) + insert + editor.text.Substring(end);
            editor.cursorIndex = editor.selectIndex = start + insert.Length;
        }
    }
}
