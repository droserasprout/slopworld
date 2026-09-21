using Google.Protobuf;
using System;
using System.Collections.Concurrent;
using System.Threading;
using Verse;

namespace SlopWorld
{
    // Main-thread WebSocket pump with reconnect backoff. The coordinator dispatches its events.
    class HubTransport
    {
        // Set by the coordinator after the services exist, so the transport can stay unaware
        // of what a connect or a message means.
        public Action OnConnected = null;
        public Action<Wire.Event> OnMessage = null;

        public string Status { get; private set; } = "disconnected";
        public bool Connected => _ws != null && _ws.Connected;

        readonly HubEventBatch _batch = new HubEventBatch();
        static readonly Action<Exception> BadEvent = e =>
            Log.Warning($"[SlopWorld] bad event: {e.Message}");

        sealed class ConnectResult
        {
            public int Serial;
            public IHubSocket Socket;
            public string Error;
        }

        readonly ConcurrentQueue<ConnectResult> _connectResults =
            new ConcurrentQueue<ConnectResult>();
        readonly System.Func<IHubSocket> _socketFactory;
        readonly System.Action<System.Action> _queueConnect;
        IHubSocket _ws;
        float _nextRetry;
        int _backoff = 1;
        int _connectSerial;
        bool _connecting;

        public HubTransport() : this(() => new MiniWebSocket(),
            action => ThreadPool.QueueUserWorkItem(_ => action()))
        { }

        // The game uses the default ThreadPool/ MiniWebSocket pair. Tests can provide a
        // deterministic scheduler and socket while exercising the same result pump.
        internal HubTransport(System.Func<IHubSocket> socketFactory,
                             System.Action<System.Action> queueConnect)
        {
            _socketFactory = socketFactory;
            _queueConnect = queueConnect;
        }

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
        public void Send(Wire.ClientMessage message)
        {
            if (_ws == null || !_ws.Connected) return;
            _ws.SendBinary(message.ToByteArray());
        }

        // Called every frame from the coordinator's Update.
        public void Update()
        {
            if (!Settings.AutoConnect)
            {
                // Disabling auto-connect must invalidate an attempt even when no socket has
                // been installed yet. Otherwise its eventual result leaves _connecting stuck.
                InvalidateConnectAttempt();
                PumpConnectResults();
                return;
            }

            PumpConnectResults();

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
            // Capture the queue: a callback may disconnect or replace the socket.
            var incoming = _ws.Incoming;
            int count = _batch.Read(incoming, BadEvent);
            try
            {
                for (int i = 0; i < _batch.Count; i++)
                {
                    if (!_batch.ShouldDispatch(i)) continue;
                    try { OnMessage?.Invoke(_batch[i]); }
                    catch (Exception e) { BadEvent(e); }
                }
            }
            finally
            {
                // Release parsed payloads immediately, including after callback failures.
                _batch.Clear();
                PerfTrace.End("ws-events", started, count, incoming.Count);
            }
        }

        void InvalidateConnectAttempt()
        {
            if (_ws != null)
            {
                // Disconnect also drains queued results and releases the installed socket.
                Disconnect();
                return;
            }

            _connectSerial++;
            _connecting = false;
            while (_connectResults.TryDequeue(out var result)) result.Socket?.Dispose();
            Status = "disconnected";
        }

        void BeginConnect()
        {
            if (_connecting) return;
            var connection = Settings.Connection;
            int serial = ++_connectSerial;
            _connecting = true;
            Status = "connecting";
            _queueConnect(() =>
            {
                var socket = _socketFactory();
                bool connected = socket.Connect(connection.Host, connection.Port, WireProtocol.WsPath,
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
                    // A stale completion belongs to an older serial and must not release a
                    // newer attempt. The current completion is safe to settle, even when auto
                    // connect was disabled after BeginConnect.
                    if (result.Serial == _connectSerial) _connecting = false;
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

    }
}
