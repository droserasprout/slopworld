using RimWorld;
using Verse;

namespace SlopWorld
{
    // Menu actions own confirmation wording; clipboard transport owns completion timing.
    internal static class UiClipboardActions
    {
        public static void Copy(string text, string confirmation) =>
            DesktopClipboard.Copy(
                text,
                () => Messages.Message(confirmation, MessageTypeDefOf.SilentInput, false),
                UiLayout.Fail
            );
    }
}
