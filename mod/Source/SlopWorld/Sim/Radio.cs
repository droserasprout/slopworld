using RimWorld;
using UnityEngine;
using Verse;

namespace SlopWorld
{
    // What the jukebox plays, decided here and played by the daemon. Both stations are the
    // same kind of thing to it - the built-in track is a path, the station is a URL - so
    // there is one mechanism rather than two taking turns, which is what the last version
    // of this file spent most of its lines on.
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
        public enum Station { Ost, Paradise }

        public const string StationName = "RadioParadise Main";

        // The quality presets the station actually serves. 64 and 96 are advertised in
        // various places on the web and 404 here, so this is the list that answers.
        public static readonly int[] Rates = { 32, 128, 192, 320 };

        const int DefaultRate = 128;

        // mp3 rather than the aac the station leads with: aac is what the game could not
        // decode, and there is no reason to hand the daemon a harder problem than the one
        // the player asked for. https because there is nothing in the way of it out there.
        static string StationUrl(int rate) => $"https://stream.radioparadise.com/mp3-{rate}";

        // The one track this mod ships, as a path under the mod's own folder. A path and
        // not a file:// URL so that neither end has anything to escape.
        static string OstPath()
        {
            var root = SlopWorldMod.Instance?.Content?.RootDir;
            return string.IsNullOrEmpty(root)
                ? null
                : System.IO.Path.Combine(root, "Sounds", "SlopWorld", "bg1.ogg");
        }

        static Station _station = Station.Ost;
        static int _rate = DefaultRate;

        // The settings string is read once, because the mod's settings are not loaded when
        // this class is first touched.
        static bool _read;

        // What was last sent, so a reconnect re-sends it and a still frame does not.
        static string _sent;
        static float _sentVolume = -1f;

        // What the daemon says came of it. Only the failure is acted on, and only once per
        // pick: a station that will not play should say so and hand the OST back.
        static bool _blamed;

        // A slider moved by a hair is not worth a packet.
        const float VolumeStep = 0.01f;

        public static Station Picked
        {
            get { Read(); return _station; }
        }

        // Which preset the station is on. Kept across a switch to the OST and back, so the
        // menu comes up on the quality it was left on.
        public static int Rate
        {
            get { Read(); return _rate; }
        }

        public static string RateLabel(int rate) => rate + "k mp3";

        public static void PickOst() { Read(); Pick(Station.Ost, _rate); }

        public static void PickParadise(int rate) => Pick(Station.Paradise, rate);

        static void Pick(Station s, int rate)
        {
            Read();
            if (_station == s && (s != Station.Paradise || _rate == rate)) return;

            _station = s;
            _rate = rate;
            _blamed = false;
            Save();
            Push();
        }

        // Every frame, menu and game alike, from Patch_Root_Update. Cheap: it sends only
        // what has changed, and most frames change nothing.
        public static void Update()
        {
            Read();

            // The game's own music manager stays off for good. It is not sharing the job
            // with anything any more - the OST is out there too - so this is a flag held
            // rather than the switching the two of them used to do. A load or a new colony
            // clears it, which is why it is asked every frame rather than once.
            var music = Find.MusicManagerPlay;
            if (music != null && !music.disabled) { music.Stop(); music.disabled = true; }

            var hub = SessionHub.Instance;
            if (hub == null) return;

            string want = Source();

            // A reconnect starts the daemon's socket over, and the mod is the only thing
            // that knows what was playing.
            if (want != _sent || !hub.Online)
            {
                if (!hub.Online) { _sent = null; _sentVolume = -1f; return; }
                hub.SendAudio(want, Volume());
                _sent = want;
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
            if (_station == Station.Paradise) return StationUrl(_rate);
            // No path means no shipped track to point at, which is silence rather than a
            // string the daemon would only fail to open.
            return OstPath();
        }

        // The daemon's answer to what was asked of it. A station that will not play is
        // said once and handed back to the OST; the OST failing is not something to fall
        // back from, so it is left to the log.
        public static void Report(bool playing, string error)
        {
            if (playing || string.IsNullOrEmpty(error)) { _blamed = false; return; }
            if (_blamed) return;
            _blamed = true;

            Log.Warning("[SlopWorld] jukebox: " + error);
            if (_station != Station.Paradise) return;

            Messages.Message($"Jukebox: {StationName} {RateLabel(_rate)} would not play. "
                + "Back to the OST.", MessageTypeDefOf.RejectInput, false);
            _station = Station.Ost;
            Save();
            Push();
        }

        // Forces the next Update to send, rather than sending from here: one place puts
        // things on the wire, and it is the one that knows whether the socket is up.
        static void Push() => _sent = null;

        // The setting is the stream's own name - "ost", or the path the station serves the
        // preset at - so there is nothing to keep in step with the list of presets. A name
        // this build does not serve reads as the OST.
        static void Save()
        {
            Settings.S.radio = _station == Station.Paradise ? "mp3-" + _rate : "ost";
            Settings.S.Write();
        }

        static void Read()
        {
            if (_read) return;
            _read = true;

            string saved = Settings.Radio;
            foreach (int rate in Rates)
            {
                if (saved != "mp3-" + rate) continue;
                _station = Station.Paradise;
                _rate = rate;
                return;
            }

            _station = Station.Ost;
            _rate = DefaultRate;
        }
    }
}
