using System;
using System.Buffers.Binary;

namespace SlopWorld
{
    // Converts a collection face to standalone SFNT bytes for native font metrics.
    // Copies shared tables before rebasing offsets; installed font bytes stay untouched.
    static class OpenTypeCollection
    {
        const uint StandaloneChecksum = 0xb1b0afba;

        internal static byte[] Extract(byte[] bytes, int face)
        {
            var font = new OpenTypeFace(bytes, face);
            if (!font.IsCollection)
                return bytes;
            int length = font.Directory.Length;
            for (int index = 0; index < font.TableCount; index++)
                length = checked(length + AlignedLength(font.Table(index).Data.Length));

            byte[] selected = new byte[length];
            font.Directory.CopyTo(selected);
            int offset = font.Directory.Length;
            int adjustment = 0;
            for (int index = 0; index < font.TableCount; index++)
            {
                var table = font.Table(index);
                table.Data.CopyTo(selected.AsSpan(offset));
                Write(
                    selected,
                    OpenTypeFace.RecordOffset(index) + OpenTypeTable.OffsetField,
                    (uint)offset
                );
                if (table.Tag == OpenTypeTags.Head && table.Length >= 12)
                {
                    adjustment = offset + OpenTypeTable.ChecksumAdjustmentField;
                    Write(selected, adjustment, 0);
                }
                offset += AlignedLength(table.Length);
            }
            if (adjustment != 0)
                AdjustChecksum(selected, adjustment);
            return selected;
        }

        static void AdjustChecksum(byte[] selected, int adjustment)
        {
            // SFNT sums big-endian uint32 words, including zero padding. The head
            // table's checksum uses a zero adjustment; the whole file then gets this fixup.
            uint sum = 0;
            for (int offset = 0; offset < selected.Length; offset += 4)
                sum = unchecked(sum + OpenTypeBinary.ReadUInt32(selected, offset));
            Write(selected, adjustment, unchecked(StandaloneChecksum - sum));
        }

        static int AlignedLength(int length) => checked(length + 3) & ~3;

        static void Write(byte[] data, int offset, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset, 4), value);
    }
}
