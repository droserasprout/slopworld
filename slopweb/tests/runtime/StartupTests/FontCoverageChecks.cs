// Synthetic cmap fixtures cover host selection without installed fonts or rendering.
using System.Buffers.Binary;

static class FontCoverageChecks
{
    internal static void Run()
    {
        var bytes = new byte[100];
        void U16(int at, ushort value) =>
            BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(at), value);
        void U32(int at, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at), value);
        U16(4, 1);
        U32(12, 0x636d6170);
        U32(20, 28);
        U16(30, 1);
        U16(32, 3);
        U16(34, 10);
        U32(36, 12);
        U16(40, 12);
        U32(52, 1);
        U32(56, 0x1f600);
        U32(60, 0x1f601);
        U32(64, 20);
        if (!FontCoverage.Contains(bytes, 0x1f600) || FontCoverage.Contains(bytes, 65))
            throw new Exception("Format 12 coverage");
        U16(34, 1);
        U16(40, 4);
        U16(46, 2);
        U16(54, 65);
        U16(58, 65);
        U16(60, 1);
        U16(62, 0);
        if (!FontCoverage.Contains(bytes, 65) || FontCoverage.Contains(bytes, 66))
            throw new Exception("Format 4 delta coverage");
        U16(62, 2);
        U16(64, 0);
        if (FontCoverage.Contains(bytes, 65))
            throw new Exception("Zero glyph is absent");
        U16(64, 2);
        if (!FontCoverage.Contains(bytes, 65))
            throw new Exception("Format 4 indexed coverage");
        if (FontCoverage.Contains(bytes[..20], 65))
            throw new Exception("Truncated fonts must not provide coverage");
        Console.WriteLine(
            "Font coverage: Unicode cmap 4/12, zero glyphs and malformed files passed."
        );
    }
}
