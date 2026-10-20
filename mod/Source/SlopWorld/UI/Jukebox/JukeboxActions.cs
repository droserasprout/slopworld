using System;
using RimWorld;
using Verse;

namespace SlopWorld
{
    // UI effects for application jukebox actions. Playback state remains owned by Radio.
    internal static class JukeboxActions
    {
        internal static void Install() => Radio.Notice += ShowNotice;

        static void ShowNotice(JukeboxNoticeKind kind, string message)
        {
            if (kind == JukeboxNoticeKind.Error)
            {
                UiLayout.Fail(message);
                return;
            }
            Messages.Message(
                message,
                kind == JukeboxNoticeKind.Success ? MessageTypeDefOf.TaskCompletion
                    : kind == JukeboxNoticeKind.Rejected ? MessageTypeDefOf.RejectInput
                    : MessageTypeDefOf.NeutralEvent,
                false
            );
        }

        public static void OpenSpotify() =>
            Radio.OpenSpotify(session => WorkspaceWindow.Open(session), UiLayout.Fail);

        public static void Like()
        {
            var record = Radio.CaptureLike(DateTime.UtcNow, out var display);
            if (record == null)
            {
                UiLayout.Fail("nothing is playing");
                return;
            }
            if (!JukeboxLikeWriter.TryAppend(ApplicationPaths.LikesPath, record, out var error))
            {
                Log.Error("[SlopWorld] jukebox: could not save liked song: " + error);
                UiLayout.Fail("could not save liked song");
                return;
            }
            ShowNotice(JukeboxNoticeKind.Success, "Jukebox: liked " + display);
        }
    }
}
