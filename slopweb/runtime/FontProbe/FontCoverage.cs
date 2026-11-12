// Read Unicode cmap coverage without creating a rasterizer or baking glyphs.
using System.Buffers.Binary;

internal static class FontCoverage
{
    internal static bool Contains(byte[] bytes, int scalar)
    {
        int U16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
        uint U32(int offset) => BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        try
        {
            int cmap = -1;
            for (int i = 0; i < U16(4); i++)
                if (U32(12 + i * 16) == 0x636d6170)
                    cmap = checked((int)U32(20 + i * 16));
            if (cmap < 0)
                return false;
            for (int i = 0; i < U16(cmap + 2); i++)
            {
                int record = cmap + 4 + i * 8;
                int platform = U16(record),
                    encoding = U16(record + 2);
                if (platform != 0 && !(platform == 3 && encoding is 1 or 10))
                    continue;
                int table = checked(cmap + (int)U32(record + 4));
                if (U16(table) == 12)
                {
                    int count = checked((int)U32(table + 12));
                    for (int group = 0; group < count; group++)
                    {
                        int at = checked(table + 16 + group * 12);
                        uint start = U32(at),
                            end = U32(at + 4);
                        if (scalar >= start && scalar <= end)
                            return U32(at + 8) + scalar - start != 0;
                    }
                }
                else if (U16(table) == 4 && scalar <= 0xffff)
                {
                    int count = U16(table + 6) / 2;
                    int ends = table + 14,
                        starts = ends + count * 2 + 2;
                    int deltas = starts + count * 2,
                        ranges = deltas + count * 2;
                    for (int segment = 0; segment < count; segment++)
                    {
                        if (scalar < U16(starts + segment * 2) || scalar > U16(ends + segment * 2))
                            continue;
                        int delta = U16(deltas + segment * 2),
                            offset = U16(ranges + segment * 2);
                        int glyph =
                            offset == 0
                                ? scalar
                                : U16(
                                    ranges
                                        + segment * 2
                                        + offset
                                        + (scalar - U16(starts + segment * 2)) * 2
                                );
                        return (offset == 0 || glyph != 0) && ((glyph + delta) & 0xffff) != 0;
                    }
                }
            }
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or OverflowException) { }
        return false;
    }
}
