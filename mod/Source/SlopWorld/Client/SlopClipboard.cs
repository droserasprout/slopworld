using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Sidecar playback and terminals stay in the container, but the clipboard belongs to the
    // native game. Native Linux still mirrors through slopd because Unity's buffer is unreliable
    // under some compositors.
    public static class SlopClipboard
    {
        static readonly HashSet<string> Pending = new HashSet<string>();
        static string _lastCopiedText;

        public static void Reset()
        {
            Pending.Clear();
            _lastCopiedText = null;
        }

        public static void Copy(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            GUIUtility.systemCopyBuffer = text;
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                ok?.Invoke();
                return;
            }

            // wl-copy can remain alive as the Wayland clipboard owner. Rapidly repeating the
            // same selection must not create another owner (and another GNOME readiness popup).
            if (text == _lastCopiedText || !Pending.Add(text))
            {
                ok?.Invoke();
                return;
            }

            SlopClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}",
                _ =>
                {
                    Pending.Remove(text);
                    _lastCopiedText = text;
                    ok?.Invoke();
                },
                error =>
                {
                    Pending.Remove(text);
                    fail?.Invoke(error);
                });
        }
    }
}
