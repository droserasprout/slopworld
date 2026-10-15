using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;

namespace SlopWorld
{
    // Owns HTTP admission, request deadlines, and host-pumped callback delivery.
    // DaemonClient supplies the process-wide facade; tests can own an isolated instance.
    internal sealed class DaemonHttpTransport : IDisposable
    {
        const int DefaultTimeoutMs = 5000;
        const string TokenHeader = WireProtocol.TokenHeader;
        const string SessionHeader = WireProtocol.SessionHeader;
        const string ContentType = "application/x-protobuf";
        const int MaxCompletionsPerFrame = 32;
        static readonly long CompletionBudgetTicks = System.Diagnostics.Stopwatch.Frequency / 500;
        const string CompletionTraceName = "http-completions";

        // Admission includes body reads, keeping buffered responses and parsing bounded.
        const int MaxRequests = 8;
        readonly SemaphoreSlim Requests = new SemaphoreSlim(MaxRequests);
        readonly HttpClient Http = new HttpClient(
            new SocketsHttpHandler { UseProxy = false, MaxConnectionsPerServer = MaxRequests }
        )
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };

        readonly MainThreadCompletions Completions = new MainThreadCompletions(
            MaxCompletionsPerFrame,
            CompletionBudgetTicks,
            System.Diagnostics.Stopwatch.GetTimestamp
        );

        // Queue background integration callbacks with HTTP callbacks.
        // They can then update Unity and RimWorld state on the main thread.
        public void OnMainThread(Action action)
        {
            if (action != null)
                Completions.Enqueue(action);
        }

        public void Send<T>(
            string method,
            string path,
            IMessage body,
            Action<T> ok,
            Action<string> fail,
            string session = null,
            int timeoutMs = DefaultTimeoutMs
        )
            where T : IMessage<T>, new()
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, Timeout.Infinite);

            // Start a request trace only when the route has a trace category.
            // Other routes leave a null name, which can cause PerfTrace.End to fail before the caller's callback runs.
            string trace = TraceName(path);
            long started = trace == null ? 0L : PerfTrace.Start();
            _ = Task.Run(async () =>
            {
                Action completion;
                try
                {
                    var value = await Request<T>(method, path, body, session, timeoutMs)
                        .ConfigureAwait(false);
                    completion = () =>
                    {
                        if (started != 0L)
                            PerfTrace.End(trace, started, 1, Completions.Count);
                        ok?.Invoke(value);
                    };
                }
                catch (Exception e)
                {
                    completion = () =>
                    {
                        if (started != 0L)
                            PerfTrace.End(trace, started, 1, Completions.Count);
                        fail?.Invoke(e.Message);
                    };
                }
                // Request scopes release admission and HTTP resources before callbacks settle.
                Completions.Enqueue(completion);
            });
        }

        async Task<T> Request<T>(
            string method,
            string path,
            IMessage body,
            string session,
            int timeoutMs
        )
            where T : IMessage<T>, new()
        {
            using var deadline = new CancellationTokenSource(timeoutMs);
            await Requests.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                var connection = Endpoint.Resolve();
                using var request = new HttpRequestMessage(
                    new HttpMethod(method),
                    connection.BaseUrl + path
                );
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(ContentType));
                if (!string.IsNullOrEmpty(connection.Token))
                    request.Headers.Add(TokenHeader, connection.Token);
                if (!string.IsNullOrEmpty(session))
                    request.Headers.Add(SessionHeader, session);
                if (body != null)
                {
                    request.Content = new ByteArrayContent(body.ToByteArray());
                    request.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
                }
                using var response = await Http.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        deadline.Token
                    )
                    .ConfigureAwait(false);
                using var stream = await response
                    .Content.ReadAsStreamAsync(deadline.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    string message = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                    try
                    {
                        string explanation = Wire
                            .Error.Parser.ParseFrom(
                                await ReadBounded(stream, deadline.Token).ConfigureAwait(false)
                            )
                            .Error_;
                        if (!string.IsNullOrEmpty(explanation))
                            message = explanation;
                    }
                    catch (Exception e)
                        when (e is IOException || e is InvalidProtocolBufferException)
                    {
                        // Malformed or oversized error bodies retain the HTTP failure.
                    }
                    throw new HttpRequestException(message, null, response.StatusCode);
                }
                if (response.Content.Headers.ContentType?.ToString() != ContentType)
                    throw new IOException(
                        "Expected a Protobuf protocol 2 response. Update the daemon and mod together."
                    );
                return new MessageParser<T>(() => new T()).ParseFrom(
                    await ReadBounded(stream, deadline.Token).ConfigureAwait(false)
                );
            }
            finally
            {
                Requests.Release();
            }
        }

        static async Task<byte[]> ReadBounded(Stream stream, CancellationToken cancellationToken)
        {
            const int limit = 32 * 1024 * 1024;
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while (
                    (
                        count = await stream
                            .ReadAsync(buffer.AsMemory(), cancellationToken)
                            .ConfigureAwait(false)
                    ) != 0
                )
                {
                    if (output.Length + count > limit)
                        throw new IOException("Protobuf response exceeds limit");
                    output.Write(buffer, 0, count);
                }
                return output.ToArray();
            }
        }

        static string TraceName(string path)
        {
            if (path.StartsWith(WireProtocol.Routes.Browse, StringComparison.Ordinal))
                return "http-browse";
            if (path.StartsWith(WireProtocol.Routes.Git, StringComparison.Ordinal))
                return "http-git";
            if (path.StartsWith(WireProtocol.Routes.Search, StringComparison.Ordinal))
                return "http-search";
            return null;
        }

        // Process completion callbacks once per frame on the main thread.
        // Keep remaining callbacks queued for a later frame to limit time in Unity's update loop.
        // HTTP callbacks cannot replace one another.
        public int PendingCompletions => Completions.Count;

        public int PumpCompletions(Action<Exception> reportError)
        {
            long started = PerfTrace.Start();
            int count = Completions.Pump(reportError);
            PerfTrace.End(CompletionTraceName, started, count, Completions.Count);
            return count;
        }

        // Owners dispose only after in-flight requests and their callbacks have settled.
        public void Dispose()
        {
            Http.Dispose();
            Requests.Dispose();
        }
    }
}
