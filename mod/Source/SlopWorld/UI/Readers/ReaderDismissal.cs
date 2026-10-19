namespace SlopWorld
{
    // Dismiss the UI owner before falling back to an adopted daemon reader. Stopping
    // only its process would leave a pending handoff able to publish a replacement.
    internal static class ReaderDismissal
    {
        internal static bool CanClose(SessionInfo info) =>
            info != null
            && info.Ephemeral
            && (SessionRowAction.Of(info) & (RowAct.View | RowAct.Diff)) != 0;

        internal static bool Close(string session, IPreviewTab owner, SessionInfo info)
        {
            if (session == null)
                return false;
            if (owner != null)
            {
                if (!owner.CloseTab(session))
                    return false;
            }
            else
            {
                // Editors retain their separate, confirmed Stop action.
                if (!CanClose(info))
                    return false;
                SessionHub.Instance.SessionStore.Stop(session);
            }
            Pager.SessionVisibility.Retire(session);
            return true;
        }
    }
}
