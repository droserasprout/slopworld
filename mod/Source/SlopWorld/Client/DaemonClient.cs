using System;
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
        const string TokenHeader = "X-Slop-Token";
        const string SessionHeader = "X-Slop-Session";
        const string JsonContentType = "application/json";
        const int MaxRequestBodyBytes = 4 * 1024 * 1024;
        const int MaxResponseBytes = 12 * 1024 * 1024;
        const int MaxInFlightRequests = 8;
        const int MaxCompletions = 64;
        const int MaxCompletionBytes = 32 * 1024 * 1024;
        const int CompletionOverheadBytes = 256;
        const int MaxHttpCompletionReservation = MaxResponseBytes + CompletionOverheadBytes;
        const int DefaultActionBytes = 4096;
        const int MaxErrorMessageChars = 4096;
        const int MaxCompletionsPerFrame = 32;
        const string CompletionTraceName = "http-completions";

        static readonly BoundedActionQueue Completions =
            new BoundedActionQueue(MaxCompletions, MaxCompletionBytes);
        static int _inFlightRequests;
        static int _droppedCompletions;

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
        public static void OnMainThread(Action action, int estimatedBytes = DefaultActionBytes)
        {
            if (action == null) return;
            if (!Completions.TryEnqueue(action, estimatedBytes))
                Interlocked.Increment(ref _droppedCompletions);
        }

        public static void Send(string method, string path, string body,
                                Action<JVal> ok, Action<string> fail, string session = null,
                                int timeoutMs = DefaultTimeoutMs)
        {
            int bodyBytes = body == null ? 0 : Encoding.UTF8.GetByteCount(body);
            if (bodyBytes > MaxRequestBodyBytes)
            {
                Reject(fail, "request body exceeds the client limit");
                return;
            }

            bool needsCompletion = ok != null || fail != null;
            if (needsCompletion && !Completions.TryReserve(MaxHttpCompletionReservation))
            {
                Reject(fail, "HTTP completion queue is full");
                return;
            }

            if (!TryAcquireRequestSlot())
            {
                if (needsCompletion) Completions.ReleaseReservation(MaxHttpCompletionReservation);
                Reject(fail, "too many HTTP requests are in flight");
                return;
            }

            bool queued;
            try
            {
                queued = ThreadPool.QueueUserWorkItem(_ => RunRequest(
                    method, path, body, ok, fail, session, timeoutMs, needsCompletion));
            }
            catch
            {
                queued = false;
            }

            if (queued) return;
            Interlocked.Decrement(ref _inFlightRequests);
            if (needsCompletion)
                QueueReservedFailure(fail, "could not queue HTTP request",
                                      MaxHttpCompletionReservation);
        }

        static void RunRequest(string method, string path, string body,
                               Action<JVal> ok, Action<string> fail, string session,
                               int timeoutMs, bool needsCompletion)
        {
            bool reservationHeld = needsCompletion;
            try
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
                    using (var stream = resp.GetResponseStream() ?? Stream.Null)
                    {
                        var text = ReadBody(stream);
                        var val = JVal.Parse(text);
                        if (ok != null)
                        {
                            EnqueueReserved(() => ok(val), CompletionBytes(text),
                                             MaxHttpCompletionReservation);
                            reservationHeld = false;
                        }
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
                            using (r)
                            using (var stream = r.GetResponseStream() ?? Stream.Null)
                            {
                                var parsed = JVal.Parse(ReadBody(stream))["error"].Str;
                                if (!string.IsNullOrEmpty(parsed)) msg = parsed;
                            }
                        }
                        catch { /* fall back to we.Message */ }
                    }
                    QueueFailure(fail, msg, ref reservationHeld);
                }
                catch (Exception e)
                {
                    QueueFailure(fail, e.Message, ref reservationHeld);
                }
            }
            finally
            {
                if (reservationHeld)
                    Completions.ReleaseReservation(MaxHttpCompletionReservation);
                Interlocked.Decrement(ref _inFlightRequests);
            }
        }

        static bool TryAcquireRequestSlot()
        {
            while (true)
            {
                int current = Volatile.Read(ref _inFlightRequests);
                if (current >= MaxInFlightRequests) return false;
                if (Interlocked.CompareExchange(ref _inFlightRequests, current + 1, current) == current)
                    return true;
            }
        }

        static void Reject(Action<string> fail, string message)
        {
            if (fail != null) QueueCompletion(() => fail(LimitError(message)), message);
        }

        static void QueueFailure(Action<string> fail, string message, ref bool reservationHeld)
        {
            if (fail == null) return;
            message = LimitError(message);
            EnqueueReserved(() => fail(message), CompletionBytes(message),
                            MaxHttpCompletionReservation);
            reservationHeld = false;
        }

        static void QueueReservedFailure(Action<string> fail, string message, int reservedBytes)
        {
            if (fail == null)
            {
                Completions.ReleaseReservation(reservedBytes);
                return;
            }
            message = LimitError(message);
            EnqueueReserved(() => fail(message), CompletionBytes(message), reservedBytes);
        }

        static void EnqueueReserved(Action action, int actualBytes, int reservedBytes)
        {
            if (!Completions.EnqueueReserved(action, reservedBytes, actualBytes))
                Interlocked.Increment(ref _droppedCompletions);
        }

        static void QueueCompletion(Action action, string sizeSource)
        {
            if (Completions.TryEnqueue(action, CompletionBytes(sizeSource))) return;
            Interlocked.Increment(ref _droppedCompletions);
        }

        static string ReadBody(Stream stream)
        {
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[8192];
                int total = 0;
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (read > MaxResponseBytes - total)
                        throw new InvalidDataException("HTTP response exceeds the client limit");
                    ms.Write(buffer, 0, read);
                    total += read;
                }
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        static int CompletionBytes(string value)
        {
            long bytes = (value == null ? 0 : Encoding.UTF8.GetByteCount(value)) +
                         CompletionOverheadBytes;
            return (int)Math.Min(MaxHttpCompletionReservation, bytes);
        }

        static string LimitError(string message)
        {
            if (string.IsNullOrEmpty(message)) return "HTTP request failed";
            return message.Length <= MaxErrorMessageChars
                ? message : message.Substring(0, MaxErrorMessageChars);
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
            int dropped = Interlocked.Exchange(ref _droppedCompletions, 0);
            if (dropped > 0)
                Log.Warning($"[SlopWorld] dropped {dropped} main-thread completions at queue limit");
            PerfTrace.End(CompletionTraceName, started, count, Completions.Count);
            return count;
        }
    }
}
