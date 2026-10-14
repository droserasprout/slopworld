using System;
using Google.Protobuf;

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
        static readonly DaemonHttpTransport Transport = new DaemonHttpTransport();

        public static string BaseUrl => Endpoint.Resolve().BaseUrl;

        public static void Get<T>(
            string path,
            Action<T> ok,
            Action<string> fail = null,
            string session = null,
            int timeoutMs = DefaultTimeoutMs
        )
            where T : IMessage<T>, new() =>
            Send(GetMethod, path, null, ok, fail, session, timeoutMs);

        public static void Post<T>(
            string path,
            IMessage body,
            Action<T> ok,
            Action<string> fail = null,
            string session = null
        )
            where T : IMessage<T>, new() =>
            Send(PostMethod, path, body ?? new Wire.Empty(), ok, fail, session);

        public static void Put<T>(
            string path,
            IMessage body,
            Action<T> ok,
            Action<string> fail = null,
            string session = null
        )
            where T : IMessage<T>, new() =>
            Send(PutMethod, path, body ?? new Wire.Empty(), ok, fail, session);

        public static void Delete<T>(
            string path,
            Action<T> ok,
            Action<string> fail = null,
            string session = null
        )
            where T : IMessage<T>, new() => Send(DeleteMethod, path, null, ok, fail, session);

        public static void Delete<T>(
            string path,
            IMessage body,
            Action<T> ok,
            Action<string> fail = null,
            string session = null
        )
            where T : IMessage<T>, new() => Send(DeleteMethod, path, body, ok, fail, session);

        public static void Post(
            string path,
            IMessage body,
            Action<Wire.Ack> ok,
            Action<string> fail = null,
            string session = null
        ) => Post<Wire.Ack>(path, body, ok, fail, session);

        public static void Put(
            string path,
            IMessage body,
            Action<Wire.Ack> ok,
            Action<string> fail = null,
            string session = null
        ) => Put<Wire.Ack>(path, body, ok, fail, session);

        public static void Delete(
            string path,
            Action<Wire.Ack> ok,
            Action<string> fail = null,
            string session = null
        ) => Delete<Wire.Ack>(path, ok, fail, session);

        public static void Delete(
            string path,
            IMessage body,
            Action<Wire.Ack> ok,
            Action<string> fail = null,
            string session = null
        ) => Delete<Wire.Ack>(path, body, ok, fail, session);

        public static void OnMainThread(Action action) => Transport.OnMainThread(action);

        public static void Send<T>(
            string method,
            string path,
            IMessage body,
            Action<T> ok,
            Action<string> fail,
            string session = null,
            int timeoutMs = DefaultTimeoutMs
        )
            where T : IMessage<T>, new() =>
            Transport.Send(method, path, body, ok, fail, session, timeoutMs);

        public static int PendingCompletions => Transport.PendingCompletions;

        public static int PumpCompletions(Action<Exception> reportError) =>
            Transport.PumpCompletions(reportError);
    }
}
