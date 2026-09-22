using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SlopWorld
{
    // Playback identity is deliberately independent from station metadata. A station may be
    // audible while its ICY title is empty, and a late recognition result must still be rejected
    // after a stop or source replacement.
    public sealed class RecognitionTrackState
    {
        public bool Playing { get; private set; }
        public bool Muted { get; private set; }
        public string Source { get; private set; }
        public int Revision { get; private set; }

        public bool Eligible => Playing && !Muted;

        public bool Update(bool playing, bool muted, string source)
        {
            bool changed = Playing != playing || Muted != muted
                || !string.Equals(Source, source, StringComparison.Ordinal);
            Playing = playing;
            Muted = muted;
            Source = source;
            if (changed) Revision++;
            return changed;
        }

        public void Advance() => Revision++;

        public bool IsCurrent(int revision, string source) => Eligible
            && Revision == revision
            && string.Equals(Source, source, StringComparison.Ordinal);

        public bool CanApply(int revision, string source, CancellationToken cancel) =>
            !cancel.IsCancellationRequested && IsCurrent(revision, source);
    }

    public interface IRecognitionService
    {
        AudioInput SelectInput(CancellationToken cancel);
        RecognitionResult Recognize(AudioInput input, CancellationToken cancel);
    }

    // The SongRec boundary, lifted out of Radio so playback state and this external lookup
    // stop sharing one method. Everything here is deliberately free of Unity and Verse so the
    // process spawning, device discovery, JSON parsing and timeout behaviour can be exercised
    // with a fake runner in the game-free tests. Radio owns the "is this still the track we
    // heard" question; this owns "what did SongRec say".

    // A single command invocation, described so a test can answer it without a real process.
    public sealed class ProcessSpec
    {
        public string FileName;
        public string Arguments;
        public int TimeoutMs;
    }

    // What became of one invocation. `Started` is false when the executable is missing - the
    // one failure a jukebox on a microphone-only or songrec-less box must survive quietly.
    public sealed class ProcessRun
    {
        public bool Started;
        public bool TimedOut;
        public bool Canceled;
        public int ExitCode;
        public string StandardOutput = "";
        public string StandardError = "";
    }

    // The seam the tests inject across. The real one spawns a child; a fake one answers from a
    // table keyed on the command name.
    public interface IProcessRunner
    {
        ProcessRun Run(ProcessSpec spec, CancellationToken cancel);
    }

    // The chosen capture source and a label the UI can show. A null device leaves SongRec on
    // its own default input, which is what a box without pactl gets.
    public sealed class AudioInput
    {
        public readonly string Device;
        public readonly string Label;

        public AudioInput(string device, string label)
        {
            Device = device;
            Label = label;
        }
    }

    public enum RecognitionStatus
    {
        Ok,
        NoMatch,
        ProcessMissing,
        TimedOut,
        Canceled,
        Error,
    }

    // A typed answer in place of the old three out-parameters. `Input` travels with every
    // result, success or failure, so the UI can always name where it listened.
    public sealed class RecognitionResult
    {
        public RecognitionStatus Status;
        public string Artist;
        public string Title;
        public string Input;
        public string Message;

        public bool Ok => Status == RecognitionStatus.Ok;
    }

    public sealed class SongRecognizer : IRecognitionService
    {
        // Shazam over a full stream can take a while, so the lookup is patient; the sink probe
        // is a local query that either answers at once or is not there at all.
        public const int DefaultTimeoutMs = 30_000;
        const int SinkTimeoutMs = 1000;

        readonly IProcessRunner _runner;

        public SongRecognizer(IProcessRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        // SongRec's bare command captures the default input source, usually a microphone or a
        // loopback capture, while the useful signal is the monitor of the current speaker sink.
        // PulseAudio's compatibility CLI is available on PipeWire too; when it is absent, leave
        // the device unchosen so microphone-only setups still work.
        public AudioInput SelectInput(CancellationToken cancel = default)
        {
            var run = _runner.Run(
                new ProcessSpec { FileName = "pactl", Arguments = "get-default-sink", TimeoutMs = SinkTimeoutMs },
                cancel);

            string sink = null;
            if (run != null && run.Started && !run.TimedOut && !run.Canceled && run.ExitCode == 0)
                sink = (run.StandardOutput ?? "").Trim();

            if (string.IsNullOrEmpty(sink))
                return new AudioInput(null, "default input");

            string monitor = sink.EndsWith(".monitor", StringComparison.Ordinal)
                ? sink : sink + ".monitor";
            return new AudioInput(monitor, "monitor of " + sink);
        }

        // Run one lookup end to end: pick the input, ask SongRec, and turn its output into a
        // typed result. Cancellation short-circuits to a quiet Canceled the caller can drop.
        public RecognitionResult Recognize(CancellationToken cancel = default)
        {
            return Recognize(SelectInput(cancel), cancel);
        }

        // The same, with the input already chosen - so a caller can surface which source it
        // settled on before the slow Shazam call returns.
        public RecognitionResult Recognize(AudioInput input, CancellationToken cancel = default)
        {
            if (input == null) input = SelectInput(cancel);
            var result = new RecognitionResult { Input = input.Label };
            if (cancel.IsCancellationRequested)
            {
                result.Status = RecognitionStatus.Canceled;
                return result;
            }

            string arguments = "recognize --json";
            if (!string.IsNullOrEmpty(input.Device))
                arguments += " --audio-device " + ProcessArgument(input.Device);

            ProcessRun run = _runner.Run(
                new ProcessSpec { FileName = "songrec", Arguments = arguments, TimeoutMs = DefaultTimeoutMs },
                cancel);

            if (run != null && run.Canceled || cancel.IsCancellationRequested)
            {
                result.Status = RecognitionStatus.Canceled;
                return result;
            }
            if (run == null || !run.Started)
            {
                result.Status = RecognitionStatus.ProcessMissing;
                result.Message = "could not start songrec";
                return result;
            }
            if (run.TimedOut)
            {
                result.Status = RecognitionStatus.TimedOut;
                result.Message = "recognition timed out";
                return result;
            }

            string artist, title;
            ParseRecognition(run.StandardOutput, out artist, out title);
            if (string.IsNullOrEmpty(artist) || string.IsNullOrEmpty(title))
            {
                result.Status = RecognitionStatus.NoMatch;
                result.Message = ShortError(run.StandardError, "songrec found no matching track");
                return result;
            }

            result.Status = RecognitionStatus.Ok;
            result.Artist = artist.Trim();
            result.Title = title.Trim();
            return result;
        }

        // SongRec prints a JSON object to stdout; some builds precede it with progress lines,
        // so take the outermost braces rather than the whole stream. Malformed JSON is not a
        // crash - it is simply no match, and the caller falls back to stderr for a reason.
        internal static void ParseRecognition(string output, out string artist, out string title)
        {
            artist = null;
            title = null;
            int first = output == null ? -1 : output.IndexOf('{');
            int last = output == null ? -1 : output.LastIndexOf('}');
            if (first < 0 || last <= first) return;

            try
            {
                var track = Newtonsoft.Json.Linq.JObject.Parse(output.Substring(first, last - first + 1))["track"];
                artist = ((string)track?["subtitle"])?.Trim();
                title = ((string)track?["title"])?.Trim();
            }
            catch
            {
                // Leave both null: an unparseable response is a failed lookup, not an error to
                // surface to the player.
            }
        }

        // The first line of stderr, trimmed and capped, gives a specific reason for a failed
        // lookup - a missing device or a network error - without pasting a stack into a toast.
        internal static string ShortError(string stderr, string fallback)
        {
            string text = (stderr ?? "").Trim();
            if (text.Length == 0) return fallback;
            int line = text.IndexOf('\n');
            if (line >= 0) text = text.Substring(0, line).Trim();
            if (text.Length == 0) return fallback;
            return text.Length > 240 ? text.Substring(0, 240) : text;
        }

        internal static string ProcessArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }

    // The real seam: a child process whose stdout/stderr are drained while it runs, killed on
    // timeout or on cancellation. Kept beside the recognizer because it too avoids Unity, so
    // the whole file compiles into the test assembly. Local helper commands exercise process
    // output, timeout and cancellation without invoking audio capture or SongRec.
    public sealed class SystemProcessRunner : IProcessRunner
    {
        public ProcessRun Run(ProcessSpec spec, CancellationToken cancel)
        {
            var run = new ProcessRun();
            var info = new ProcessStartInfo
            {
                FileName = spec.FileName,
                Arguments = spec.Arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            try
            {
                using (var process = new Process { StartInfo = info })
                {
                    try
                    {
                        if (!process.Start()) return run;
                    }
                    catch
                    {
                        // A missing executable throws here (Win32Exception); report it as a
                        // clean "did not start" rather than an error status.
                        return run;
                    }

                    run.Started = true;
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    Task<string> error = process.StandardError.ReadToEndAsync();

                    using (cancel.Register(() => Kill(process)))
                    {
                        if (!process.WaitForExit(spec.TimeoutMs))
                        {
                            Kill(process);
                            try { process.WaitForExit(1000); } catch { }
                            run.TimedOut = true;
                        }
                    }

                    run.Canceled = cancel.IsCancellationRequested;
                    run.StandardOutput = Drain(output);
                    run.StandardError = Drain(error);
                    try { run.ExitCode = process.ExitCode; } catch { }
                    return run;
                }
            }
            catch
            {
                return run;
            }
        }

        static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); } catch { }
        }

        static string Drain(Task<string> task)
        {
            try { return task.GetAwaiter().GetResult() ?? ""; }
            catch { return ""; }
        }
    }
}
