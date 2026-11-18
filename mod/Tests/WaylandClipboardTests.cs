using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WaylandClipboardTests
    {
        public static void WaylandPrimaryCopyAndImagePasteUseNativeSelections()
        {
            using var completions = new BlockingCollection<Action>();
            byte[] bytes = { 137, 80, 78, 71 };
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true, ExitCode = 1 },
                new ProcessRun { Started = true, BinaryOutput = bytes }
            );
            var clipboard = new LinuxClipboard(runner, completions.Add);
            clipboard.Copy("primary", true, null, e => Assert.Fail(e));
            clipboard.ReadPaste(
                _ => Assert.Fail("expected image"),
                value => Assert.That(value, Is.EqualTo(bytes)),
                e => Assert.Fail(e)
            );
            for (int i = 0; i < 2; i++)
            {
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
            }
            Assert.That(runner.Specs[0].FileName, Is.EqualTo("wl-copy"));
            Assert.That(runner.Specs[0].Arguments, Does.Contain("--primary"));
            Assert.That(runner.Specs[1].FileName, Is.EqualTo("wl-paste"));
            Assert.That(runner.Specs[2].Arguments, Does.Contain("image/png"));
        }

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
            var clipboard = new LinuxClipboard(runner, completions.Add);
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
            Assert.That(runner.Specs[1].Arguments, Is.EqualTo("--no-newline --type image/png"));
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
                results.Add(new ProcessRun { Started = true, BinaryOutput = bytes });
                results.Add(new ProcessRun { Started = true });
                var runner = new Runner(results.ToArray());
                var clipboard = new LinuxClipboard(runner, completions.Add);
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
                int imageIndex = 1;
                Assert.That(runner.Specs[imageIndex].BinaryOutputLimit, Is.Positive);
                Assert.That(runner.Specs[imageIndex + 1].StandardInput, Is.EqualTo("later copy"));
                paste();
                copy();
                Assert.That(received, Is.EqualTo(bytes));
            }
        }

        public static void EncodedLocalImageLinkUploadsBytesBeforeQueuedCopy()
        {
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            string path = Path.Combine(directory, "Telegram Desktop", "чат_ларнаа", "photo.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                byte[] bytes = { 255, 216, 255, 0, 128 };
                File.WriteAllBytes(path, bytes);
                using var completions = new BlockingCollection<Action>();
                var runner = new Runner(
                    new ProcessRun
                    {
                        Started = true,
                        StandardOutput = new Uri(path).AbsoluteUri + "\r\n",
                    },
                    new ProcessRun { Started = true }
                );
                var clipboard = new LinuxClipboard(runner, completions.Add);
                byte[] received = null;
                clipboard.ReadPaste(
                    _ => Assert.Fail("expected image"),
                    value => received = value,
                    e => Assert.Fail(e)
                );
                clipboard.Copy("later", false, () => { }, e => Assert.Fail(e));
                Assert.That(completions.TryTake(out var paste, 5000), Is.True);
                Assert.That(completions.TryTake(out var copy, 5000), Is.True);
                File.Delete(path);
                paste();
                copy();
                Assert.That(received, Is.EqualTo(bytes));
                Assert.That(runner.Specs.Count, Is.EqualTo(2));
                Assert.That(runner.Specs[1].StandardInput, Is.EqualTo("later"));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        public static void ImageLinksRemainLiteralOutsideImagePasteAndForNonlocalText()
        {
            foreach (
                string value in new[]
                {
                    "file:///tmp/photo.jpg",
                    "file://remote/tmp/photo.jpg",
                    "https://example.com/photo.jpg",
                    "look at file:///tmp/photo.jpg",
                    "file:///tmp/photo.txt",
                    "file:///tmp/a.jpg\nfile:///tmp/b.jpg",
                }
            )
            {
                using var completions = new BlockingCollection<Action>();
                var runner = new Runner(new ProcessRun { Started = true, StandardOutput = value });
                var clipboard = new LinuxClipboard(runner, completions.Add);
                string received = null;
                clipboard.ReadPaste(
                    text => received = text,
                    value == "file:///tmp/photo.jpg" ? null : _ => Assert.Fail("unexpected image"),
                    e => Assert.Fail(e)
                );
                Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                complete();
                Assert.That(received, Is.EqualTo(value));
                Assert.That(runner.Specs.Count, Is.EqualTo(1));
            }
        }

        public static void UnreadableOrOversizedImageLinkFailsAndLaterPasteRecovers()
        {
            string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
            try
            {
                foreach (bool oversized in new[] { false, true })
                {
                    if (oversized)
                    {
                        using var file = File.Create(path);
                        file.SetLength(20 * 1024 * 1024 + 1);
                    }
                    using var completions = new BlockingCollection<Action>();
                    var runner = new Runner(
                        new ProcessRun
                        {
                            Started = true,
                            StandardOutput = new Uri(path).AbsoluteUri,
                        },
                        new ProcessRun { Started = true, StandardOutput = "next paste" }
                    );
                    var clipboard = new LinuxClipboard(runner, completions.Add);
                    string error = null,
                        received = null;
                    clipboard.ReadPaste(
                        _ => Assert.Fail("unexpected text"),
                        _ => Assert.Fail("unexpected image"),
                        e => error = e
                    );
                    clipboard.ReadPaste(
                        text => received = text,
                        _ => Assert.Fail("unexpected image"),
                        e => Assert.Fail(e)
                    );
                    for (int i = 0; i < 2; i++)
                    {
                        Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                        complete();
                    }
                    Assert.That(error, Is.Not.Null.And.Not.Empty);
                    Assert.That(received, Is.EqualTo("next paste"));
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        public static void TextPasteDoesNotProbeImageTarget()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(new ProcessRun { Started = true, StandardOutput = "text" });
            var clipboard = new LinuxClipboard(runner, completions.Add);
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
            var backend = DesktopClipboard.Selection;
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
                DesktopClipboard.Selection = new LinuxClipboard(
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
                DesktopClipboard.Selection = backend;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
            }
        }

        public static void LinuxUsesClientSelectionsWithoutDaemonRequests()
        {
            using var completions = new BlockingCollection<Action>();
            var oldBackend = DesktopClipboard.Selection;
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
                DesktopClipboard.Selection = new LinuxClipboard(runner, completions.Add);
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
                DesktopClipboard.Selection = oldBackend;
                UnityEngine.Application.platform = platform;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
                DaemonClient.Requests.Clear();
            }
        }

        public static void ApplicationCopiesStayOnClientAndCanClearClipboard()
        {
            using var completions = new BlockingCollection<Action>();
            var backend = DesktopClipboard.Selection;
            var platform = UnityEngine.Application.platform;
            string buffer = UnityEngine.GUIUtility.systemCopyBuffer;
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true }
            );
            try
            {
                DaemonClient.Requests.Clear();
                DesktopClipboard.Selection = new LinuxClipboard(runner, completions.Add);
                foreach (
                    var target in new[]
                    {
                        UnityEngine.RuntimePlatform.LinuxPlayer,
                        UnityEngine.RuntimePlatform.WindowsPlayer,
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
                Assert.That(
                    runner.Specs[0].Arguments,
                    Is.EqualTo("--type text/plain;charset=utf-8")
                );
            }
            finally
            {
                DesktopClipboard.Selection = backend;
                UnityEngine.Application.platform = platform;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
                DaemonClient.Requests.Clear();
            }
        }

        public static void CopiesUseStdinAndFinishBeforeFollowingReads()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(
                new ProcessRun { Started = true },
                new ProcessRun { Started = true, StandardOutput = "copied" }
            );
            var clipboard = new LinuxClipboard(runner, completions.Add);
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
            Assert.That(runner.Specs[0].Arguments, Is.EqualTo("--type text/plain;charset=utf-8"));
            Assert.That(runner.Specs[1].StandardInput, Is.Null);
            Assert.That(runner.Specs[1].Arguments, Is.EqualTo("--no-newline --type text"));
        }

        public static void FailureDoesNotPoisonLaterOperationsAndBinaryIsNotText()
        {
            using var completions = new BlockingCollection<Action>();
            var runner = new Runner(
                new ProcessRun
                {
                    Started = true,
                    ExitCode = 1,
                    StandardError = "no display",
                },
                new ProcessRun { Started = true, StandardOutput = "binary\0data" }
            );
            var clipboard = new LinuxClipboard(runner, completions.Add);
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
