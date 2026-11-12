using System.Diagnostics;

// Exercise the actual exported startup coordinator with a deterministic catalog.
// Rendering/native dispatch remain covered by the underlying-workspace checks.
static class Check
{
    static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    static async Task Main()
    {
        foreach (
            var origin in new[]
            {
                "http://127.0.0.1:8787",
                "http://localhost:8787",
                "http://g14.tail86ccc.ts.net:8787",
                "https://g14.tail86ccc.ts.net",
                "http://[::1]:8787",
            }
        )
        {
            BrowserAdapters.BrowserTransport.Configure(origin);
            Expect(
                BrowserAdapters.BrowserTransport.BaseUrl == origin + "/host",
                "Browser origin was not preserved"
            );
            var socket = BrowserAdapters.BrowserTransport.SocketUri;
            var source = new Uri(origin);
            Expect(
                socket.Scheme == (source.Scheme == "https" ? "wss" : "ws")
                    && socket.Host == source.Host
                    && socket.Port == source.Port
                    && socket.AbsolutePath == "/host/ws",
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
        var hub = Hub.Instance;
        var timer = Stopwatch.StartNew();
        Expect(
            await Probe.WorkspaceFrame("test", "") == "[\"chrome\"]",
            "Empty catalog did not render chrome"
        );
        Expect(
            timer.Elapsed < TimeSpan.FromSeconds(5),
            "Empty catalog waited for startup deadline"
        );
        Expect(Probe.Opened == null, "Empty catalog opened a terminal");

        hub.Sessions =
        [
            new Session("dead", false),
            new Session("first", true),
            new Session("requested", true),
        ];
        Expect(
            await Probe.WorkspaceFrame("test", "") == "[]" && Probe.Opened == "first",
            "Default live session selection failed"
        );
        Expect(
            await Probe.WorkspaceFrame("test", "requested") == "[]" && Probe.Opened == "requested",
            "Explicit selection attached another session"
        );
        Probe.Opened = null;
        bool rejected = false;
        try
        {
            await Probe.WorkspaceFrame("test", "missing");
        }
        catch (InvalidOperationException e)
        {
            rejected = e.Message.Contains("No matching live session");
        }
        Expect(
            rejected && Probe.Opened == null,
            "Missing explicit session attached an unrelated terminal"
        );
        hub.Sessions = [];
        hub.SessionsVersion = 0;
        Expect(
            await Probe.WorkspaceFrame("test", "") == "[\"chrome\"]",
            "Unavailable catalog stranded startup"
        );
        Console.WriteLine(
            "Startup: empty catalog, live default, explicit selection, missing reference, and unavailable daemon passed."
        );
    }
}

public sealed class Session(string name, bool alive)
{
    public string Name = name;
    public bool Alive = alive;
}

public sealed class Hub
{
    public static readonly Hub Instance = new();
    public List<Session> Sessions { get; set; } = [];
    public long SessionsVersion { get; set; } = 1;

    public void Connect() { }

    public void Update() { }
}

public partial class Probe
{
    static readonly object entryEngine = new();
    public static string? Opened;

    static Type Type(string assembly, string name) => typeof(Hub);

    static object? Invoke(string assembly, string owner, string method, params object[] args)
    {
        if (method == "Paths")
            return Array.Empty<string>();
        if (method == "Open")
        {
            Opened = (string)args[0];
            return new object();
        }
        if (method == "Repaint")
            return "[]";
        if (method == "Frame")
            return "{\"commands\":[\"chrome\"]}";
        return null;
    }
}
