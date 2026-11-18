using System.Text.Json;
using BrowserAdapters;

static class Program
{
    static void Require(bool value)
    {
        if (!value)
            throw new Exception("Audio contract failed");
    }

    static string Track()
    {
        using var state = JsonDocument.Parse(BrowserAudio.Snapshot());
        return state.RootElement.GetProperty("track").GetString()!;
    }

    static void Main()
    {
        Require(Track() == "pace-20261012");
        BrowserAudio.Playing(true);
        Require(BrowserAudio.ReadNativeTrack() == null);
        BrowserAudio.SetMuted(false);
        Require(
            BrowserAudio.ReadNativeTrack()
                is SlopWorld.NativeTrackSnapshot { Clip: "pace-20261012" }
        );
        BrowserAudio.Navigate(1, false);
        Require(Track() == "dive-20261012" && BrowserAudio.ReadNativeTrack() == null);
        BrowserAudio.Navigate(-1, false);
        BrowserAudio.Navigate(-1, false);
        Require(Track() == "dawn-20261012");
        BrowserAudio.Ended();
        Require(Track() == "pace-20261012");
        BrowserAudio.SetMuted(true);
        BrowserAudio.Ended();
        Require(Track() == "pace-20261012");
        BrowserAudio.Navigate(1, true);
        Require(Track() != "pace-20261012");
        Require(Math.Abs(BrowserAudio.Volume - .1f) < .0001f);
        Console.WriteLine("Browser audio policy contracts passed");
    }
}

namespace Verse
{
    public static class Prefs
    {
        public static float VolumeMusic => .2f;
        public static float VolumeMaster => .5f;
    }
}

namespace SlopWorld
{
    public sealed record NativeTrackSnapshot(string Clip)
    {
        public static NativeTrackSnapshot FromOstClipPath(string path) => new(path);
    }
}
