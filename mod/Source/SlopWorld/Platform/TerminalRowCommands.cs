using System;
using UnityEngine;

namespace SlopWorld
{
    // Optional drawing backend only. Native painting has no recorder; the browser
    // installs these delegates explicitly on the GUI thread. Keys refer to final
    // decorated run lists; Complete cancels capture when painting throws.
    public static class TerminalRowCommands
    {
        public static Func<object, GUIStyle, float[], bool> TryReplay;
        public static Action<bool, GUIStyle> Complete;
    }
}
