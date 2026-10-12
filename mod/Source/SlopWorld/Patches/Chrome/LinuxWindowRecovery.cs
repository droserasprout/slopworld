namespace SlopWorld
{
    // Owns process-lifetime recovery policy and timing. LinuxGameWindow samples game
    // state and performs Unity/X11 effects; this owner only consumes their outcomes.
    internal sealed class LinuxWindowRecovery
    {
        internal enum WindowChange { Retry, WaitingForResize, Applied, Unsupported }

        const float RetrySeconds = 1f;
        const float StartupSettleSeconds = 1.5f;
        const float ResyncPollSeconds = 0.05f;
        const float ResyncTimeoutSeconds = 0.5f;
        const float WindowSettleSeconds = 1f;

        internal bool? Applied { get; private set; }
        internal bool Unsupported { get; private set; }
        internal float NextTry { get; private set; }
        internal float ReadySince { get; private set; } = -1f;
        internal float ResyncedAt { get; private set; } = -1f;
        internal bool HasResize => ResyncedAt >= 0f;

        bool _watchWindow = true;
        float _windowQuietSince = -1f;

        internal bool TryBeginAttempt(float now, bool fullscreen, bool loading, out bool watch)
        {
            watch = false;
            if (Unsupported) return false;
            if (Applied == null)
            {
                if (ReadySince < 0f) ReadySince = now;
                if (now - ReadySince < StartupSettleSeconds) return false;
            }
            watch = WatchingWindow(now, loading);
            return (Applied != fullscreen || watch) && now >= NextTry;
        }

        // Completion time is sampled after native work, rather than reusing admission time.
        internal void CompleteAttempt(float now, bool fullscreen, bool watch, WindowChange result)
        {
            if (result == WindowChange.Applied || result == WindowChange.Unsupported) Applied = fullscreen;
            if (result == WindowChange.Unsupported)
            {
                Unsupported = true;
                _watchWindow = false;
            }
            // Sending a request does not confirm recovery. Keep unsuccessful polls
            // from starting the quiet settling period.
            if (result == WindowChange.Retry || result == WindowChange.WaitingForResize)
                _windowQuietSince = -1f;
            NextTry = now + (result == WindowChange.WaitingForResize ||
                (result == WindowChange.Applied && watch) ? ResyncPollSeconds : RetrySeconds);
        }

        internal void WatchWindow()
        {
            // Unsupported integration is terminal, unlike a retired scene watch.
            if (Unsupported) return;
            _watchWindow = true;
            _windowQuietSince = -1f;
            ClearResize();
            RetryNow();
        }

        internal bool WatchingWindow(float now, bool loading)
        {
            if (Unsupported) return false;
            // Arm once: active loading must retain an in-flight resize deadline.
            if (!_watchWindow && loading) WatchWindow();
            if (!_watchWindow) return false;
            if (loading) _windowQuietSince = -1f;
            else if (_windowQuietSince < 0f) _windowQuietSince = now;
            else if (Applied.HasValue && now - _windowQuietSince >= WindowSettleSeconds)
                _watchWindow = false;
            return _watchWindow;
        }

        internal void RetryNow() => NextTry = 0f;
        internal void BeginResize(float now) => ResyncedAt = now;
        internal void ClearResize() => ResyncedAt = -1f;

        // A timeout permits the next stage; it is not confirmation of surface geometry.
        internal bool ResyncLanded(float now, bool surfaceSized) =>
            surfaceSized || now - ResyncedAt >= ResyncTimeoutSeconds;

        internal WindowChange AwaitFullscreenRecovery(bool sent, bool surfaceSized)
        {
            if (!sent) return WindowChange.Retry;
            // ADD completes this attempt, not recovery. An incorrect surface needs
            // a fresh remove/resize/add cycle after the normal retry interval.
            ClearResize();
            return surfaceSized ? WindowChange.WaitingForResize : WindowChange.Retry;
        }
    }
}
