namespace SlopWorld
{
    public static partial class Radio
    {
        public static void OpenSpotify()
        {
            if (_quit || _openingSpotify) return;
            Read();
            if (!SpotifyAvailable)
            { UiLayout.Fail("ncspot is not available on the daemon host"); return; }
            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online) { UiLayout.Fail("daemon is offline"); return; }
            if (!hub.Capabilities.AudioPlayback)
            { UiLayout.Fail("ncspot playback requires a native Linux daemon"); return; }

            _spotify = true;
            _station = null;
            _muted = false;
            _blamed = false;
            Save();
            Push();
            _openingSpotify = true;
            // Update sends the selection through the same socket that Quit uses to stop playback.
            // The audio reply identifies the terminal session, so no HTTP launch request is necessary.
        }

        static void ReportSpotify(string error, string source, string session)
        {
            if (_quit || !_openingSpotify || source != "ncspot") return;
            if (!string.IsNullOrEmpty(error))
            {
                _openingSpotify = false;
                UiLayout.Fail(error);
                return;
            }
            if (string.IsNullOrEmpty(session)) return;
            _openingSpotify = false;
            int revision = _selectionRevision;
            SessionHub.Instance.SessionStore.Refresh(() =>
            {
                if (!_quit && revision == _selectionRevision)
                    TerminalWindow.Open(session);
            }, UiLayout.Fail);
        }
    }
}
