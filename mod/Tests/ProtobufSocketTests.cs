using System;
using System.IO;
using System.Net.Sockets;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Google.Protobuf;
namespace SlopWorld.Tests
{
    static class ProtobufSocketTests
    {
        static string Header(NetworkStream stream)
        {
            var text = new StringBuilder();
            while (!text.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                int b = stream.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                text.Append((char)b);
            }
            return text.ToString();
        }
        static byte[] Read(NetworkStream stream, int count)
        {
            var bytes = new byte[count];
            int at = 0;
            while (at < count)
            {
                int n = stream.Read(bytes, at, count - at);
                if (n == 0) throw new EndOfStreamException();
                at += n;
            }
            return bytes;
        }
        static void Frame(NetworkStream stream, byte first, byte[] body, int offset, int count)
        {
            if (count < 0 || count > 125) throw new ArgumentOutOfRangeException(nameof(count), "Fixture supports short frames only");
            stream.WriteByte(first);
            stream.WriteByte((byte)count);
            stream.Write(body, offset, count);
        }
        static void Upgrade(NetworkStream stream)
        {
            string request = Header(stream);
            AssertEx.True(request.Contains("Sec-WebSocket-Protocol: slopworld.protobuf.v2"), "version negotiation requested");
            string key = null;
            foreach (string line in request.Split('\n')) if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.Ordinal)) key = line.Substring(18).Trim();
            string accept;
            // Match the RFC 6455 section 4.2.2 handshake, including its required SHA-1.
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
            using (var sha = SHA1.Create()) accept = Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
#pragma warning restore CA5350
            byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Protocol: slopworld.protobuf.v2\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
            stream.Write(header, 0, header.Length);
        }

        static (int Opcode, byte[] Payload) ReadClientFrame(NetworkStream stream)
        {
            int first = stream.ReadByte();
            int size = stream.ReadByte();
            if (first < 0 || size < 0) throw new EndOfStreamException();
            AssertEx.True((size & 128) != 0, "client frames masked");
            int length = size & 127;
            AssertEx.True(length <= 125, "fixture expects short client frames");
            var mask = Read(stream, 4);
            var payload = Read(stream, length);
            for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
            return (first & 15, payload);
        }

        public static void BinaryFramesFragmentationPingAndMaskedCommands()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var server = Task.Run(() => {
                using (var client = listener.AcceptTcpClient())
                {
                    client.ReceiveTimeout = 3000; client.SendTimeout = 3000;
                    using (var stream = client.GetStream())
                    {
                        Upgrade(stream);
                        var payload = new Wire.Event { Screen = new Wire.ScreenView { Name = "agent", Lines = { "hello 🦀" } } }.ToByteArray();
                        int split = payload.Length / 2;
                        Frame(stream, 0x02, payload, 0, split);
                        Frame(stream, 0x89, new byte[] { 42 }, 0, 1);
                        Frame(stream, 0x80, payload, split, payload.Length - split);
                        bool pong = false, command = false;
                        while (!pong || !command)
                        {
                            var received = ReadClientFrame(stream);
                            if (received.Opcode == 10)
                            {
                                AssertEx.Equal(1, received.Payload.Length, "pong payload length");
                                AssertEx.Equal((byte)42, received.Payload[0], "ping payload echoed");
                                pong = true;
                            }
                            else
                            {
                                AssertEx.Equal(2, received.Opcode, "binary command opcode");
                                AssertEx.Equal("agent", Wire.ClientMessage.Parser.ParseFrom(received.Payload).Sub.Name, "generated command decoded");
                                command = true;
                            }
                        }
                    }
                }
            });
            try
            {
                using (var socket = new MiniWebSocket())
                {
                    AssertEx.True(socket.Connect("127.0.0.1", port, "/ws", ""), "binary socket connected");
                    AssertEx.True(SpinWait.SpinUntil(() => socket.Incoming.Count > 0, 3000), "fragmented event arrived");
                    AssertEx.True(socket.Incoming.TryDequeue(out var received), "dequeue binary event");
                    AssertEx.Equal("hello 🦀", received.Value.Screen.Lines[0], "fragmented UTF-8 preserved");
                    socket.SendBinary(new Wire.ClientMessage { Sub = new Wire.NameReq { Name = "agent" } }.ToByteArray());
                    AssertEx.True(server.Wait(4000), "server received command and pong");
                }
            }
            finally { listener.Stop(); }
        }
    }
}
