using System;

namespace SlopWorld.Tests
{
    static class DaemonClipboardTests
    {
        public static void DuplicateWaitersReceiveFailureAndRetry()
        {
            foreach (bool primary in new[] { false, true })
            {
                DaemonClipboard.Reset();
                DaemonClient.Requests.Clear();
                SessionHub.Instance.Capabilities.Clipboard = true;
                Action<string, Action, Action<string>> copy = primary ? DaemonClipboard.CopyPrimary : DaemonClipboard.Copy;
                int successes = 0, failures = 0;
                copy("text", () => successes++, e => { AssertEx.Equal("failed", e, "shared error"); failures++; });
                copy("text", () => successes++, e => { AssertEx.Equal("failed", e, "shared error"); failures++; });
                AssertEx.Equal(1, DaemonClient.Requests.Count, "one pending write");
                AssertEx.Equal(0, successes, "no premature success");
                DaemonClient.Requests[0].Fail("failed");
                AssertEx.Equal(2, failures, "both waiters fail");
                copy("text", () => successes++, null);
                DaemonClient.Requests[1].Ok(JVal.Parse("{}"));
                AssertEx.Equal(1, successes, "retry succeeds");
            }
        }

        public static void RepeatedCopiesCheckCurrentContents()
        {
            foreach (bool primary in new[] { false, true })
            {
                DaemonClipboard.Reset();
                DaemonClient.Requests.Clear();
                SessionHub.Instance.Capabilities.Clipboard = true;
                Action<string, Action, Action<string>> copy = primary ? DaemonClipboard.CopyPrimary : DaemonClipboard.Copy;
                int successes = 0;
                copy("text", null, null);
                DaemonClient.Requests[0].Ok(JVal.Parse("{}"));
                copy("text", () => successes++, null);
                copy("text", () => successes++, null);
                AssertEx.Equal("GET", DaemonClient.Requests[1].Method, "verify current clipboard");
                AssertEx.Equal(primary ? WireProtocol.Routes.ClipboardPrimaryText : WireProtocol.Routes.ClipboardText,
                    DaemonClient.Requests[1].Path, "independent selection");
                DaemonClient.Requests[1].Ok(JVal.Parse("{\"text\":\"other app\"}"));
                AssertEx.Equal("POST", DaemonClient.Requests[2].Method, "replace stale contents");
                AssertEx.Equal(0, successes, "wait for actual write");
                DaemonClient.Requests[2].Ok(JVal.Parse("{}"));
                AssertEx.Equal(2, successes, "both waiters succeed");
                copy("text", () => successes++, null);
                DaemonClient.Requests[3].Ok(JVal.Parse("{\"text\":\"text\"}"));
                AssertEx.Equal(4, DaemonClient.Requests.Count, "matching contents preserve owner");
                AssertEx.Equal(3, successes, "verified copy succeeds");
                copy("text", null, null);
                DaemonClient.Requests[4].Fail("read unavailable");
                AssertEx.Equal("POST", DaemonClient.Requests[5].Method, "failed verification still writes");
                DaemonClient.Requests[5].Ok(JVal.Parse("{}"));
            }
        }

        public static void ResetRejectsDelayedReadWithoutRemovingReplacement()
        {
            DaemonClipboard.Reset();
            DaemonClient.Requests.Clear();
            SessionHub.Instance.Capabilities.Clipboard = true;
            DaemonClipboard.CopyPrimary("text");
            DaemonClient.Requests[0].Ok(JVal.Parse("{}"));
            int failures = 0;
            DaemonClipboard.CopyPrimary("text", null, e => failures++);
            DaemonClipboard.Reset();
            DaemonClipboard.CopyPrimary("text");
            DaemonClient.Requests[1].Ok(JVal.Parse("{\"text\":\"changed\"}"));
            AssertEx.Equal(1, failures, "stale verification fails");
            DaemonClipboard.CopyPrimary("text");
            AssertEx.Equal(3, DaemonClient.Requests.Count, "replacement remains pending");
            DaemonClient.Requests[2].Ok(JVal.Parse("{}"));
        }
    }
}
