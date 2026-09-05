using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // Sidecar playback and terminals stay in the container, but the clipboard belongs to the
    // native game. Native Linux still mirrors through slopd because Unity's buffer is unreliable
    // under some compositors.
    public static class DaemonClipboard
    {
        static readonly HashSet<string> Pending = new HashSet<string>();
        static readonly HashSet<string> PendingPrimary = new HashSet<string>();
        static string _lastCopiedText;
        static string _lastCopiedPrimaryText;

        public static void Reset()
        {
            Pending.Clear();
            PendingPrimary.Clear();
            _lastCopiedText = null;
            _lastCopiedPrimaryText = null;
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

            DaemonClient.Post("/api/clipboard", "{\"text\":" + JVal.Q(text) + "}",
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

        public static void CopyPrimary(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                ok?.Invoke();
                return;
            }

            // PRIMARY has its own owner, independent of CLIPBOARD. Avoid replacing that owner
            // when the same line is selected repeatedly, just as Copy does for CLIPBOARD.
            if (text == _lastCopiedPrimaryText || !PendingPrimary.Add(text))
            {
                ok?.Invoke();
                return;
            }

            DaemonClient.Post("/api/clipboard/primary", "{\"text\":" + JVal.Q(text) + "}",
                _ =>
                {
                    PendingPrimary.Remove(text);
                    _lastCopiedPrimaryText = text;
                    ok?.Invoke();
                },
                error =>
                {
                    PendingPrimary.Remove(text);
                    fail?.Invoke(error);
                });
        }
    }
}
