using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using Verse;

namespace SlopWorld
{
    // Requests run on the thread pool; completions are queued and replayed on Unity's
    // main thread, because callers touch game state and IMGUI from them.
    public static class DaemonClient
    {
        const string GetMethod = "GET";
        const string PostMethod = "POST";
        const string PutMethod = "PUT";
        const string DeleteMethod = "DELETE";
        const string EmptyJsonObject = "{}";
        const int DefaultTimeoutMs = 5000;
        const string TokenHeader = WireContract.TokenHeader;
        const string SessionHeader = WireContract.SessionHeader;
        const string JsonContentType = "application/json";
        const int MaxCompletionsPerFrame = 32;
        const string CompletionTraceName = "http-completions";

        static readonly ConcurrentQueue<Action> Completions = new ConcurrentQueue<Action>();

        public static string BaseUrl => Settings.Connection.BaseUrl;

        public static void Get(string path, Action<JVal> ok, Action<string> fail = null,
                               string session = null) =>
            Send(GetMethod, path, null, ok, fail, session);

        public static void Get(string path, Action<JVal> ok, Action<string> fail,
                               string session, int timeoutMs) =>
            Send(GetMethod, path, null, ok, fail, session, timeoutMs);

        public static void Post(string path, string body, Action<JVal> ok, Action<string> fail = null,
                                string session = null) =>
            Send(PostMethod, path, body ?? EmptyJsonObject, ok, fail, session);

        public static void Put(string path, string body, Action<JVal> ok, Action<string> fail = null,
                               string session = null) =>
            Send(PutMethod, path, body ?? EmptyJsonObject, ok, fail, session);

        public static void Delete(string path, Action<JVal> ok, Action<string> fail = null,
                                  string session = null) =>
            Send(DeleteMethod, path, null, ok, fail, session);

        public static void Delete(string path, string body, Action<JVal> ok,
                                  Action<string> fail = null, string session = null) =>
            Send(DeleteMethod, path, body ?? EmptyJsonObject, ok, fail, session);

        // Background integrations use the same completion lane as HTTP so their callbacks
        // can safely update Unity and RimWorld state.
        public static void OnMainThread(Action action)
        {
            if (action != null) Completions.Enqueue(action);
        }

        public static void Send(string method, string path, string body,
                                Action<JVal> ok, Action<string> fail, string session = null,
                                int timeoutMs = DefaultTimeoutMs)
        {
            // Only start a request trace when this route has a bucket. Starting traces for
            // untracked routes leaves a null name, and the completion callback can fail in
            // PerfTrace.End before the caller's clipboard/paste callback runs.
            string trace = TraceName(path);
            long started = trace == null ? 0L : PerfTrace.Start();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var connection = Settings.Connection;
                    var req = (HttpWebRequest)WebRequest.Create(connection.BaseUrl + path);
                    // Sidebar refreshes fan out over projects. The framework default can
                    // leave fast status reads queued behind two slow workspace requests.
                    req.ServicePoint.ConnectionLimit = 16;
                    req.Method = method;
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(connection.Token))
                        req.Headers[TokenHeader] = connection.Token;
                    if (!string.IsNullOrEmpty(session))
                        req.Headers[SessionHeader] = session;

                    if (body != null)
                    {
                        req.ContentType = JsonContentType;
                        var data = Encoding.UTF8.GetBytes(body);
                        req.ContentLength = data.Length;
                        using (var s = req.GetRequestStream())
                            s.Write(data, 0, data.Length);
                    }

                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var sr = new StreamReader(resp.GetResponseStream() ?? Stream.Null))
                    {
                        var text = sr.ReadToEnd();
                        var val = JVal.Parse(text);
                        Completions.Enqueue(() =>
                        {
                            if (started != 0L) PerfTrace.End(trace, started, 1, Completions.Count);
                            ok?.Invoke(val);
                        });
                    }
                }
                catch (WebException we)
                {
                    // slopd puts a human-readable reason in the error body; prefer it.
                    string msg = we.Message;
                    if (we.Response is HttpWebResponse r)
                    {
                        try
                        {
                            using (var sr = new StreamReader(r.GetResponseStream() ?? Stream.Null))
                            {
                                var parsed = JVal.Parse(sr.ReadToEnd())["error"].Str;
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

        static string TraceName(string path)
        {
            if (path.StartsWith(WireContract.Routes.Browse, StringComparison.Ordinal))
                return "http-browse";
            if (path.StartsWith(WireContract.Routes.Git, StringComparison.Ordinal))
                return "http-git";
            if (path.StartsWith(WireContract.Routes.Search, StringComparison.Ordinal))
                return "http-search";
            return null;
        }

        // Drained once per frame from the main thread. HTTP callbacks are non-replaceable, so
        // leave the remainder queued for a later frame instead of allowing a response burst to
        // monopolize Unity's update loop.
        public static int PendingCompletions => Completions.Count;

        public static int PumpCompletions()
        {
            long started = PerfTrace.Start();
            int count = 0;
            while (count < MaxCompletionsPerFrame && Completions.TryDequeue(out var a))
            {
                count++;
                try { a(); }
                catch (Exception e) { Log.Error($"[SlopWorld] completion: {e}"); }
            }
            PerfTrace.End(CompletionTraceName, started, count, Completions.Count);
            return count;
        }
    }
}
