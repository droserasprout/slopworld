using System;
using System.Collections.Generic;
using UnityEngine;

namespace SlopWorld
{
    // The native game manages the clipboard when playback and terminals run in the sidecar container.
    // Native Linux synchronizes the clipboard through slopd because Unity's buffer is unreliable under some compositors.
    public static class DaemonClipboard
    {
        sealed class PendingCopy
        {
            public readonly List<Action> Success = new List<Action>();
            public readonly List<Action<string>> Failure = new List<Action<string>>();
        }

        sealed class Channel
        {
            readonly string _writeRoute, _readRoute;
            readonly Dictionary<string, PendingCopy> _pending = new Dictionary<string, PendingCopy>();
            string _lastCopied;

            public Channel(string writeRoute, string readRoute)
            {
                _writeRoute = writeRoute;
                _readRoute = readRoute;
            }

            public void Reset()
            {
                _pending.Clear();
                _lastCopied = null;
            }

            public void Copy(string text, Action ok, Action<string> fail)
            {
                bool exists = _pending.TryGetValue(text, out var pending);
                if (!exists) _pending[text] = pending = new PendingCopy();
                pending.Success.Add(ok);
                pending.Failure.Add(fail);
                if (exists) return;

                // Another desktop app may have replaced our last selection. Only a
                // current read can justify preserving the existing clipboard owner.
                if (text == _lastCopied)
                    DaemonClient.Get<Wire.TextResult>(_readRoute,
                        value => { if (value.Text == text) Finish(text, pending, null); else Write(text, pending); },
                        _ => Write(text, pending));
                else Write(text, pending);
            }

            bool IsCurrent(string text, PendingCopy pending) =>
                _pending.TryGetValue(text, out var current) && ReferenceEquals(current, pending);

            void Write(string text, PendingCopy pending)
            {
                if (!IsCurrent(text, pending)) { Finish(text, pending, "Clipboard connection reset"); return; }
                DaemonClient.Post(_writeRoute, new Wire.ClipReq { Text = text },
                    _ => Finish(text, pending, null), error => Finish(text, pending, error));
            }

            void Finish(string text, PendingCopy pending, string error)
            {
                if (IsCurrent(text, pending))
                {
                    _pending.Remove(text);
                    if (error == null) _lastCopied = text;
                }
                // One caller must not prevent the remaining waiters from settling.
                for (int i = 0; i < pending.Success.Count; i++)
                {
                    try
                    {
                        if (error == null) pending.Success[i]?.Invoke();
                        else pending.Failure[i]?.Invoke(error);
                    }
                    catch (Exception e) { Verse.Log.Error("[SlopWorld] clipboard callback: " + e); }
                }
            }
        }

        static readonly Channel Clipboard = new Channel(WireProtocol.Routes.Clipboard, WireProtocol.Routes.ClipboardText);
        static readonly Channel Primary = new Channel(WireProtocol.Routes.ClipboardPrimary, WireProtocol.Routes.ClipboardPrimaryText);

        public static void Reset()
        {
            Clipboard.Reset();
            Primary.Reset();
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

            Clipboard.Copy(text, ok, fail);
        }

        public static void CopyPrimary(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (!SessionHub.Instance.Capabilities.Clipboard)
            {
                ok?.Invoke();
                return;
            }

            Primary.Copy(text, ok, fail);
        }
    }
}
