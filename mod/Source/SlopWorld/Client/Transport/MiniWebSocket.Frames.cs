using System.IO;
using System.Text;

namespace SlopWorld
{
    public partial class MiniWebSocket
    {
        sealed class WebSocketFrame
        {
            public readonly bool Fin;
            public readonly int Opcode;
            public readonly byte[] Payload;

            public WebSocketFrame(bool fin, int opcode, byte[] payload)
            {
                Fin = fin;
                Opcode = opcode;
                Payload = payload;
            }
        }

        WebSocketFrame ReadFrame(int fragOpcode, long fragLength)
        {
            int b0 = _net.ReadByte();
            if (b0 < 0) return null;
            int b1 = _net.ReadByte();
            if (b1 < 0) return null;

            bool fin = (b0 & 0x80) != 0;
            int opcode = b0 & 0x0f;
            if ((b0 & 0x70) != 0)
                throw new IOException("websocket extensions are not supported");
            // Server frames are never masked, so bit 7 of b1 is always 0 here.
            if ((b1 & 0x80) != 0)
                throw new IOException("masked server frame");
            int lengthMarker = b1 & 0x7f;
            long len = lengthMarker;

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

            // Validate the 64-bit length before using it for an allocation.
            // An invalid header could request excessive memory or become negative during conversion.
            if (len < 0 || len > MaxFrame)
                throw new IOException($"frame length out of range: {len}");

            if ((lengthMarker == 126 && len < 126) ||
                (lengthMarker == 127 && len < 65536))
                throw new IOException("nonminimal websocket length");

            if ((opcode & 0x8) != 0 && (!fin || len > 125))
                throw new IOException("malformed control frame");
            if (!SupportedOpcode(opcode))
                throw new IOException($"unsupported websocket opcode: {opcode}");
            if (opcode == 0x0 && fragOpcode == 0)
                throw new IOException("unexpected continuation frame");
            if (opcode == 0x2 && fragOpcode != 0)
                throw new IOException("new binary frame while fragmented message is pending");
            if (opcode == 0x0 && fragLength + len > MaxFragmentedMessage)
                throw new IOException("fragmented message is too large");
            if (opcode == 0x2 && !fin && len > MaxFragmentedMessage)
                throw new IOException("fragmented message is too large");

            return new WebSocketFrame(fin, opcode, ReadExactly((int)len));
        }

        static bool SupportedOpcode(int opcode)
        {
            switch (opcode)
            {
                case 0x0: // continuation
                case 0x2: // binary
                case 0x8: // close
                case 0x9: // ping
                case 0xA: // pong
                    return true;
                default: return false;
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

        static void ValidateClosePayload(byte[] payload)
        {
            if (payload.Length == 0) return;
            if (payload.Length == 1) throw new IOException("invalid websocket close payload");
            int status = (payload[0] << 8) | payload[1];
            bool standard = status >= 1000 && status <= 1014 &&
                status != 1004 && status != 1005 && status != 1006;
            if (!standard && (status < 3000 || status >= 5000))
                throw new IOException("invalid websocket close status");
            try { new UTF8Encoding(false, true).GetString(payload, 2, payload.Length - 2); }
            catch (DecoderFallbackException) { throw new IOException("invalid websocket close reason"); }
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
    }
}
