using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Verse;

namespace SlopWorld
{
    // The socket half of the hub: owns the WebSocket, reconnects with backoff, and is pumped
    // once per frame on the main thread. It knows nothing about sessions, projects, or config
    // — on connect it raises OnConnected, and each incoming event goes to OnMessage. The
    // coordinator wires those to the stores.
    class HubTransport
    {
        // Set by the coordinator after the services exist, so the transport can stay unaware
        // of what a connect or a message means.
        public Action OnConnected;
        public Action<JVal> OnMessage;

        public string Status { get; private set; } = "disconnected";
        public bool Connected => _ws != null && _ws.Connected;

        const int MaxEventsPerFrame = 32;

        sealed class ConnectResult
        {
            public int Serial;
            public MiniWebSocket Socket;
            public string Error;
        }

        readonly ConcurrentQueue<ConnectResult> _connectResults =
            new ConcurrentQueue<ConnectResult>();
        MiniWebSocket _ws;
        float _nextRetry;
        int _backoff = 1;
        int _connectSerial;
        bool _connecting;

        public void Connect()
        {
            Disconnect();
            _nextRetry = 0f;
            BeginConnect();
        }

        void ScheduleRetry(string error)
        {
            Status = $"offline: {error}";
            _ws?.Dispose();
            _ws = null;
            _nextRetry = UnityEngine.Time.realtimeSinceStartup + _backoff;
            // Capped low: the usual reason the socket dies is `make install-daemon`, which is
            // over in about two seconds.
            _backoff = Math.Min(_backoff * 2, 5);
        }

        public void Disconnect()
        {
            _connectSerial++;
            _connecting = false;
            _ws?.Dispose();
            _ws = null;
            while (_connectResults.TryDequeue(out var result)) result.Socket?.Dispose();
            Status = "disconnected";
        }

        // A guarded write: everything the hub sends over the socket goes through here, and a
        // send while the socket is down is simply dropped.
        public void Send(string json)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendText(json);
        }

        // Called every frame from the coordinator's Update.
        public void Update()
        {
            PumpConnectResults();
            if (!Settings.AutoConnect)
            {
                // Auto-connect is a live setting: turning it off must also release an
                // already-open socket, not merely stop the next retry.
                if (_ws != null) Disconnect();
                return;
            }

            if (_ws == null || !_ws.Connected)
            {
                if (_ws != null && !_ws.Connected)
                {
                    // The reader thread noticed the socket die.
                    ScheduleRetry(_ws.LastError ?? "closed");
                }
                if (!_connecting && UnityEngine.Time.realtimeSinceStartup >= _nextRetry)
                    BeginConnect();
                return;
            }

            long started = PerfTrace.Start();
            int count = 0;
            var batch = new List<JVal>(MaxEventsPerFrame);
            while (count < MaxEventsPerFrame && _ws.Incoming.TryDequeue(out var text))
            {
                count++;
                try { batch.Add(JVal.Parse(text)); }
                catch (Exception e) { Log.Warning($"[SlopWorld] bad event: {e.Message}"); }
            }

            // A live screen is replaceable, but history replies and control events are not. If
            // several live frames for the same session arrived in this batch, dispatch only the
            // newest one while retaining the original order of all other messages.
            var latestLive = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < batch.Count; i++)
            {
                if (IsReplaceableScreen(batch[i], out var name)) latestLive[name] = i;
            }
            for (int i = 0; i < batch.Count; i++)
            {
                var ev = batch[i];
                if (IsReplaceableScreen(ev, out var name) && latestLive[name] != i) continue;
                try { OnMessage?.Invoke(ev); }
                catch (Exception e) { Log.Warning($"[SlopWorld] bad event: {e.Message}"); }
            }

            PerfTrace.End("ws-events", started, count, _ws.Incoming.Count);
        }

        void BeginConnect()
        {
            if (_connecting) return;
            var connection = Settings.Connection;
            int serial = ++_connectSerial;
            _connecting = true;
            Status = "connecting";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var socket = new MiniWebSocket();
                bool connected = socket.Connect(connection.Host, connection.Port, WireContract.WsPath,
                                                connection.Token);
                _connectResults.Enqueue(new ConnectResult
                {
                    Serial = serial,
                    Socket = socket,
                    Error = connected ? null : socket.LastError,
                });
            });
        }

        void PumpConnectResults()
        {
            while (_connectResults.TryDequeue(out var result))
            {
                if (result.Serial != _connectSerial || !Settings.AutoConnect)
                {
                    result.Socket?.Dispose();
                    continue;
                }

                _connecting = false;
                if (result.Socket != null && result.Socket.Connected)
                {
                    _ws = result.Socket;
                    Status = "connected";
                    _backoff = 1;
                    OnConnected?.Invoke();
                }
                else
                {
                    result.Socket?.Dispose();
                    _ws = null;
                    ScheduleRetry(result.Error);
                }
            }
        }

        static bool IsReplaceableScreen(JVal ev, out string name)
        {
            name = null;
            if (!string.Equals(ev["t"].AsString(), WireContract.Events.Screen, StringComparison.Ordinal)) return false;
            var screen = ev["screen"];
            if (screen["off"].AsInt(0) != 0 || screen["request_id"].AsLong(0) != 0) return false;
            name = screen["name"].AsString(null);
            return !string.IsNullOrEmpty(name);
        }
    }
}
