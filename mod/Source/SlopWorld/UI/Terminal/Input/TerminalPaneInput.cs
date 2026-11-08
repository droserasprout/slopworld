using UnityEngine;

namespace SlopWorld
{
    // Wheel admission is independent of the selected pane's keyboard focus.
    internal static class TerminalPaneInput
    {
        internal static bool Wheel(bool hostInput, bool visible, EventType type, bool hovered) =>
            hostInput && visible && hovered && type == EventType.ScrollWheel;
    }
}
