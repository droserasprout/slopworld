using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The mod selects a station key. slopd owns the catalog and opens stream URLs.
    // Unity and FMOD do not reliably support these HTTPS Icecast streams.
    // Save station selection in user settings so it persists across colonies.
    public static partial class Radio
    {
        // The daemon shuffles OST files. The mod selects the directory as one source.
        static readonly System.Random Dice = new System.Random();

        static string OstPath() => NativeJukebox.OstPath();

        static int _selectionRevision;
        public const string OstSourceId = "ost";

        static bool _stopOnExit;

        // Read settings once after initialization. They are unavailable when this class first loads.
        static bool _read;

        // Report a station failure once per selection, then return to the OST.
        static bool _blamed;

        // Prevent Update from restarting playback after Quit.
        // Root.Shutdown can return before the process exits because saving and application shutdown continue across frames.
        static bool _quit;

        // Retain the raw daemon title separately from recognized values and formatted titles for the like history.
        static string _rawTitle;
        static bool _playing;
        static string _recognizedArtist;
        static string _recognizedTitle;
        static string _cachedNowPlaying;
        static double _cachedNowPlayingAt = double.NegativeInfinity;
        static int _cachedNowPlayingVersion = -1;

        // Use bundled SongDefs when the daemon cannot play audio or open the OST.
        // Until capabilities arrive, the default permits daemon playback.
        static bool NativeAudio =>
            LocalOst
            || (SessionHub.Instance != null && !SessionHub.Instance.Capabilities.AudioPlayback);

        internal static Func<NativeTrackSnapshot> NativeTrackProvider =
            NativeJukebox.ReadNativeTrack;

        // Read metadata from the current native SongDef during sidecar playback.
        static string NativeNowPlaying()
        {
            return SampleNativeTrack()?.Display;
        }

        static NativeTrackSnapshot SampleNativeTrack()
        {
            try
            {
                return NativeTrackProvider();
            }
            catch
            {
                return null;
            }
        }

        // Return the selected radio station. OST and Spotify selections have no station object.
        public static Station Picked
        {
            get
            {
                Read();
                return _station;
            }
        }

        // Stop daemon playback when muted to avoid downloading an inaudible stream.
        // For sidecar playback, mute the native music manager. RemoveVanillaSongs.xml limits its songs to the SlopWorld OST.
        public static bool Muted
        {
            get
            {
                Read();
                return _muted;
            }
        }

        // Control whether the daemon stops playback when the game exits. The daemon can continue after the game closes.
        public static bool StopOnExit
        {
            get
            {
                Read();
                return _stopOnExit;
            }
        }

        public static bool SourceShown(string id)
        {
            Read();
            if (string.IsNullOrEmpty(id))
                return false;
            foreach (string hidden in HiddenSources())
                if (hidden == id)
                    return false;
            return true;
        }

        public static void SetSourceShown(string id, bool shown)
        {
            Read();
            if (string.IsNullOrEmpty(id))
                return;
            var hidden = HiddenSources();
            hidden.Remove(id);
            if (!shown)
                hidden.Add(id);
            Settings.S.radioHiddenSources = string.Join("\n", hidden);
            Settings.S.Write();
        }

        static List<string> HiddenSources()
        {
            var result = new List<string>();
            string text = Settings.S.radioHiddenSources ?? "";
            foreach (
                string line in text.Split(
                    new[] { '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
            )
            {
                string id = line.Trim();
                if (id.Length > 0 && !result.Contains(id))
                    result.Add(id);
            }
            return result;
        }

        // Return the current track label, or null if playback is muted or unavailable.
        // Prefer recognized artist and title values when available. Retain the raw station title for Like.
        public static string NowPlaying
        {
            get
            {
                Read();
                if (_muted)
                    return null;
                if (NativeAudio)
                    return NativeNowPlaying();
                if (!_playing)
                    return null;
                if (HasRecognition())
                    return _recognizedArtist + " - " + _recognizedTitle;
                return StationNowPlaying();
            }
        }

        // Cache the track label because IMGUI can draw a tooltip repeatedly during one event.
        // Refresh at the regular update interval or when the track revision changes.
        public static string CachedNowPlaying
        {
            get
            {
                Read();
                double now = Time.realtimeSinceStartupAsDouble;
                if (
                    _cachedNowPlayingVersion != RecognitionTrack.Revision
                    || now - _cachedNowPlayingAt >= UpdateInterval
                )
                {
                    _cachedNowPlaying = NowPlaying;
                    _cachedNowPlayingVersion = RecognitionTrack.Revision;
                    _cachedNowPlayingAt = now;
                }
                return _cachedNowPlaying;
            }
        }

        // Format station metadata without recognition results so the UI can show both sources.
        static string StationNowPlaying()
        {
            if (_spotify)
                return _rawTitle;
            return _station != null ? FormatTitle(_station, _rawTitle)
                : string.IsNullOrEmpty(_rawTitle) ? "Terry Fail - OST"
                : "Terry Fail - " + _rawTitle;
        }

        public static string Artist
        {
            get
            {
                Read();
                string artist,
                    title;
                CurrentParts(out artist, out title);
                return artist;
            }
        }

        public static string Title
        {
            get
            {
                Read();
                string artist,
                    title;
                CurrentParts(out artist, out title);
                return title;
            }
        }

        public static string Source
        {
            get
            {
                Read();
                return SourceLabel();
            }
        }

        // Expose recognized metadata separately so the UI can identify its source.
        public static bool Recognized
        {
            get
            {
                Read();
                return HasRecognition();
            }
        }

        public static string RecognizedLine
        {
            get
            {
                Read();
                return HasRecognition() ? _recognizedArtist + " - " + _recognizedTitle : null;
            }
        }

        public static string StationLine
        {
            get
            {
                Read();
                return _muted || NativeAudio ? null : StationNowPlaying();
            }
        }

        // Capture once so confirmation and persistence identify the same track.
        internal static JukeboxLikeRecord CaptureLike(DateTime now, out string display)
        {
            display = null;
            if (NativeAudio)
            {
                var track = SampleNativeTrack();
                if (track == null || !track.IsUsable)
                    return null;
                display = track.Display;
                return new JukeboxLikeRecord(
                    now,
                    track.Source,
                    track.Artist,
                    track.Title,
                    track.Artist,
                    track.Title
                );
            }
            display = NowPlaying;
            if (string.IsNullOrEmpty(display))
                return null;
            CurrentParts(out var artist, out var title);
            return new JukeboxLikeRecord(
                now,
                SourceLabel(),
                artist,
                title,
                OriginalArtist(),
                _rawTitle
            );
        }

        static bool HasRecognition() =>
            !string.IsNullOrEmpty(_recognizedArtist) && !string.IsNullOrEmpty(_recognizedTitle);

        static string SourceLabel() =>
            _spotify ? "Spotify"
            : _station == null ? "SlopWorld OST"
            : _station.Name;

        static string OriginalArtist()
        {
            string artist;
            string title;
            return _station != null && _station.TryTitleParts(_rawTitle, out artist, out title)
                ? artist
                : "";
        }

        static void CurrentParts(out string artist, out string title)
        {
            if (_spotify)
            {
                artist = "";
                title = _rawTitle;
                return;
            }
            if (HasRecognition())
            {
                artist = _recognizedArtist;
                title = _recognizedTitle;
                return;
            }

            if (_station != null)
            {
                if (_station.TryTitleParts(_rawTitle, out artist, out title))
                    return;
                artist = "";
                title = _rawTitle;
                return;
            }

            artist = "Terry Fail";
            title = string.IsNullOrEmpty(_rawTitle) ? "OST" : _rawTitle;
        }

        // Use metadata.title_regex with named artist and title groups for station-specific formatting.
        // Retain unmatched titles for diagnosis.
        static string FormatTitle(Station station, string title)
        {
            return station == null ? title : station.FormatTitle(title);
        }

        public static string RateLabel(int rate) => rate + "k mp3";

        public static void PickOst()
        {
            Read();
            SelectSource();
        }

        // Select OST or a radio station at random. Select the station rate separately.
        // Pick handles unmuting, saved settings, and the pending daemon update.
        public static void PickRandom()
        {
            Read();

            var candidates = new List<Station>();
            foreach (var candidate in Stations)
                if (SourceShown(candidate.Id) && (_muted || candidate != _station))
                    candidates.Add(candidate);

            // Exclude OST if it is already playing.
            bool ostCurrent = !_spotify && !_muted && _station == null;
            bool canPickOst = SourceShown(OstSourceId) && !ostCurrent;
            int count = candidates.Count + (canPickOst ? 1 : 0);
            if (count == 0)
                return;

            if (canPickOst && Dice.Next(count) == 0)
            {
                Pick(null, 0);
                return;
            }

            var station = candidates[Dice.Next(candidates.Count)];
            Pick(station, station.Rates[Dice.Next(station.Rates.Length)]);
        }

        public static void ToggleMute()
        {
            Read();
            if (NativeAudio)
            {
                _muted = !_muted;
                if (_muted)
                    NativeJukebox.SetMuted(true);
                else
                    NativeJukebox.Resume();
                Save();
                return;
            }

            _muted = !_muted;
            _blamed = false;
            Save();
            Push();
        }

        // Save the preference now. Apply it when the game exits.
        public static void ToggleStopOnExit()
        {
            Read();
            _stopOnExit = !_stopOnExit;
            Save();
        }

        // Select a station and rate. A null station selects OST and ignores the rate.
        public static void Pick(Station s, int rate)
        {
            Read();
            // Selecting a source also unmutes playback. Skip only an identical selection that is already unmuted.
            if (!_spotify && _station == s && (s == null || s.Rate == rate) && !_muted)
                return;

            SelectSource(s, rate);
        }

        // Patch_Root_Update calls Update every frame in menus and during play.
        // Send pending selections immediately. Limit regular checks to six per second.
        // Read settings before these checks.
        const double UpdateInterval = RadioPlayback.UpdateInterval;

        public static void Update()
        {
            if (_quit)
                return;
            Read();
            var hub = SessionHub.Instance;
            if (hub == null)
                return;
            // On macOS, wait for daemon capabilities before disabling native music.
            if (!hub.Capabilities.Known && Application.platform == RuntimePlatform.OSXPlayer)
                return;
            if (NativeAudio)
            {
                // Stop the failed daemon source once before running local music.
                if (hub.Capabilities.AudioPlayback)
                    UpdatePlayback(Time.realtimeSinceStartupAsDouble, hub, Volume());
                NativeJukebox.SetMuted(_muted);
                return;
            }

            NativeJukebox.DisableForDaemon();

            UpdatePlayback(Time.realtimeSinceStartupAsDouble, hub, Volume());
        }

        // Apply both music and master volume because daemon playback bypasses the game AudioListener.
        static float Volume() => NativeJukebox.Volume;

        // Read the daemon playback report. Log failures once per selection.
        // Failed stations select OST; failed daemon OST playback uses bundled SongDefs.
        public static void Report(
            bool playing,
            string error,
            string title,
            string source = null,
            string session = null
        )
        {
            if (!AcceptsReport(source, error))
                return;
            ReportSpotify(error, source, session);
            bool identityChanged = RecognitionTrack.Update(playing, _muted, SourceLabel());
            if (identityChanged)
            {
                _recognizedArtist = null;
                _recognizedTitle = null;
            }
            // Accept raw titles even while muted. Station metadata can arrive after selection and change during playback.
            string raw = string.IsNullOrEmpty(title) ? null : title;
            if (_rawTitle != raw)
            {
                _rawTitle = raw;
                _recognizedArtist = null;
                _recognizedTitle = null;
                RecognitionTrack.Advance();
            }
            _playing = playing;

            // Muted playback stops audio, so ignore playback errors in that state.
            if (_muted)
                return;
            if (playing || string.IsNullOrEmpty(error))
            {
                _blamed = false;
                return;
            }
            if (FallbackToLocalOst(source, error))
            {
                Log.Warning("[SlopWorld] jukebox: " + error + ". Using the bundled OST locally.");
                NativeJukebox.Resume();
                return;
            }
            if (_blamed)
                return;
            _blamed = true;

            Log.Warning("[SlopWorld] jukebox: " + error);
            if (_station == null)
                return;

            Notify(
                JukeboxNoticeKind.Rejected,
                $"Jukebox: {_station.Name} {RateLabel(_station.Rate)} would not play. Back to the OST."
            );
            SelectSource(clearFailure: false);
        }

        // Apply the exit preference before Root.Shutdown closes the socket. Forced process termination bypasses this method.
        public static void Quit()
        {
            Read();
            // Prevent later frames from sending further playback updates.
            _quit = true;
            // Keep muted playback stopped even if the exit preference permits continued playback.
            if (!_stopOnExit && !_muted)
                return;

            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online)
                return;
            if (!hub.Capabilities.AudioPlayback)
                return;
            hub.Audio.Stop(Volume());
        }

        // Mark the selection for the next Update, which checks connection state before sending.
        // Clear old metadata so the new source does not display the previous track title.
        static void Push()
        {
            InvalidatePlayback();
            _playing = false;
            _rawTitle = null;
            _recognizedArtist = null;
            _recognizedTitle = null;
            RecognitionTrack.Update(false, _muted, SourceLabel());
            RecognitionTrack.Advance();
        }

        // Save station-id:stream-key, ncspot, or ost.
        // The station identifier distinguishes stations that use the same stream key.
        static void Save()
        {
            Settings.S.radio =
                _spotify ? "ncspot"
                : _station == null ? "ost"
                : _station.SelectionKey(_station.Rate);
            Settings.S.radioMute = _muted;
            Settings.S.radioStopOnExit = _stopOnExit;
            Settings.S.Write();
        }

        static void Read()
        {
            if (_read)
                return;
            _read = true;

            _muted = Settings.RadioMute;
            _stopOnExit = Settings.RadioStopOnExit;

            // Retain the saved key until the catalog arrives. SetStations restores the matching station.
            _spotify = Settings.Radio == "ncspot";
            _station = FindSelection(Settings.Radio);
        }
    }
}
