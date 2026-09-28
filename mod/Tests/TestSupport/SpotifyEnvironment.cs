using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        static bool _quit, _muted, _blamed;
        static object _station;
        static int _selectionRevision;
        static void Read() { }
        static void Save() { }
        static void Push() { _selectionRevision++; _openingSpotify = false; }
        internal static void ResetSpotifyTest()
        {
            _quit = _openingSpotify = _spotify = _muted = _blamed = false;
            _station = null;
            _selectionRevision = 0;
            SessionHub.Instance = new SessionHub();
            TerminalWindow.Current = null;
        }
        internal static bool SpotifyRequested => _spotify && !_muted && !_blamed && _station == null;
        internal static void QuitSpotifyTest() { _quit = true; }
        internal static void ReplaceSpotifyTest() { _spotify = false; Push(); }
        internal static void ReportSpotifyTest(string session, string error = null) =>
            ReportSpotify(error, "ncspot", session);
    }

    sealed partial class SessionHub
    {
        public bool Online = true;
        public DaemonCapabilities Capabilities = new DaemonCapabilities();
    }

    sealed partial class PagerTestStore
    {
        public Action Refreshed;
        public void Refresh(Action ok, Action<string> fail) { Refreshed = ok; }
    }
}
