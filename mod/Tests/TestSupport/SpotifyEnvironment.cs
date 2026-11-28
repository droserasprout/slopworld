using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        static readonly RecognitionTrackState RecognitionTrack = new RecognitionTrackState();

        internal static void SelectPlaybackTest(
            Station station = null,
            int rate = 0,
            bool spotify = false
        ) => SelectSource(station, rate, spotify);

        internal static void MutePlaybackTest()
        {
            _muted = true;
            Push();
        }

        internal static void UpdatePlaybackTest(double now, float volume) =>
            UpdatePlayback(now, SessionHub.Instance, volume);

        internal static void ResetSpotifyTest()
        {
            _quit = _openingSpotify = _spotify = _muted = _blamed = false;
            _stopOnExit = _handoffAcknowledged = _sharedActive = false;
            _localStartPending = false;
            _shared = null;
            _rawTitle = _recognizedArtist = _recognizedTitle = null;
            _playing = false;
            _cachedNowPlayingAt = double.NegativeInfinity;
            BrowserJukebox.Current = null;
            _stopToken = _spotifyRequest = null;
            _spotifyOpened = _spotifyFailed = null;
            _station = null;
            _read = false;
            _stations = Array.Empty<Station>();
            _catalogReady = false;
            _selectionRevision = 0;
            Playback.Invalidate();
            SessionHub.Instance = new SessionHub();
            WorkspaceWindow.Current = null;
        }

        internal static bool CatalogReadyTest => _catalogReady;
        internal static bool SpotifyRequested =>
            _spotify && !_muted && !_blamed && _station == null;

        internal static void QuitSpotifyTest()
        {
            _quit = true;
        }

        internal static void ReplaceSpotifyTest()
        {
            _spotify = false;
            Push();
        }

        internal static void ReportSpotifyTest(
            string session,
            string error = null,
            string source = "ncspot"
        ) => ReportSpotify(error, source, session);
    }

    static class NativeJukebox
    {
        internal static void SetMuted(bool muted) { }

        internal static void Navigate(int direction, bool random) { }

        internal static float Volume => 0.5f;

        internal static NativeTrackSnapshot ReadNativeTrack() => null;
    }

    sealed partial class SessionHub
    {
        public AudioBus Audio;
        public bool Online = true;
        public DaemonCapabilities Capabilities = new DaemonCapabilities();
    }

    sealed partial class PagerTestStore
    {
        public Action Refreshed;
        public Action<string> RefreshFailed;

        public void Refresh(Action ok = null, Action<string> fail = null)
        {
            Refreshed = ok;
            RefreshFailed = fail;
        }
    }
}
