using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace SlopWorld
{
    // Linux selections belong to the game desktop, even when the connected daemon is remote.
    public static class DesktopClipboard
    {
        static readonly MainThreadCompletions Completions = new MainThreadCompletions(
            64,
            System.Diagnostics.Stopwatch.Frequency / 500,
            System.Diagnostics.Stopwatch.GetTimestamp
        );
        internal static LinuxClipboard Selection { get; set; } =
            new(new SystemProcessRunner(), Completions.Enqueue);
        public static bool UsesLinuxSelections => SupportsSelections();

        internal static Func<bool> SupportsSelections { get; set; } =
            () => Application.platform == RuntimePlatform.LinuxPlayer;

        internal static System.Action<
            Action<string>,
            Action<byte[]>,
            Action<string>
        > ReadLocalPasteHandler { get; set; } = DesktopReadLocalPaste;

        public static void ReadLocalPaste(
            Action<string> text,
            Action<byte[]> image,
            Action<string> fail
        ) => ReadLocalPasteHandler(text, image, fail);

        static void DesktopReadLocalPaste(
            Action<string> text,
            Action<byte[]> image,
            Action<string> fail
        )
        {
            if (LinuxGameWindow.WindowReady)
                _ = ReadWaylandPaste(text, image, fail);
            else
                Selection.ReadPaste(text, image, fail);
        }

        // Use the focused game client's Wayland seat. External clipboard clients can
        // acquire focus while Ctrl+V is held, synthesizing another press on return.
        static class NativeClipboard
        {
            internal static readonly PropertyInfo Clipboard = AccessTools.Property(
                LinuxGameWindow.PlatformType,
                "Clipboard"
            );
            internal static readonly PropertyInfo Primary = AccessTools.Property(
                LinuxGameWindow.PlatformType,
                "PrimarySelection"
            );
            static readonly Type Reader = AccessTools.TypeByName("Waylith.Windowing.IDataReader");
            internal static readonly MethodInfo Text = AccessTools.Method(Reader, "ReadTextAsync");
            internal static readonly MethodInfo Contains = AccessTools.Method(Reader, "Contains");
            internal static readonly MethodInfo Read = AccessTools
                .Method(Reader, "ReadAsync")
                .MakeGenericMethod(typeof(byte[]));
            internal static readonly object Png = AccessTools
                .Method(AccessTools.TypeByName("Waylith.Windowing.DataFormat"), "Mime")
                .Invoke(null, new object[] { "image/png" });
            internal static readonly MethodInfo SetText = AccessTools.Method(
                Primary.PropertyType,
                "SetText"
            );
        }

        static async Task ReadWaylandPaste(
            Action<string> text,
            Action<byte[]> image,
            Action<string> fail
        )
        {
            try
            {
                var clipboard = NativeClipboard.Clipboard.GetValue(LinuxGameWindow.Platform);
                var format = new[] { NativeClipboard.Png };
                if (image != null && (bool)NativeClipboard.Contains.Invoke(clipboard, format))
                {
                    var bytes = await (ValueTask<byte[]>)
                        NativeClipboard.Read.Invoke(clipboard, format);
                    if (bytes == null || bytes.Length == 0 || bytes.Length > 20 * 1024 * 1024)
                        throw new InvalidOperationException(
                            "Clipboard PNG must be between 1 byte and 20 MiB."
                        );
                    Completions.Enqueue(() => image(bytes));
                    return;
                }
                string value = await (ValueTask<string>)
                    NativeClipboard.Text.Invoke(clipboard, null);
                string path = image == null ? null : LinuxClipboard.LocalImagePath(value);
                if (path != null)
                {
                    var bytes = await Task.Run(() => LinuxClipboard.ReadImageFile(path));
                    Completions.Enqueue(() => image(bytes));
                }
                else
                    Completions.Enqueue(() => text(value));
            }
            catch (Exception e)
            {
                Completions.Enqueue(() => fail?.Invoke(e.GetBaseException().Message));
            }
        }

        public static void Pump() =>
            Completions.Pump(e => Verse.Log.Error("[SlopWorld] clipboard callback: " + e));

        internal static System.Action<
            bool,
            Action<string>,
            Action<string>
        > ReadLocalHandler { get; set; } = DesktopReadLocal;

        public static void ReadLocal(
            bool primary,
            Action<string> receive,
            Action<string> fail = null
        ) => ReadLocalHandler(primary, receive, fail);

        static void DesktopReadLocal(bool primary, Action<string> receive, Action<string> fail)
        {
            Selection.Read(
                primary,
                receive,
                error =>
                {
                    // An explicit failure handler owns non-text paste behavior; Unity may
                    // still contain text from an earlier copy when the clipboard holds an image.
                    if (primary || fail != null)
                        fail?.Invoke(error);
                    else
                        receive(GUIUtility.systemCopyBuffer);
                }
            );
        }

        // Server application writes terminate at this desktop. Never POST them back
        // to the daemon.
        internal static System.Action<string> CopyFromApplicationHandler { get; set; } =
            DesktopCopyFromApplication;

        public static void CopyFromApplication(string text) => CopyFromApplicationHandler(text);

        static void DesktopCopyFromApplication(string text)
        {
            if (UsesLinuxSelections)
            {
                Selection.Copy(
                    text,
                    false,
                    null,
                    error => Verse.Log.Warning("[SlopWorld] OSC 52 clipboard: " + error)
                );
                return;
            }
            GUIUtility.systemCopyBuffer = text;
        }

        internal static System.Action<string, Action, Action<string>> CopyHandler { get; set; } =
            DesktopCopy;

        public static void Copy(string text, Action ok = null, Action<string> fail = null) =>
            CopyHandler(text, ok, fail);

        static void DesktopCopy(string text, Action ok, Action<string> fail)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (UsesLinuxSelections)
            {
                // Unity's setter can claim the desktop selection immediately, bypassing
                // queued reads (including their image fallback).
                Selection.Copy(text, false, ok, fail);
                return;
            }
            GUIUtility.systemCopyBuffer = text;
            ok?.Invoke();
        }

        internal static System.Action<
            string,
            Action,
            Action<string>
        > CopyPrimaryHandler { get; set; } = DesktopCopyPrimary;

        public static void CopyPrimary(string text, Action ok = null, Action<string> fail = null) =>
            CopyPrimaryHandler(text, ok, fail);

        static void DesktopCopyPrimary(string text, Action ok, Action<string> fail)
        {
            if (string.IsNullOrEmpty(text))
                return;
            if (LinuxGameWindow.WindowReady)
            {
                try
                {
                    var primary = NativeClipboard.Primary.GetValue(LinuxGameWindow.Platform);
                    NativeClipboard.SetText.Invoke(primary, new object[] { text });
                }
                catch (Exception e)
                {
                    fail?.Invoke(e.GetBaseException().Message);
                    return;
                }
                ok?.Invoke();
                return;
            }
            if (UsesLinuxSelections)
            {
                Selection.Copy(text, true, ok, fail);
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
            if (DesktopClipboard.UsesLinuxSelections)
            {
                DesktopClipboard.ReadLocal(primary, receive);
                return;
            }
            if (!primary)
                receive(GUIUtility.systemCopyBuffer);
        }
    }
}
