using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        static bool _quit,
            _blamed;
        static bool _read;
        static int _selectionRevision;

        // Persistence is intentionally omitted; this harness checks Spotify lifecycle only.
        static void Read() { }

        static void Save() { }

        static void Push()
        {
            InvalidatePlayback();
        }

        static string OstPath() => "/test/ost";

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
            _station = null;
            _read = false;
            _stations = Array.Empty<Station>();
            _catalogReady = false;
            _selectionRevision = 0;
            Playback.Invalidate();
            SessionHub.Instance = new SessionHub();
            TerminalWindow.Current = null;
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

        public void Refresh(Action ok, Action<string> fail)
        {
            Refreshed = ok;
            RefreshFailed = fail;
        }
    }
}
