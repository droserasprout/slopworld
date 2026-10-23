using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class X11ClipboardTests
    {
        public static void ImageReadPreservesBytesAndReportsUnavailableTarget()
        {
            using var completions = new BlockingCollection<Action>();
            byte[] bytes = { 137, 80, 78, 71, 0, 255 };
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true, BinaryOutput = bytes },
                new ProcessRun { Started = true },
                new ProcessRun
                {
                    Started = true,
                    ExitCode = 1,
                    StandardError = "no PNG target",
                }
            );
            var clipboard = new X11Clipboard(runner, completions.Add);
            byte[] received = null;
            string error = null;
            clipboard.ReadPaste(
                _ => Assert.Fail("expected image"),
                value => received = value,
                e => Assert.Fail(e)
            );
            clipboard.ReadPaste(
                _ => Assert.Fail("expected image"),
                _ => Assert.Fail("unavailable image succeeded"),
                e => error = e
            );
            Assert.That(received, Is.Null);
            for (int i = 0; i < 2; i++)
            {
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
            }
            Assert.That(received, Is.EqualTo(bytes));
            Assert.That(error, Does.Contain("no PNG target"));
            Assert.That(
                runner.Specs[1].Arguments,
                Is.EqualTo("-selection clipboard -target image/png -out")
            );
            Assert.That(runner.Specs[1].BinaryOutputLimit, Is.EqualTo(20 * 1024 * 1024));
        }

        public static void ImageFallbackFinishesBeforeQueuedCopyWithoutPumpingCallbacks()
        {
            foreach (bool failedText in new[] { false, true })
            {
                using var completions = new BlockingCollection<Action>();
                byte[] bytes = { 137, 80, 78, 71 };
                var results = new List<ProcessRun>
                {
                    new ProcessRun { Started = true, ExitCode = failedText ? 1 : 0 },
                };
                if (failedText)
                    results.Add(new ProcessRun { Started = true, ExitCode = 1 });
                results.Add(new ProcessRun { Started = true, BinaryOutput = bytes });
                results.Add(new ProcessRun { Started = true });
                var runner = new Runner(results.ToArray());
                var clipboard = new X11Clipboard(runner, completions.Add);
                byte[] received = null;
                clipboard.ReadPaste(
                    _ => Assert.Fail("expected image"),
                    value => received = value,
                    e => Assert.Fail(e)
                );
                clipboard.Copy("later copy", false, () => { }, e => Assert.Fail(e));
                // Hold both completions: the worker must finish the entire paste before
                // executing the copy, without needing a UI callback to enqueue fallback.
                Assert.That(completions.TryTake(out var paste, 5000), Is.True);
                Assert.That(completions.TryTake(out var copy, 5000), Is.True);
                Assert.That(received, Is.Null);
                int imageIndex = failedText ? 2 : 1;
                Assert.That(runner.Specs[imageIndex].BinaryOutputLimit, Is.Positive);
                Assert.That(runner.Specs[imageIndex + 1].StandardInput, Is.EqualTo("later copy"));
                paste();
                copy();
                Assert.That(received, Is.EqualTo(bytes));
            }
        }

        public static void TextPasteDoesNotProbeImageTarget()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(new ProcessRun { Started = true, StandardOutput = "text" });
            var clipboard = new X11Clipboard(runner, completions.Add);
            string received = null;
            clipboard.ReadPaste(
                value => received = value,
                _ => Assert.Fail("unexpected image"),
                e => Assert.Fail(e)
            );
            Assert.That(completions.TryTake(out var complete, 5000), Is.True);
            complete();
            Assert.That(received, Is.EqualTo("text"));
            Assert.That(runner.Specs.Count, Is.EqualTo(1));
        }

        public static void FailedTextReadUsesExplicitHandlerInsteadOfStaleUnityText()
        {
            using var completions = new BlockingCollection<Action>();
            var backend = DesktopClipboard.X11;
            string buffer = UnityEngine.GUIUtility.systemCopyBuffer;
            var failure = new ProcessRun
            {
                Started = true,
                ExitCode = 1,
                StandardError = "text target unavailable",
            };
            try
            {
                UnityEngine.GUIUtility.systemCopyBuffer = "stale text";
                DesktopClipboard.X11 = new X11Clipboard(
                    new Runner(failure, failure, failure, failure, failure, failure),
                    completions.Add
                );
                string error = null,
                    ordinary = null,
                    primary = null;
                DesktopClipboard.ReadLocal(
                    false,
                    _ => Assert.Fail("non-text paste must not receive stale Unity text"),
                    message => error = message
                );
                DesktopClipboard.ReadLocal(false, text => ordinary = text);
                DesktopClipboard.ReadLocal(true, text => primary = text);
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                    complete();
                }
                Assert.That(error, Does.Contain("text target unavailable"));
                Assert.That(ordinary, Is.EqualTo("stale text"));
                Assert.That(primary, Is.Null);
            }
            finally
            {
                DesktopClipboard.X11 = backend;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
            }
        }

        public static void LinuxUsesClientSelectionsWithoutDaemonRequests()
        {
            using var completions = new BlockingCollection<Action>();
            var oldBackend = DesktopClipboard.X11;
            var platform = UnityEngine.Application.platform;
            string buffer = UnityEngine.GUIUtility.systemCopyBuffer;
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true },
                new ProcessRun { Started = true, StandardOutput = "local primary" },
                new ProcessRun { Started = true, StandardOutput = "local clipboard" }
            );
            try
            {
                DaemonClient.Requests.Clear();
                UnityEngine.Application.platform = UnityEngine.RuntimePlatform.LinuxPlayer;
                DesktopClipboard.X11 = new X11Clipboard(runner, completions.Add);
                DesktopClipboard.Copy("copy");
                DesktopClipboard.CopyPrimary("selection");
                string primary = null,
                    clipboard = null;
                var provider = new DesktopUiClipboard();
                provider.Read(true, text => primary = text);
                provider.Read(false, text => clipboard = text);
                for (int i = 0; i < 4; i++)
                {
                    Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                    complete();
                }
                Assert.That(primary, Is.EqualTo("local primary"));
                Assert.That(clipboard, Is.EqualTo("local clipboard"));
                Assert.That(DaemonClient.Requests, Is.Empty);
                Assert.That(UnityEngine.GUIUtility.systemCopyBuffer, Is.EqualTo(buffer));
            }
            finally
            {
                DesktopClipboard.X11 = oldBackend;
                UnityEngine.Application.platform = platform;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
                DaemonClient.Requests.Clear();
            }
        }

        public static void ApplicationCopiesStayOnClientAndCanClearClipboard()
        {
            using var completions = new BlockingCollection<Action>();
            var backend = DesktopClipboard.X11;
            var platform = UnityEngine.Application.platform;
            string buffer = UnityEngine.GUIUtility.systemCopyBuffer;
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true }
            );
            try
            {
                DaemonClient.Requests.Clear();
                DesktopClipboard.X11 = new X11Clipboard(runner, completions.Add);
                foreach (
                    var target in new[]
                    {
                        UnityEngine.RuntimePlatform.LinuxPlayer,
                        UnityEngine.RuntimePlatform.OSXPlayer,
                    }
                )
                {
                    UnityEngine.Application.platform = target;
                    string before = UnityEngine.GUIUtility.systemCopyBuffer;
                    DesktopClipboard.CopyFromApplication("copy λ");
                    Assert.That(
                        UnityEngine.GUIUtility.systemCopyBuffer,
                        Is.EqualTo(
                            target == UnityEngine.RuntimePlatform.LinuxPlayer ? before : "copy λ"
                        )
                    );
                    DesktopClipboard.CopyFromApplication("");
                    Assert.That(
                        UnityEngine.GUIUtility.systemCopyBuffer,
                        Is.EqualTo(target == UnityEngine.RuntimePlatform.LinuxPlayer ? before : "")
                    );
                }
                for (int i = 0; i < 2; i++)
                {
                    Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                    complete();
                }
                Assert.That(
                    DaemonClient.Requests,
                    Is.Empty,
                    "application copies must never bounce back to daemon"
                );
                Assert.That(runner.Specs[0].StandardInput, Is.EqualTo("copy λ"));
                Assert.That(runner.Specs[1].StandardInput, Is.Empty);
                Assert.That(runner.Specs[0].Arguments, Is.EqualTo("-selection clipboard -in"));
            }
            finally
            {
                DesktopClipboard.X11 = backend;
                UnityEngine.Application.platform = platform;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
                DaemonClient.Requests.Clear();
            }
        }

        public static void FailedXclipFallsBackToXselAndKeepsPrimarySeparate()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(
                new ProcessRun(),
                new ProcessRun { Started = true, StandardOutput = "primary text\n" }
            );
            var clipboard = new X11Clipboard(runner, completions.Add);
            string received = null;
            clipboard.Read(true, text => received = text, e => Assert.Fail(e));
            Assert.That(completions.TryTake(out var complete, 5000), Is.True);
            Assert.That(received, Is.Null, "worker must not invoke UI callbacks");
            complete();
            Assert.That(received, Is.EqualTo("primary text\n"));
            Assert.That(
                runner.Specs[0].Arguments,
                Is.EqualTo("-selection primary -target UTF8_STRING -out")
            );
            Assert.That(runner.Specs[1].Arguments, Is.EqualTo("--primary --output"));
        }

        public static void CopiesUseStdinAndFinishBeforeFollowingReads()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true, StandardOutput = "copied" }
            );
            var clipboard = new X11Clipboard(runner, completions.Add);
            const string text = "é\n$(not a command) `literal`";
            clipboard.Copy(text, false, () => { }, e => Assert.Fail(e));
            clipboard.Read(false, _ => { }, e => Assert.Fail(e));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
            }
            Assert.That(runner.Specs[0].StandardInput, Is.EqualTo(text));
            Assert.That(
                runner.Specs[0].InheritOutput,
                Is.True,
                "selection child must not retain capture pipes"
            );
            Assert.That(runner.Specs[0].Arguments, Is.EqualTo("-selection clipboard -in"));
            Assert.That(runner.Specs[1].StandardInput, Is.Null);
            Assert.That(
                runner.Specs[1].Arguments,
                Is.EqualTo("-selection clipboard -target UTF8_STRING -out")
            );
        }

        public static void FailureDoesNotPoisonLaterOperationsAndBinaryIsNotText()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(
                new ProcessRun { Started = true, TimedOut = true },
                new ProcessRun
                {
                    Started = true,
                    ExitCode = 1,
                    StandardError = "no display",
                },
                new ProcessRun { Started = true, StandardOutput = "binary\0data" }
            );
            var clipboard = new X11Clipboard(runner, completions.Add);
            string error = null,
                received = null;
            clipboard.Read(true, _ => Assert.Fail("failed read succeeded"), e => error = e);
            clipboard.Read(false, text => received = text, e => Assert.Fail(e));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
            }
            Assert.That(error, Does.Contain("no display"));
            Assert.That(received, Is.Empty);
        }

        internal sealed class Runner : IProcessRunner
        {
            readonly Queue<ProcessRun> _results;
            public readonly List<ProcessSpec> Specs = new List<ProcessSpec>();

            public Runner(params ProcessRun[] results) => _results = new Queue<ProcessRun>(results);

            public ProcessRun Run(ProcessSpec spec, CancellationToken cancel)
            {
                Specs.Add(spec);
                return _results.Dequeue();
            }
        }
    }
}
