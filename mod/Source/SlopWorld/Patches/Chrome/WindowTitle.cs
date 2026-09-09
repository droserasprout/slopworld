using UnityEngine;

namespace SlopWorld
{
    public static class WindowTitle
    {
        const string NativeTitle = "SlopWorld";
        const string SidecarTitle = "SlopWorld [s]";
        const float RetrySeconds = 1f;

        static string _applied;
        static float _nextTry;

        // The capability arrives in the initial WebSocket snapshot, so the title follows the
        // daemon this profile is actually connected to rather than the endpoint path's name.
        public static void Follow()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer) return;

            string title = SessionHub.Instance.Capabilities.Runtime == "slopcar"
                ? SidecarTitle : NativeTitle;
            if (_applied == title) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextTry) return;
            if (WindowMaximizer.TrySetTitle(title))
            {
                _applied = title;
                return;
            }

            // The player can spend its first frames before the X11 client is mapped.
            _nextTry = now + RetrySeconds;
        }
    }
}
