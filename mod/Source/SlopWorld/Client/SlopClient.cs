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
    public static class SlopClient
    {
        static readonly ConcurrentQueue<Action> Completions = new ConcurrentQueue<Action>();

        public static string BaseUrl => Settings.Connection.BaseUrl;

        public static void Get(string path, Action<JVal> ok, Action<string> fail = null) =>
            Send("GET", path, null, ok, fail);

        public static void Post(string path, string body, Action<JVal> ok, Action<string> fail = null) =>
            Send("POST", path, body ?? "{}", ok, fail);

        public static void Put(string path, string body, Action<JVal> ok, Action<string> fail = null) =>
            Send("PUT", path, body ?? "{}", ok, fail);

        public static void Delete(string path, Action<JVal> ok, Action<string> fail = null) =>
            Send("DELETE", path, null, ok, fail);

        public static void Send(string method, string path, string body,
                                Action<JVal> ok, Action<string> fail)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var connection = Settings.Connection;
                    var req = (HttpWebRequest)WebRequest.Create(connection.BaseUrl + path);
                    req.Method = method;
                    req.Timeout = 5000;
                    req.ReadWriteTimeout = 5000;
                    req.Proxy = null;
                    if (!string.IsNullOrEmpty(connection.Token))
                        req.Headers["X-Slop-Token"] = connection.Token;

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

        // Drained once per frame from the main thread.
        public static void PumpCompletions()
        {
            while (Completions.TryDequeue(out var a))
            {
                try { a(); }
                catch (Exception e) { Log.Error($"[SlopWorld] completion: {e}"); }
            }
        }
    }
}
