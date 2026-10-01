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
        // The coordinator assigns callbacks after creating the services.
        // These callbacks keep connection and message handling outside the transport.
        public Action OnConnecting = null;
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

        // The game uses ThreadPool and MiniWebSocket by default.
        // Tests can supply a controlled scheduler and socket while using the same result processing code.
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
            if (TerminalLatency.Enabled) TerminalLatency.Timeline.Reset();
            Status = $"offline: {error}";
            _ws?.Dispose();
            _ws = null;
            _nextRetry = UnityEngine.Time.realtimeSinceStartup + _backoff;
            // Keep the maximum reconnect delay short so the client recovers promptly after daemon installation.
            _backoff = Math.Min(_backoff * 2, 5);
        }

        public void Disconnect()
        {
            if (TerminalLatency.Enabled) TerminalLatency.Timeline.Reset();
            _connectSerial++;
            _connecting = false;
            _ws?.Dispose();
            _ws = null;
            while (_connectResults.TryDequeue(out var result)) result.Socket?.Dispose();
            Status = "disconnected";
        }

        // Send all hub socket messages through this method.
        // Discard messages when the socket is disconnected.
        public void Send(Wire.ClientMessage message)
        {
            if (_ws == null || !_ws.Connected) return;
            TerminalLatency.Begin(message);
            _ws.SendBinary(message.ToByteArray());
        }

        // Called every frame from the coordinator's Update.
        public void Update()
        {
            if (!Settings.AutoConnect)
            {
                // Disabling auto-connect must invalidate pending attempts, including attempts without an assigned socket.
                // Otherwise, a delayed result can leave _connecting set indefinitely.
                InvalidateConnectAttempt();
                PumpConnectResults();
                return;
            }

            PumpConnectResults();

            if (_ws == null || !_ws.Connected)
            {
                if (_ws != null && !_ws.Connected)
                {
                    // The reader thread detected disconnection.
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
                    try
                    {
                        if (TerminalLatency.Enabled) TerminalLatency.Timeline.Dispatch(_batch[i].Screen, _batch.ReceivedAt(i));
                        OnMessage?.Invoke(_batch[i]);
                    }
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
            OnConnecting?.Invoke();
            try { _queueConnect(() => OpenSocket(connection, serial)); }
            catch (Exception error)
            {
                _connectResults.Enqueue(new ConnectResult { Serial = serial, Error = error.Message });
            }
        }

        void OpenSocket(ConnectionInfo connection, int serial)
        {
            // Transfer even a partially initialized socket to the main-thread result
            // pump, which owns disposal, stale-attempt rejection, and retry timing.
            var result = new ConnectResult { Serial = serial };
            try
            {
                result.Socket = _socketFactory();
                if (!result.Socket.Connect(connection.Host, connection.Port, WireProtocol.WsPath, connection.Token))
                    result.Error = result.Socket.LastError ?? "connection failed";
            }
            catch (Exception error) { result.Error = error.Message; }
            _connectResults.Enqueue(result);
        }

        void PumpConnectResults()
        {
            while (_connectResults.TryDequeue(out var result))
            {
                if (result.Serial != _connectSerial || !Settings.AutoConnect)
                {
                    // Ignore a completion with an older serial so it cannot release a newer attempt.
                    // Complete the current attempt even if the user disables auto-connect after BeginConnect.
                    if (result.Serial == _connectSerial) _connecting = false;
                    result.Socket?.Dispose();
                    continue;
                }

                _connecting = false;
                if (result.Error == null && result.Socket != null && result.Socket.Connected)
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
