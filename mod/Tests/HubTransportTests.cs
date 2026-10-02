using System;
using System.Collections.Generic;
using System.Linq;
using Google.Protobuf;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class HubTransportTests
    {
        sealed class Socket : IHubSocket
        {
            readonly bool _connects;
            public bool Connected { get; set; }
            public string LastError { get; set; }
            public IncomingMessageQueue Incoming { get; } = new IncomingMessageQueue();
            public readonly List<Wire.ClientMessage> Sent = new List<Wire.ClientMessage>();
            public int Disposals;
            public bool ThrowOnConnect;
            public string Host, Path, Token;
            public int Port;
            public Socket(bool connects = true, string error = null) { _connects = connects; LastError = error; }
            public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000)
            {
                Host = host; Port = port; Path = path; Token = token;
                if (ThrowOnConnect) { Connected = true; throw new InvalidOperationException("setup failed"); }
                return Connected = _connects;
            }
            public bool SendBinary(byte[] payload)
            {
                if (!Connected) return false;
                Sent.Add(Wire.ClientMessage.Parser.ParseFrom(payload));
                return true;
            }
            public void Dispose() { Disposals++; Connected = false; Incoming.Close(); }
            public void Enqueue(ulong id) => Incoming.Enqueue(new Wire.Event {
                Sessions = new Wire.SessionsReply { Sessions = { new Wire.SessionView { Runtime = new Wire.SessionRuntimeView { Seq = id } } } }
            }.ToByteArray());
        }

        sealed class Environment : IDisposable
        {
            readonly bool _autoConnect = Settings.S.autoConnect;
            readonly float _time = UnityEngine.Time.realtimeSinceStartup;
            readonly Queue<Socket> _sockets;
            public readonly Queue<Action> Pending = new Queue<Action>();
            public readonly HubTransport Hub;
            public Environment(params Socket[] sockets)
            {
                Settings.S.autoConnect = true;
                UnityEngine.Time.realtimeSinceStartup = 100f;
                _sockets = new Queue<Socket>(sockets);
                Hub = new HubTransport(() => _sockets.Dequeue(), action => Pending.Enqueue(action));
            }
            public void Complete() => Pending.Dequeue()();
            public void Connect() { Hub.Connect(); Complete(); Hub.Update(); }
            public void Dispose()
            {
                Hub.Disconnect();
                Settings.S.autoConnect = _autoConnect;
                UnityEngine.Time.realtimeSinceStartup = _time;
            }
        }

        public static void InputAcceptanceTracksConnectionWithoutReplay()
        {
            var socket = new Socket();
            using (var env = new Environment(socket))
            {
                var terminal = new TerminalIO(env.Hub);
                Assert.That(terminal.SendKeys("agent", new[] { "before" }, true), Is.False);
                env.Connect();
                Assert.That(socket.Sent, Is.Empty);
                Assert.That(terminal.SendKeys("agent", new[] { "accepted" }, true), Is.True);
                Assert.That(socket.Sent.Count, Is.EqualTo(1));
                socket.Dispose();
                Assert.That(terminal.SendKeys("agent", new[] { "after" }, true), Is.False);
                Assert.That(socket.Sent.Count, Is.EqualTo(1));
            }
        }

        public static void EveryConnectionAttemptInvalidatesCapabilitiesBeforeScheduling()
        {
            var previous = DaemonCapabilities.Current;
            try
            {
                var first = new Socket();
                using var env = new Environment(first, new Socket(false, "refused"), new Socket());
                var capabilities = DaemonCapabilities.FromWire(new Wire.Capabilities { Runtime = "slopcar" });
                int attempts = 0;
                env.Hub.OnConnecting = () => { capabilities = DaemonCapabilities.Reset(); attempts++; };
                env.Hub.Connect();
                Assert.That(capabilities.Known, Is.False, "manual attempt invalidates before completion");
                Assert.That(DaemonCapabilities.Current, Is.SameAs(capabilities));
                env.Complete();
                env.Hub.Update();
                Assert.That(capabilities.Known, Is.False, "socket success is not a capability announcement");
                capabilities = DaemonCapabilities.FromWire(new Wire.Capabilities { Runtime = "native" });
                first.Connected = false;
                env.Hub.Update();
                UnityEngine.Time.realtimeSinceStartup += 1f;
                env.Hub.Update();
                Assert.That(capabilities.Known, Is.False, "automatic retry invalidates both views");
                Assert.That(DaemonCapabilities.Current, Is.SameAs(capabilities));
                env.Complete();
                env.Hub.Update();
                UnityEngine.Time.realtimeSinceStartup += 2f;
                env.Hub.Update();
                Assert.That(attempts, Is.EqualTo(3), "failed attempts also notify before retry");
                Assert.That(capabilities.Known, Is.False);
            }
            finally { DaemonCapabilities.Current = previous; }
        }

        public static void SocketSetupExceptionsArePumpedAndRetryAfterBackoff()
        {
            var broken = new Socket { ThrowOnConnect = true };
            using var env = new Environment(broken, new Socket());
            env.Hub.Connect();
            env.Complete();
            Assert.That(env.Hub.Status, Is.EqualTo("connecting"), "worker cannot publish UI state");
            Assert.That(broken.Disposals, Is.Zero, "main thread owns failed socket cleanup");
            env.Hub.Update();
            Assert.That(broken.Disposals, Is.EqualTo(1));
            Assert.That(env.Hub.Status, Is.EqualTo("offline: setup failed"));
            UnityEngine.Time.realtimeSinceStartup += 1f;
            env.Hub.Update();
            env.Complete();
            env.Hub.Update();
            Assert.That(env.Hub.Connected, Is.True, "exception releases attempt for retry");
        }

        public static void FactoryAndSchedulerExceptionsRecoverAndStaleFailuresAreIgnored()
        {
            using var env = new Environment();
            foreach (bool schedulerFails in new[] { false, true })
            {
                bool fail = true;
                var socket = new Socket();
                var pending = new Queue<Action>();
                var hub = new HubTransport(
                    () => { if (fail) throw new InvalidOperationException("factory failed"); return socket; },
                    action => { if (fail && schedulerFails) throw new InvalidOperationException("scheduler failed"); pending.Enqueue(action); });
                try
                {
                    hub.Connect();
                    if (!schedulerFails) pending.Dequeue()();
                    hub.Update();
                    Assert.That(hub.Status, Is.EqualTo("offline: " + (schedulerFails ? "scheduler" : "factory") + " failed"));
                    Assert.That(pending, Is.Empty, "backoff prevents immediate retry");
                    fail = false;
                    UnityEngine.Time.realtimeSinceStartup += 1f;
                    hub.Update();
                    pending.Dequeue()();
                    hub.Update();
                    Assert.That(hub.Connected, Is.True);
                    fail = true;
                    hub.Connect();
                    if (!schedulerFails) pending.Dequeue()();
                    fail = false;
                    hub.Connect();
                    pending.Dequeue()();
                    hub.Update();
                    Assert.That(hub.Connected, Is.True, "stale failure cannot replace newer success");
                }
                finally { hub.Disconnect(); }
            }
        }

        public static void FailedConnectionsBackOffCapAndResetAfterSuccess()
        {
            var failed = Enumerable.Range(0, 5).Select(_ => new Socket(false, "refused")).ToArray();
            var connected = new Socket();
            using var env = new Environment(failed.Concat(new[] { connected, new Socket() }).ToArray());
            env.Hub.Connect();
            foreach (var item in failed.Select((socket, index) => (socket, delay: new[] { 1, 2, 4, 5, 5 }[index])))
            {
                env.Complete();
                env.Hub.Update();
                Assert.That(env.Hub.Status, Is.EqualTo("offline: refused"));
                Assert.That(item.socket.Disposals, Is.EqualTo(1));
                Assert.That(env.Hub.Connected, Is.False);
                float now = UnityEngine.Time.realtimeSinceStartup;
                UnityEngine.Time.realtimeSinceStartup = now + item.delay - 0.25f;
                env.Hub.Update();
                Assert.That(env.Pending, Is.Empty, "no retry before backoff deadline");
                UnityEngine.Time.realtimeSinceStartup = now + item.delay;
                env.Hub.Update();
                env.Hub.Update();
                Assert.That(env.Pending.Count, Is.EqualTo(1), "only one attempt at the deadline");
            }
            int installed = 0;
            env.Hub.OnConnected = () => { Assert.That(env.Hub.Connected, Is.True); installed++; };
            env.Complete();
            env.Hub.Update();
            Assert.That(installed, Is.EqualTo(1));
            Assert.That(env.Hub.Status, Is.EqualTo("connected"));
            connected.Connected = false;
            env.Hub.Update();
            Assert.That(env.Hub.Status, Is.EqualTo("offline: closed"));
            Assert.That(connected.Disposals, Is.EqualTo(1));
            UnityEngine.Time.realtimeSinceStartup += 1f;
            env.Hub.Update();
            Assert.That(env.Pending.Count, Is.EqualTo(1), "successful connection resets retry delay to one second");
        }

        public static void ManualReconnectBypassesBackoffAndSendsOnlyWhileConnected()
        {
            var old = new Socket();
            var replacement = new Socket();
            using var env = new Environment(old, replacement);
            var message = new Wire.ClientMessage { Sub = new Wire.NameReq { Name = "agent" } };
            env.Hub.Send(message);
            env.Connect();
            var endpoint = Settings.Connection;
            Assert.That(old.Host, Is.EqualTo(endpoint.Host));
            Assert.That(old.Port, Is.EqualTo(endpoint.Port));
            Assert.That(old.Token, Is.EqualTo(endpoint.Token));
            Assert.That(old.Path, Is.EqualTo(WireProtocol.WsPath));
            env.Hub.Send(message);
            Assert.That(old.Sent.Single().Sub.Name, Is.EqualTo("agent"));
            old.Connected = false;
            old.LastError = "read failed";
            env.Hub.Send(message);
            env.Hub.Update();
            Assert.That(env.Hub.Status, Is.EqualTo("offline: read failed"));
            env.Hub.Connect();
            Assert.That(env.Pending.Count, Is.EqualTo(1), "manual reconnect skips retry delay");
            env.Hub.Send(message);
            env.Complete();
            env.Hub.Update();
            env.Hub.Send(message);
            Assert.That(old.Sent.Count, Is.EqualTo(1));
            Assert.That(replacement.Sent.Single().Sub.Name, Is.EqualTo("agent"));
            Assert.That(old.Disposals, Is.EqualTo(1));
        }

        public static void DisconnectDisposesCompletedAttemptBeforeItCanBeInstalled()
        {
            var socket = new Socket();
            using var env = new Environment(socket);
            int installed = 0;
            env.Hub.OnConnected = () => installed++;
            env.Hub.Connect();
            env.Complete();
            socket.Enqueue(1);
            env.Hub.Disconnect();
            Assert.That(socket.Disposals, Is.EqualTo(1));
            Assert.That(socket.Incoming.Count, Is.Zero);
            Assert.That(installed, Is.Zero);
            Assert.That(env.Hub.Status, Is.EqualTo("disconnected"));
            Assert.That(env.Hub.Connected, Is.False);
        }

        public static void DisablingAutoConnectReleasesInstalledSocketAndQueuedEvents()
        {
            var socket = new Socket();
            var replacement = new Socket();
            using var env = new Environment(socket, replacement);
            env.Connect();
            int messages = 0;
            env.Hub.OnMessage = _ => messages++;
            socket.Enqueue(1);
            Settings.S.autoConnect = false;
            env.Hub.Update();
            env.Hub.Update();
            Assert.That(socket.Disposals, Is.EqualTo(1));
            Assert.That(socket.Incoming.Count, Is.Zero);
            Assert.That(messages, Is.Zero);
            Assert.That(env.Hub.Status, Is.EqualTo("disconnected"));
            Assert.That(env.Pending, Is.Empty);
            Settings.S.autoConnect = true;
            env.Hub.Update();
            Assert.That(env.Pending.Count, Is.EqualTo(1));
            env.Complete();
            env.Hub.Update();
            Assert.That(env.Hub.Connected, Is.True);
        }

        public static void DisabledAutoConnectDisposesQueuedCompletion()
        {
            var socket = new Socket();
            using var env = new Environment(socket);
            env.Hub.Connect();
            env.Complete();
            Settings.S.autoConnect = false;
            env.Hub.Update();
            Assert.That(socket.Disposals, Is.EqualTo(1));
            Assert.That(env.Hub.Connected, Is.False);
            Assert.That(env.Hub.Status, Is.EqualTo("disconnected"));
            Assert.That(env.Pending, Is.Empty);
        }

        public static void EventPumpBoundsWorkAndContinuesAfterMalformedMessagesAndCallbackFailures()
        {
            var socket = new Socket();
            using var env = new Environment(socket);
            env.Connect();
            socket.Incoming.Enqueue(new byte[] { 0x80 });
            for (ulong i = 1; i <= 33; i++) socket.Enqueue(i);
            var delivered = new List<ulong>();
            env.Hub.OnMessage = ev =>
            {
                ulong id = ev.Sessions.Sessions[0].Runtime.Seq;
                delivered.Add(id);
                if (id == 1) throw new InvalidOperationException("consumer failed");
            };
            env.Hub.Update();
            Assert.That(delivered, Is.EqualTo(Enumerable.Range(1, 31).Select(i => (ulong)i)));
            Assert.That(socket.Incoming.Count, Is.EqualTo(2), "malformed event also consumes frame budget");
            env.Hub.Update();
            Assert.That(delivered, Is.EqualTo(Enumerable.Range(1, 33).Select(i => (ulong)i)));
            env.Hub.Update();
            Assert.That(delivered.Count, Is.EqualTo(33), "idle update does not replay prior batch");
            Assert.That(env.Hub.Connected, Is.True, "bad event does not disconnect transport");
        }

        public static void ReconnectInsideMessageCallbackRetainsBatchOwnership()
        {
            var old = new Socket();
            var next = new Socket();
            using var env = new Environment(old, next);
            env.Connect();
            old.Enqueue(1);
            old.Enqueue(2);
            var delivered = new List<ulong>();
            env.Hub.OnMessage = ev =>
            {
                ulong id = ev.Sessions.Sessions[0].Runtime.Seq;
                delivered.Add(id);
                if (id == 1)
                {
                    env.Hub.Connect();
                    env.Complete();
                    next.Enqueue(3);
                }
            };
            env.Hub.Update();
            Assert.That(delivered, Is.EqualTo(new ulong[] { 1, 2 }), "captured batch finishes without draining replacement queue");
            Assert.That(next.Incoming.Count, Is.EqualTo(1));
            Assert.That(old.Disposals, Is.EqualTo(1));
            env.Hub.Update();
            Assert.That(delivered, Is.EqualTo(new ulong[] { 1, 2, 3 }));
            Assert.That(env.Hub.Connected, Is.True);
            Assert.That(next.Disposals, Is.Zero);
        }
    }
}
