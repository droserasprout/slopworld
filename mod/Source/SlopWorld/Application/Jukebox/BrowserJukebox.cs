#nullable enable

namespace SlopWorld
{
    internal interface IBrowserJukebox
    {
        string BrowserName { get; }
        bool Playing { get; }
        string Error { get; }
        string Title { get; }
        string Stream { get; }
        NativeTrackSnapshot? ReadNativeTrack();
        void Navigate(int direction, bool random);
        void PlayOst();
        void PlayRadio(string station, string[] streams);
        void Stop();
        void SetVolume(float volume);
    }

    internal static class BrowserJukebox
    {
        internal static IBrowserJukebox? Current { get; set; }
        internal static bool Available => Current != null;
    }
}
