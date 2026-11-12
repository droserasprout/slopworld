using System.Text.Json;
using BrowserAdapters;

static class Program
{
    static void Require(bool value)
    {
        if (!value)
            throw new Exception("Audio/portrait contract failed");
    }

    static string Track()
    {
        using var state = JsonDocument.Parse(BrowserAudio.Snapshot());
        return state.RootElement.GetProperty("track").GetString()!;
    }

    static void Main()
    {
        BrowserPortraits.Bind();
        Require(
            ReferenceEquals(SlopWorld.SessionPortraits.Source!("first"), SlopWorld.Icons.Agents)
        );
        Require(
            ReferenceEquals(SlopWorld.SessionPortraits.Source!("second"), SlopWorld.Icons.Agents)
        );
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
        Console.WriteLine("Browser audio policy and shared portrait icon contracts passed");
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

namespace UnityEngine
{
    public class Texture { }
}

namespace SlopWorld
{
    public static class Icons
    {
        public static UnityEngine.Texture Agents { get; } = new();
    }

    public static class SessionPortraits
    {
        public static Func<string, UnityEngine.Texture?>? Source;
    }

    public sealed record NativeTrackSnapshot(string Clip)
    {
        public static NativeTrackSnapshot FromOstClipPath(string path) => new(path);
    }
}
