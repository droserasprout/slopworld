using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SlopWorld
{
    internal enum IncomingEnqueueResult
    {
        Accepted,
        Closed,
        Oversized,
    }

    // A bounded queue for socket events.
    // New live screens can replace queued live screens before the main thread receives them.
    // Replies, history, and control events wait for space without losing events.
    internal sealed class IncomingMessageQueue
    {
        // Limit the queue to 256 messages and 16 MiB of encoded payload data.
        // Reject any single message above 8 MiB before adding it to the queue.
        internal const int MaxMessages = 256;
        internal const int MaxBytes = 16 * 1024 * 1024;
        internal const int MaxMessageBytes = 8 * 1024 * 1024;

        sealed class Entry
        {
            public readonly ReceivedEvent Text;
            public readonly string LiveName;
            public readonly int Bytes;

            public Entry(ReceivedEvent text, string liveName, int bytes)
            {
                Text = text;
                LiveName = liveName;
                Bytes = bytes;
            }
        }

        readonly object _gate = new object();
        readonly LinkedList<Entry> _queue = new LinkedList<Entry>();
        readonly Dictionary<string, LinkedListNode<Entry>> _latestLive =
            new Dictionary<string, LinkedListNode<Entry>>(StringComparer.Ordinal);
        int _bytes;
        bool _closed;

        public int Count
        {
            get { lock (_gate) return _queue.Count; }
        }

        public int Bytes
        {
            get { lock (_gate) return _bytes; }
        }

        public IncomingEnqueueResult Enqueue(byte[] payload)
        {
            if (payload == null) return IncomingEnqueueResult.Oversized;

            int bytes = payload.Length;
            if (bytes > MaxMessageBytes) return IncomingEnqueueResult.Oversized;

            // Validate and classify before acquiring the queue lock. Decode retained live frames on dispatch.
            // Ownership of the payload transfers to the queue. Callers must not mutate it.
            // Ambiguous or malformed messages never replace queued messages.
            var text = new ReceivedEvent(payload, deferLive: true);
            var liveName = text.LiveName;
            lock (_gate)
            {
                while (!_closed)
                {
                    // Remove the old live frame before testing capacity. Appending the new one
                    // keeps every non-live event in order, including replies between frames.
                    if (liveName != null && _latestLive.TryGetValue(liveName, out var old))
                        Remove(old);

                    if (_queue.Count < MaxMessages && _bytes + bytes <= MaxBytes)
                    {
                        var node = _queue.AddLast(new Entry(text, liveName, bytes));
                        _bytes += bytes;
                        if (liveName != null) _latestLive[liveName] = node;
                        Monitor.PulseAll(_gate);
                        return IncomingEnqueueResult.Accepted;
                    }

                    // Wait for queue space without discarding events.
                    // Disposal and connection failure wake this wait so a blocked producer can exit.
                    Monitor.Wait(_gate);
                }
            }
            return IncomingEnqueueResult.Closed;
        }

        public bool TryDequeue(out ReceivedEvent text)
        {
            return TryDequeue(out text, out _);
        }

        internal bool TryDequeue(out ReceivedEvent text, out string liveName)
        {
            lock (_gate)
            {
                if (_queue.First == null)
                {
                    text = null;
                    liveName = null;
                    return false;
                }

                var node = _queue.First;
                text = node.Value.Text;
                liveName = node.Value.LiveName;
                Remove(node);
                Monitor.PulseAll(_gate);
                return true;
            }
        }

        public void Close()
        {
            lock (_gate)
            {
                _closed = true;
                // Discard queued events after disconnection to release their payload references.
                // Wake producers that are waiting for queue space.
                _queue.Clear();
                _latestLive.Clear();
                _bytes = 0;
                Monitor.PulseAll(_gate);
            }
        }

        void Remove(LinkedListNode<Entry> node)
        {
            _queue.Remove(node);
            _bytes -= node.Value.Bytes;
            if (node.Value.LiveName != null &&
                _latestLive.TryGetValue(node.Value.LiveName, out var current) &&
                ReferenceEquals(current, node))
                _latestLive.Remove(node.Value.LiveName);
        }
    }

    // Support binary Protobuf frames, ping/pong, and connection closure.
    // Use TcpClient because ClientWebSocket is unreliable on Unity Mono and Wine.
    // Read on a background thread. Callers retrieve queued events through Incoming.
    internal interface IHubSocket : IDisposable
    {
        bool Connected { get; }
        string LastError { get; }
        IncomingMessageQueue Incoming { get; }
        bool Connect(string host, int port, string path, string token, int timeoutMs = 3000);
        void SendBinary(byte[] text);
    }

    public partial class MiniWebSocket : IDisposable, IHubSocket
    {
        internal readonly IncomingMessageQueue Incoming = new IncomingMessageQueue();
        readonly ConcurrentQueue<byte[]> _outgoing = new ConcurrentQueue<byte[]>();

        // Limit frame allocations while allowing terminal screens with encoded SGR text.
        const long MaxFrame = 32L * 1024 * 1024;
        // A peer can split one message across multiple frames.
        // Limit the combined message size as well as individual frame allocations.
        const long MaxFragmentedMessage = 32L * 1024 * 1024;

        volatile bool _connected;
        public bool Connected => _connected;
        public string LastError { get; private set; }
        public int OutgoingCount => _outgoing.Count;

        IncomingMessageQueue IHubSocket.Incoming => Incoming;

        TcpClient _tcp;
        NetworkStream _net;
        Thread _reader;
        Thread _writer;
        volatile bool _closing;
        readonly object _sendLock = new object();
        readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();

        public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000)
        {
            try
            {
                LastError = null;
                _closing = false;
                _tcp = new TcpClient { NoDelay = true };
                var ar = _tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !_tcp.Connected)
                {
                    LastError = "connection timed out";
                    Cleanup();
                    return false;
                }
                _tcp.EndConnect(ar);
                // ConnectTimeout covers only the TCP handshake.
                // The HTTP upgrade also runs synchronously on Unity's main thread.
                // Limit its socket waits so an unresponsive server cannot block the game indefinitely.
                _tcp.ReceiveTimeout = timeoutMs;
                _tcp.SendTimeout = timeoutMs;
                _net = _tcp.GetStream();

                var keyBytes = new byte[16];
                _rng.GetBytes(keyBytes);
                string key = Convert.ToBase64String(keyBytes);

                var req = new StringBuilder();
                req.Append($"GET {path} HTTP/1.1\r\n");
                req.Append($"Host: {host}:{port}\r\n");
                req.Append("Upgrade: websocket\r\n");
                req.Append("Connection: Upgrade\r\n");
                req.Append($"Sec-WebSocket-Key: {key}\r\n");
                req.Append("Sec-WebSocket-Version: 13\r\n");
                req.Append("Sec-WebSocket-Protocol: slopworld.protobuf.v2\r\n");
                if (!string.IsNullOrEmpty(token))
                    req.Append($"{WireProtocol.TokenHeader}: {token}\r\n");
                req.Append("\r\n");

                var bytes = Encoding.ASCII.GetBytes(req.ToString());
                _net.Write(bytes, 0, bytes.Length);

                string response = ReadHandshake();
                if (!ValidHandshake(response, key, out string status))
                {
                    LastError = status == null ? "no handshake response" : $"handshake refused: {status}";
                    Cleanup();
                    return false;
                }

                // An established WebSocket can remain idle indefinitely.
                // Let the background reader wait without reconnecting because of inactivity.
                // Keep the socket send timeout for queued frames.
                _tcp.ReceiveTimeout = 0;
                _connected = true;
                _writer = new Thread(WriteLoop)
                {
                    IsBackground = true,
                    Name = "SlopWorld WS writer",
                };
                _reader = new Thread(ReadLoop) { IsBackground = true, Name = "SlopWorld WS" };
                _writer.Start();
                _reader.Start();
                return true;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Cleanup();
                return false;
            }
        }

        // Read one byte at a time to avoid consuming frame data after the handshake.
        string ReadHandshake()
        {
            var sb = new StringBuilder();
            int consecutive = 0;
            while (sb.Length < 8192)
            {
                int b = _net.ReadByte();
                if (b < 0) return null;
                sb.Append((char)b);
                if (b == '\n')
                {
                    consecutive++;
                    if (consecutive == 2) break;
                }
                else if (b != '\r')
                {
                    consecutive = 0;
                }
            }
            return consecutive == 2 ? sb.ToString() : null;
        }

        static bool ValidHandshake(string response, string key, out string status)
        {
            status = null;
            if (response == null) return false;

            var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return false;
            status = lines[0].Trim();
            if (!status.StartsWith("HTTP/1.1 101 ", StringComparison.Ordinal) &&
                status != "HTTP/1.1 101") return false;

            string upgrade = null;
            string connection = null;
            string accept = null;
            string protocol = null;
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (name.Equals("Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase)) protocol = value;
                else if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) upgrade = value;
                else if (name.Equals("Connection", StringComparison.OrdinalIgnoreCase)) connection = value;
                else if (name.Equals("Sec-WebSocket-Accept", StringComparison.OrdinalIgnoreCase)) accept = value;
            }

            if (!string.Equals(upgrade, "websocket", StringComparison.OrdinalIgnoreCase)) return false;
            if (connection == null || !Array.Exists(connection.Split(','),
                v => v.Trim().Equals("Upgrade", StringComparison.OrdinalIgnoreCase))) return false;

            byte[] challenge = Encoding.ASCII.GetBytes(
                key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
            string expected;
            using (var sha1 = SHA1.Create())
                expected = Convert.ToBase64String(sha1.ComputeHash(challenge));
            return protocol == "slopworld.protobuf.v2" && string.Equals(accept, expected, StringComparison.Ordinal);
        }

        public void SendBinary(byte[] text)
        {
            if (!Connected || (text == null || text.Length == 0)) return;
            _outgoing.Enqueue(text);
            _sendSignal.Set();
        }

        void WriteLoop()
        {
            try
            {
                while (!_closing)
                {
                    if (!_outgoing.TryDequeue(out var text))
                    {
                        _sendSignal.WaitOne(250);
                        continue;
                    }

                    if (_closing) break;
                    var payload = text;
                    lock (_sendLock)
                    {
                        if (_net == null) break;
                        WriteFrame(0x2, payload);
                    }
                }
            }
            catch (Exception e)
            {
                if (!_closing) LastError = e.Message;
            }
            finally
            {
                if (!_closing)
                {
                    _closing = true;
                    _connected = false;
                    _sendSignal.Set();
                }
            }
        }

        void WriteFrame(byte opcode, byte[] payload)
        {
            using (var ms = new MemoryStream())
            {
                ms.WriteByte((byte)(0x80 | opcode)); // FIN + opcode

                // Clients must mask. The length prefix widens with the payload.
                int len = payload.Length;
                if (len < 126)
                {
                    ms.WriteByte((byte)(0x80 | len));
                }
                else if (len <= ushort.MaxValue)
                {
                    ms.WriteByte(0x80 | 126);
                    ms.WriteByte((byte)(len >> 8));
                    ms.WriteByte((byte)len);
                }
                else
                {
                    ms.WriteByte(0x80 | 127);
                    for (int i = 7; i >= 0; i--)
                        ms.WriteByte((byte)((long)len >> (8 * i)));
                }

                var mask = new byte[4];
                _rng.GetBytes(mask);
                ms.Write(mask, 0, 4);

                for (int i = 0; i < payload.Length; i++)
                    ms.WriteByte((byte)(payload[i] ^ mask[i & 3]));

                var frame = ms.ToArray();
                _net.Write(frame, 0, frame.Length);
                _net.Flush();
            }
        }

        void ReadLoop()
        {
            try
            {
                var frag = new MemoryStream();
                int fragOpcode = 0;

                while (!_closing)
                {
                    var frame = ReadFrame(fragOpcode, frag.Length);
                    if (frame == null) break;

                    var payload = frame.Payload;

                    switch (frame.Opcode)
                    {
                        case 0x0: // continuation
                            frag.Write(payload, 0, payload.Length);
                            if (frame.Fin)
                            {
                                if (fragOpcode == 0x2)
                                    EnqueueIncoming(frag.ToArray());
                                frag.SetLength(0);
                                fragOpcode = 0;
                            }
                            break;

                        case 0x2: // binary
                            if (frame.Fin)
                            {
                                EnqueueIncoming(payload);
                            }
                            else
                            {
                                fragOpcode = 0x2;
                                frag.SetLength(0);
                                frag.Write(payload, 0, payload.Length);
                            }
                            break;

                        case 0x8: // close
                            _closing = true;
                            break;

                        case 0x9: // ping -> pong with same payload
                            lock (_sendLock) WriteFrame(0xA, payload);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                if (!_closing) LastError = e.Message;
            }
            finally
            {
                _closing = true;
                _connected = false;
                _sendSignal.Set();
            }
        }

        void EnqueueIncoming(byte[] text)
        {
            switch (Incoming.Enqueue(text))
            {
                case IncomingEnqueueResult.Accepted:
                    return;
                case IncomingEnqueueResult.Closed:
                    throw new IOException("websocket incoming queue closed");
                case IncomingEnqueueResult.Oversized:
                    throw new IOException($"websocket message exceeds {IncomingMessageQueue.MaxMessageBytes} bytes");
            }
        }

        public void Dispose()
        {
            _closing = true;
            _connected = false;
            Incoming.Close();
            _sendSignal.Set();
            Cleanup();
        }

        void Cleanup()
        {
            try { _net?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            Incoming.Close();
            _net = null;
            _tcp = null;
        }
    }
}
