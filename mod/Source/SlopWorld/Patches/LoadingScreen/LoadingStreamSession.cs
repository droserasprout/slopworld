namespace SlopWorld
{
    // Queue gaps and scene handoffs are not interactive boundaries. Only an idle UI frame
    // ends a stream; the next actual loading event then starts a fresh one.
    internal sealed class LoadingStreamSession
    {
        bool active;
        bool fullScreen;
        internal bool FullScreen => fullScreen;

        internal bool DrawFullScreen(bool canUseGameWindow)
        {
            fullScreen |= !canUseGameWindow;
            return fullScreen;
        }

        internal bool Begin(bool hasEvent)
        {
            if (!hasEvent || active)
                return false;
            active = true;
            return true;
        }

        internal void InteractiveFrame(bool eventsPending)
        {
            if (!eventsPending)
            {
                active = false;
                fullScreen = false;
            }
        }
    }
}
