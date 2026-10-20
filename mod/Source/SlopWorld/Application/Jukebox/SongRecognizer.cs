using System;
using System.Threading;

namespace SlopWorld
{
    // Track playback independently of station metadata. A station can play without an ICY title.
    // Reject recognition results after playback stops or the source changes.
    public sealed class RecognitionTrackState
    {
        public bool Playing { get; private set; }
        public bool Muted { get; private set; }
        public string Source { get; private set; }
        public int Revision { get; private set; }

        public bool Eligible => Playing && !Muted;

        public bool Update(bool playing, bool muted, string source)
        {
            bool changed =
                Playing != playing
                || Muted != muted
                || !string.Equals(Source, source, StringComparison.Ordinal);
            Playing = playing;
            Muted = muted;
            Source = source;
            if (changed)
                Revision++;
            return changed;
        }

        public void Advance() => Revision++;

        public bool IsCurrent(int revision, string source) =>
            Eligible
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

    // Keep SongRec calls independent of Unity and Verse so tests can use a fake process runner.
    // Radio checks whether results still match current playback. This module runs SongRec and reads its output.

    // Store the capture device and its display label.
    // A null device lets SongRec use its default input when pactl cannot provide a sink.
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

    // Include the input label with each result so the UI can identify the capture source.
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
        // Allow more time for song recognition than for the local sink query.
        public const int DefaultTimeoutMs = 30_000;
        const int SinkTimeoutMs = 1000;

        readonly IProcessRunner _runner;

        public SongRecognizer(IProcessRunner runner)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        }

        // Select the monitor of the default speaker sink to capture playback.
        // If pactl cannot provide a sink, let SongRec use its default input.
        public AudioInput SelectInput(CancellationToken cancel = default)
        {
            if (cancel.IsCancellationRequested)
                return new AudioInput(null, "default input");
            var run = _runner.Run(
                new ProcessSpec
                {
                    FileName = "pactl",
                    Arguments = "get-default-sink",
                    TimeoutMs = SinkTimeoutMs,
                },
                cancel
            );

            string sink = null;
            if (run != null && run.Started && !run.TimedOut && !run.Canceled && run.ExitCode == 0)
                sink = (run.StandardOutput ?? "").Trim();

            if (string.IsNullOrEmpty(sink))
                return new AudioInput(null, "default input");

            string monitor = sink.EndsWith(".monitor", StringComparison.Ordinal)
                ? sink
                : sink + ".monitor";
            return new AudioInput(monitor, "monitor of " + sink);
        }

        // Select an input and run recognition. Return Canceled when the caller cancels the request.
        public RecognitionResult Recognize(CancellationToken cancel = default)
        {
            return Recognize(SelectInput(cancel), cancel);
        }

        // Accept a selected input so the caller can display its label before recognition completes.
        public RecognitionResult Recognize(AudioInput input, CancellationToken cancel = default)
        {
            if (input == null)
                input = SelectInput(cancel);
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
                new ProcessSpec
                {
                    FileName = "songrec",
                    Arguments = arguments,
                    TimeoutMs = DefaultTimeoutMs,
                },
                cancel
            );

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

            string artist,
                title;
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

        // Extract JSON between the outermost braces because SongRec can print progress lines before it.
        // Treat invalid JSON as no match. The caller uses standard error for the failure message.
        internal static void ParseRecognition(string output, out string artist, out string title)
        {
            artist = null;
            title = null;
            int first = output == null ? -1 : output.IndexOf('{');
            int last = output == null ? -1 : output.LastIndexOf('}');
            if (first < 0 || last <= first)
                return;

            try
            {
                var track = Newtonsoft.Json.Linq.JObject.Parse(
                    output.Substring(first, last - first + 1)
                )["track"];
                artist = ((string)track?["subtitle"])?.Trim();
                title = ((string)track?["title"])?.Trim();
            }
            catch
            {
                // Ignore parsing failures. The caller treats missing artist or title values as no match.
            }
        }

        // Use at most 240 characters from the first nonempty line after trimming standard error.
        // Use the fallback if no text remains.
        internal static string ShortError(string stderr, string fallback)
        {
            string text = (stderr ?? "").Trim();
            if (text.Length == 0)
                return fallback;
            int line = text.IndexOf('\n');
            if (line >= 0)
                text = text.Substring(0, line).Trim();
            if (text.Length == 0)
                return fallback;
            return text.Length > 240 ? text.Substring(0, 240) : text;
        }

        internal static string ProcessArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
