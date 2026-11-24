using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        // Own Spotify selection and reply identity; callers own terminal opening and error presentation.
        // Radio owns source transitions, persistence and the ordered audio connection.
        static bool _openingSpotify;
        static string _spotifyRequest;
        static Action<string> _spotifyOpened,
            _spotifyFailed;
        public static bool Spotify => _spotify;
        public const string SpotifySourceId = "spotify";

        // Keep Spotify available until the daemon reports its capabilities.
        public static bool SpotifyAvailable =>
            BrowserJukebox.Available
                ? ServerAvailable && SessionHub.Instance.Capabilities.Ncspot
                : SessionHub.Instance == null
                    || !SessionHub.Instance.Capabilities.Known
                    || SessionHub.Instance.Capabilities.Ncspot;

        // Apply the daemon capability snapshot.
        // Replace a saved Spotify selection with OST if the daemon does not support ncspot.
        public static void CapabilitiesChanged()
        {
            if (_quit || BrowserJukebox.Available)
                return;
            Read();
            if (DaemonAudioUnavailable())
            {
                SelectSource(userSelected: false);
                return;
            }
            if (SpotifyAvailable || !_spotify)
                return;
            SelectSource(userSelected: false);
        }

        static bool DaemonAudioUnavailable() =>
            SessionHub.Instance?.Capabilities.Known == true
            && !SessionHub.Instance.Capabilities.AudioPlayback
            && (
                _spotify
                || _station != null
                || (!string.IsNullOrEmpty(Settings.Radio) && Settings.Radio != "ost")
            );

        public static void OpenSpotify(Action<string> opened, Action<string> failed)
        {
            if (_quit || _openingSpotify)
                return;
            Read();
            if (BrowserJukebox.Available && !ServerAvailable)
            {
                failed?.Invoke(ServerUnavailable);
                return;
            }
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

            if (BrowserJukebox.Available)
            {
                InvalidateSpotifyOpen();
                string request = Guid.NewGuid().ToString("N");
                if (!hub.Audio.SendSpotify(requestId: request))
                {
                    failed?.Invoke("Could not send the Spotify request to the daemon.");
                    return;
                }
                _spotifyRequest = request;
            }
            else
                SelectSource(spotify: true);
            _openingSpotify = true;
            _spotifyOpened = opened;
            _spotifyFailed = failed;
            // The daemon reply identifies the terminal to open after the session refresh.
        }

        // Only the direct response to this tab's explicit request can switch its
        // output. Broadcasts and earlier launches may report the same terminal.
        static void SettleBrowserSpotify(Wire.AudioState reply)
        {
            if (reply.RequestId != _spotifyRequest)
                return;
            _spotifyRequest = null;
            string error = reply.Error;
            if (
                string.IsNullOrEmpty(error)
                && (reply.Source != "ncspot" || string.IsNullOrEmpty(reply.Session))
            )
                error = "The daemon did not open a Spotify terminal.";
            if (string.IsNullOrEmpty(error))
            {
                _shared = reply;
                if (ServerOutput)
                    ApplySharedSnapshot();
                else
                    ChangeOutput(true);
            }
            ReportSpotify(error, "ncspot", reply.Session);
        }

        static void InvalidateSpotifyOpen()
        {
            _selectionRevision++;
            _spotifyRequest = null;
            _openingSpotify = false;
            _spotifyOpened = _spotifyFailed = null;
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
