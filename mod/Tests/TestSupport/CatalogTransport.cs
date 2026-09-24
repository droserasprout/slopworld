using System;
using System.Collections.Generic;
using Google.Protobuf;
namespace SlopWorld
{
    static class DaemonClient
    {
        const string GetMethod = "GET", PostMethod = "POST", PutMethod = "PUT", DeleteMethod = "DELETE";
        const int DefaultTimeoutMs = 5000;
        internal sealed class Request
        {
            public string Method, Path, Session;
            public IMessage Body;
            public Action<JVal> Ok;
            public Action<string> Fail;
        }
        public static readonly List<Request> Requests = new List<Request>();
        static void Send<T>(string method, string path, IMessage body, Action<T> ok, Action<string> fail,
            string session = null, int timeoutMs = DefaultTimeoutMs) where T : IMessage<T>, new() =>
            Requests.Add(new Request { Method = method, Path = path, Body = body, Session = session,
                Ok = value => ok?.Invoke(ProtobufFixtures.Read<T>(value)), Fail = fail });
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

    }
}
