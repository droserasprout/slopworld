// Browser OST transport only. Radio retains source selection, daemon handoff,
// mute and volume policy; the DOM owns decoding and autoplay permission.
using System.Reflection;
using System.Text.Json;

namespace BrowserAdapters;

public static class BrowserAudio
{
    static readonly string[] Tracks =
    [
        "pace-20261012",
        "dive-20261012",
        "hime-20261012",
        "dawn-20261012",
    ];
    static readonly Lazy<MethodInfo> TrackFactory = new(() =>
        Type.GetType("SlopWorld.NativeTrackSnapshot, SlopWorld", true)!
            .GetMethod("FromOstClipPath")!
    );
    static int index;
    static int revision;
    static bool muted = true;
    static bool playing;
    static readonly Random Dice = new();

    public static void SetMuted(bool value) => muted = value;

    public static void Resume() => revision++;

    public static void Navigate(int direction, bool random)
    {
        index =
            (index + (random ? Dice.Next(1, Tracks.Length) : direction) + Tracks.Length)
            % Tracks.Length;
        playing = false;
        revision++;
    }

    public static void Ended()
    {
        if (!muted)
            Navigate(1, false);
    }

    public static void Playing(bool value) => playing = value;

    public static object? ReadNativeTrack() =>
        !muted && playing ? TrackFactory.Value.Invoke(null, [Tracks[index]]) : null;

    public static float Volume =>
        Math.Clamp(Verse.Prefs.VolumeMusic * Verse.Prefs.VolumeMaster, 0, 1);

    public static string Snapshot() =>
        JsonSerializer.Serialize(
            new
            {
                track = Tracks[index],
                revision,
                play = !muted,
                volume = Volume,
            }
        );
}
