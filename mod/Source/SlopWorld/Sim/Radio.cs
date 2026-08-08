using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What the jukebox plays, decided here and played by the daemon. Everything on the list
    // is the same kind of thing to it - the built-in track is a path, a station is a URL -
    // so there is one mechanism rather than one per source taking turns, which is what the
    // last version of this file spent most of its lines on.
    //
    // It is out there because it cannot be in here. Unity 2022 will not send a cleartext
    // request; FMOD, which is what fetches a streamed clip, has no TLS, no desktop AAC
    // decoder, and will not start on a response with no Content-Length - which is every
    // Icecast stream there is. See slopd/src/audio.rs.
    //
    // Which station is picked is a setting rather than colony data, for the reason the
    // pane's font is one: it is about this room and these ears, and it is wanted back on
    // the next colony rather than buried with this one.
    public static class Radio
    {
        // A station out there: what it is called, the qualities it serves, and where each
        // one is served from. mp3 throughout rather than the aac some of them lead with:
        // aac is what the game could not decode, and there is no reason to hand the daemon
        // a harder problem than the one the player asked for. https because there is
        // nothing in the way of it out there.
        public sealed class Station
        {
            public readonly string Name;

            // The qualities this one answers on. One entry is a whole list, and the menu
            // is built the same way either way - see Jukebox.Presets.
            public readonly int[] Rates;

            readonly string _host;
            readonly string _path; // a format; {0} is the rate

            // The quality it was last left on, so a switch away and back comes up where it
            // was. Kept per station rather than as one number for the lot of them: the
            // lists do not overlap, and RP's 192 is not a rate WeFunk has ever served.
            public int Rate;

            public Station(string name, string host, string path, int[] rates, int rate)
            {
                Name = name;
                _host = host;
                _path = path;
                Rates = rates;
                Rate = rate;
            }

            // The stream's own name at a rate - "mp3-192", "wefunk64.mp3". This is what is
            // saved, so it doubles as the station's key: see Save.
            public string Path(int rate) => string.Format(_path, rate);

            public string Url(int rate) => _host + Path(rate);
        }

        // The stations, in menu order. RP's 64 and 96 are advertised in various places on
        // the web and 404 there, so its list is the one that actually answers.
        //
        // WeFunk publishes a .pls of four mirrors - s-00, s-09, s-14, s-17 - shuffled per
        // request, all serving the one 64k stream. One of them is named here rather than
        // the playlist: the daemon opens a URL and decodes what comes back, and teaching it
        // to unpick a playlist first would be a second fetch and a second thing to go
        // wrong. A mirror that is down is the same failure as a station that is down, and
        // Report already hands that back to the OST.
        public static readonly Station[] Stations =
        {
            new Station("RadioParadise Main", "https://stream.radioparadise.com/",
                "mp3-{0}", new[] { 32, 128, 192, 320 }, 128),
            new Station("WeFunk Radio", "https://s-00.wefunkradio.com:8443/",
                "wefunk{0}.mp3", new[] { 64 }, 64),
        };

        // The one track this mod ships, as a path under the mod's own folder. A path and
        // not a file:// URL so that neither end has anything to escape.
        static string OstPath()
        {
            var root = SlopWorldMod.Instance?.Content?.RootDir;
            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Sounds", "SlopWorld", "bg1.ogg");
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

        // What the station says it is playing. The daemon's, not a setting: it is true for
        // the next three minutes and belongs to nothing that outlives the process.
        static string _title;

        // The one track this mod ships names itself, there being nobody else to do it.
        const string OstTitle = "Terry Fail - slopbg";

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
        // `icy-metaint`. The OST names itself, there being nobody else to do it.
        public static string NowPlaying
        {
            get
            {
                Read();
                if (_muted) return null;
                return _station != null ? _title : OstTitle;
            }
        }

        public static string RateLabel(int rate) => rate + "k mp3";

        public static void PickOst() => Pick(null, 0);

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

        // Every frame, menu and game alike, from Patch_Root_Update. Cheap: it sends only
        // what has changed, and most frames change nothing.
        public static void Update()
        {
            if (_quit) return;
            Read();

            // The game's own music manager stays off for good. It is not sharing the job
            // with anything any more - the OST is out there too - so this is a flag held
            // rather than the switching the two of them used to do. A load or a new colony
            // clears it, which is why it is asked every frame rather than once.
            var music = Find.MusicManagerPlay;
            if (music != null && !music.disabled) { music.Stop(); music.disabled = true; }

            var hub = SessionHub.Instance;
            if (hub == null) return;

            // A reconnect starts the daemon's socket over, and the mod is the only thing
            // that knows what was playing.
            if (!hub.Online) { _told = false; _sentVolume = -1f; return; }

            string want = Source();
            if (!_told || want != _sent)
            {
                hub.SendAudio(want, Volume());
                _sent = want;
                _told = true;
                _sentVolume = Volume();
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

        static string Source()
        {
            // Muted is silence rather than a quiet stream, and null is how silence is
            // asked for: see SessionHub.SendAudio.
            if (_muted) return null;
            if (_station != null) return _station.Url(_station.Rate);
            // No path means no shipped track to point at, which is silence rather than a
            // string the daemon would only fail to open.
            return OstPath();
        }

        // The daemon's answer to what was asked of it. A station that will not play is
        // said once and handed back to the OST; the OST failing is not something to fall
        // back from, so it is left to the log.
        public static void Report(bool playing, string error, string title)
        {
            // The station's own, spliced into its audio and unpicked out there: it arrives
            // a second or so after a pick and changes on its own thereafter. Taken even
            // while muted, the mute being about the speakers rather than about the wire.
            _title = string.IsNullOrEmpty(title) ? null : title;

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

        // The daemon outlives the game: what it was told to play it keeps playing, and
        // whoever has just quit is not listening. Sent from a Root.Shutdown prefix, where
        // the socket is still up and MiniWebSocket writes on the calling thread, so the
        // bytes are in the kernel before the process goes. A killed game is not covered and
        // cannot be - nothing of ours gets to run - which is what `GET /api/audio` and a
        // restarted daemon are for.
        public static void Quit()
        {
            Read();
            // Whatever is on stays on or goes off here, and either way this is the end of
            // the conversation: the frames that follow have nothing left to say.
            _quit = true;
            if (!_stopOnExit) return;

            var hub = SessionHub.Instance;
            if (hub == null || !hub.Online) return;
            hub.SendAudio(null, Volume());
        }

        // Forces the next Update to send, rather than sending from here: one place puts
        // things on the wire, and it is the one that knows whether the socket is up. The
        // title goes with it - every caller is something about to change what is playing,
        // and the old song's name on the new station is worse than no name at all.
        static void Push()
        {
            _told = false;
            _title = null;
        }

        // Saved as the stream's own name - "ost", or the path the station serves that
        // preset at - so there is nothing to keep in step with the list of stations or with
        // any of their lists of presets. A name this build does not serve reads as the OST,
        // which is what a station that has been dropped comes back as.
        static void Save()
        {
            Settings.S.radio = _station == null ? "ost" : _station.Path(_station.Rate);
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

            string saved = Settings.Radio;
            foreach (var station in Stations)
            {
                foreach (int rate in station.Rates)
                {
                    if (saved != station.Path(rate)) continue;
                    _station = station;
                    station.Rate = rate;
                    return;
                }
            }

            // Anything else, "ost" included, is the OST. The stations keep the default
            // quality they were declared with, since nothing has been said about them.
            _station = null;
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
