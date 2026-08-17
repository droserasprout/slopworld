using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // The mod selects an opaque station key; slopd owns the catalog and opens the URL because
    // Unity/FMOD cannot reliably fetch HTTPS Icecast streams or decode their common responses.
    // The station is a user setting, not colony state, so it survives loading another colony.
    public static class Radio
    {
        // The part of a station the UI will eventually use in addition to its streams. Keep
        // donation links here, beside the name, rather than throwing them away in the loader.
        public sealed class Metadata
        {
            public readonly string Name;
            public readonly string Donate;
            public readonly string TitleRegex;

            internal Metadata(string name, string donate, string titleRegex)
            {
                Name = name;
                Donate = donate ?? "";
                TitleRegex = titleRegex ?? "";
            }
        }

        // A station from the daemon's catalog: metadata and the qualities it serves. URLs stay
        // out of selection messages; only stable ids and stream keys cross that boundary.
        public sealed class Station
        {
            public readonly string Id;
            public readonly Metadata Metadata;

            public string Name => Metadata.Name;

            // The qualities this one answers on. One entry is a whole list, and the menu
            // is built the same way either way - see Jukebox.Presets.
            public readonly int[] Rates;

            readonly string[] _keys;
            readonly Regex _titleRegex;

            // The quality it was last left on, so a switch away and back comes up where it
            // was. Kept per station rather than as one number for the lot of them: the
            // lists do not overlap, and RP's 192 is not a rate WeFunk has ever served.
            public int Rate;

            internal Station(string id, string name, string donate, string titleRegex,
                             int[] rates, string[] keys, int defaultRate)
            {
                Id = id;
                Metadata = new Metadata(name, donate, titleRegex);
                Rates = rates;
                _keys = keys;
                Rate = defaultRate;

                if (string.IsNullOrEmpty(Metadata.TitleRegex)) return;
                try
                {
                    _titleRegex = new Regex(Metadata.TitleRegex,
                        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
                catch (ArgumentException e)
                {
                    Log.Warning($"[SlopWorld] jukebox: invalid title regex for {Id}: {e.Message}");
                }
            }

            // The stream's stable key at a rate - "mp3-192", "wefunk64.mp3", "classic".
            // It gives the setting a human-editable tail.
            public string Path(int rate)
            {
                int index = Find(rate);
                return index < 0 ? null : _keys[index];
            }

            public string SelectionKey(int rate) => Id + ":" + Path(rate);

            internal string FormatTitle(string title)
            {
                string artist;
                string song;
                return TryTitleParts(title, out artist, out song)
                    ? artist + " - " + song
                    : title;
            }

            internal bool TryTitleParts(string title, out string artist, out string song)
            {
                artist = null;
                song = title;
                if (_titleRegex == null || string.IsNullOrEmpty(title)) return false;
                Match match = _titleRegex.Match(title);
                if (!match.Success) return false;

                Group artistGroup = match.Groups["artist"];
                Group titleGroup = match.Groups["title"];
                if (!artistGroup.Success || !titleGroup.Success) return false;

                artist = artistGroup.Value.Trim();
                song = titleGroup.Value.Trim();
                return !string.IsNullOrEmpty(artist) && !string.IsNullOrEmpty(song);
            }

            int Find(int rate)
            {
                for (int i = 0; i < Rates.Length; i++)
                    if (Rates[i] == rate) return i;
                return -1;
            }
        }

        static Station[] _stations = new Station[0];
        // Before the first catalog, a null station can still be a saved radio selection.
        static bool _catalogReady;

        // Stations in daemon catalog order. The last catalog remains in memory while the
        // socket reconnects, so a temporary daemon restart does not empty an open menu.
        public static Station[] Stations => _stations;

        // The daemon sends metadata and stable keys, never stream URLs. Invalid entries are
        // ignored individually so one bad user definition cannot take the whole menu down.
        public static void SetStations(JVal catalog)
        {
            if (catalog == null || catalog.IsNull || catalog["stations"].IsNull) return;
            _catalogReady = true;

            string saved = null;
            if (_station != null) saved = _station.SelectionKey(_station.Rate);
            else if (_read) saved = Settings.Radio;

            var next = new List<Station>();
            foreach (var item in catalog["stations"].Items)
            {
                try
                {
                    string id = item["id"].AsString(null);
                    if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("missing id");

                    var metadata = item["metadata"];
                    string name = metadata["name"].AsString(id);
                    string donate = metadata["donate"].AsString("");
                    string titleRegex = metadata["title_regex"].AsString("");

                    var rates = new List<int>();
                    var keys = new List<string>();
                    foreach (var stream in item["streams"].Items)
                    {
                        int rate = stream["rate"].AsInt(0);
                        string key = stream["key"].AsString("");
                        if (rate <= 0 || string.IsNullOrEmpty(key))
                            throw new InvalidOperationException("stream needs a positive rate and key");
                        if (rates.Contains(rate) || keys.Contains(key))
                            throw new InvalidOperationException("duplicate stream rate or key");
                        rates.Add(rate);
                        keys.Add(key);
                    }
                    if (rates.Count == 0) throw new InvalidOperationException("no streams");

                    int defaultRate = item["default_rate"].AsInt(rates[0]);
                    if (!rates.Contains(defaultRate))
                        throw new InvalidOperationException("default rate has no stream");

                    next.Add(new Station(id, name, donate, titleRegex,
                        rates.ToArray(), keys.ToArray(), defaultRate));
                }
                catch (Exception e)
                {
                    Log.Warning("[SlopWorld] jukebox: ignoring catalog station: " + e.Message);
                }
            }

            _stations = next.ToArray();
            if (!_read) return;

            _station = FindSelection(saved);
            string nextSelection = _station?.SelectionKey(_station.Rate);
            if (nextSelection == null && saved != null && saved != "ost")
            {
                Save();
                Push();
            }
            // This also covers the first catalog arriving after Read selected the OST
            // provisionally, and a reload that changes the URL behind the same stable key.
            if (nextSelection != null) Push();
        }

        static Station FindSelection(string saved)
        {
            if (string.IsNullOrEmpty(saved)) return null;
            foreach (var station in Stations)
            {
                foreach (int rate in station.Rates)
                {
                    if (saved != station.SelectionKey(rate)) continue;
                    station.Rate = rate;
                    return station;
                }
            }
            return null;
        }

        // The daemon shuffles the files in this directory, so the mod selects the OST as one
        // source instead of trying to track which file the host is currently feeding.
        static readonly System.Random Dice = new System.Random();

        static string OstPath()
        {
            var root = SlopWorldMod.Instance?.Content?.RootDir;
            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Sounds", "SlopWorld", "OST");
        }

        // Null is the OST, which is the one thing played that is not a station.
        static Station _station;
        static bool _muted;
        static bool _stopOnExit = true;

        // The settings string is read once, because the mod's settings are not loaded when
        // this class is first touched.
        static bool _read;

        // What was last sent, so a reconnect re-sends it and a still frame does not. The
        // flag beside it is not redundant: silence is a thing to send - muted, the source
        // *is* null - so a null `_sent` cannot also stand for "not told yet".
        static string _sent;
        static bool _told;
        static float _sentVolume = -1f;

        // What the daemon says came of it. Only the failure is acted on, and only once per
        // pick: a station that will not play should say so and hand the OST back.
        static bool _blamed;

        // The last word has been said. Root.Shutdown does not end the process where it is
        // called - Application.Quit lets the frame finish, and there are more frames after
        // it while the save is written - so Update runs on after Quit, and without this it
        // put the station straight back on: the music stopped for a second and returned.
        static bool _quit;

        // The daemon's raw title. It is deliberately kept beside the recognized override so a
        // like can record what the station actually sent, without regex normalization leaking
        // into the history.
        static string _rawTitle;
        static bool _playing;
        static string _recognizedArtist;
        static string _recognizedTitle;
        static int _trackVersion;

        // Recognition is a background lookup; the gate serialises its state across the worker
        // thread and the UI. Input and error are kept for the UI to make the operation legible:
        // where it listened, and why it came back empty. The token source lets a cancel action
        // stop a slow Shazam call.
        static readonly object RecognitionGate = new object();
        static bool _recognizing;
        static string _recognitionInput;
        static string _recognitionError;
        static CancellationTokenSource _recognitionCancel;

        // A slider moved by a hair is not worth a packet.
        const float VolumeStep = 0.01f;

        // What is on, or null for the OST. The station carries its own quality, so this is
        // the whole of the answer to "what is playing" - which is what the menu's rows and
        // Report each ask in their own way.
        public static Station Picked
        {
            get { Read(); return _station; }
        }

        // Off, which is the only off this box has. A stop and not a volume of zero: there
        // is no sense in a station that keeps downloading for nobody, and a live stream
        // comes back where it is now rather than where it was left, which is what a radio
        // does anyway.
        public static bool Muted
        {
            get { Read(); return _muted; }
        }

        // Whether the daemon is told to go quiet on the way out. It outlives the game, so
        // without this the music is still playing when the window has gone.
        public static bool StopOnExit
        {
            get { Read(); return _stopOnExit; }
        }

        // "Artist - Song", or null when there is nothing to say: muted, or a station that
        // has not named itself yet - a title is spliced into the audio and so arrives a
        // second or two behind the pick, and never at all if the host stops sending
        // `icy-metaint`. A successful SongRec lookup temporarily supplies the artist/title
        // pair; the station's raw title remains available to Like.
        public static string NowPlaying
        {
            get
            {
                Read();
                if (_muted) return null;
                if (HasRecognition()) return _recognizedArtist + " - " + _recognizedTitle;
                return StationNowPlaying();
            }
        }

        // The station's own line, ignoring any recognition override, so the UI can show what
        // the station reported beside what Shazam heard rather than silently replacing it.
        static string StationNowPlaying()
        {
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

        // The UI's window onto the background lookup. Read under the gate because the worker
        // thread writes them; each answers one question the recognizing state needs to show.
        public static bool Recognizing
        {
            get { lock (RecognitionGate) return _recognizing; }
        }

        public static string RecognizingInput
        {
            get { lock (RecognitionGate) return _recognitionInput; }
        }

        public static string RecognitionError
        {
            get { lock (RecognitionGate) return _recognitionError; }
        }

        // Whether a Shazam lookup is currently overriding the station's line, and that line
        // itself, so the UI can present the two provenances side by side.
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
            get { Read(); return _muted ? null : StationNowPlaying(); }
        }

        // The like list is deliberately separate from profile settings: it belongs to the
        // machine's music collection rather than to one RimWorld profile. One TOML table per
        // action keeps the file append-friendly while remaining readable by other tools.
        public static void Like()
        {
            string now = NowPlaying;
            if (string.IsNullOrEmpty(now))
            {
                SlopWidgets.Fail("nothing is playing");
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
                SlopWidgets.Fail("could not save liked song");
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

        // Start one lookup at a time. SongRecognizer owns the capture device, the child process,
        // its JSON and its timeout; this keeps it off the Unity thread so a slow Shazam response
        // cannot freeze the game, and binds the result to the track that was heard. Duplicate
        // requests are refused rather than queued: two Shazam calls on one song help nobody.
        public static void Recognize()
        {
            Read();
            if (_muted || !_playing || string.IsNullOrEmpty(NowPlaying))
            {
                SlopWidgets.Fail("nothing is playing");
                return;
            }

            int version;
            string source;
            CancellationToken token;
            lock (RecognitionGate)
            {
                if (_recognizing)
                {
                    SlopWidgets.Fail("recognition is already running");
                    return;
                }
                _recognizing = true;
                _recognitionError = null;
                _recognitionInput = null;
                _recognitionCancel = new CancellationTokenSource();
                token = _recognitionCancel.Token;
                version = _trackVersion;
                source = SourceLabel();
            }

            Messages.Message("Jukebox: recognizing...", MessageTypeDefOf.NeutralEvent, false);
            ThreadPool.QueueUserWorkItem(_ => RunRecognition(version, source, token));
        }

        // Cancel an in-flight lookup. The worker's result then comes back Canceled and is
        // dropped without a toast, because the player already knows they stopped it.
        public static void CancelRecognition()
        {
            CancellationTokenSource cancel;
            lock (RecognitionGate)
            {
                if (!_recognizing || _recognitionCancel == null) return;
                cancel = _recognitionCancel;
            }
            try { cancel.Cancel(); } catch { }
        }

        static void RunRecognition(int version, string source, CancellationToken token)
        {
            var recognizer = new SongRecognizer(new SystemProcessRunner());
            RecognitionResult result;
            try
            {
                // Choose the input first and publish its label so the recognizing state can
                // name where it is listening while the slow lookup runs.
                AudioInput input = recognizer.SelectInput(token);
                SlopClient.OnMainThread(() => SetRecognitionInput(input.Label));
                result = recognizer.Recognize(input, token);
            }
            catch (Exception e)
            {
                result = new RecognitionResult
                {
                    Status = RecognitionStatus.Error,
                    Message = e.Message,
                };
            }

            SlopClient.OnMainThread(() => FinishRecognition(version, source, result));
        }

        static void SetRecognitionInput(string label)
        {
            lock (RecognitionGate)
                if (_recognizing) _recognitionInput = label;
        }

        // Apply a result only if it still belongs to what is playing: SongRec heard a snippet,
        // and by the time it answers the station may have moved on or the source been switched.
        static void FinishRecognition(int version, string source, RecognitionResult result)
        {
            lock (RecognitionGate)
            {
                _recognizing = false;
                _recognitionCancel?.Dispose();
                _recognitionCancel = null;
                if (result != null && !string.IsNullOrEmpty(result.Input))
                    _recognitionInput = result.Input;
            }

            if (result == null || result.Status == RecognitionStatus.Canceled) return;

            if (version != _trackVersion || source != SourceLabel())
            {
                SetRecognitionError("track changed before recognition finished");
                return;
            }
            if (!result.Ok)
            {
                SetRecognitionError(string.IsNullOrEmpty(result.Message)
                    ? "recognition failed" : result.Message);
                return;
            }

            lock (RecognitionGate) _recognitionError = null;
            _recognizedArtist = result.Artist;
            _recognizedTitle = result.Title;
            Messages.Message("Jukebox: recognized " + _recognizedArtist + " - "
                + _recognizedTitle, MessageTypeDefOf.TaskCompletion, false);
        }

        static void SetRecognitionError(string message)
        {
            lock (RecognitionGate) _recognitionError = message;
            SlopWidgets.Fail(message);
        }

        static bool HasRecognition() => !string.IsNullOrEmpty(_recognizedArtist)
            && !string.IsNullOrEmpty(_recognizedTitle);

        static string SourceLabel() => _station == null ? "SlopWorld OST" : _station.Name;

        static string OriginalArtist() => "";

        static void CurrentParts(out string artist, out string title)
        {
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

        // A station may provide metadata.title_regex with named `artist` and `title` groups.
        // Keep the station-specific title shape in its TOML rather than in this list of
        // stations, and leave an unmatched title untouched for diagnosis.
        static string FormatTitle(Station station, string title)
        {
            return station == null ? title : station.FormatTitle(title);
        }

        public static string RateLabel(int rate) => rate + "k mp3";

        public static void PickOst()
        {
            Read();
            _station = null;
            _muted = false;
            _blamed = false;
            Save();
            Push();
        }

        // Pick one OST track or one station, with a station quality chosen independently.
        // OST tracks are the only source without a meaningful quality, so their branch just
        // chooses the track and lets Pick handle mute, persistence, and the daemon push.
        public static void PickRandom()
        {
            Read();

            var candidates = new List<Station>();
            foreach (var candidate in Stations)
                if (_muted || candidate != _station) candidates.Add(candidate);

            // The OST is one source candidate, but when it is active it is not picked again.
            bool ostCurrent = !_muted && _station == null;
            bool canPickOst = !ostCurrent;
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
            _muted = !_muted;
            _blamed = false;
            Save();
            Push();
        }

        // Nothing to push: it is a question asked once, on the way out.
        public static void ToggleStopOnExit()
        {
            Read();
            _stopOnExit = !_stopOnExit;
            Save();
        }

        // A station and one of its rates, or null for the OST, whose rate is meaningless.
        public static void Pick(Station s, int rate)
        {
            Read();
            // Picking something is asking to hear it, which answers the mute as well - a
            // menu row that does nothing because of a tick two rows down is a menu row
            // nobody can explain.
            if (_station == s && (s == null || s.Rate == rate) && !_muted) return;

            _station = s;
            // On the station rather than beside it, so the one being left keeps the quality
            // it was left on. Picking the OST changes nobody's.
            if (s != null) s.Rate = rate;
            _muted = false;
            _blamed = false;
            Save();
            Push();
        }

        // Every frame, menu and game alike, from Patch_Root_Update. Pending selections go out
        // immediately; steady-state work runs one frame in ten because volume changes slowly.
        // `Read()` stays above it, being the answer to what the daemon is playing.
        static int _updateSkip;
        const int UpdateInterval = 10;

        public static void Update()
        {
            if (_quit) return;
            Read();

            // A fresh colony creates an enabled music manager, so stop vanilla music before
            // the throttle gives it a chance to start an OST track.
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

            var hub = SessionHub.Instance;
            if (hub == null) return;

            // A reconnect starts the daemon's socket over, and the mod is the only thing
            // that knows what was playing.
            if (!hub.Online) { _told = false; _sentVolume = -1f; return; }

            // A pending selection is a control message, not steady-state work. In particular,
            // send the muted stop on the first frame after a daemon restart.
            if (!_told)
            {
                float pendingVolume = Volume();
                string pending = Selection();
                SendSelection(hub, pendingVolume, pending);
                _sent = pending;
                _told = true;
                _sentVolume = pendingVolume;
                _updateSkip = 0;
                return;
            }

            // Throttle steady-state work: most frames change nothing.
            if (++_updateSkip < UpdateInterval) return;
            _updateSkip = 0;

            string want = Selection();
            if (want != _sent)
            {
                SendSelection(hub, Volume(), want);
                _sent = want;
                _sentVolume = Volume();
                _updateSkip = 0;
                return;
            }

            float volume = Volume();
            if (Mathf.Abs(volume - _sentVolume) < VolumeStep) return;
            hub.SendVolume(volume);
            _sentVolume = volume;
        }

        // The game's own music slider. Master is left out because the daemon is not behind
        // the game's AudioListener - out there, this is the whole of it.
        static float Volume() => Mathf.Clamp01(Prefs.VolumeMusic * Prefs.VolumeMaster);

        static string Selection()
        {
            if (_muted) return "stop";
            if (_station != null) return "station:" + _station.SelectionKey(_station.Rate);
            if (!_catalogReady && IsSavedStation()) return "stop";
            string path = OstPath();
            return path == null ? "stop" : "file:" + path;
        }

        static bool IsSavedStation() => !string.IsNullOrEmpty(Settings.Radio) && Settings.Radio != "ost";

        static void SendSelection(SessionHub hub, float volume, string selection)
        {
            if (selection == "stop")
            {
                hub.SendAudio(null, null, null, volume);
                return;
            }
            if (_station != null)
            {
                hub.SendAudio(_station.Id, _station.Path(_station.Rate), null, volume);
                return;
            }
            hub.SendAudio(null, null, OstPath(), volume);
        }

        // The daemon's answer to what was asked of it. A station that will not play is
        // said once and handed back to the OST; the OST failing is not something to fall
        // back from, so it is left to the log.
        public static void Report(bool playing, string error, string title)
        {
            // The station's own, spliced into its audio and unpicked out there: it arrives
            // a second or so after a pick and changes on its own thereafter. Taken even
            // while muted, the mute being about the speakers rather than about the wire.
            string raw = string.IsNullOrEmpty(title) ? null : title;
            if (_rawTitle != raw)
            {
                _rawTitle = raw;
                _recognizedArtist = null;
                _recognizedTitle = null;
                _trackVersion++;
            }
            _playing = playing;

            // Muted, "not playing" is the answer that was asked for, and the error beside
            // it is whatever last went wrong before the box was turned off.
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

        // Root.Shutdown sends the daemon's last selection before the socket closes; killed games require GET /api/audio or a daemon restart.
        public static void Quit()
        {
            Read();
            // Whatever is on stays on or goes off here, and either way this is the end of
            // the conversation: the frames that follow have nothing left to say.
            _quit = true;
            // Mute is an explicit silence request, so it takes precedence over leaving
            // active audio running on exit.
            if (!_stopOnExit && !_muted) return;

            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online) return;
            hub.SendAudio(null, null, null, Volume());
        }

        // Forces the next Update to send, rather than sending from here: one place puts
        // things on the wire, and it is the one that knows whether the socket is up. The
        // title goes with it - every caller is something about to change what is playing,
        // and the old song's name on the new station is worse than no name at all.
        static void Push()
        {
            _told = false;
            _playing = false;
            _rawTitle = null;
            _recognizedArtist = null;
            _recognizedTitle = null;
            _trackVersion++;
        }

        // Saved as "station-id:stream-key" - or "ost". The stream key keeps the setting
        // readable, while the id prevents two user stations serving the same path from
        // stealing one another's selection.
        static void Save()
        {
            Settings.S.radio = _station == null ? "ost" : _station.SelectionKey(_station.Rate);
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

            // The catalog arrives over the socket. If it has not arrived yet, keep the saved
            // key in Settings and SetStations will restore it when the daemon sends the list.
            _station = FindSelection(Settings.Radio);
        }
    }

    // Told on the way out, in a prefix so the socket is still up. Its own patch beside the
    // autosave's rather than a line inside it: two things happen on the way out and neither
    // is the other's business. QuitInterceptor routes the window's close button through
    // Root.Shutdown as well, so this is the whole of an orderly quit.
    [HarmonyPatch(typeof(Root), nameof(Root.Shutdown))]
    public static class Patch_RadioOnShutdown
    {
        static void Prefix() => Radio.Quit();
    }
}
