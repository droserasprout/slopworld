using System;
using UnityEngine;

namespace SlopWorld
{
    // Linux selections belong to the game desktop, even when the connected daemon is remote.
    // macOS retains Unity's local text buffer fallback.
    public static class DesktopClipboard
    {
        static readonly MainThreadCompletions Completions = new MainThreadCompletions(
            64,
            System.Diagnostics.Stopwatch.Frequency / 500,
            System.Diagnostics.Stopwatch.GetTimestamp
        );
        internal static X11Clipboard X11 { get; set; } =
            new X11Clipboard(new SystemProcessRunner(), Completions.Enqueue);
        public static bool UsesX11 => Application.platform == RuntimePlatform.LinuxPlayer;

        public static void ReadLocalPaste(
            Action<string> text,
            Action<byte[]> image,
            Action<string> fail
        ) => X11.ReadPaste(text, image, fail);

        public static void Pump() =>
            Completions.Pump(e => Verse.Log.Error("[SlopWorld] clipboard callback: " + e));

        public static void ReadLocal(
            bool primary,
            Action<string> receive,
            Action<string> fail = null
        )
        {
            X11.Read(
                primary,
                receive,
                error =>
                {
                    // An explicit failure handler owns non-text paste behavior; Unity may
                    // still contain text from an earlier copy when X11 holds an image.
                    if (primary || fail != null)
                        fail?.Invoke(error);
                    else
                        receive(GUIUtility.systemCopyBuffer);
                }
            );
        }

        // Server application writes terminate at this desktop. Never POST them back
        // to the daemon.
        public static void CopyFromApplication(string text)
        {
            if (UsesX11)
            {
                X11.Copy(
                    text,
                    false,
                    null,
                    error => Verse.Log.Warning("[SlopWorld] OSC 52 clipboard: " + error)
                );
                return;
            }
            GUIUtility.systemCopyBuffer = text;
        }

        public static void Copy(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (UsesX11)
            {
                // Unity's setter can claim the desktop selection immediately, bypassing
                // queued reads (including their image fallback). X11 owns every Linux write.
                X11.Copy(text, false, ok, fail);
                return;
            }
            GUIUtility.systemCopyBuffer = text;
            ok?.Invoke();
        }

        public static void CopyPrimary(string text, Action ok = null, Action<string> fail = null)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (UsesX11)
            {
                X11.Copy(text, true, ok, fail);
                return;
            }
            ok?.Invoke();
        }
    }

    // Shared fields use the same desktop policy as terminal panes. The UI retains
    // focus and delayed-edit lifetime checks.
    internal sealed class DesktopUiClipboard : IUiClipboard
    {
        public void Copy(string text, bool primary)
        {
            if (primary)
                DesktopClipboard.CopyPrimary(text);
            else
                DesktopClipboard.Copy(text);
        }

        public void Read(bool primary, Action<string> receive)
        {
            if (DesktopClipboard.UsesX11)
            {
                DesktopClipboard.ReadLocal(primary, receive);
                return;
            }
            if (!primary)
                receive(GUIUtility.systemCopyBuffer);
        }
    }
}
