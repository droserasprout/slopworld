using System;
using System.Buffers.Binary;
using System.Text;

namespace SlopWorld
{
    // Reads family names from installed OpenType files without a platform font service.
    static class InstalledFontNames
    {
        public static string Read(byte[] bytes, int face = 0)
        {
            try
            {
                var data = bytes.AsSpan();
                int header = Header(data, face);
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

        public static bool Monospaced(byte[] bytes, int face = 0)
        {
            try
            {
                var data = bytes.AsSpan();
                int header = Header(data, face);
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

        public static int Count(byte[] bytes)
        {
            try
            {
                var data = bytes.AsSpan();
                if (U32(data, 0) != 0x74746366)
                    return 1;
                int count = checked((int)U32(data, 8));
                _ = data.Slice(12, checked(count * 4));
                return count;
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return 0;
        }

        // Prefer upright, normal-width, 400-weight faces within a family. File names
        // cannot distinguish collection entries and often omit a face's actual style.
        public static int StyleDistance(byte[] bytes, int face = 0)
        {
            try
            {
                var data = bytes.AsSpan();
                int header = Header(data, face);
                int tables = U16(data, header + 4);
                int fallback = int.MaxValue;
                for (int i = 0; i < tables; i++)
                {
                    int record = header + 12 + i * 16;
                    uint tag = U32(data, record);
                    if (tag != 0x4f532f32 && tag != 0x68656164) // OS/2 or head
                        continue;
                    var table = data.Slice(
                        checked((int)U32(data, record + 8)),
                        checked((int)U32(data, record + 12))
                    );
                    if (tag == 0x4f532f32 && table.Length >= 64)
                    {
                        int weight = U16(table, 4),
                            width = U16(table, 6);
                        if (weight >= 1 && weight <= 1000 && width >= 1 && width <= 9)
                            return Distance(weight, width, (U16(table, 62) & 0x201) != 0);
                    }
                    if (tag == 0x68656164 && table.Length >= 46)
                    {
                        int style = U16(table, 44);
                        fallback = Distance((style & 1) != 0 ? 700 : 400, 5, (style & 2) != 0);
                    }
                }
                return fallback;
            }
            catch (ArgumentOutOfRangeException) { }
            catch (OverflowException) { }
            return int.MaxValue;
        }

        static int Distance(int weight, int width, bool italic) =>
            (italic ? 100_000 : 0) + Math.Abs(width - 5) * 10_000 + Math.Abs(weight - 400);

        // Waylith's metrics reader accepts standalone SFNT data, not TTC headers.
        // Copy the chosen face's directory and tables, rebasing offsets and checksum.
        // The installed collection and its shared tables are never modified.
        public static byte[] Select(byte[] bytes, int face)
        {
            var data = bytes.AsSpan();
            int header = Header(data, face);
            if (U32(data, 0) != 0x74746366)
                return bytes;
            int tables = U16(data, header + 4);
            int directory = 12 + tables * 16;
            int length = directory;
            for (int i = 0; i < tables; i++)
            {
                int record = header + 12 + i * 16;
                int source = checked((int)U32(data, record + 8));
                uint size = U32(data, record + 12);
                _ = data.Slice(source, checked((int)size));
                length = checked(length + AlignedLength(size));
            }
            byte[] selected = new byte[length];
            data.Slice(header, directory).CopyTo(selected);
            int offset = directory,
                adjustment = 0;
            for (int i = 0; i < tables; i++)
            {
                int record = 12 + i * 16;
                int source = checked((int)U32(data, header + record + 8));
                int size = checked((int)U32(data, header + record + 12));
                data.Slice(source, size).CopyTo(selected.AsSpan(offset));
                BinaryPrimitives.WriteUInt32BigEndian(selected.AsSpan(record + 8), (uint)offset);
                if (U32(data, header + record) == 0x68656164 && size >= 12) // head.checkSumAdjustment
                {
                    adjustment = offset + 8;
                    BinaryPrimitives.WriteUInt32BigEndian(selected.AsSpan(adjustment), 0);
                }
                offset += AlignedLength((uint)size);
            }
            if (adjustment != 0)
            {
                uint sum = 0;
                for (int i = 0; i < selected.Length; i += 4)
                    sum = unchecked(sum + U32(selected, i));
                BinaryPrimitives.WriteUInt32BigEndian(
                    selected.AsSpan(adjustment),
                    unchecked(0xb1b0afba - sum)
                );
            }
            return selected;
        }

        static int AlignedLength(uint length) => checked((int)length + 3) & ~3;

        static int Header(ReadOnlySpan<byte> data, int face)
        {
            bool collection = U32(data, 0) == 0x74746366;
            if (face < 0 || face >= (collection ? U32(data, 8) : 1u))
                throw new ArgumentOutOfRangeException(nameof(face));
            return collection ? checked((int)U32(data, checked(12 + face * 4))) : 0;
        }

        static ushort U16(ReadOnlySpan<byte> bytes, int at) =>
            BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(at, 2));

        static uint U32(ReadOnlySpan<byte> bytes, int at) =>
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(at, 4));
    }
}
