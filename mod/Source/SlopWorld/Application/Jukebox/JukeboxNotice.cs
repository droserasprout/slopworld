using System;

namespace SlopWorld
{
    internal enum JukeboxNoticeKind
    {
        Progress,
        Success,
        Error,
        Rejected,
    }

    public static partial class Radio
    {
        // The host installs presentation once; playback and recognition own when notices occur.
        internal static event Action<JukeboxNoticeKind, string> Notice;

        static void Notify(JukeboxNoticeKind kind, string message) => Notice?.Invoke(kind, message);
    }
}
