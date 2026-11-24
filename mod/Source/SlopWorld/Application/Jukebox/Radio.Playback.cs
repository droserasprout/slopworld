namespace SlopWorld
{
    public static partial class Radio
    {
        // Platform initialization enables this when a local OST transport exists
        // (native music manager or browser decoder), before the first workspace frame.
        public static bool LocalPlaybackSupported { get; set; } = true;
        public static bool TransportAvailable =>
            BrowserJukebox.Available
                ? (ServerOutput ? SharedReady : !_muted || LocalOst || RelayAvailable)
                : (!LocalOst && SessionHub.Instance.Capabilities.AudioPlayback)
                    || (LocalOst && LocalPlaybackSupported);

        // Resolve the selected source and dispatch cadence decisions through the audio transport.
        // NativeJukebox adapts game music; Radio owns metadata.
        static readonly RadioPlayback Playback = new RadioPlayback();

        // A unique stop acknowledgment prevents delayed reports from starting local
        // music after a newer source selection or connection has taken ownership.
        static string _stopToken;
        static bool _handoffAcknowledged;
        static bool _sharedActive;
        static bool _stopOnExit;

        static bool LocalOst => !_spotify && _station == null;

        internal static bool NativePlaybackAllowed =>
            !BrowserJukebox.Available
            && LocalPlaybackSupported
            && !_quit
            && !_muted
            && LocalOst
            && (_catalogReady || string.IsNullOrEmpty(Settings.Radio) || Settings.Radio == "ost")
            && (
                SessionHub.Instance?.Capabilities.Known == true
                && (
                    !SessionHub.Instance.Capabilities.AudioPlayback
                    || (_handoffAcknowledged && !_sharedActive)
                )
            );

        internal static void AcknowledgeStop(string token, bool playing = false)
        {
            // A later shared command temporarily owns the speakers. The completed
            // handoff remains valid when that player goes idle again.
            _sharedActive = playing;
            if (playing)
                NativeJukebox.SetMuted(true);
            if (token != null && token == _stopToken)
                _handoffAcknowledged = true;
        }

        // Apply the exit preference before Root.Shutdown closes the socket. Forced process termination bypasses this method.
        public static void Quit()
        {
            if (_quit)
                return;
            Read();
            // Prevent later frames from sending further playback updates.
            _quit = true;
            // Keep muted playback stopped even if the exit preference permits continued playback.
            if (BrowserJukebox.Available)
            {
                BrowserJukebox.Current.Stop();
                if (ServerOutput && _stopOnExit && ServerAvailable)
                    FlushStop(null);
                return;
            }
            if (!_stopOnExit && !_muted)
                return;

            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online)
                return;
            if (!hub.Capabilities.AudioPlayback)
                return;
            FlushStop(Volume());
        }

        static void FlushStop(float? volume)
        {
            if (!SessionHub.Instance.Audio.StopForShutdown(volume))
                Verse.Log.Warning("[SlopWorld] radio stop could not be flushed before shutdown");
        }

        static void InvalidatePlayback()
        {
            _handoffAcknowledged = false;
            _stopToken = null;
            NativeJukebox.SetMuted(true);
            InvalidateSpotifyOpen();
            Playback.Invalidate();
        }

        static void UpdatePlayback(double now, SessionHub hub, float volume)
        {
            if (!hub.Online)
            {
                _handoffAcknowledged = false;
                _stopToken = null;
            }
            // A saved station has no object until its catalog arrives. Do not send
            // an OST stop in that gap: the daemon may still be playing this station.
            if (
                !_catalogReady
                && !_muted
                && !_spotify
                && _station == null
                && !string.IsNullOrEmpty(Settings.Radio)
                && Settings.Radio != "ost"
                && Settings.Radio != "ncspot"
            )
                return;
            // A client without local audio starts as an observer. Its unavailable
            // default OST must not stop a host stream merely by opening a tab.
            // Explicit station pause still sends Stop because it retains _station.
            if (!LocalPlaybackSupported && LocalOst)
                return;
            switch (Playback.Step(now, hub.Online, PlaybackPlan, volume, out var plan))
            {
                case RadioPlayback.UpdateKind.Selection:
                    SendSelection(hub, volume, plan);
                    break;
                case RadioPlayback.UpdateKind.Volume:
                    if (!LocalOst && !_muted)
                        hub.Audio.SendVolume(volume);
                    break;
            }
        }

        static RadioPlaybackPlan PlaybackPlan()
        {
            if (_muted || LocalOst)
                return RadioPlaybackPlan.Stop;
            if (_spotify)
                return RadioPlaybackPlan.Spotify;
            if (_station != null)
                return RadioPlaybackPlan.Station(_station.Id, _station.Path(_station.Rate));
            return RadioPlaybackPlan.Stop;
        }

        static void SendSelection(SessionHub hub, float volume, RadioPlaybackPlan plan)
        {
            switch (plan.Kind)
            {
                case RadioPlaybackPlan.SourceKind.Stop:
                    _handoffAcknowledged = false;
                    _stopToken = System.Guid.NewGuid().ToString("N");
                    hub.Audio.Stop(volume, _stopToken);
                    break;
                case RadioPlaybackPlan.SourceKind.Spotify:
                    hub.Audio.SendSpotify(volume);
                    break;
                case RadioPlaybackPlan.SourceKind.Station:
                    hub.Audio.PlayStation(plan.Id, plan.Path, volume);
                    break;
            }
        }
    }
}
