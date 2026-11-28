using System;
using System.IO;

namespace SlopWorld
{
    // Isolate settings writes from source actions and restore the surrounding profile.
    internal sealed class RadioTestProfile : IDisposable
    {
        readonly string _oldFolder = Verse.GenFilePaths.SaveDataFolderPath;
        readonly string _folder = Path.Combine(Path.GetTempPath(), "jukebox-" + Guid.NewGuid());
        readonly string _hidden = Settings.S.radioHiddenSources;
        readonly string _selection = Settings.S.radio;
        readonly string _output = Settings.S.radioOutput;
        readonly float _volume = Settings.S.radioVolume;
        readonly bool _mute = Settings.S.radioMute;
        readonly bool _stop = Settings.S.radioStopOnExit;

        internal RadioTestProfile()
        {
            Verse.GenFilePaths.SaveDataFolderPath = _folder;
            Settings.S.radio = "ost";
            Settings.S.radioOutput = "local";
            Settings.S.radioVolume = 0.8f;
            Settings.S.radioMute = false;
            Settings.S.radioStopOnExit = true;
        }

        public void Dispose()
        {
            Verse.GenFilePaths.SaveDataFolderPath = _oldFolder;
            Settings.S.radioHiddenSources = _hidden;
            Settings.S.radio = _selection;
            Settings.S.radioOutput = _output;
            Settings.S.radioVolume = _volume;
            Settings.S.radioMute = _mute;
            Settings.S.radioStopOnExit = _stop;
            if (Directory.Exists(_folder))
                Directory.Delete(_folder, true);
        }
    }

    // Simulates the browser decoder boundary. The routing tests exercise the production
    // Radio entry points and serialized socket commands without a media element.
    internal sealed class FakeBrowserJukebox : IBrowserJukebox
    {
        public string BrowserName => "Browser";
        public bool Playing { get; set; }
        public string Stream { get; set; } = "";
        public string Title { get; set; } = "";
        public string Error { get; set; } = "";
        public int Starts;
        public int Stops;
        public string Station;
        public string[] Streams;
        public float Volume;

        public NativeTrackSnapshot ReadNativeTrack() =>
            Playing && Station == null
                ? NativeTrackSnapshot.FromOstClipPath("pace-20261012")
                : null;

        public int Direction { get; private set; }

        public void Navigate(int direction, bool random) => Direction = direction;

        public void PlayOst()
        {
            Station = null;
            Streams = null;
            Error = "";
            Starts++;
            Playing = true;
        }

        public void PlayRadio(string station, string[] streams)
        {
            PlayOst();
            Station = station;
            Streams = streams;
            Stream = streams[0];
        }

        public void Stop()
        {
            Stops++;
            Playing = false;
            Stream = Title = Error = "";
        }

        public void SetVolume(float value) => Volume = value;
    }
}
