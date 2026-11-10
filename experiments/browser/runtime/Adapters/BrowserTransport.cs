// Browser origin only; authentication is injected by the loopback host bridge.
namespace BrowserAdapters;

public static class BrowserTransport
{
    public static string BaseUrl { get; private set; } = "";

    public static void Configure(string origin)
    {
        var uri = new Uri(origin);
        if (
            uri.Scheme != "http"
            || uri.Host != "127.0.0.1"
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0
            || uri.UserInfo.Length != 0
        )
            throw new ArgumentException("Expected the loopback browser origin");
        BaseUrl = origin.TrimEnd('/') + "/host";
    }

    public static System.Net.Http.HttpClient CreateHttp() =>
        new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
}
