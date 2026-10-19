using System;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class TerminalPathProbeTests
    {
        public static void ExactFileDoesNotDependOnParentListing()
        {
            int start = DaemonClient.Requests.Count;
            try
            {
                bool? found = null;
                string path = "/repo/many/files/a #?.cs";
                TerminalPathProbe.Resolve(path, file => found = file, Assert.Fail);
                var request = DaemonClient.Requests[start];
                Assert.That(
                    request.Path,
                    Is.EqualTo(WireProtocol.Routes.FileStat + "?path=" + Uri.EscapeDataString(path))
                );
                request.Reply(new Wire.FileStatResult { IsFile = true });
                Assert.That(found, Is.True);
                Assert.That(DaemonClient.Requests.Count, Is.EqualTo(start + 1));
            }
            finally
            {
                DaemonClient.Requests.RemoveRange(start, DaemonClient.Requests.Count - start);
            }
        }

        public static void DirectoryNeedsSuccessfulExactBrowseAndFailuresReachCaller()
        {
            int start = DaemonClient.Requests.Count;
            try
            {
                bool? found = null;
                string error = null;
                TerminalPathProbe.Resolve(
                    "/repo/dir",
                    file => found = file,
                    message => error = message
                );
                DaemonClient.Requests[start].Reply(new Wire.FileStatResult());
                Assert.That(found, Is.Null);
                var browse = DaemonClient.Requests[start + 1];
                Assert.That(browse.Path, Does.Contain("limit=1&path=%2Frepo%2Fdir"));
                browse.Fail("missing");
                Assert.That(error, Is.EqualTo("missing"));
                Assert.That(found, Is.Null);

                TerminalPathProbe.Resolve("/repo/dir", file => found = file, Assert.Fail);
                DaemonClient.Requests[start + 2].Reply(new Wire.FileStatResult());
                DaemonClient.Requests[start + 3].Reply(new Wire.BrowseResult { Truncated = true });
                Assert.That(
                    found,
                    Is.False,
                    "even a capped exact browse proves this is a directory"
                );
            }
            finally
            {
                DaemonClient.Requests.RemoveRange(start, DaemonClient.Requests.Count - start);
            }
        }
    }
}
