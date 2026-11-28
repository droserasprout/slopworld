using System;

namespace SlopWorld
{
    public static partial class Radio
    {
        // The snapshot belongs to the connected daemon. Only local selection and gain
        // live in the profile. The active source fields project whichever player is shown.
        static Wire.AudioState _shared;
        static bool _localStartPending;

        internal static bool ServerOutput =>
            BrowserJukebox.Available && Settings.S.radioOutput == "server";
        internal static bool ServerAvailable =>
            SessionHub.Instance?.Online == true
            && SessionHub.Instance.Capabilities.Known
            && SessionHub.Instance.Capabilities.AudioPlayback
            && SessionHub.Instance.Capabilities.SharedAudioTransport;
        internal static bool SharedReady => ServerAvailable && _shared != null;
        internal static bool RelayAvailable =>
            SessionHub.Instance?.Online == true
            && SessionHub.Instance.Capabilities.Known
            && SessionHub.Instance.Capabilities.RadioRelay;

        internal static string ServerUnavailable =>
            SessionHub.Instance?.Online != true ? "daemon is offline"
            : !SessionHub.Instance.Capabilities.Known ? "waiting for daemon capabilities"
            : !SessionHub.Instance.Capabilities.AudioPlayback ? "host audio output is unavailable"
            : !SessionHub.Instance.Capabilities.SharedAudioTransport
                ? "update the daemon to enable shared audio controls"
            : _shared == null ? "waiting for shared player"
            : null;

        static bool HasSavedLocalStation =>
            !string.IsNullOrEmpty(Settings.Radio)
            && Settings.Radio != OstSourceId
            && Settings.Radio != "ncspot";

        internal static string PlaybackMessage =>
            ServerOutput ? ServerUnavailable ?? _shared?.Error
            : BrowserJukebox.Available && _catalogReady && HasSavedLocalStation && _station == null
                ? "The selected radio stream is no longer in the catalog."
            : BrowserJukebox.Available ? BrowserJukebox.Current.Error
            : null;

        static string SharedStationId =>
            ServerOutput && _shared?.Selection?.HasStation == true
                ? _shared.Selection.Station
                : null;

        internal static bool OstSelected =>
            ServerOutput
                ? _shared?.Selection?.HasTrack == true
                : LocalOst && (!BrowserJukebox.Available || !HasSavedLocalStation);

        internal static string SharedSelectionLabel =>
            !ServerOutput || _shared?.Selection == null ? null
            : _shared.Selection.HasTrack
                ? NativeTrackSnapshot.FromOstClipPath(_shared.Selection.Track)?.Display
            : _shared.Selection.Ncspot ? "Spotify"
            : _station?.Name ?? _shared.Selection.Station;

        internal static string SharedPlaybackLabel
        {
            get
            {
                string selection = SharedSelectionLabel;
                if (selection == null)
                    return null;
                string status =
                    _muted ? "Paused"
                    : _playing ? "Playing"
                    : _spotify ? "Player open"
                    : "Starting";
                return status + " · " + selection;
            }
        }

        internal static float OutputVolume =>
            ServerOutput ? _shared?.Volume ?? 0f : Settings.S.radioVolume;

        internal static void SetOutputVolume(float value)
        {
            if (_quit)
                return;
            value = Math.Clamp(value, 0f, 1f);
            if (ServerOutput)
            {
                if (SharedReady)
                    SessionHub.Instance.Audio.SendVolume(value);
            }
            else
            {
                Settings.S.radioVolume = value;
                Settings.S.Write();
                ApplyLocalVolume();
            }
        }

        static void ApplyLocalVolume() =>
            BrowserJukebox.Current.SetVolume(
                Math.Clamp(Settings.S.radioVolume * Verse.Prefs.VolumeMaster, 0f, 1f)
            );

        static void ReadBrowserPreferences()
        {
            if (ServerOutput)
                ApplySharedSnapshot();
            else
            {
                _station = FindSelection(Settings.Radio);
                _muted = Settings.RadioMute || Settings.Radio == "ncspot";
                _localStartPending = !_muted;
            }
        }

        internal static void SelectOutput(bool server)
        {
            Read();
            if (_quit || !BrowserJukebox.Available)
                return;
            if (server && !ServerAvailable)
            {
                Notify(JukeboxNoticeKind.Rejected, ServerUnavailable);
                return;
            }
            InvalidateSpotifyOpen();
            if (server != ServerOutput)
                ChangeOutput(server);
        }

        static void ChangeOutput(bool server)
        {
            BrowserJukebox.Current.Stop();
            _localStartPending = false;
            Settings.S.radioOutput = server ? "server" : "local";
            if (server)
                ApplySharedSnapshot();
            else
            {
                _spotify = false;
                _station = FindSelection(Settings.Radio);
                _muted = Settings.S.radioMute = true;
                ObserveTrack(false, null);
                ApplyLocalVolume();
            }
            Settings.S.Write();
        }

