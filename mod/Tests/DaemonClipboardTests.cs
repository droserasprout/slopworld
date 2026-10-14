using System;

namespace SlopWorld.Tests
{
    static class DaemonClipboardTests
    {
        public static void DelayedWriteAfterResetCannotRemoveOrSeedReplacement()
        {
            using var fixture = new Fixture();
            int old = 0,
                current = 0;
            DaemonClipboard.CopyPrimary("text", () => old++);
            DaemonClipboard.Reset();
            DaemonClipboard.Reset();
            DaemonClipboard.CopyPrimary("text", () => current++);
            DaemonClient.Requests[0].Reply(new Wire.Ack());
            AssertEx.Equal(1, old, "already dispatched write reports its outcome");
            DaemonClipboard.CopyPrimary("text", () => current++);
            AssertEx.Equal(2, DaemonClient.Requests.Count, "replacement still owns pending entry");
            DaemonClient.Requests[1].Fail("failed");
            DaemonClipboard.CopyPrimary("text");
            AssertEx.Equal(
                "POST",
                DaemonClient.Requests[2].Method,
                "old success did not seed new connection cache"
            );
            AssertEx.Equal(0, current, "replacement did not inherit old success");
        }

        public static void ThrowingWaitersDoNotPreventSettlementOrRetry()
        {
            foreach (bool success in new[] { false, true })
            {
                using var fixture = new Fixture();
                int settled = 0;
                DaemonClipboard.CopyPrimary(
                    "text",
                    () => throw new Exception("success waiter"),
                    _ => throw new Exception("failure waiter")
                );
                DaemonClipboard.CopyPrimary("text", () => settled++, _ => settled++);
                if (success)
                    DaemonClient.Requests[0].Reply(new Wire.Ack());
                else
                    DaemonClient.Requests[0].Fail("failed");
                AssertEx.Equal(1, settled, "later waiter settles despite exception");
                DaemonClipboard.CopyPrimary("text");
                AssertEx.Equal(
                    2,
                    DaemonClient.Requests.Count,
                    "completed batch releases pending entry"
                );
                AssertEx.Equal(
                    success ? "GET" : "POST",
                    DaemonClient.Requests[1].Method,
                    "only success caches text"
                );
            }
        }

        public static void ChannelsDoNotSharePendingWritesOrVerifiedContents()
        {
            using var fixture = new Fixture();
            int clipboard = 0,
                primary = 0;
            DaemonClipboard.Copy("same", () => clipboard++);
            DaemonClipboard.CopyPrimary("same", () => primary++);
            AssertEx.Equal(2, DaemonClient.Requests.Count, "each channel writes independently");
            AssertEx.Equal(
                WireProtocol.Routes.Clipboard,
                DaemonClient.Requests[0].Path,
                "ordinary route"
            );
            AssertEx.Equal(
                WireProtocol.Routes.ClipboardPrimary,
                DaemonClient.Requests[1].Path,
                "primary route"
            );
            AssertEx.Equal("same", ((Wire.ClipReq)DaemonClient.Requests[0].Body).Text, "wire text");
            DaemonClient.Requests[0].Reply(new Wire.Ack());
            AssertEx.Equal(1, clipboard, "ordinary waiter completes");
            AssertEx.Equal(0, primary, "primary still pending");
            DaemonClipboard.Copy("same", () => clipboard++);
            AssertEx.Equal("GET", DaemonClient.Requests[2].Method, "ordinary contents verified");
            DaemonClipboard.CopyPrimary("same", () => primary++);
            AssertEx.Equal(3, DaemonClient.Requests.Count, "primary coalesces only its own write");
            DaemonClient.Requests[1].Reply(new Wire.Ack());
            DaemonClient.Requests[2].Reply(new Wire.TextResult { Text = "same" });
            AssertEx.Equal(2, primary, "primary waiters complete");
            AssertEx.Equal(2, clipboard, "ordinary verification completes");
        }

        public static void UnavailableClipboardUsesNativeBufferWithoutHostRequests()
        {
            using var fixture = new Fixture();
            SessionHub.Instance.Capabilities.Clipboard = false;
            int successes = 0;
            DaemonClipboard.Copy("ordinary", () => successes++);
            AssertEx.Equal(
                "ordinary",
                UnityEngine.GUIUtility.systemCopyBuffer,
                "ordinary native fallback"
            );
            DaemonClipboard.CopyPrimary("primary", () => successes++);
            AssertEx.Equal(
                "ordinary",
                UnityEngine.GUIUtility.systemCopyBuffer,
                "primary leaves native clipboard alone"
            );
            DaemonClipboard.Copy("", () => successes++);
            DaemonClipboard.CopyPrimary(null, () => successes++);
            AssertEx.Equal(2, successes, "empty copies are ignored");
            AssertEx.Equal(0, DaemonClient.Requests.Count, "no host requests");
        }

