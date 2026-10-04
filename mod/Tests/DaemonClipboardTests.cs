using System;

namespace SlopWorld.Tests
{
    static class DaemonClipboardTests
    {
        public static void FieldClipboardReadsUseHostChannelsAndFallbackOnlyForOrdinaryPaste()
        {
            bool capability = SessionHub.Instance.Capabilities.Clipboard;
            string buffer = UnityEngine.GUIUtility.systemCopyBuffer;
            try
            {
                var provider = new DaemonUiClipboard();
                SessionHub.Instance.Capabilities.Clipboard = true;
                foreach (bool primary in new[] { false, true })
                {
                    DaemonClient.Requests.Clear();
                    string received = null;
                    provider.Read(primary, text => received = text);
                    AssertEx.Equal(null, received, "read waits for transport");
                    AssertEx.Equal(primary ? WireProtocol.Routes.ClipboardPrimaryText : WireProtocol.Routes.ClipboardText,
                        DaemonClient.Requests[0].Path, "selection channel is preserved");
                    DaemonClient.Requests[0].Ok(JVal.Parse("{\"text\":\"host text\"}"));
                    AssertEx.Equal("host text", received, "host reply reaches field owner");

                    received = null;
                    provider.Read(primary, text => received = text);
                    UnityEngine.GUIUtility.systemCopyBuffer = "local fallback";
                    DaemonClient.Requests[1].Fail("unavailable");
                    AssertEx.Equal(primary ? null : "local fallback", received,
                        "PRIMARY never falls back to CLIPBOARD");
                }

                SessionHub.Instance.Capabilities.Clipboard = false;
                DaemonClient.Requests.Clear();
                string local = null;
                provider.Read(false, text => local = text);
                AssertEx.Equal("local fallback", local, "sidecar uses native buffer");
                provider.Read(true, _ => { throw new Exception("PRIMARY unavailable"); });
                AssertEx.Equal(0, DaemonClient.Requests.Count, "unavailable host avoids requests");
            }
            finally
            {
                SessionHub.Instance.Capabilities.Clipboard = capability;
                UnityEngine.GUIUtility.systemCopyBuffer = buffer;
                DaemonClient.Requests.Clear();
            }
        }

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
