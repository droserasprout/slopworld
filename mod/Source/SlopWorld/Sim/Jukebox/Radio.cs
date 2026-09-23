using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;
using RimWorld;
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

        static string OstPath()
        {
            var root = ModEntry.Instance?.Content?.RootDir;
            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Sounds", "SlopWorld", "OST");
        }

        // A null station selects the OST unless Spotify is selected.
        static Station _station;
        static bool _spotify;
        static bool _openingSpotify;
        static int _selectionRevision;
        public static bool Spotify => _spotify;
        public const string OstSourceId = "ost";
        public const string SpotifySourceId = "spotify";

        // Keep Spotify available until the daemon reports its capabilities.
        public static bool SpotifyAvailable => SessionHub.Instance == null
            || !SessionHub.Instance.Capabilities.Known
            || SessionHub.Instance.Capabilities.Ncspot;
        static bool _muted;
        static bool _stopOnExit;

        // Read settings once after initialization. They are unavailable when this class first loads.
        static bool _read;

        // Retain the last selection to avoid repeated sends. Resend after reconnection.
        // Use _told to distinguish a pending selection from one already sent.
        static string _sent;
        static bool _told;
        static float _sentVolume = -1f;

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

        // Send volume changes only when they meet this minimum difference.
        const float VolumeStep = 0.01f;

        // Use native SlopWorld SongDefs when the daemon cannot play audio.
        // Until capabilities arrive, the default permits daemon playback.
        static bool SidecarAudio => SessionHub.Instance != null
            && !SessionHub.Instance.Capabilities.AudioPlayback;

        static RimWorld.MusicManagerPlay NativeMusic()
        {
            try { return Find.MusicManagerPlay; }
            catch { return null; } // Root_Entry has no play music manager.
        }

        // Use the native music manager for sidecar playback.
        // The provider lets tests supply track snapshots without the game.
        internal static Func<NativeTrackSnapshot> NativeTrackProvider = ReadNativeTrack;

        static NativeTrackSnapshot ReadNativeTrack()
        {
            try
            {
                var music = NativeMusic();
                var song = music?.CurrentSong;
                if (music == null || !music.IsPlaying || song == null) return null;

                return NativeTrackSnapshot.FromOstClipPath(song.clipPath);
            }
            catch
            {
                return null;
            }
        }

        static void SetNativeMusicMuted(bool muted)
        {
            var music = NativeMusic();
            if (music == null) return;

            bool changed = music.disabled != muted;
            music.disabled = muted;
            if (muted && (changed || music.IsPlaying)) music.Stop();
        }

        // Start the next SlopWorld song immediately after unmuting.
        // Clearing disabled alone waits for the native music transition timer.
        static void ResumeNativeMusic()
        {
            var music = NativeMusic();
            if (music == null) return;

            music.disabled = false;
            if (Current.ProgramState == ProgramState.Playing) music.StartNewSong();
        }

        // Read metadata from the current native SongDef during sidecar playback.
        static string NativeNowPlaying()
        {
            return SampleNativeTrack()?.Display;
        }

        static NativeTrackSnapshot SampleNativeTrack()
        {
            try { return NativeTrackProvider(); }
            catch { return null; }
        }

        // Return the selected radio station. OST and Spotify selections have no station object.
        public static Station Picked
        {
            get { Read(); return _station; }
        }

        // Stop daemon playback when muted to avoid downloading an inaudible stream.
        // For sidecar playback, mute the native music manager. RemoveVanillaSongs.xml limits its songs to the SlopWorld OST.
        public static bool Muted
        {
            get { Read(); return _muted; }
        }

        // Control whether the daemon stops playback when the game exits. The daemon can continue after the game closes.
        public static bool StopOnExit
        {
            get { Read(); return _stopOnExit; }
        }

        public static bool SourceShown(string id)
        {
            Read();
            if (string.IsNullOrEmpty(id)) return false;
            foreach (string hidden in HiddenSources())
                if (hidden == id) return false;
            return true;
        }

        public static void SetSourceShown(string id, bool shown)
        {
            Read();
            if (string.IsNullOrEmpty(id)) return;
            var hidden = HiddenSources();
            hidden.Remove(id);
            if (!shown) hidden.Add(id);
            Settings.S.radioHiddenSources = string.Join("\n", hidden);
            Settings.S.Write();
        }

        static List<string> HiddenSources()
        {
            var result = new List<string>();
            string text = Settings.S.radioHiddenSources ?? "";
            foreach (string line in text.Split(new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                string id = line.Trim();
                if (id.Length > 0 && !result.Contains(id)) result.Add(id);
            }
            return result;
        }

        // Apply the daemon capability snapshot.
        // Replace a saved Spotify selection with OST if the daemon does not support ncspot.
        public static void CapabilitiesChanged()
        {
            if (_quit) return;
            Read();
            if (SpotifyAvailable || !_spotify) return;
            _spotify = false;
            _station = null;
            _muted = false;
            Save();
            Push();
        }

        // Return the current track label, or null if playback is muted or unavailable.
        // Prefer recognized artist and title values when available. Retain the raw station title for Like.
        public static string NowPlaying
        {
            get
            {
                Read();
                if (_muted) return null;
                if (SidecarAudio) return NativeNowPlaying();
                if (!_playing) return null;
                if (HasRecognition()) return _recognizedArtist + " - " + _recognizedTitle;
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
                if (_cachedNowPlayingVersion != RecognitionTrack.Revision
                    || now - _cachedNowPlayingAt >= UpdateInterval)
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
            if (_spotify) return _rawTitle;
            return _station != null
                ? FormatTitle(_station, _rawTitle)
                : string.IsNullOrEmpty(_rawTitle)
                    ? "Terry Fail - OST" : "Terry Fail - " + _rawTitle;
        }

        public static string Artist
        {
            get { Read(); string artist, title; CurrentParts(out artist, out title); return artist; }
        }

        public static string Title
        {
            get { Read(); string artist, title; CurrentParts(out artist, out title); return title; }
        }

        public static string Source
        {
            get { Read(); return SourceLabel(); }
        }

        // Expose recognized metadata separately so the UI can identify its source.
        public static bool Recognized
        {
            get { Read(); return HasRecognition(); }
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
            get { Read(); return _muted || SidecarAudio ? null : StationNowPlaying(); }
        }

        // Store likes separately from RimWorld profile settings.
        // Append one TOML table per action so other tools can read the history.
        public static void Like()
        {
            if (SidecarAudio)
            {
                NativeTrackSnapshot native = SampleNativeTrack();
                NativeLikeRecord record;
                string nativeError;
                if (!JukeboxLikeWriter.TryAppend(
                    LikesPath(), native, DateTime.UtcNow, out record, out nativeError))
                {
                    if (nativeError == "nothing is playing") UiLayout.Fail(nativeError);
                    else
                    {
                        Log.Error("[SlopWorld] jukebox: could not save liked song: " + nativeError);
                        UiLayout.Fail("could not save liked song");
                    }
                    return;
                }
                Messages.Message($"Jukebox: liked {record.Display}",
                    MessageTypeDefOf.TaskCompletion, false);
                return;
            }

            string now = NowPlaying;
            if (string.IsNullOrEmpty(now))
            {
                UiLayout.Fail("nothing is playing");
                return;
            }

            try
            {
                string path = LikesPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string artist;
                string title;
                CurrentParts(out artist, out title);
                string stamp = DateTime.UtcNow.ToString(
                    "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
                File.AppendAllText(path,
                    "[[like]]" + Environment.NewLine +
                    "at = " + Toml.Quote(stamp) + Environment.NewLine +
                    "source = " + Toml.Quote(SourceLabel()) + Environment.NewLine +
                    "artist = " + Toml.Quote(artist) + Environment.NewLine +
                    "title = " + Toml.Quote(title) + Environment.NewLine +
                    "original_artist = " + Toml.Quote(OriginalArtist()) + Environment.NewLine +
                    "original_title = " + Toml.Quote(_rawTitle) + Environment.NewLine +
                    Environment.NewLine);
                Messages.Message($"Jukebox: liked {now}", MessageTypeDefOf.TaskCompletion, false);
            }
            catch (Exception e)
            {
                Log.Error("[SlopWorld] jukebox: could not save liked song: " + e);
                UiLayout.Fail("could not save liked song");
            }
        }

        public static string LikesPath()
        {
            string root = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrEmpty(root))
                root = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(root, "slopworld", "jukebox.toml");
        }

        static bool HasRecognition() => !string.IsNullOrEmpty(_recognizedArtist)
            && !string.IsNullOrEmpty(_recognizedTitle);

        static string SourceLabel() => _spotify ? "Spotify" : _station == null ? "SlopWorld OST" : _station.Name;

        static string OriginalArtist()
        {
            string artist;
            string title;
            return _station != null && _station.TryTitleParts(_rawTitle, out artist, out title)
                ? artist : "";
        }

        static void CurrentParts(out string artist, out string title)
        {
            if (_spotify) { artist = ""; title = _rawTitle; return; }
            if (HasRecognition())
            {
                artist = _recognizedArtist;
                title = _recognizedTitle;
                return;
            }

            if (_station != null)
            {
                if (_station.TryTitleParts(_rawTitle, out artist, out title)) return;
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
            _station = null;
            _spotify = false;
            _muted = false;
            _blamed = false;
            Save();
            Push();
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
            if (count == 0) return;

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
            if (SidecarAudio)
            {
                _muted = !_muted;
                if (_muted) SetNativeMusicMuted(true);
                else ResumeNativeMusic();
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
            if (!_spotify && _station == s && (s == null || s.Rate == rate) && !_muted) return;

            _spotify = false;
            _station = s;
            // Retain the selected rate on each station. Selecting OST does not change station rates.
            if (s != null) s.Rate = rate;
            _muted = false;
            _blamed = false;
            Save();
            Push();
        }

        // Patch_Root_Update calls Update every frame in menus and during play.
        // Send pending selections immediately. Limit regular checks to six per second.
        // Read settings before these checks.
        static PeriodicWork _steadyUpdate;
        const double UpdateInterval = 1.0 / 6.0;

        public static void Update()
        {
            if (_quit) return;
            Read();
            var hub = SessionHub.Instance;
            if (hub == null) return;
            // On macOS, wait for daemon capabilities before disabling native music.
            if (!hub.Capabilities.Known && Application.platform == RuntimePlatform.OSXPlayer) return;
            if (!hub.Capabilities.AudioPlayback)
            {
                SetNativeMusicMuted(_muted);
                return;
            }

            // Disable native music before the update delay can let a new colony start an OST track.
            if (Current.ProgramState == ProgramState.Playing)
            {
                try
                {
                    var music = Find.MusicManagerPlay;
                    if (music != null && !music.disabled)
                    {
                        music.Stop();
                        music.disabled = true;
                    }
                }
                catch { /* Root_Play castclass fails on menu */ }
            }

            // Reset sent state while offline so reconnection sends the selection again.
            if (!hub.Online) { _told = false; _sentVolume = -1f; return; }

            // Send pending selections before the regular update delay.
            // This includes a stop request on the first connected frame after a daemon restart.
            if (!_told)
            {
                float pendingVolume = Volume();
                string pending = Selection();
                SendSelection(hub, pendingVolume, pending);
                _sent = pending;
                _told = true;
                _sentVolume = pendingVolume;
                _steadyUpdate.Delay(Time.realtimeSinceStartupAsDouble, UpdateInterval);
                return;
            }

            // Limit regular checks because most frames do not change playback.
            if (!_steadyUpdate.Due(Time.realtimeSinceStartupAsDouble, UpdateInterval)) return;

            string want = Selection();
            if (want != _sent)
            {
                SendSelection(hub, Volume(), want);
                _sent = want;
                _sentVolume = Volume();
                return;
            }

            float volume = Volume();
            if (Mathf.Abs(volume - _sentVolume) < VolumeStep) return;
            hub.Audio.SendVolume(volume);
            _sentVolume = volume;
        }

        // Apply both music and master volume because daemon playback bypasses the game AudioListener.
        static float Volume() => Mathf.Clamp01(Prefs.VolumeMusic * Prefs.VolumeMaster);

        static string Selection()
        {
            if (_muted) return "stop";
            if (_spotify) return "ncspot";
            if (_station != null) return "station:" + _station.SelectionKey(_station.Rate);
            if (!_catalogReady && IsSavedStation()) return "stop";
            string path = OstPath();
            return path == null ? "stop" : "file:" + path;
        }

        static bool IsSavedStation() => !string.IsNullOrEmpty(Settings.Radio) && Settings.Radio != "ost" && Settings.Radio != "ncspot";

        static void SendSelection(SessionHub hub, float volume, string selection)
        {
            if (selection == "stop")
            {
                hub.Audio.SendAudio(null, null, null, volume);
                return;
            }
            if (_spotify)
            {
                hub.Audio.SendSpotify(volume);
                return;
            }
            if (_station != null)
            {
                hub.Audio.SendAudio(_station.Id, _station.Path(_station.Rate), null, volume);
                return;
            }
            hub.Audio.SendAudio(null, null, OstPath(), volume);
        }

        // Read the daemon playback report. Log failures once per selection.
        // If a radio station fails, notify the player and select OST. OST failures have no fallback.
        public static void Report(bool playing, string error, string title, string source = null, string session = null)
        {
            ReportSpotify(error, source, session);
            if (_spotify != (source == "ncspot") && string.IsNullOrEmpty(error)) return;
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
            if (_muted) return;
            if (playing || string.IsNullOrEmpty(error)) { _blamed = false; return; }
            if (_blamed) return;
            _blamed = true;

            Log.Warning("[SlopWorld] jukebox: " + error);
            if (_station == null) return;

            Messages.Message($"Jukebox: {_station.Name} {RateLabel(_station.Rate)} would not "
                + "play. Back to the OST.", MessageTypeDefOf.RejectInput, false);
            _station = null;
            Save();
            Push();
        }

        // Apply the exit preference before Root.Shutdown closes the socket. Forced process termination bypasses this method.
        public static void Quit()
        {
            Read();
            // Prevent later frames from sending further playback updates.
            _quit = true;
            // Keep muted playback stopped even if the exit preference permits continued playback.
            if (!_stopOnExit && !_muted) return;

            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online) return;
            if (!hub.Capabilities.AudioPlayback) return;
            hub.Audio.SendAudio(null, null, null, Volume());
        }

        // Mark the selection for the next Update, which checks connection state before sending.
        // Clear old metadata so the new source does not display the previous track title.
        static void Push()
        {
            _selectionRevision++;
            _openingSpotify = false;
            _told = false;
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
            Settings.S.radio = _spotify ? "ncspot" : _station == null ? "ost" : _station.SelectionKey(_station.Rate);
            Settings.S.radioMute = _muted;
            Settings.S.radioStopOnExit = _stopOnExit;
            Settings.S.Write();
        }

        static void Read()
        {
            if (_read) return;
            _read = true;

            _muted = Settings.RadioMute;
            _stopOnExit = Settings.RadioStopOnExit;

            // Retain the saved key until the catalog arrives. SetStations restores the matching station.
            _spotify = Settings.Radio == "ncspot";
            _station = FindSelection(Settings.Radio);
        }
    }

    // Run before Root.Shutdown closes the socket. Keep playback shutdown separate from saving.
    // QuitInterceptor also routes the window close action through Root.Shutdown.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_RadioOnShutdown
    {
        static void Prefix() => Radio.Quit();
    }
}
