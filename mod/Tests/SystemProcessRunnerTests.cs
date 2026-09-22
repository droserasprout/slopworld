using System;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class SystemProcessRunnerTests
    {
        static ProcessSpec Shell(string command, int timeout = 5000) => new ProcessSpec
        {
            FileName = "/bin/sh",
            Arguments = "-c \"" + command + "\"",
            TimeoutMs = timeout,
        };

        public static void CapturesBothStreamsAndNonzeroExitStatus()
        {
            var run = new SystemProcessRunner().Run(Shell("printf 'track output\\n'; printf 'diagnostic\\n' >&2; exit 7"), CancellationToken.None);
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
            var run = new SystemProcessRunner().Run(Shell(
                "i=0; while [ $i -lt 4096 ]; do printf '" + chunk + "'; printf '" + chunk + "' >&2; i=$((i+1)); done"), CancellationToken.None);
            Assert.That(run.Started, Is.True);
            Assert.That(run.TimedOut || run.Canceled, Is.False, "both redirected streams must drain concurrently");
            Assert.That(run.ExitCode, Is.Zero);
            string expected = string.Concat(System.Linq.Enumerable.Repeat(chunk, 4096));
            Assert.That(run.StandardOutput, Is.EqualTo(expected));
            Assert.That(run.StandardError, Is.EqualTo(expected));
        }

        public static void MissingExecutableReturnsNotStarted()
        {
            var run = new SystemProcessRunner().Run(new ProcessSpec
            {
                FileName = Path.Combine(Path.GetTempPath(), "slopworld-missing-" + Guid.NewGuid().ToString("N")),
                Arguments = "", TimeoutMs = 1000,
            }, CancellationToken.None);
            Assert.That(run.Started, Is.False);
            Assert.That(run.TimedOut || run.Canceled, Is.False);
            Assert.That(run.StandardOutput, Is.Empty);
            Assert.That(run.StandardError, Is.Empty);
        }

        public static void TimeoutKillsChildAndRetainsPartialOutput()
        {
            var run = new SystemProcessRunner().Run(Shell("printf 'before timeout'; exec /bin/sleep 5", 200), CancellationToken.None);
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
            var run = new SystemProcessRunner().Run(new ProcessSpec
            {
                FileName = "/bin/sleep", Arguments = "5", TimeoutMs = 3000,
            }, cancel.Token);
            Assert.That(run.Started, Is.True);
            Assert.That(run.Canceled, Is.True);
            Assert.That(run.TimedOut, Is.False);
            Assert.That(run.ExitCode, Is.Not.Zero);
        }

        public static void AlreadyCanceledTokenTerminatesStartedChild()
        {
            using var cancel = new CancellationTokenSource();
            cancel.Cancel();
            var run = new SystemProcessRunner().Run(new ProcessSpec
            {
                FileName = "/bin/sleep", Arguments = "5", TimeoutMs = 3000,
            }, cancel.Token);
            Assert.That(run.Started && run.Canceled, Is.True);
            Assert.That(run.TimedOut, Is.False);
        }
    }
}
