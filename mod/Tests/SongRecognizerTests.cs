using System;
using System.Collections.Generic;
using System.Threading;

namespace SlopWorld.Tests
{
    static class SongRecognizerTests
    {
        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            yield return ("selects the default sink's monitor", SelectsMonitor);
            yield return ("does not double an existing monitor suffix", KeepsMonitorSuffix);
            yield return ("falls back to the default input without pactl", MissingPactl);
            yield return ("reports songrec's absence as process missing", MissingSongrec);
            yield return ("reports a timeout", Timeout);
            yield return ("treats malformed JSON as no match", MalformedJson);
            yield return ("surfaces the first stderr line on no match", StderrDiagnostics);
            yield return ("returns the recognized artist and title", Success);
            yield return ("passes the monitor device to songrec", PassesDeviceToSongrec);
        }

        public static void CanceledLookupDoesNotStartRecognition()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var runner = Runner(Missing(), Ok(TrackJson("A", "B")));
            var result = new SongRecognizer(runner).Recognize(new AudioInput("device", "chosen input"), cancel.Token);
            AssertEx.Equal(RecognitionStatus.Canceled, result.Status, "canceled lookup is quiet");
            AssertEx.Equal("chosen input", result.Input, "input label survives cancellation");
            AssertEx.Equal(0, runner.Calls.Count, "already canceled recognition launches nothing");
        }

        public static void RunnerCancellationOverridesSuccessfulOutput()
        {
            var run = Ok(TrackJson("A", "B"));
            run.Canceled = true;
            var result = new SongRecognizer(Runner(Missing(), run)).Recognize(new AudioInput(null, "fallback"));
            AssertEx.Equal(RecognitionStatus.Canceled, result.Status, "canceled subprocess result is not a match");
            AssertEx.Equal(null, result.Artist, "canceled output is not published");
            AssertEx.Equal("fallback", result.Input, "failed lookup retains input identity");
        }

        public static void FailedSinkProbesIgnoreOutputAndUseDefaultInput()
        {
            foreach (var probe in new ProcessRun[]
            {
                null,
                new ProcessRun { Started = true, TimedOut = true, StandardOutput = "stale" },
                new ProcessRun { Started = true, Canceled = true, StandardOutput = "stale" },
                new ProcessRun { Started = true, ExitCode = 1, StandardOutput = "stale" },
                new ProcessRun { Started = true, StandardOutput = null },
                Ok("   "),
            })
            {
                var runner = Runner(probe, null);
                var recognizer = new SongRecognizer(runner);
                var input = recognizer.SelectInput();
                AssertEx.Equal(null, input.Device, "failed or empty probe does not invent monitor");
                AssertEx.Equal("default input", input.Label, "fallback label");
                AssertEx.Equal(1000, runner.Calls[0].TimeoutMs, "probe is bounded");
                var result = recognizer.Recognize((AudioInput)null);
                AssertEx.Equal(RecognitionStatus.ProcessMissing, result.Status, "null runner reply is process missing");
                AssertEx.Equal("default input", result.Input, "null input triggers selection");
            }
        }

        public static void DiagnosticsAreTrimmedAndBounded()
        {
            AssertEx.Equal("fallback", SongRecognizer.ShortError(null, "fallback"), "null diagnostics use fallback");
            AssertEx.Equal("fallback", SongRecognizer.ShortError(" \r\n ", "fallback"), "blank diagnostics use fallback");
            AssertEx.Equal("first", SongRecognizer.ShortError("  first  \r\nsecond", "fallback"), "only first trimmed line is shown");
            AssertEx.Equal(new string('x', 240), SongRecognizer.ShortError(new string('x', 300), "fallback"), "long diagnostics are capped");
            AssertEx.Throws<ArgumentNullException>(() => new SongRecognizer(null), "runner is required");
        }

        // A runner that answers each command from a table keyed on the executable name and
        // records the specs it was asked to run, so a test can also assert the argv.
        sealed class FakeRunner : IProcessRunner
        {
            readonly Dictionary<string, ProcessRun> _answers;
            public readonly List<ProcessSpec> Calls = new List<ProcessSpec>();

            public FakeRunner(Dictionary<string, ProcessRun> answers)
            {
                _answers = answers;
            }

            public ProcessRun Run(ProcessSpec spec, CancellationToken cancel)
            {
                Calls.Add(spec);
                return _answers.TryGetValue(spec.FileName, out var run) ? run : Missing();
            }
        }

        static ProcessRun Missing() => new ProcessRun { Started = false };

        static ProcessRun Ok(string stdout, string stderr = "") =>
            new ProcessRun { Started = true, ExitCode = 0, StandardOutput = stdout, StandardError = stderr };

        static FakeRunner Runner(ProcessRun pactl, ProcessRun songrec)
        {
            return new FakeRunner(new Dictionary<string, ProcessRun>
            {
                ["pactl"] = pactl,
                ["songrec"] = songrec,
            });
        }

        static string TrackJson(string artist, string title) =>
            "{\"track\":{\"subtitle\":\"" + artist + "\",\"title\":\"" + title + "\"}}";

        static void SelectsMonitor()
        {
            var runner = Runner(Ok("alsa_output.pci.analog-stereo"), Missing());
            var input = new SongRecognizer(runner).SelectInput();
            AssertEx.Equal("alsa_output.pci.analog-stereo.monitor", input.Device,
                "the sink's monitor source is chosen");
            AssertEx.Equal("monitor of alsa_output.pci.analog-stereo", input.Label,
                "the label names the sink");
        }

        static void KeepsMonitorSuffix()
        {
            var runner = Runner(Ok("some.sink.monitor"), Missing());
            var input = new SongRecognizer(runner).SelectInput();
            AssertEx.Equal("some.sink.monitor", input.Device,
                "an already-monitor sink is not suffixed twice");
        }

        static void MissingPactl()
        {
            var runner = Runner(Missing(), Missing());
            var input = new SongRecognizer(runner).SelectInput();
            AssertEx.Equal(null, input.Device, "no device is chosen without pactl");
            AssertEx.Equal("default input", input.Label, "the label names the fallback");
        }

        static void MissingSongrec()
        {
            var result = new SongRecognizer(Runner(Missing(), Missing())).Recognize();
            AssertEx.Equal(RecognitionStatus.ProcessMissing, result.Status, "songrec is absent");
            AssertEx.Equal("could not start songrec", result.Message, "the reason is specific");
        }

        static void Timeout()
        {
            var songrec = new ProcessRun { Started = true, TimedOut = true };
            var result = new SongRecognizer(Runner(Missing(), songrec)).Recognize();
            AssertEx.Equal(RecognitionStatus.TimedOut, result.Status, "the lookup timed out");
            AssertEx.Equal("recognition timed out", result.Message, "the reason is specific");
        }

        static void MalformedJson()
        {
            var result = new SongRecognizer(Runner(Missing(), Ok("this is not json"))).Recognize();
            AssertEx.Equal(RecognitionStatus.NoMatch, result.Status, "garbage output is no match");

            var braced = new SongRecognizer(Runner(Missing(), Ok("{ not really json }"))).Recognize();
            AssertEx.Equal(RecognitionStatus.NoMatch, braced.Status, "unparseable braces are no match");

            var empty = new SongRecognizer(Runner(Missing(), Ok("{\"track\":{}}"))).Recognize();
            AssertEx.Equal(RecognitionStatus.NoMatch, empty.Status, "a track without names is no match");
        }

        static void StderrDiagnostics()
        {
            var songrec = Ok("no match here", "Error: no audio device found\nsecond line ignored");
            var result = new SongRecognizer(Runner(Missing(), songrec)).Recognize();
            AssertEx.Equal(RecognitionStatus.NoMatch, result.Status, "no track was found");
            AssertEx.Equal("Error: no audio device found", result.Message,
                "the first stderr line becomes the reason");
        }

        static void Success()
        {
            var songrec = Ok("noise\n" + TrackJson("  The Artist ", " The Song ") + "\ntrailer");
            var result = new SongRecognizer(Runner(Missing(), songrec)).Recognize();
            AssertEx.True(result.Ok, "a track was recognized");
            AssertEx.Equal("The Artist", result.Artist, "the artist is trimmed");
            AssertEx.Equal("The Song", result.Title, "the title is trimmed");
        }

        static void PassesDeviceToSongrec()
        {
            var runner = Runner(Ok("speakers"), Ok(TrackJson("A", "B")));
            new SongRecognizer(runner).Recognize();

            ProcessSpec songrec = runner.Calls.Find(c => c.FileName == "songrec");
            AssertEx.True(songrec != null, "songrec was invoked");
            AssertEx.True(songrec.Arguments.Contains("--audio-device \"speakers.monitor\""),
                "the chosen monitor is passed through: " + songrec.Arguments);

            var noPactl = Runner(Missing(), Ok(TrackJson("A", "B")));
            new SongRecognizer(noPactl).Recognize();
            ProcessSpec bare = noPactl.Calls.Find(c => c.FileName == "songrec");
            AssertEx.Equal("recognize --json", bare.Arguments,
                "without a device songrec keeps its default input");
        }
    }
}
