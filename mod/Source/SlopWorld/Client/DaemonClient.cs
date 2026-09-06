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
        static readonly ConcurrentQueue<Action> Completions = new ConcurrentQueue<Action>();

        public static string BaseUrl => Settings.Connection.BaseUrl;

        public static void Get(string path, Action<JVal> ok, Action<string> fail = null,
                               string session = null) =>
            Send("GET", path, null, ok, fail, session);

        public static void Get(string path, Action<JVal> ok, Action<string> fail,
                               string session, int timeoutMs) =>
            Send("GET", path, null, ok, fail, session, timeoutMs);

        public static void Post(string path, string body, Action<JVal> ok, Action<string> fail = null,
                                string session = null) =>
            Send("POST", path, body ?? "{}", ok, fail, session);

        public static void Put(string path, string body, Action<JVal> ok, Action<string> fail = null,
                               string session = null) =>
            Send("PUT", path, body ?? "{}", ok, fail, session);

        public static void Delete(string path, Action<JVal> ok, Action<string> fail = null,
                                  string session = null) =>
            Send("DELETE", path, null, ok, fail, session);

        public static void Delete(string path, string body, Action<JVal> ok,
                                  Action<string> fail = null, string session = null) =>
            Send("DELETE", path, body ?? "{}", ok, fail, session);

        // Background integrations use the same completion lane as HTTP so their callbacks
        // can safely update Unity and RimWorld state.
        public static void OnMainThread(Action action)
        {
            if (action != null) Completions.Enqueue(action);
        }

        public static void Send(string method, string path, string body,
                                Action<JVal> ok, Action<string> fail, string session = null,
                                int timeoutMs = 5000)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var connection = Settings.Connection;
                    var req = (HttpWebRequest)WebRequest.Create(connection.BaseUrl + path);
                    req.Method = method;
                    req.Timeout = timeoutMs;
                    req.ReadWriteTimeout = timeoutMs;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(connection.Token))
                        req.Headers["X-Slop-Token"] = connection.Token;
                    if (!string.IsNullOrEmpty(session))
                        req.Headers["X-Slop-Session"] = session;

                    if (body != null)
                    {
                        req.ContentType = "application/json";
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
                        Completions.Enqueue(() => ok?.Invoke(val));
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
                    Completions.Enqueue(() => fail?.Invoke(msg));
                }
                catch (Exception e)
                {
                    Completions.Enqueue(() => fail?.Invoke(e.Message));
                }
            });
        }

        // Drained once per frame from the main thread. HTTP callbacks are non-replaceable, so
        // leave the remainder queued for a later frame instead of allowing a response burst to
        // monopolize Unity's update loop.
        const int MaxCompletionsPerFrame = 32;

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
            PerfTrace.End("http-completions", started, count, Completions.Count);
            return count;
        }
    }
}
