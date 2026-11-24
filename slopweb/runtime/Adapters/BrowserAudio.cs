// BrowserJukebox commands describe one local listener. The DOM owns decoding,
// permission and relay requests. Reports belong to the command that opened them.
using System.Text.Json;
using SlopWorld;

namespace BrowserAdapters;

internal sealed class BrowserAudio : IBrowserJukebox
{
    int index;
    int revision;
    bool requested;
    string station = "";
    string[] streams = [];
    float volume = 1;
    string? snapshot;

    internal static BrowserAudio Instance { get; } = new();
    public string BrowserName { get; private set; } = "Browser";
    public string Error { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Stream { get; private set; } = "";
    public bool Playing { get; private set; }

    public void Identify(string name) => BrowserName = name;

    void Begin(bool play)
    {
        requested = play;
        Playing = false;
        Error = Title = Stream = "";
        revision++;
        snapshot = null;
    }

    public void PlayOst()
    {
        station = "";
        streams = [];
        Begin(true);
    }

    public void PlayRadio(string station, string[] streams)
    {
        this.station = station;
        this.streams = streams.Where(key => !string.IsNullOrEmpty(key)).Distinct().ToArray();
        Begin(true);
    }

    public void Stop() => Begin(false);

    public void SetVolume(float value)
    {
        value = float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
        if (volume == value)
            return;
        volume = value;
        snapshot = null;
    }

    public void Navigate(int direction, bool random)
    {
        int count = BrowserOstManifest.Tracks.Length;
        index =
            (index + (random ? Random.Shared.Next(1, count) : Math.Sign(direction)) + count)
            % count;
        if (station.Length == 0)
            Begin(requested);
    }

    public void Ended(int run)
    {
        if (run == revision && requested && station.Length == 0)
            Navigate(1, false);
    }

    public void Report(int run, bool playing, string stream, string title, string error)
    {
        if (run != revision || !requested)
            return;
        Playing = playing;
        Stream = stream;
        Title = title;
        Error = error;
    }

    public NativeTrackSnapshot? ReadNativeTrack() =>
        Playing && station.Length == 0
            ? NativeTrackSnapshot.FromOstClipPath(BrowserOstManifest.Tracks[index])
            : null;

    public string Snapshot() =>
        snapshot ??= JsonSerializer.Serialize(
            new
            {
                revision,
                play = requested,
                track = BrowserOstManifest.Tracks[index],
                station,
                streams,
                volume,
            }
        );
}
