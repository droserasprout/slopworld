// Browser origin only; authentication is injected by the same-origin host bridge.
namespace BrowserAdapters;

public static class BrowserTransport
{
    sealed class ActivityHandler : System.Net.Http.DelegatingHandler
    {
        public ActivityHandler(System.Net.Http.HttpMessageHandler inner)
            : base(inner) { }

        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            try
            {
                return await base.SendAsync(request, cancellationToken);
            }
            finally
            {
                BrowserActivity.Notify();
            }
        }
    }

    public static string BaseUrl { get; private set; } = "";

    public static void Configure(string origin)
    {
        var uri = new Uri(origin);
        if (
            (uri.Scheme != "http" && uri.Scheme != "https")
            || uri.Host.Length == 0
            || uri.AbsolutePath != "/"
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || uri.UserInfo.Length != 0
        )
            throw new ArgumentException("Expected an HTTP(S) browser origin");
        BaseUrl = uri.GetLeftPart(UriPartial.Authority) + "/host";
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000",
        Justification = "The returned HttpClient owns both handlers; failed construction disposes the chain."
    )]
    public static System.Net.Http.HttpClient CreateHttp()
    {
        var inner = new System.Net.Http.HttpClientHandler();
        ActivityHandler? handler = null;
        try
        {
            handler = new ActivityHandler(inner);
            // HttpClient owns the whole handler chain after successful construction.
            return new System.Net.Http.HttpClient(handler, disposeHandler: true)
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
        }
        catch
        {
            if (handler != null)
                handler.Dispose();
            else
                inner.Dispose();
            throw;
        }
    }
}
