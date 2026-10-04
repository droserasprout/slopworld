using System;
using UnityEngine;

namespace UnityEngine
{
    static class Input
    {
        public static bool Held;
        public static bool GetMouseButton(int button) => Held;
    }
}

namespace Verse
{
    static class Mouse
    {
        public static bool IsOver(Rect rect) => rect.Contains(Event.current.mousePosition);
    }
    // Native drawing/delta calculation is outside the harness. Canned native results
    // exercise the adapter's capture, bounds and cleanup, including event consumption.
    class WindowResizer
    {
        public Vector2 minWindowSize;
        bool isResizing;
        public static Rect Result;
        public static int Calls;
        public static int InputCalls;
        public static bool Compact;
        public bool Active => isResizing;
        public Rect DoResizeControl(Rect rect)
        {
            Calls++;
            Compact = SlopWorld.UiAreaResize.DrawingGrip;
            if (Event.current.type == EventType.MouseDown ||
                Event.current.type == EventType.MouseDrag || Event.current.type == EventType.MouseUp)
                InputCalls++;
            if (Event.current.type == EventType.MouseDown)
            {
                isResizing = true;
                // WindowResizer draws Widgets.ButtonImage, which reaches GUI.Button.
                // Unity's button takes mouse capture for its own control on the press.
                GUIUtility.hotControl = 12345;
                Event.current.Use();
            }
            if (Event.current.type == EventType.MouseUp) Event.current.Use();
            return isResizing ? Result : rect;
        }
    }
}

namespace SlopWorld
{
    static class FieldFocusScope { internal sealed class Memory { } }
    static class TextFieldSelection { public static void Retire(FieldLifetime lifetime) { } }
}
