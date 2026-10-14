using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopWorld
{
    // Reads family names from installed OpenType files without a platform font service.
    static class InstalledFontNames
    {
        public static string Read(byte[] bytes)
        {
            try
            {
                var data = bytes.AsSpan();
                int header = U32(data, 0) == 0x74746366 ? checked((int)U32(data, 12)) : 0;
                int tables = U16(data, header + 4);
                for (int i = 0; i < tables; i++)
                {
                    int record = header + 12 + i * 16;
                    if (U32(data, record) != 0x6e616d65) // name
                        continue;
                    int start = checked((int)U32(data, record + 8));
                    var names = data.Slice(start, checked((int)U32(data, record + 12)));
                    int count = U16(names, 2),
                        strings = U16(names, 4);
                    string best = null;
                    int bestRank = -1;
                    for (int n = 0; n < count; n++)
                    {
                        int at = 6 + n * 12;
                        int platform = U16(names, at),
                            language = U16(names, at + 4);
                        int id = U16(names, at + 6);
                        if ((id != 1 && id != 16) || (platform != 0 && platform != 3))
                            continue;
                        int rank = (id == 16 ? 2 : 0) + (language == 0x409 ? 1 : 0);
                        if (rank <= bestRank)
                            continue;
                        var text = names.Slice(strings + U16(names, at + 10), U16(names, at + 8));
                        string family = Encoding.BigEndianUnicode.GetString(text).Trim('\0', ' ');
                        if (family.Length == 0)
                            continue;
                        best = family;
                        bestRank = rank;
                    }
                    return best;
                }
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return null;
        }

        public static bool Monospaced(byte[] bytes)
        {
            try
            {
                var data = bytes.AsSpan();
                int header = U32(data, 0) == 0x74746366 ? checked((int)U32(data, 12)) : 0;
                int tables = U16(data, header + 4);
                for (int i = 0; i < tables; i++)
                {
                    int record = header + 12 + i * 16;
                    if (U32(data, record) == 0x706f7374) // post.isFixedPitch
                        return U32(data, checked((int)U32(data, record + 8)) + 12) != 0;
                }
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return false;
        }

        static ushort U16(ReadOnlySpan<byte> bytes, int at) =>
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));

        static uint U32(ReadOnlySpan<byte> bytes, int at) =>
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
    }
}
