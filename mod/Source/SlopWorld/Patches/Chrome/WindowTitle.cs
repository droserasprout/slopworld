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

        // Use daemon capabilities from the initial WebSocket snapshot to select the title.
        // The endpoint path does not determine the connected runtime.
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

            // Retry because the X11 client window can be unavailable during startup.
            _nextTry = now + RetrySeconds;
        }
    }
}
