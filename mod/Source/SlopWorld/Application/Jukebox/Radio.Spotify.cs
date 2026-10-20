using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        // Own Spotify selection and reply identity; callers own terminal opening and error presentation.
        // Radio owns source transitions, persistence and the ordered audio connection.
        static bool _openingSpotify;
        static Action<string> _spotifyOpened,
            _spotifyFailed;
        public static bool Spotify => _spotify;
        public const string SpotifySourceId = "spotify";

        // Keep Spotify available until the daemon reports its capabilities.
        public static bool SpotifyAvailable =>
            SessionHub.Instance == null
            || !SessionHub.Instance.Capabilities.Known
            || SessionHub.Instance.Capabilities.Ncspot;

        // Apply the daemon capability snapshot.
        // Replace a saved Spotify selection with OST if the daemon does not support ncspot.
        public static void CapabilitiesChanged()
        {
            if (_quit)
                return;
            Read();
            if (SpotifyAvailable || !_spotify)
                return;
            SelectSource(clearFailure: false);
        }

        public static void OpenSpotify(Action<string> opened, Action<string> failed)
        {
            if (_quit || _openingSpotify)
                return;
            Read();
            if (!SpotifyAvailable)
            {
                failed?.Invoke("ncspot is not available on the daemon host");
                return;
            }
            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online)
            {
                failed?.Invoke("daemon is offline");
                return;
            }
            if (!hub.Capabilities.AudioPlayback)
            {
                failed?.Invoke("ncspot playback requires a native Linux daemon");
                return;
            }

            SelectSource(spotify: true);
            _openingSpotify = true;
            _spotifyOpened = opened;
            _spotifyFailed = failed;
            // Update sends the selection through the same socket that Quit uses to stop playback.
            // The audio reply identifies the terminal session, so no HTTP launch request is necessary.
        }

        // Reject known Spotify reports after a source change, including failures.
        // Unidentified failures still belong to the selected non-Spotify source.
        internal static bool AcceptsReport(string source, string error) =>
            !_quit && (source == "ncspot" ? _spotify : !_spotify || !string.IsNullOrEmpty(error));

        static void ReportSpotify(string error, string source, string session)
        {
            if (_quit || !_openingSpotify || source != "ncspot")
                return;
            var opened = _spotifyOpened;
            var failed = _spotifyFailed;
            if (!string.IsNullOrEmpty(error))
            {
                _openingSpotify = false;
                _spotifyOpened = _spotifyFailed = null;
                failed?.Invoke(error);
                return;
            }
            if (string.IsNullOrEmpty(session))
                return;
            _openingSpotify = false;
            _spotifyOpened = _spotifyFailed = null;
            int revision = _selectionRevision;
            SessionHub.Instance.SessionStore.Refresh(
                () =>
                {
                    if (!_quit && revision == _selectionRevision)
                        opened?.Invoke(session);
                },
                errorMessage =>
                {
                    if (!_quit && revision == _selectionRevision)
                        failed?.Invoke(errorMessage);
                }
            );
        }
    }
}
