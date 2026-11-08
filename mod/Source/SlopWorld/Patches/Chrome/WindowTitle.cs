using UnityEngine;

namespace SlopWorld
{
    public static class WindowTitle
    {
        const float RetrySeconds = 1f;

        static string _applied;
        static float _nextTry;

        internal static void Startup() => Apply("SlopWorld");

        // Follow the connected daemon's hostname as health information becomes available.
        public static void Follow()
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer)
                return;

            string hostname = SessionHub.Instance.Health.Hostname;
            Apply(
                string.IsNullOrEmpty(hostname) || hostname == "?"
                    ? "SlopWorld"
                    : "SlopWorld - " + hostname
            );
        }

        static void Apply(string title)
        {
            if (Application.platform != RuntimePlatform.LinuxPlayer)
                return;
            if (_applied == title)
                return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextTry)
                return;
            if (LinuxGameWindow.TrySetTitle(title))
            {
                _applied = title;
                return;
            }

            // Retry because the X11 client window can be unavailable during startup.
            _nextTry = now + RetrySeconds;
        }
    }
}