        // Connection establishment and reports only publish state, including when local
        // playback continues across reconnect. No shared command is queued for later.
        internal static void ResetSharedSnapshot()
        {
            if (!BrowserJukebox.Available)
                return;
            _shared = null;
            var failed = _spotifyFailed;
            InvalidateSpotifyOpen();
            if (ServerOutput && _read)
                ApplySharedSnapshot();
            if (!_quit)
                failed?.Invoke("The daemon disconnected before Spotify could open.");
        }

        internal static void ReceiveAudio(Wire.AudioState state)
        {
            if (_quit)
                return;
            if (!BrowserJukebox.Available)
            {
                AcknowledgeStop(state.StopAck, state.Playing || state.Active);
                Report(state.Playing, state.Error, state.Title, state.Source, state.Session);
                return;
            }
            if (state.HasRequestId)
            {
                SettleBrowserSpotify(state);
                return;
            }

            bool replacedSpotify =
                _shared?.Selection?.Ncspot == true && state.Selection?.Ncspot != true;
            _shared = state;
            if (ServerOutput)
            {
                if (replacedSpotify && !_openingSpotify)
                    InvalidateSpotifyOpen();
                ApplySharedSnapshot();
            }
        }

        static void ApplySharedSnapshot()
        {
            var selection = _shared?.Selection;
            _station =
                selection?.HasStation == true
                    ? FindSelection(selection.Station + ":" + selection.Stream)
                    : null;
            _spotify = selection?.Ncspot == true;
            _muted = _shared?.Active != true && _shared?.Playing != true;
            var title =
                selection?.HasTrack == true
                    ? NativeTrackSnapshot.FromOstClipPath(selection.Track)?.Title
                    : _shared?.Title;
            ObserveTrack(_shared?.Playing == true, title);
        }

        static void RestoreBrowserCatalog()
        {
            if (!_read)
                return;
            if (ServerOutput)
                ApplySharedSnapshot();
            else
            {
                var previous = _station;
                _station = FindSelection(Settings.Radio);
                if (previous != null && _station == null)
                    PauseLocal();
            }
        }

        static void UpdateBrowserPlayback()
        {
            if (SessionHub.Instance?.Online != true && (_shared != null || _openingSpotify))
                ResetSharedSnapshot();
            if (ServerOutput)
                return;
            ApplyLocalVolume();
            if (_localStartPending)
            {
                if (
                    HasSavedLocalStation
                    && (!_catalogReady || SessionHub.Instance?.Capabilities.Known != true)
                )
                    return;
                _localStartPending = false;
                if (HasSavedLocalStation && (_station == null || !RelayAvailable))
                {
                    _muted = true;
                    Notify(
                        JukeboxNoticeKind.Rejected,
                        "The saved radio stream is unavailable on this device."
                    );
                }
                else
                    StartLocal();
            }
            ObserveTrack(BrowserJukebox.Current.Playing, BrowserJukebox.Current.Title);
        }

        static void StartLocal()
        {
            _localStartPending = false;
            ApplyLocalVolume();
            if (_station == null)
                BrowserJukebox.Current.PlayOst();
            else
                BrowserJukebox.Current.PlayRadio(
                    _station.Id,
                    _station.StreamsStartingWith(_station.Rate)
                );
        }

        static void PauseLocal()
        {
            InvalidateSpotifyOpen();
            _localStartPending = false;
            BrowserJukebox.Current.Stop();
            _muted = Settings.S.radioMute = true;
            ObserveTrack(false, null);
            Settings.S.Write();
        }

        static void PickBrowserSource(Station station, int rate)
        {
            if (_quit)
                return;
            if (ServerOutput)
            {
                if (!ServerAvailable)
                {
                    Notify(JukeboxNoticeKind.Rejected, ServerUnavailable);
                    return;
                }
                InvalidateSpotifyOpen();
                if (station == null)
                    SessionHub.Instance.Audio.SendTransport("ost");
                else
                    SessionHub.Instance.Audio.PlayStation(station.Id, station.Path(rate));
                return;
            }
            if (station != null && !RelayAvailable)
            {
                Notify(JukeboxNoticeKind.Rejected, "Radio relay is unavailable on this daemon.");
                return;
            }
            InvalidateSpotifyOpen();
            _station = station;
            if (station != null)
                station.Rate = rate;
            _muted = Settings.S.radioMute = false;
            Settings.S.radio = station?.SelectionKey(rate) ?? OstSourceId;
            Settings.S.Write();
            ObserveTrack(false, null);
            StartLocal();
        }

        static void ToggleBrowserPlayback()
        {
            if (ServerOutput)
            {
                InvalidateSpotifyOpen();
                if (SharedReady)
                    SessionHub.Instance.Audio.SendTransport(_muted ? "play" : "pause");
            }
            else if (_muted || !string.IsNullOrEmpty(BrowserJukebox.Current.Error))
            {
                if (HasSavedLocalStation && _station == null)
                    Notify(
                        JukeboxNoticeKind.Rejected,
                        "The selected radio stream is unavailable. Choose a source in Tune."
                    );
                else
                    PickBrowserSource(_station, _station?.Rate ?? 0);
            }
            else
                PauseLocal();
        }
    }
}
