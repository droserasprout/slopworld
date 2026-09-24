using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using Google.Protobuf;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace SlopWorld
{
    // Await bounded background requests. Queue completion callbacks for Unity's main thread.
    // Callbacks use game state and IMGUI, which require the main thread.
    public static class DaemonClient
    {
        const string GetMethod = "GET";
        const string PostMethod = "POST";
        const string PutMethod = "PUT";
        const string DeleteMethod = "DELETE";
        const int DefaultTimeoutMs = 5000;
        const string TokenHeader = WireProtocol.TokenHeader;
        const string SessionHeader = WireProtocol.SessionHeader;
        const string ContentType = "application/x-protobuf";
        const int MaxCompletionsPerFrame = 32;
        static readonly long CompletionBudgetTicks = System.Diagnostics.Stopwatch.Frequency / 500;
        const string CompletionTraceName = "http-completions";

        // Mono can also block inside request startup. Limit concurrent HTTP work
        // so bursts leave workers available for network and timer completions.
        const int MaxRequests = 8;
        static readonly SemaphoreSlim Requests = new SemaphoreSlim(MaxRequests);

        static readonly ConcurrentQueue<Action> Completions = new ConcurrentQueue<Action>();

        public static string BaseUrl => Settings.Connection.BaseUrl;

        public static void Get<T>(string path, Action<T> ok, Action<string> fail = null,
            string session = null, int timeoutMs = DefaultTimeoutMs) where T : IMessage<T>, new() =>
            Send(GetMethod, path, null, ok, fail, session, timeoutMs);
        public static void Post<T>(string path, IMessage body, Action<T> ok, Action<string> fail = null,
            string session = null) where T : IMessage<T>, new() =>
            Send(PostMethod, path, body ?? new Wire.Empty(), ok, fail, session);
        public static void Put<T>(string path, IMessage body, Action<T> ok, Action<string> fail = null,
            string session = null) where T : IMessage<T>, new() =>
            Send(PutMethod, path, body ?? new Wire.Empty(), ok, fail, session);
        public static void Delete<T>(string path, Action<T> ok, Action<string> fail = null,
            string session = null) where T : IMessage<T>, new() =>
            Send(DeleteMethod, path, null, ok, fail, session);
        public static void Delete<T>(string path, IMessage body, Action<T> ok, Action<string> fail = null,
            string session = null) where T : IMessage<T>, new() =>
            Send(DeleteMethod, path, body, ok, fail, session);
        public static void Post(string path, IMessage body, Action<Wire.Ack> ok, Action<string> fail = null,
            string session = null) => Post<Wire.Ack>(path, body, ok, fail, session);
        public static void Put(string path, IMessage body, Action<Wire.Ack> ok, Action<string> fail = null,
            string session = null) => Put<Wire.Ack>(path, body, ok, fail, session);
        public static void Delete(string path, Action<Wire.Ack> ok, Action<string> fail = null,
            string session = null) => Delete<Wire.Ack>(path, ok, fail, session);
        public static void Delete(string path, IMessage body, Action<Wire.Ack> ok, Action<string> fail = null,
            string session = null) => Delete<Wire.Ack>(path, body, ok, fail, session);

        // Queue background integration callbacks with HTTP callbacks.
        // They can then update Unity and RimWorld state on the main thread.
        public static void OnMainThread(Action action)
        {
            if (action != null) Completions.Enqueue(action);
        }

        public static void Send<T>(string method, string path, IMessage body,
                                Action<T> ok, Action<string> fail, string session = null,
                                int timeoutMs = DefaultTimeoutMs) where T : IMessage<T>, new()
        {
            // Start a request trace only when the route has a trace category.
            // Other routes leave a null name, which can cause PerfTrace.End to fail before the caller's callback runs.
            string trace = TraceName(path);
            long started = trace == null ? 0L : PerfTrace.Start();
            ThreadPool.QueueUserWorkItem(async _ =>
            {
                bool acquired = false;
                HttpWebRequest req = null;
                using (var deadline = new CancellationTokenSource(timeoutMs))
                using (deadline.Token.Register(() => req?.Abort()))
                {
                    try
                    {
                        await Requests.WaitAsync(deadline.Token).ConfigureAwait(false);
                        acquired = true;
                        var connection = Settings.Connection;
                        req = (HttpWebRequest)WebRequest.Create(connection.BaseUrl + path);
                        // Sidebar refreshes send requests for multiple projects.
                        // The default connection limit can delay status reads behind two slow workspace requests.
                        req.ServicePoint.ConnectionLimit = MaxRequests;
                        req.Method = method;
                        req.Accept = ContentType;
                        req.Timeout = timeoutMs;
                        req.ReadWriteTimeout = timeoutMs;
                        req.Proxy = null;
                        if (!string.IsNullOrEmpty(connection.Token))
                            req.Headers[TokenHeader] = connection.Token;
                        if (!string.IsNullOrEmpty(session))
                            req.Headers[SessionHeader] = session;

                        // Mono's synchronous HttpWebRequest blocks workers that its
                        // own network completion path needs. A burst of clipboard
                        // reads can starve the entire pool. Await every I/O boundary.
                        // Async HttpWebRequest does not honor Timeout consistently;
                        // abort the whole operation after the explicit deadline.
                        deadline.Token.ThrowIfCancellationRequested();
                        if (body != null)
                        {
                            req.ContentType = ContentType;
                            var data = body.ToByteArray();
                            req.ContentLength = data.Length;
                            using (var s = await req.GetRequestStreamAsync().ConfigureAwait(false))
                                await s.WriteAsync(data, 0, data.Length, deadline.Token).ConfigureAwait(false);
                        }

                        using (var resp = (HttpWebResponse)await req.GetResponseAsync().ConfigureAwait(false))
                        using (var stream = resp.GetResponseStream() ?? Stream.Null)
                        {
                            if (resp.ContentType != ContentType) throw new IOException("Expected a Protobuf protocol 2 response. Update the daemon and mod together.");
                            var val = new MessageParser<T>(() => new T()).ParseFrom(await ReadBounded(stream, deadline.Token).ConfigureAwait(false));
                            Completions.Enqueue(() =>
                            {
                                if (started != 0L) PerfTrace.End(trace, started, 1, Completions.Count);
                                ok?.Invoke(val);
                            });
                        }
                    }
                    catch (WebException we)
                    {
                        // Prefer the explanation that slopd supplies in the error body.
                        string msg = we.Message;
                        if (we.Response is HttpWebResponse r)
                        {
                            try
                            {
                                using (r)
                                using (var stream = r.GetResponseStream() ?? Stream.Null)
                                {
                                    var parsed = Wire.Error.Parser.ParseFrom(await ReadBounded(stream, deadline.Token).ConfigureAwait(false)).Error_;
                                    if (!string.IsNullOrEmpty(parsed)) msg = parsed;
                                }
                            }
                            catch { /* fall back to we.Message */ }
                        }
                        Completions.Enqueue(() =>
                        {
                            if (started != 0L) PerfTrace.End(trace, started, 1, Completions.Count);
                            fail?.Invoke(msg);
                        });
                    }
                    catch (Exception e)
                    {
                        Completions.Enqueue(() =>
                        {
                            if (started != 0L) PerfTrace.End(trace, started, 1, Completions.Count);
                            fail?.Invoke(e.Message);
                        });
                    }
                    finally { if (acquired) Requests.Release(); }
                }
            });
        }

        static async Task<byte[]> ReadBounded(Stream stream, CancellationToken cancellationToken)
        {
            const int limit = 32 * 1024 * 1024;
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) != 0)
                {
                    if (output.Length + count > limit) throw new IOException("Protobuf response exceeds limit");
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
        public static int PendingCompletions => Completions.Count;

        public static int PumpCompletions()
        {
            long started = PerfTrace.Start();
            long budgetStarted = System.Diagnostics.Stopwatch.GetTimestamp();
            int count = 0;
            while (count < MaxCompletionsPerFrame && Completions.TryDequeue(out var a))
            {
                count++;
                try { a(); }
                catch (Exception e) { Log.Error($"[SlopWorld] completion: {e}"); }
                // Process at least one callback. Then yield after approximately 2 ms, even before reaching 32 callbacks.
                // The pump cannot interrupt a callback after it starts.
                if (System.Diagnostics.Stopwatch.GetTimestamp() - budgetStarted >= CompletionBudgetTicks)
                    break;
            }
            PerfTrace.End(CompletionTraceName, started, count, Completions.Count);
            return count;
        }
    }
}
