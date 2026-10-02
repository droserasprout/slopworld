using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SlopWorld.Tests
{
    static class HubConnectionTests
    {
        sealed class FakeSocket : IHubSocket
        {
            readonly bool _result;
            public readonly string Failure;
            public bool Disposed;
            public bool Connected { get; private set; }
            public string LastError => Failure;
            public IncomingMessageQueue Incoming { get; } = new IncomingMessageQueue();

            public FakeSocket(bool result, string failure = "refused")
            {
                _result = result;
                Failure = failure;
            }

            public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000)
            {
                Connected = _result;
                return _result;
            }

            public bool SendBinary(byte[] text) => Connected;

            public void Dispose()
            {
                Disposed = true;
                Connected = false;
                Incoming.Close();
            }
        }

        sealed class Scheduler
        {
            public readonly List<Action> Pending = new List<Action>();
            public void Queue(Action action) => Pending.Add(action);
            public void Run(int index)
            {
                var action = Pending[index];
                Pending.RemoveAt(index);
                action();
            }
        }

        public static void DisabledConnectResults()
        {
            bool previous = Settings.S.autoConnect;
            try
            {
                foreach (bool result in new[] { true, false })
                {
                    var scheduler = new Scheduler();
                    var socket = new FakeSocket(result);
                    var retry = new FakeSocket(true);
                    var sockets = new Queue<FakeSocket>(new[] { socket, retry });
                    var hub = new HubTransport(() => sockets.Dequeue(), scheduler.Queue);
                    Settings.S.autoConnect = true;
                    hub.Connect();
                    Settings.S.autoConnect = false;
                    hub.Update(); // invalidate while no socket is installed
                    scheduler.Run(0); // complete the discarded attempt
                    hub.Update(); // consume its result while disabled
                    AssertEx.True(socket.Disposed, "discarded socket disposed for " + result);

                    Settings.S.autoConnect = true;
                    hub.Update();
                    AssertEx.Equal(1, scheduler.Pending.Count, "same transport schedules retry");
                    scheduler.Run(0);
                    hub.Update();
                    AssertEx.True(hub.Connected && retry.Connected && !retry.Disposed,
                                  "same transport installs retry socket");
                    AssertEx.Equal("connected", hub.Status,
                                   "auto-connect retries after discarded " + result);
                }
            }
            finally { Settings.S.autoConnect = previous; }
        }

        public static void StaleConnectResults()
        {
            bool previous = Settings.S.autoConnect;
            try
            {
                Settings.S.autoConnect = true;
                var scheduler = new Scheduler();
                var stale = new FakeSocket(true);
                var current = new FakeSocket(true);
                var sockets = new Queue<FakeSocket>(new[] { stale, current });
                var hub = new HubTransport(() => sockets.Dequeue(), scheduler.Queue);
                hub.Connect();
                hub.Connect(); // invalidates the first serial, leaves the second in flight
                scheduler.Run(0);
                hub.Update();
                AssertEx.Equal(1, scheduler.Pending.Count,
                               "stale result does not release newer in-flight attempt");
                AssertEx.True(stale.Disposed, "stale socket disposed");
                scheduler.Run(0);
                hub.Update();
                AssertEx.Equal("connected", hub.Status, "newer attempt still connects");
            }
            finally { Settings.S.autoConnect = previous; }
        }
    }
}
