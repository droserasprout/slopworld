using System.Text.Json;
using BrowserAdapters;

static class Program
{
    static readonly BrowserAudio Audio = new();

    static void Require(bool value, string message)
    {
        if (!value)
            throw new Exception(message);
    }

    static JsonElement State() => JsonSerializer.Deserialize<JsonElement>(Audio.Snapshot());

    static void Main()
    {
        var tracks = BrowserOstManifest.Tracks;
        Require(
            !State().GetProperty("play").GetBoolean(),
            "Startup must await local output selection"
        );
        Require(Audio.BrowserName == "Browser", "Browser identity fallback");
        Audio.Identify("Firefox");
        Require(Audio.BrowserName == "Firefox", "Browser identity was lost");
        Audio.PlayOst();
        var state = State();
        int first = state.GetProperty("revision").GetInt32();
        Require(state.GetProperty("track").GetString() == tracks[0], "Initial OST selection");
        Audio.Report(first, true, "", "", "");
        Require(
            Audio.ReadNativeTrack() is SlopWorld.NativeTrackSnapshot track
                && track.Clip == tracks[0],
            "Live OST metadata"
        );
        Audio.Ended(first);
        Require(
            State().GetProperty("track").GetString() == tracks[1] && !Audio.Playing,
            "End advances OST"
        );
        Audio.Ended(first);
        Require(
            State().GetProperty("track").GetString() == tracks[1],
            "Late OST end advanced current track"
        );
        Audio.Navigate(-1, false);
        Audio.Navigate(-1, false);
        Require(State().GetProperty("track").GetString() == tracks[^1], "Previous track wraps");

        Audio.PlayRadio("example", ["stream1", "stream2", "stream1", ""]);
        state = State();
        int radio = state.GetProperty("revision").GetInt32();
        Require(
            state.GetProperty("streams").GetArrayLength() == 2,
            "Duplicate or missing streams reached the decoder"
        );
        Audio.Report(radio, true, "stream2", "Artist - Song", "");
        Audio.Report(first, false, "", "", "stale failure");
        Require(Audio.ReadNativeTrack() == null && Audio.Playing, "Radio playback reported OST");
        Require(
            Audio.Stream == "stream2" && Audio.Title == "Artist - Song" && Audio.Error == "",
            "Stale report replaced local metadata"
        );

        Audio.SetVolume(.3f);
        Require(
            State().GetProperty("volume").GetSingle() == .3f
                && State().GetProperty("revision").GetInt32() == radio,
            "Volume reconnected listener"
        );
        Audio.Stop();
        Audio.Report(radio, true, "stream1", "stale", "");
        Require(
            !Audio.Playing && Audio.Title == "" && Audio.Stream == "",
            "Stop retained the previous listener"
        );
        Audio.PlayOst();
        Require(
            State().GetProperty("track").GetString() == tracks[^1],
            "Radio playback discarded local OST selection"
        );
        Audio.Navigate(1, true);
        Require(
            State().GetProperty("track").GetString() != tracks[^1],
            "Shuffle repeated current track"
        );
        Console.WriteLine("Browser audio playback and generation contracts passed");
    }
}

namespace SlopWorld
{
    public sealed record NativeTrackSnapshot(string Clip)
    {
        public static NativeTrackSnapshot FromOstClipPath(string path) => new(path);
    }
}