        public static void FieldClipboardReadsUseHostChannelsAndFallbackOnlyForOrdinaryPaste()
        {
            using var fixture = new Fixture();
            var provider = new DaemonUiClipboard();
            foreach (bool primary in new[] { false, true })
            {
                DaemonClient.Requests.Clear();
                string received = null;
                provider.Read(primary, text => received = text);
                AssertEx.Equal(null, received, "read waits for transport");
                AssertEx.Equal(
                    primary
                        ? WireProtocol.Routes.ClipboardPrimaryText
                        : WireProtocol.Routes.ClipboardText,
                    DaemonClient.Requests[0].Path,
                    "selection channel is preserved"
                );
                DaemonClient.Requests[0].Reply(new Wire.TextResult { Text = "host text" });
                AssertEx.Equal("host text", received, "host reply reaches field owner");

                received = null;
                provider.Read(primary, text => received = text);
                UnityEngine.GUIUtility.systemCopyBuffer = "local fallback";
                DaemonClient.Requests[1].Fail("unavailable");
                AssertEx.Equal(
                    primary ? null : "local fallback",
                    received,
                    "PRIMARY never falls back to CLIPBOARD"
                );
            }

            SessionHub.Instance.Capabilities.Clipboard = false;
            DaemonClient.Requests.Clear();
            string local = null;
            provider.Read(false, text => local = text);
            AssertEx.Equal("local fallback", local, "sidecar uses native buffer");
            provider.Read(
                true,
                _ =>
                {
                    throw new Exception("PRIMARY unavailable");
                }
            );
            AssertEx.Equal(0, DaemonClient.Requests.Count, "unavailable host avoids requests");
        }

        public static void DuplicateWaitersReceiveFailureAndRetry()
        {
            foreach (bool primary in new[] { false, true })
            {
                using var fixture = new Fixture();
                Action<string, Action, Action<string>> copy = primary
                    ? DaemonClipboard.CopyPrimary
                    : DaemonClipboard.Copy;
                int successes = 0,
                    failures = 0;
                copy(
                    "text",
                    () => successes++,
                    e =>
                    {
                        AssertEx.Equal("failed", e, "shared error");
                        failures++;
                    }
                );
                copy(
                    "text",
                    () => successes++,
                    e =>
                    {
                        AssertEx.Equal("failed", e, "shared error");
                        failures++;
                    }
                );
                AssertEx.Equal(1, DaemonClient.Requests.Count, "one pending write");
                AssertEx.Equal(0, successes, "no premature success");
                DaemonClient.Requests[0].Fail("failed");
                AssertEx.Equal(2, failures, "both waiters fail");
                copy("text", () => successes++, null);
                DaemonClient.Requests[1].Reply(new Wire.Ack());
                AssertEx.Equal(1, successes, "retry succeeds");
            }
        }

        public static void RepeatedCopiesCheckCurrentContents()
        {
            foreach (bool primary in new[] { false, true })
            {
                using var fixture = new Fixture();
                Action<string, Action, Action<string>> copy = primary
                    ? DaemonClipboard.CopyPrimary
                    : DaemonClipboard.Copy;
                int successes = 0;
                copy("text", null, null);
                DaemonClient.Requests[0].Reply(new Wire.Ack());
                copy("text", () => successes++, null);
                copy("text", () => successes++, null);
                AssertEx.Equal("GET", DaemonClient.Requests[1].Method, "verify current clipboard");
                AssertEx.Equal(
                    primary
                        ? WireProtocol.Routes.ClipboardPrimaryText
                        : WireProtocol.Routes.ClipboardText,
                    DaemonClient.Requests[1].Path,
                    "independent selection"
                );
                DaemonClient.Requests[1].Reply(new Wire.TextResult { Text = "other app" });
                AssertEx.Equal("POST", DaemonClient.Requests[2].Method, "replace stale contents");
                AssertEx.Equal(0, successes, "wait for actual write");
                DaemonClient.Requests[2].Reply(new Wire.Ack());
                AssertEx.Equal(2, successes, "both waiters succeed");
                copy("text", () => successes++, null);
                DaemonClient.Requests[3].Reply(new Wire.TextResult { Text = "text" });
                AssertEx.Equal(4, DaemonClient.Requests.Count, "matching contents preserve owner");
                AssertEx.Equal(3, successes, "verified copy succeeds");
                copy("text", null, null);
                DaemonClient.Requests[4].Fail("read unavailable");
                AssertEx.Equal(
                    "POST",
                    DaemonClient.Requests[5].Method,
                    "failed verification still writes"
                );
                DaemonClient.Requests[5].Reply(new Wire.Ack());
            }
        }

        public static void ResetRejectsDelayedReadWithoutRemovingReplacement()
        {
            using var fixture = new Fixture();
            DaemonClipboard.CopyPrimary("text");
            DaemonClient.Requests[0].Reply(new Wire.Ack());
            int failures = 0;
            DaemonClipboard.CopyPrimary("text", null, e => failures++);
            DaemonClipboard.Reset();
            DaemonClipboard.CopyPrimary("text");
            DaemonClient.Requests[1].Reply(new Wire.TextResult { Text = "changed" });
            AssertEx.Equal(1, failures, "stale verification fails");
            DaemonClipboard.CopyPrimary("text");
            AssertEx.Equal(3, DaemonClient.Requests.Count, "replacement remains pending");
            DaemonClient.Requests[2].Reply(new Wire.Ack());
        }

        sealed class Fixture : IDisposable
        {
            readonly bool capability = SessionHub.Instance.Capabilities.Clipboard;
            readonly string buffer = UnityEngine.GUIUtility.systemCopyBuffer;

            public Fixture()
            {
                DaemonClipboard.Reset();
                DaemonClient.Requests.Clear();
                SessionHub.Instance.Capabilities.Clipboard = true;
            }

            public void Dispose()
            {
                DaemonClipboard.Reset();
                DaemonClient.Requests.Clear();
                SessionHub.Instance.Capabilities.Clipboard = capability;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
            }
        }
    }
}
