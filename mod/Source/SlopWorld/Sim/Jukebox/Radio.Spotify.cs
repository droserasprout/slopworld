namespace SlopWorld
{
    public static partial class Radio
    {
        // Spotify selection, capability fallback and terminal opening live here.
        // Radio owns source transitions, persistence and the ordered audio connection.
        static bool _openingSpotify;
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

        public static void OpenSpotify()
        {
            if (_quit || _openingSpotify)
                return;
            Read();
            if (!SpotifyAvailable)
            {
                UiLayout.Fail("ncspot is not available on the daemon host");
                return;
            }
            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online)
            {
                UiLayout.Fail("daemon is offline");
                return;
            }
            if (!hub.Capabilities.AudioPlayback)
            {
                UiLayout.Fail("ncspot playback requires a native Linux daemon");
                return;
            }

            SelectSource(spotify: true);
            _openingSpotify = true;
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
            if (!string.IsNullOrEmpty(error))
            {
                _openingSpotify = false;
                UiLayout.Fail(error);
                return;
            }
            if (string.IsNullOrEmpty(session))
                return;
            _openingSpotify = false;
            int revision = _selectionRevision;
            SessionHub.Instance.SessionStore.Refresh(
                () =>
                {
                    if (!_quit && revision == _selectionRevision)
                        WorkspaceWindow.Open(session);
                },
                errorMessage =>
                {
                    if (!_quit && revision == _selectionRevision)
                        UiLayout.Fail(errorMessage);
                }
            );
        }
    }
}
