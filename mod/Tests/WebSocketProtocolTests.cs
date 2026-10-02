using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using NUnit.Framework;

namespace SlopWorld.Tests
{
    static class WebSocketProtocolTests
    {
        // The peer stays open until assertions finish. Every socket operation and task wait is
        // bounded, and disposal releases the peer even when a client assertion fails.
        static void WithPeer(Func<string, byte[]> response, Action<NetworkStream> serve,
                             Action<MiniWebSocket, int> check)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var finished = new ManualResetEventSlim();
            using var served = new ManualResetEventSlim();
            using var cancellation = new CancellationTokenSource(10000);
            var server = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                client.ReceiveTimeout = client.SendTimeout = 5000;
                using var stream = client.GetStream();
                string request = ReadHeader(stream);
                var bytes = response(request);
                if (bytes != null) stream.Write(bytes, 0, bytes.Length);
                serve?.Invoke(stream);
                served.Set();
                Assert.That(finished.Wait(8000), Is.True, "client assertions finish before peer deadline");
            });
            using var socket = new MiniWebSocket();
            try
            {
                check(socket, ((IPEndPoint)listener.LocalEndpoint).Port);
                Assert.That(served.Wait(6000), Is.True, "peer finishes protocol assertions");
            }
            finally
            {
                finished.Set();
                socket.Dispose();
                Assert.That(server.Wait(10000), Is.True, "peer task terminates");
            }
        }

        static string ReadHeader(NetworkStream stream)
        {
            var text = new StringBuilder();
            while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                text.Append((char)b);
                if (text.Length > 8192) throw new IOException("request header too long");
            }
            return text.ToString();
        }

        static string Upgrade(string request)
        {
            string key = request.Split('\n').Single(line => line.StartsWith("Sec-WebSocket-Key:")).Substring(18).Trim();
            // Match the RFC 6455 section 4.2.2 handshake, including its required SHA-1.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            using var sha = SHA1.Create();
#pragma warning restore CA5350
            string accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            return "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Protocol: slopworld.protobuf.v2\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n";
        }

        static byte[] ValidResponse(string request) => Encoding.ASCII.GetBytes(Upgrade(request));

        static byte[] Header(byte opcode, ulong length)
        {
            using var bytes = new MemoryStream();
            bytes.WriteByte(opcode);
            if (length < 126) bytes.WriteByte((byte)length);
            else if (length <= ushort.MaxValue)
            {
                bytes.WriteByte(126);
                bytes.WriteByte((byte)(length >> 8));
                bytes.WriteByte((byte)length);
            }
            else
            {
                bytes.WriteByte(127);
                for (int i = 7; i >= 0; i--) bytes.WriteByte((byte)(length >> (8 * i)));
            }
            return bytes.ToArray();
        }

        static byte[] Read(NetworkStream stream, int length)
        {
            var data = new byte[length];
            stream.ReadExactly(data);
            return data;
        }

        public static IEnumerable<(string Name, Action Body)> Cases()
        {
            foreach (int size in new[] { 125, 126, 65535, 65536 })
                yield return ($"websocket roundtrip handles payload length {size}", () => LengthBoundary(size));
            var invalid = new (string Name, byte[] Bytes, string Error)[]
            {
                ("reserved extension", new byte[] { 0xC2, 0 }, "websocket extensions are not supported"),
                ("masked server frame", new byte[] { 0x82, 0x80 }, "masked server frame"),
                ("oversized frame", Header(0x82, 32UL * 1024 * 1024 + 1), "frame length out of range:"),
                ("negative length", Header(0x82, 1UL << 63), "frame length out of range:"),
                ("fragmented ping", new byte[] { 0x09, 0 }, "malformed control frame"),
                ("oversized ping", Header(0x89, 126), "malformed control frame"),
                ("text frame", new byte[] { 0x81, 0 }, "unsupported websocket opcode: 1"),
                ("reserved data opcode", new byte[] { 0x83, 0 }, "unsupported websocket opcode: 3"),
                ("reserved control opcode", new byte[] { 0x8B, 0 }, "unsupported websocket opcode: 11"),
                ("unexpected continuation", new byte[] { 0x80, 0 }, "unexpected continuation frame"),
                ("fragmented message budget", new byte[] { 0x02, 1, 0 }.Concat(Header(0x80, 32UL * 1024 * 1024)).ToArray(), "fragmented message is too large"),
                ("nested binary message", new byte[] { 0x02, 0, 0x82, 0 }, "new binary frame while fragmented message is pending"),
                ("nonminimal short length", new byte[] { 0x82, 126, 0, 125 }, "nonminimal websocket length"),
                ("nonminimal long length", new byte[] { 0x82, 127, 0, 0, 0, 0, 0, 0, 255, 255 }, "nonminimal websocket length"),
                ("one-byte close", new byte[] { 0x88, 1, 0 }, "invalid websocket close payload"),
                ("reserved close status", new byte[] { 0x88, 2, 3, 237 }, "invalid websocket close status"),
                ("invalid close UTF8", new byte[] { 0x88, 3, 3, 232, 255 }, "invalid websocket close reason"),
                ("truncated short length", new byte[] { 0x82, 126, 0 }, "socket closed mid-frame"),
                ("truncated long length", new byte[] { 0x82, 127, 0, 0 }, "socket closed mid-frame"),
                ("truncated payload", new byte[] { 0x82, 3, 1 }, "socket closed mid-payload"),
            };
            foreach (var item in invalid)
                yield return ($"websocket rejects {item.Name}", () => RejectFrame(item.Bytes, item.Error));
            foreach (string kind in new[] { "status", "upgrade", "connection", "missing-connection", "accept", "protocol", "oversized-header", "eof" })
                yield return ($"websocket rejects handshake {kind}", () => RejectHandshake(kind));
        }

        static void LengthBoundary(int size)
        {
            byte[] sent = Enumerable.Range(0, size).Select(i => (byte)i).ToArray();
            // A valid protobuf event exercises decoding after the extended frame-length read.
            var incoming = new Wire.Event { Screen = new Wire.ScreenView { Name = "agent", Lines = { new string('x', size) } } }.ToByteArray();
            WithPeer(ValidResponse, stream =>
            {
                byte[] header = Header(0x82, (ulong)incoming.Length);
                stream.Write(header);
                stream.Write(incoming);
                Assert.That(stream.ReadByte(), Is.EqualTo(0x82), "binary FIN opcode");
                int prefix = stream.ReadByte();
                Assert.That(prefix & 128, Is.EqualTo(128), "client masks all payload sizes");
                Assert.That(prefix & 127, Is.EqualTo(size < 126 ? size : size <= 65535 ? 126 : 127));
                ulong length = (ulong)(prefix & 127);
                if (length == 126) { var b = Read(stream, 2); length = (ulong)(b[0] << 8 | b[1]); }
                else if (length == 127) { length = 0; foreach (byte b in Read(stream, 8)) length = (length << 8) | b; }
                Assert.That(length, Is.EqualTo((ulong)size));
                var mask = Read(stream, 4);
                var body = Read(stream, (int)length);
                for (int i = 0; i < body.Length; i++) body[i] ^= mask[i & 3];
                Assert.That(body, Is.EqualTo(sent), "masking preserves every byte");
            }, (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.True);
                socket.SendBinary(null);
                socket.SendBinary(Array.Empty<byte>());
                socket.SendBinary(sent);
                Assert.That(SpinWait.SpinUntil(() => socket.Incoming.Count > 0, 5000), Is.True);
                Assert.That(socket.Incoming.TryDequeue(out var received), Is.True);
                Assert.That(received.Value.Screen.Lines.Single(), Is.EqualTo(new string('x', size)));
                Assert.That(socket.LastError, Is.Null);
            });
        }

        static void RejectFrame(byte[] frame, string error)
        {
            WithPeer(ValidResponse, stream =>
            {
                stream.Write(frame);
                stream.Socket.Shutdown(SocketShutdown.Send);
            }, (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.True);
                Assert.That(SpinWait.SpinUntil(() => !socket.Connected, 5000), Is.True, "invalid frame disconnects");
                Assert.That(socket.LastError, Does.StartWith(error));
                Assert.That(socket.Incoming.Count, Is.Zero, "invalid frame is never delivered");
            });
        }

        static void RejectHandshake(string kind)
        {
            WithPeer(request =>
            {
                string response = Upgrade(request);
                switch (kind)
                {
                    case "status": response = response.Replace("101 Switching Protocols", "403 Forbidden"); break;
                    case "upgrade": response = response.Replace("Upgrade: websocket", "Upgrade: other"); break;
                    case "connection": response = response.Replace("Connection: Upgrade", "Connection: keep-alive"); break;
                    case "missing-connection": response = response.Replace("Connection: Upgrade\r\n", ""); break;
                    case "accept": response = response.Replace("Sec-WebSocket-Accept: ", "Sec-WebSocket-Accept: invalid"); break;
                    case "protocol": response = response.Replace("slopworld.protobuf.v2", "slopworld.protobuf.v1"); break;
                    case "oversized-header": response = new string('x', 8192); break;
                    case "eof": response = ""; break;
                }
                return Encoding.ASCII.GetBytes(response);
            }, stream => stream.Socket.Shutdown(SocketShutdown.Send), (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.False);
                Assert.That(socket.Connected, Is.False);
                Assert.That(socket.LastError, Does.StartWith(kind == "eof" || kind == "oversized-header" ? "no handshake response" : "handshake refused:"));
                Assert.That(socket.Incoming.Enqueue(Array.Empty<byte>()), Is.EqualTo(IncomingEnqueueResult.Closed));
            });
        }

        public static void PeerCloseIsEchoedMaskedBeforeTransportCleanup()
        {
            foreach (byte[] payload in new[] { Array.Empty<byte>(), new byte[] { 3, 232, (byte)'b', (byte)'y', (byte)'e' } })
            {
                WithPeer(ValidResponse, stream =>
                {
                    stream.Write(Header(0x88, (ulong)payload.Length));
                    stream.Write(payload);
                    Assert.That(stream.ReadByte(), Is.EqualTo(0x88));
                    Assert.That(stream.ReadByte(), Is.EqualTo(0x80 | payload.Length), "close response is masked");
                    var mask = Read(stream, 4);
                    var echoed = Read(stream, payload.Length);
                    for (int i = 0; i < echoed.Length; i++) echoed[i] ^= mask[i & 3];
                    Assert.That(echoed, Is.EqualTo(payload));
                    Assert.That(stream.ReadByte(), Is.EqualTo(-1), "transport closes after response");
                }, (socket, port) =>
                {
                    Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.True);
                    Assert.That(SpinWait.SpinUntil(() => !socket.Connected, 5000), Is.True);
                    Assert.That(socket.LastError, Is.Null);
                    Assert.That(socket.Incoming.Enqueue(Array.Empty<byte>()), Is.EqualTo(IncomingEnqueueResult.Closed));
                });
            }
        }

        public static void UpgradeAcceptsHeaderCaseAndConnectionTokensAndSendsAuthentication()
        {
            WithPeer(request =>
            {
                Assert.That(request, Does.StartWith("GET /custom/ws HTTP/1.1\r\n"));
                Assert.That(request, Does.Contain(WireProtocol.TokenHeader + ": test-token\r\n"));
                return Encoding.ASCII.GetBytes(Upgrade(request)
                    .Replace("Upgrade: websocket", "uPgRaDe: WebSocket")
                    .Replace("Connection: Upgrade", "cOnNeCtIoN: keep-alive, upgrade")
                    .Replace("Sec-WebSocket-Accept:", "sec-websocket-accept:"));
            }, stream => stream.Write(new byte[] { 0x8A, 0, 0x88, 0 }), (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/custom/ws", "test-token"), Is.True);
                Assert.That(SpinWait.SpinUntil(() => !socket.Connected, 5000), Is.True, "pong is ignored and close ends reader");
                Assert.That(socket.LastError, Is.Null, "normal close is not an error");
                socket.SendBinary(new byte[] { 1 });
                Assert.That(socket.OutgoingCount, Is.Zero, "disconnected sends are ignored");
            });
        }

        public static void IncomingMessageBudgetRejectsCompleteOversizedFrame()
        {
            WithPeer(ValidResponse, stream =>
            {
                var payload = new byte[IncomingMessageQueue.MaxMessageBytes + 1];
                stream.Write(Header(0x82, (ulong)payload.Length));
                stream.Write(payload);
            }, (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.True);
                Assert.That(SpinWait.SpinUntil(() => !socket.Connected, 5000), Is.True);
                Assert.That(socket.LastError, Is.EqualTo($"websocket message exceeds {IncomingMessageQueue.MaxMessageBytes} bytes"));
                Assert.That(socket.Incoming.Count, Is.Zero);
                Assert.That(socket.Incoming.Bytes, Is.Zero);
            });
        }

        public static void ClosedIncomingQueueStopsReader()
        {
            using var ready = new ManualResetEventSlim();
            WithPeer(ValidResponse, stream =>
            {
                Assert.That(ready.Wait(5000), Is.True);
                stream.Write(new byte[] { 0x82, 0 });
            }, (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", ""), Is.True);
                socket.Incoming.Close();
                ready.Set();
                Assert.That(SpinWait.SpinUntil(() => !socket.Connected, 5000), Is.True);
                Assert.That(socket.LastError, Is.EqualTo("websocket incoming queue closed"));
            });
        }

        public static void IncompleteUpgradeTimesOut()
        {
            WithPeer(_ => Encoding.ASCII.GetBytes("HTTP/1.1 101"), null, (socket, port) =>
            {
                Assert.That(socket.Connect("127.0.0.1", port, "/ws", "", timeoutMs: 250), Is.False);
                Assert.That(socket.LastError, Is.Not.Null.And.Not.Empty);
                Assert.That(socket.Connected, Is.False);
                Assert.That(socket.Incoming.Enqueue(Array.Empty<byte>()), Is.EqualTo(IncomingEnqueueResult.Closed));
            });
        }
    }
}
