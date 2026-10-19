using System;

namespace SlopWorld
{
    // A capped parent listing cannot establish absence. Stat the exact file, then
    // probe the directory itself; callers retain menu context and error presentation.
    internal static class TerminalPathProbe
    {
        internal static void Resolve(string path, Action<bool> found, Action<string> fail)
        {
            DaemonClient.Get<Wire.FileStatResult>(
                WireProtocol.Routes.FileStat + "?path=" + Uri.EscapeDataString(path),
                stat =>
                {
                    if (stat.IsFile)
                        found(true);
                    else
                        DaemonClient.Get<Wire.BrowseResult>(
                            WireProtocol.Routes.Browse
                                + "?limit=1&path="
                                + Uri.EscapeDataString(path),
                            _ => found(false),
                            fail
                        );
                },
                fail
            );
        }
    }
}
