namespace RimWorld
{
    enum MessageTypeDefOf
    {
        TaskCompletion,
        RejectInput,
        NeutralEvent,
    }
}

namespace Verse
{
    static class Messages
    {
        internal static string Last;

        public static void Message(string text, RimWorld.MessageTypeDefOf type, bool historical) =>
            Last = text;
    }
}

namespace SlopWorld
{
    internal sealed class JukeboxLikesEditor(string original)
    {
        internal string Original => original;
    }

    partial class WorkspaceWindow
    {
        internal static JukeboxLikesEditor LikesEditor;

        internal static void OpenOverPane(JukeboxLikesEditor editor) => LikesEditor = editor;
    }
}

namespace UnityEngine
{
    public static partial class Application
    {
        internal static string OpenedUrl;

        public static void OpenURL(string url) => OpenedUrl = url;
    }
}
