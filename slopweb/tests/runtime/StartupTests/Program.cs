using System.Diagnostics;
using System.Text.Json;
using BrowserAdapters;
using SlopWorld;

// Exercise the actual exported startup coordinator with a deterministic catalog.
// Rendering/native dispatch remain covered by the underlying-workspace checks.
static class Check
{
    internal static readonly List<string> Calls = [];
    internal static string? Opened;

    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static async Task Main()
    {
        var runtime = new WorkspaceRuntime();
        try
        {
            runtime.WorkspaceUpdatePacket("[]", 0, -1);
            throw new Exception("Stopped engine accepted a frame");
        }
        catch (InvalidOperationException error)
            when (error.Message == "Workspace engine is stopped") { }
        runtime.entry = new object();
        BrowserActivity.Wake = () => Calls.Add("Notify");
        CoreLanguageChecks.Run();
        const int mib = 1024 * 1024;
        Expect(
            BrowserAdapters.BrowserSocketLimits.FitsOutgoing(0, 20 * mib + 128),
            "Image paste envelope rejected"
        );
        Expect(
            BrowserAdapters.BrowserSocketLimits.FitsOutgoing(12 * mib, 20 * mib),
            "Exact queue limit rejected"
        );
        Expect(
            !BrowserAdapters.BrowserSocketLimits.FitsOutgoing(12 * mib, 20 * mib + 1),
            "Queue bound exceeded"
        );
        Expect(
            !BrowserAdapters.BrowserSocketLimits.FitsOutgoing(mib, int.MaxValue),
            "Queue size overflow"
        );
        runtime.WorkspaceFontFallbackComplete("{}");
        Expect(
            Calls.SequenceEqual(
                new[] { "CompleteFallbacks", "FontsChanged", "Invalidate", "Notify" }
            ),
            "Fallback publication must invalidate native terminal textures before waking presentation"
        );
        Calls.Clear();
        BrowserFontBank.AcceptFallback = false;
        runtime.WorkspaceFontFallbackComplete("{}");
        Expect(
            Calls.SequenceEqual(new[] { "CompleteFallbacks" }),
            "Stale fallback response changed presentation"
        );
        Calls.Clear();
        foreach (
            var origin in new[]
            {
                "http://127.0.0.1:8787",
                "http://localhost:8787",
                "http://g14.tail86ccc.ts.net:8787",
                "https://g14.tail86ccc.ts.net",
                "http://[::1]:8787",
                "https://example.com/slopweb-demo/",
            }
        )
        {
            BrowserAdapters.BrowserTransport.Configure(origin);
            Expect(
                BrowserAdapters.BrowserTransport.BaseUrl == origin.TrimEnd('/') + "/host",
                "Browser origin was not preserved"
            );
            var socket = BrowserAdapters.BrowserTransport.SocketUri;
            var source = new Uri(origin);
            Expect(
                socket.Scheme == (source.Scheme == "https" ? "wss" : "ws")
                    && socket.Host == source.Host
                    && socket.Port == source.Port
                    && socket.AbsolutePath == source.AbsolutePath.TrimEnd('/') + "/host/ws",
                "Socket origin was not preserved"
            );
        }
        foreach (
            var origin in new[]
            {
                "ftp://localhost",
                "http://localhost/path",
                "http://localhost/?query",
                "http://localhost/#fragment",
                "http://user@localhost/",
            }
        )
        {
            bool invalid = false;
            try
            {
                BrowserAdapters.BrowserTransport.Configure(origin);
            }
            catch (ArgumentException)
            {
                invalid = true;
            }
            Expect(invalid, "Non-origin URL was accepted");
        }
        foreach (string batch in new[] { "[]", "[{\"kind\":\"down\"},{\"kind\":\"up\"}]" })
        {
            Calls.Clear();
            Expect(runtime.WorkspaceUpdatePacket(batch, 1, 0) == batch, "GUI input batch changed");
            Expect(Calls[^1] == "EndFrame", "Input processing did not finish the frame");
        }
        var hub = SessionHub.Instance;
        var timer = Stopwatch.StartNew();
        Expect(
            await runtime.WorkspaceFrame("https://example.com/", "") == "[\"chrome\"]",
            "Empty catalog did not render chrome"
        );
        Expect(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            "Empty catalog waited for startup deadline"
        );
        Expect(Opened == null, "Empty catalog opened a terminal");

        hub.Sessions =
        [
            new SessionInfo("dead", false),
            new SessionInfo("first", true),
            new SessionInfo("requested", true),
        ];
        Expect(
            await runtime.WorkspaceFrame("https://example.com/", "") == "[\"chrome\"]"
                && Opened == "first",
            "Default live session selection failed"
        );
        Expect(
            await runtime.WorkspaceFrame("https://example.com/", "requested") == "[\"chrome\"]"
                && Opened == "requested",
            "Explicit selection attached another session"
        );
        Opened = null;
        bool rejected = false;
        try
        {
            await runtime.WorkspaceFrame("https://example.com/", "missing");
        }
        catch (InvalidOperationException e)
        {
            rejected = e.Message.Contains("No matching live session");
        }
        Expect(
            rejected && Opened == null,
            "Missing explicit session attached an unrelated terminal"
        );
        hub.Sessions = [];
        hub.SessionsVersion = 0;
        Expect(
            await runtime.WorkspaceFrame("https://example.com/", "") == "[\"chrome\"]",
            "Unavailable catalog stranded startup"
        );
        Console.WriteLine(
            "Startup: empty catalog, live default, explicit selection, missing reference, and unavailable daemon passed."
        );
    }
}
