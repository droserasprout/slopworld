using System.Numerics;
using Waylith.Audio;

namespace BrowserAdapters;

// Mixer/control plane for deliberately silent headless startup. Sound decoding
// is explicitly unsupported; never return a fabricated successfully loaded clip.
public sealed class SilentAudio : AudioBackend
{
    private sealed class Group : IAudioGroup
    {
        public float Volume { get; set; } = 1;
        public bool Muted { get; set; }
        public bool Paused { get; set; }
        public float Pitch { get; set; } = 1;
    }

    private bool initialized;
    public override string Name => "Browser silent mixer (no audio decoder/output)";
    public override bool IsInitialized => initialized;
    public override int SampleRate => 48000;
    public override int OutputChannels => 2;
    public override int DriverChannels => 0;
    public override int DspBufferFrames => 0;
    public override double DspTime => Waylith.Time.Elapsed;
    public override int PlayingVoices => 0;

    public override void Initialize(bool noAudio)
    {
        if (!noAudio)
            throw new NotSupportedException("SilentAudio requires no-audio mode");
        initialized = true;
    }

    public override void Shutdown() => initialized = false;

    public override void Update() { }

    public override void SetMasterVolume(float volume) { }

    public override void SetOutputPaused(bool paused) { }

    public override void SetListener(Vector3 position, Vector3 forward, Vector3 up) { }

    public override IAudioGroup CreateGroup(string name, IAudioGroup? parent)
    {
        if (!initialized)
            throw new InvalidOperationException("Audio mixer is stopped");
        return new Group();
    }

    private static Exception Unsupported() =>
        new NotSupportedException("Browser audio decoding is not implemented");

    public override IAudioSound LoadSound(string path, bool streaming) => throw Unsupported();

    public override IAudioSound LoadSoundFromMemory(ReadOnlySpan<byte> data, bool streaming) =>
        throw Unsupported();

    public override IAudioSound LoadSoundFromPcm16(
        ReadOnlySpan<byte> pcm,
        int channels,
        int frequency
    ) => throw Unsupported();

    public override IAudioSound CreateStreamSound(
        int channels,
        int frequency,
        long lengthFrames,
        IAudioStreamSource source
    ) => throw Unsupported();

    public override IAudioVoice? Play(
        IAudioSound sound,
        IAudioGroup? group,
        bool paused,
        bool autoRelease
    ) => throw Unsupported();
}
