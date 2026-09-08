using System;
using System.Collections.Generic;

namespace SlopWorld
{
    static class DaemonClient
    {
        internal sealed class Request
        {
            public string Method, Path;
            public Action<JVal> Ok;
            public Action<string> Fail;
        }
        public static readonly List<Request> Requests = new List<Request>();
        static void Add(string method, string path, Action<JVal> ok, Action<string> fail) =>
            Requests.Add(new Request { Method = method, Path = path, Ok = ok, Fail = fail });
        public static void Get(string path, Action<JVal> ok, Action<string> fail) => Add("GET", path, ok, fail);
        public static void Post(string path, string body, Action<JVal> ok, Action<string> fail) => Add("POST", path, ok, fail);
        public static void Put(string path, string body, Action<JVal> ok, Action<string> fail) => Add("PUT", path, ok, fail);
        public static void Delete(string path, Action<JVal> ok, Action<string> fail) => Add("DELETE", path, ok, fail);
    }
}
