using System;
using System.Collections.Concurrent;
using System.Threading;
using NUnit.Framework;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class DesktopClipboardTests
    {
        public static void LinuxCopiesCannotOvertakePendingImagePasteThroughUnity()
        {
            var platform = Application.platform;
            var backend = DesktopClipboard.X11;
            string previous = GUIUtility.systemCopyBuffer;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            using var completions = new BlockingCollection<Action>();
            var runner = new PendingPasteRunner(entered, release);
            try
            {
                Application.platform = RuntimePlatform.LinuxPlayer;
                GUIUtility.systemCopyBuffer = "original selection";
                DesktopClipboard.X11 = new X11Clipboard(runner, completions.Add);
                byte[] received = null;
                DesktopClipboard.ReadLocalPaste(
                    _ => Assert.Fail("expected image"),
                    bytes => received = bytes,
                    error => Assert.Fail(error)
                );
                Assert.That(entered.Wait(5000), Is.True);
                DesktopClipboard.Copy("later local copy");
                DesktopClipboard.CopyFromApplication("later application copy");
                Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("original selection"));
                release.Set();
                for (int i = 0; i < 3; i++)
                {
                    Assert.That(completions.TryTake(out var complete, 5000), Is.True);
                    complete();
                }
                Assert.That(received, Is.EqualTo(new byte[] { 137, 80, 78, 71 }));
                Assert.That(
                    runner.Copies.ToArray(),
                    Is.EqualTo(new[] { "later local copy", "later application copy" })
                );
            }
            finally
            {
                release.Set();
                DesktopClipboard.X11 = backend;
                Application.platform = platform;
                GUIUtility.systemCopyBuffer = previous;
            }
        }

        sealed class PendingPasteRunner : IProcessRunner
        {
            readonly ManualResetEventSlim _entered;
            readonly ManualResetEventSlim _release;
            public readonly ConcurrentQueue<string> Copies = new ConcurrentQueue<string>();

            public PendingPasteRunner(ManualResetEventSlim entered, ManualResetEventSlim release)
            {
                _entered = entered;
                _release = release;
            }

            public ProcessRun Run(ProcessSpec spec, CancellationToken cancel)
            {
                if (spec.StandardInput != null)
                    Copies.Enqueue(spec.StandardInput);
                else if (spec.BinaryOutputLimit > 0)
                    return new ProcessRun
                    {
                        Started = true,
                        BinaryOutput = new byte[] { 137, 80, 78, 71 },
                    };
                else
                {
                    _entered.Set();
                    if (!_release.Wait(5000, cancel))
                        throw new TimeoutException("paste was not released");
                }
                return new ProcessRun { Started = true };
            }
        }

        public static void MacTextClipboardStaysLocalAndPrimaryDoesNotAliasClipboard()
        {
            var platform = Application.platform;
            string previous = GUIUtility.systemCopyBuffer;
            try
            {
                Application.platform = RuntimePlatform.OSXPlayer;
                DaemonClient.Requests.Clear();
                var provider = new DesktopUiClipboard();
                int successes = 0;
                DesktopClipboard.Copy("local λ", () => successes++);
                Assert.That(successes, Is.EqualTo(1));
                Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo("local λ"));
                provider.Copy("primary", true);
                string primary = null,
                    clipboard = null;
                provider.Read(true, value => primary = value);
                provider.Read(false, value => clipboard = value);
                Assert.That(primary, Is.Null);
                Assert.That(clipboard, Is.EqualTo("local λ"));
                Assert.That(DaemonClient.Requests, Is.Empty);
            }
            finally
            {
                Application.platform = platform;
                GUIUtility.systemCopyBuffer = previous;
                DaemonClient.Requests.Clear();
            }
        }
    }
}
