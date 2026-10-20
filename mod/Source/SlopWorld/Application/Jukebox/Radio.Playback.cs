namespace SlopWorld
{
    public static partial class Radio
    {
        // Resolve the selected source and dispatch cadence decisions through the audio transport.
        // NativeJukebox adapts game music; Radio owns metadata.
        static readonly RadioPlayback Playback = new RadioPlayback();

        static void InvalidatePlayback()
        {
            _selectionRevision++;
            _openingSpotify = false;
            _spotifyOpened = _spotifyFailed = null;
            Playback.Invalidate();
        }

        static void UpdatePlayback(double now, SessionHub hub, float volume)
        {
            switch (Playback.Step(now, hub.Online, PlaybackPlan, volume, out var plan))
            {
                case RadioPlayback.UpdateKind.Selection:
                    SendSelection(hub, volume, plan);
                    break;
                case RadioPlayback.UpdateKind.Volume:
                    hub.Audio.SendVolume(volume);
                    break;
            }
        }

        static RadioPlaybackPlan PlaybackPlan()
        {
            if (_muted)
                return RadioPlaybackPlan.Stop;
            if (_spotify)
                return RadioPlaybackPlan.Spotify;
            if (_station != null)
                return RadioPlaybackPlan.Station(_station.Id, _station.Path(_station.Rate));
            if (!_catalogReady && IsSavedStation())
                return RadioPlaybackPlan.Stop;
            return RadioPlaybackPlan.File(OstPath());
        }

        static bool IsSavedStation() =>
            !string.IsNullOrEmpty(Settings.Radio)
            && Settings.Radio != "ost"
            && Settings.Radio != "ncspot";

        static void SendSelection(SessionHub hub, float volume, RadioPlaybackPlan plan)
        {
            switch (plan.Kind)
            {
                case RadioPlaybackPlan.SourceKind.Stop:
                    hub.Audio.Stop(volume);
                    break;
                case RadioPlaybackPlan.SourceKind.Spotify:
                    hub.Audio.SendSpotify(volume);
                    break;
                case RadioPlaybackPlan.SourceKind.Station:
                    hub.Audio.PlayStation(plan.Id, plan.Path, volume);
                    break;
                case RadioPlaybackPlan.SourceKind.File:
                    hub.Audio.PlayFile(plan.Path, volume);
                    break;
            }
        }
    }
}
