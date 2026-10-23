using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SystemProcessRunnerTests
    {
        public static void BinaryCapturePreservesNonUtf8AndRejectsOversize()
        {
            var spec = Shell("printf '\\377\\000\\200A'");
            spec.BinaryOutputLimit = 4;
            var run = new SystemProcessRunner().Run(spec, CancellationToken.None);
            Assert.That(run.ExitCode, Is.Zero);
            Assert.That(run.BinaryOutput, Is.EqualTo(new byte[] { 255, 0, 128, 65 }));
            spec.BinaryOutputLimit = 3;
            run = new SystemProcessRunner().Run(spec, CancellationToken.None);
            Assert.That(run.ExitCode, Is.Not.Zero);
            Assert.That(run.BinaryOutput, Is.Empty);
        }

        static ProcessSpec Shell(string command, int timeout = 5000) =>
            new ProcessSpec
            {
                FileName = "/bin/sh",
                Arguments = "-c \"" + command + "\"",
                TimeoutMs = timeout,
            };

        public static void StandardInputPreservesLiteralUnicodeAndNewlines()
        {
            var spec = Shell("cat");
            spec.StandardInput = "é\n$(literal) `literal`\n";
            var run = new SystemProcessRunner().Run(spec, CancellationToken.None);
            Assert.That(run.Started, Is.True);
            Assert.That(run.ExitCode, Is.Zero);
            Assert.That(run.StandardOutput, Is.EqualTo(spec.StandardInput));
        }

        public static void StandardInputUsesUtf8EvenWhenConsoleEncodingIsAscii()
        {
            var original = Console.InputEncoding;
            try
            {
                Console.InputEncoding = System.Text.Encoding.ASCII;
                var spec = Shell("cat");
                spec.StandardInput = "é Ελληνικά 日本語 🙂 — ’\n";
                spec.BinaryOutputLimit = 1024;
                var run = new SystemProcessRunner().Run(spec, CancellationToken.None);
                Assert.That(run.ExitCode, Is.Zero);
                Assert.That(
                    run.BinaryOutput,
                    Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(spec.StandardInput))
                );
            }
            finally
            {
                Console.InputEncoding = original;
            }
        }

        public static void BlockedStandardInputObeysProcessDeadline()
        {
            var spec = Shell("exec /bin/sleep 5", 200);
            spec.StandardInput = new string('x', 1024 * 1024);
            var run = new SystemProcessRunner().Run(spec, CancellationToken.None);
            Assert.That(run.Started, Is.True);
            Assert.That(run.ExitCode, Is.Not.Zero);
        }

        public static void InheritedPipesObeyDeadlineAndCancellationAfterParentExits()
        {
            foreach (bool input in new[] { false, true })
            foreach (bool canceled in new[] { false, true })
            {
                string pidFile = Path.Combine(
                    Path.GetTempPath(),
                    "slopworld-pipe-" + Guid.NewGuid().ToString("N")
                );
                using var cancel = new CancellationTokenSource();
                try
                {
                    // Save stdin before Dash replaces it with /dev/null for background jobs;
                    // <&0 alone would duplicate /dev/null. The parent exits while sleep
                    // retains the pipe ends.
                    var spec = Shell(
                        "exec 3<&0; sleep 5 <&3 3<&- & echo $! > " + pidFile + "; exit 0",
                        canceled ? 10000 : 200
                    );
                    spec.InheritOutput = input;
                    if (input)
                        spec.StandardInput = new string('x', 1024 * 1024);
                    if (canceled)
                        cancel.CancelAfter(200);
                    var elapsed = System.Diagnostics.Stopwatch.StartNew();
                    var run = new SystemProcessRunner().Run(spec, cancel.Token);
                    Assert.That(elapsed.Elapsed.TotalSeconds, Is.LessThan(2));
                    Assert.That(run.Started, Is.True);
                    Assert.That(run.TimedOut, Is.EqualTo(!canceled));
                    Assert.That(run.Canceled, Is.EqualTo(canceled));
                    Assert.That(run.ExitCode, Is.Not.Zero);
                }
                finally
                {
                    if (File.Exists(pidFile))
                    {
                        using var child = System.Diagnostics.Process.GetProcessById(
                            int.Parse(
                                File.ReadAllText(pidFile).Trim(),
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                        );
                        if (!child.HasExited)
                            child.Kill();
                        File.Delete(pidFile);
                    }
                }
            }
        }

        public static void CapturesBothStreamsAndNonzeroExitStatus()
        {
            var run = new SystemProcessRunner().Run(
                Shell("printf 'track output\\n'; printf 'diagnostic\\n' >&2; exit 7"),
                CancellationToken.None
            );
            Assert.That(run.Started, Is.True);
            Assert.That(run.StandardOutput, Is.EqualTo("track output\n"));
            Assert.That(run.StandardError, Is.EqualTo("diagnostic\n"));
            Assert.That(run.ExitCode, Is.EqualTo(7));
            Assert.That(run.TimedOut || run.Canceled, Is.False);
        }

        public static void DrainsBothPipesWhileProcessIsRunning()
        {
            const string chunk = "0123456789abcdef0123456789abcdef";
            // Shell builtins produce more than a pipe buffer on each stream, without leaving
            // grandchildren holding inherited pipe handles when the runner kills the child.
            var run = new SystemProcessRunner().Run(
                Shell(
                    "i=0; while [ $i -lt 4096 ]; do printf '"
                        + chunk
                        + "'; printf '"
                        + chunk
                        + "' >&2; i=$((i+1)); done"
                ),
                CancellationToken.None
            );
            Assert.That(run.Started, Is.True);
            Assert.That(
                run.TimedOut || run.Canceled,
                Is.False,
                "both redirected streams must drain concurrently"
            );
            Assert.That(run.ExitCode, Is.Zero);
            string expected = string.Concat(System.Linq.Enumerable.Repeat(chunk, 4096));
            Assert.That(run.StandardOutput, Is.EqualTo(expected));
            Assert.That(run.StandardError, Is.EqualTo(expected));
        }

        public static void MissingExecutableReturnsNotStarted()
        {
            var run = new SystemProcessRunner().Run(
                new ProcessSpec
                {
                    FileName = Path.Combine(
                        Path.GetTempPath(),
                        "slopworld-missing-" + Guid.NewGuid().ToString("N")
                    ),
                    Arguments = "",
                    TimeoutMs = 1000,
                },
                CancellationToken.None
            );
            Assert.That(run.Started, Is.False);
            Assert.That(run.TimedOut || run.Canceled, Is.False);
            Assert.That(run.StandardOutput, Is.Empty);
            Assert.That(run.StandardError, Is.Empty);
        }

        public static void TimeoutKillsChildAndRetainsPartialOutput()
        {
            var run = new SystemProcessRunner().Run(
                Shell("printf 'before timeout'; exec /bin/sleep 5", 200),
                CancellationToken.None
            );
            Assert.That(run.Started, Is.True);
            Assert.That(run.TimedOut, Is.True);
            Assert.That(run.Canceled, Is.False);
            Assert.That(run.StandardOutput, Is.EqualTo("before timeout"));
            Assert.That(run.ExitCode, Is.Not.Zero, "timed-out child did not finish normally");
        }

        public static void CancellationKillsChildWithoutReportingTimeout()
        {
            using var cancel = new CancellationTokenSource();
            cancel.CancelAfter(200);
            var run = new SystemProcessRunner().Run(
                new ProcessSpec
                {
                    FileName = "/bin/sleep",
                    Arguments = "5",
                    TimeoutMs = 3000,
                },
                cancel.Token
            );
            Assert.That(run.Started, Is.True);
            Assert.That(run.Canceled, Is.True);
            Assert.That(run.TimedOut, Is.False);
            Assert.That(run.ExitCode, Is.Not.Zero);
        }

        public static void AlreadyCanceledTokenTerminatesStartedChild()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var run = new SystemProcessRunner().Run(
                new ProcessSpec
                {
                    FileName = "/bin/sleep",
                    Arguments = "5",
                    TimeoutMs = 3000,
                },
                cancel.Token
            );
            Assert.That(run.Started && run.Canceled, Is.True);
            Assert.That(run.TimedOut, Is.False);
        }
    }
}
