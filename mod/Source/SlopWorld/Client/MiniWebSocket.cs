using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace SlopWorld
{
    // Text frames, ping/pong, close. ClientWebSocket is not dependable on Unity's
    // mono and less so under Wine, so we speak the protocol over a plain TcpClient.
    // Reads happen on a background thread; callers drain Incoming.
    public class MiniWebSocket : IDisposable
    {
        public readonly ConcurrentQueue<string> Incoming = new ConcurrentQueue<string>();

        // A frame is one screen's worth of SGR text, and the daemon clamps a pane to 500x200,
        // so the widest thing it can send is orders of magnitude under this.
        const long MaxFrame = 32L * 1024 * 1024;

        public bool Connected { get; private set; }
        public string LastError { get; private set; }

        TcpClient _tcp;
        NetworkStream _net;
        Thread _reader;
        volatile bool _closing;
        readonly object _sendLock = new object();
        readonly RNGCryptoServiceProvider _rng = new RNGCryptoServiceProvider();

        public bool Connect(string host, int port, string path, string token, int timeoutMs = 3000)
        {
            try
            {
                _tcp = new TcpClient { NoDelay = true };
                var ar = _tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs) || !_tcp.Connected)
                {
                    LastError = "connection timed out";
                    Cleanup();
                    return false;
                }
                _tcp.EndConnect(ar);
                // ConnectTimeout covers only the TCP handshake. The HTTP upgrade below is
                // synchronous too, and runs on Unity's main thread, so a listener that accepts
                // and then says nothing must not freeze the game forever.
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
                if (!string.IsNullOrEmpty(token))
                    req.Append($"X-Slop-Token: {token}\r\n");
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

                // An established websocket is expected to sit quiet indefinitely. Keep the
                // write timeout, since game-thread sends must still be bounded, but let the
                // background reader wait for the next event without reconnecting every few
                // seconds.
                _tcp.ReceiveTimeout = 0;
                Connected = true;
                _closing = false;
                _reader = new Thread(ReadLoop) { IsBackground = true, Name = "SlopWorld WS" };
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

        // Byte by byte: we must not over-read into frame data.
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
            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon <= 0) continue;
                string name = lines[i].Substring(0, colon).Trim();
                string value = lines[i].Substring(colon + 1).Trim();
                if (name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)) upgrade = value;
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
            return string.Equals(accept, expected, StringComparison.Ordinal);
        }

        public void SendText(string text)
        {
            if (!Connected) return;
            try
            {
                var payload = Encoding.UTF8.GetBytes(text);
                lock (_sendLock)
                {
                    WriteFrame(0x1, payload);
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Connected = false;
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
                    int b0 = _net.ReadByte();
                    if (b0 < 0) break;
                    int b1 = _net.ReadByte();
                    if (b1 < 0) break;

                    bool fin = (b0 & 0x80) != 0;
                    int opcode = b0 & 0x0f;
                    // Server frames are never masked, so bit 7 of b1 is always 0 here.
                    long len = b1 & 0x7f;

                    if (len == 126)
                    {
                        len = (ReadByteOrThrow() << 8) | ReadByteOrThrow();
                    }
                    else if (len == 127)
                    {
                        len = 0;
                        for (int i = 0; i < 8; i++)
                            len = (len << 8) | (uint)ReadByteOrThrow();
                    }

                    // The length arrives as 64 bits and is about to become an allocation
                    // size. Unchecked, a desynced or corrupt header asks for gigabytes, or
                    // casts negative and throws somewhere further from the cause than here.
                    if (len < 0 || len > MaxFrame)
                        throw new IOException($"frame length out of range: {len}");

                    var payload = ReadExactly((int)len);

                    switch (opcode)
                    {
                        case 0x0: // continuation
                            frag.Write(payload, 0, payload.Length);
                            if (fin)
                            {
                                if (fragOpcode == 0x1)
                                    Incoming.Enqueue(Encoding.UTF8.GetString(frag.ToArray()));
                                frag.SetLength(0);
                                fragOpcode = 0;
                            }
                            break;

                        case 0x1: // text
                            if (fin)
                            {
                                Incoming.Enqueue(Encoding.UTF8.GetString(payload));
                            }
                            else
                            {
                                fragOpcode = 0x1;
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
                Connected = false;
            }
        }

        int ReadByteOrThrow()
        {
            int b = _net.ReadByte();
            if (b < 0) throw new IOException("socket closed mid-frame");
            return b;
        }

        byte[] ReadExactly(int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = _net.Read(buf, got, n - got);
                if (r <= 0) throw new IOException("socket closed mid-payload");
                got += r;
            }
            return buf;
        }

        public void Dispose()
        {
            _closing = true;
            Connected = false;
            try
            {
                if (_net != null && _tcp != null && _tcp.Connected)
                    lock (_sendLock) WriteFrame(0x8, new byte[0]);
            }
            catch { /* the socket is going away regardless */ }
            Cleanup();
        }

        void Cleanup()
        {
            try { _net?.Close(); } catch { }
            try { _tcp?.Close(); } catch { }
            _net = null;
            _tcp = null;
        }
    }
}
