using System;
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

        MiniWebSocket _ws;
        float _nextRetry;
        int _backoff = 1;

        public void Connect()
        {
            Disconnect();
            _ws = new MiniWebSocket();
            Status = "connecting";

            var connection = Settings.Connection;
            if (_ws.Connect(connection.Host, connection.Port, "/ws", connection.Token))
            {
                Status = "connected";
                _backoff = 1;
                OnConnected?.Invoke();
            }
            else
            {
                ScheduleRetry(_ws.LastError);
            }
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
            _ws?.Dispose();
            _ws = null;
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
                if (UnityEngine.Time.realtimeSinceStartup >= _nextRetry)
                    Connect();
                return;
            }

            while (_ws.Incoming.TryDequeue(out var text))
            {
                try { OnMessage?.Invoke(JVal.Parse(text)); }
                catch (Exception e) { Log.Warning($"[SlopWorld] bad event: {e.Message}"); }
            }
        }
    }
}
