using System;
using UnityEngine;

namespace SlopWorld
{
    // Sidecar playback and terminals stay in the container, but the clipboard belongs to the
    // native game. Native Linux still mirrors through slopd because Unity's buffer is unreliable
    // under some compositors.
    public static class SlopClipboard
    {
        public static void Copy(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIUtility.systemCopyBuffer = text;
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                ok?.Invoke();
                return;
            }
            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}",
                _ => ok?.Invoke(), fail);
        }
    }
}
