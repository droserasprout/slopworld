using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using Google.Protobuf;
using System.Threading;
using Verse;

namespace SlopWorld
{
    // Run requests on the thread pool. Queue completion callbacks for Unity's main thread.
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
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var connection = Settings.Connection;
                    var req = (HttpWebRequest)WebRequest.Create(connection.BaseUrl + path);
                    // Sidebar refreshes send requests for multiple projects.
                    // The default connection limit can delay status reads behind two slow workspace requests.
                    req.ServicePoint.ConnectionLimit = 16;
                    req.Method = method;
                    req.Accept = ContentType;
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(connection.Token))
                        req.Headers[TokenHeader] = connection.Token;
                    if (!string.IsNullOrEmpty(session))
                        req.Headers[SessionHeader] = session;

                    if (body != null)
                    {
                        req.ContentType = ContentType;
                        var data = body.ToByteArray();
                        req.ContentLength = data.Length;
                        using (var s = req.GetRequestStream())
                            s.Write(data, 0, data.Length);
                    }

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var stream = resp.GetResponseStream() ?? Stream.Null)
                    {
                        if (resp.ContentType != ContentType) throw new IOException("Expected a Protobuf protocol 2 response. Update the daemon and mod together.");
                        var val = new MessageParser<T>(() => new T()).ParseFrom(ReadBounded(stream));
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
                            using (var stream = r.GetResponseStream() ?? Stream.Null)
                            {
                                var parsed = Wire.Error.Parser.ParseFrom(ReadBounded(stream)).Error_;
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
            });
        }

        static byte[] ReadBounded(Stream stream)
        {
            const int limit = 32 * 1024 * 1024;
            using (var output = new MemoryStream())
            {
                var buffer = new byte[8192];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) != 0)
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
