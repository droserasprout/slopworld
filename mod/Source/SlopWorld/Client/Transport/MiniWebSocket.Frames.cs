using System.IO;

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

            // Validate the 64-bit length before using it for an allocation.
            // An invalid header could request excessive memory or become negative during conversion.
            if (len < 0 || len > MaxFrame)
                throw new IOException($"frame length out of range: {len}");

            if ((opcode & 0x8) != 0 && (!fin || len > 125))
                throw new IOException("malformed control frame");
            if ((opcode == 0x1 || (opcode >= 0x3 && opcode <= 0x7)) ||
                ((opcode & 0x8) != 0 && opcode != 0x8 && opcode != 0x9 && opcode != 0xA))
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
