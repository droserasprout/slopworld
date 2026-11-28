// The bank mounts outline bytes just as it does in WASM. Fixtures supply the rasterizer.
using System.Buffers.Binary;
using System.Reflection;
using BrowserAdapters;

static class TestFont
{
    internal static readonly string Ascii = new(
        Enumerable.Range(' ', 95).Select(c => (char)c).ToArray()
    );

    internal static void Run(Action<byte[]> check)
    {
        var rootField = typeof(BrowserFontBank).GetField(
            "mountRoot",
            BindingFlags.Static | BindingFlags.NonPublic
        )!;
        var previous = rootField.GetValue(null);
        string root = Path.Combine(Path.GetTempPath(), "local-font-check-" + Guid.NewGuid());
        rootField.SetValue(null, root);
        // Only head/hhea are needed by the managed metrics reader. Actual font
        // parsing/rasterization is covered separately by the WASM fidelity test.
        byte[] font = new byte[80];
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(12), 0x68656164);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(20), 44);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(28), 0x68686561);
        BinaryPrimitives.WriteUInt32BigEndian(font.AsSpan(36), 64);
        BinaryPrimitives.WriteUInt16BigEndian(font.AsSpan(62), 1000);
        BinaryPrimitives.WriteInt16BigEndian(font.AsSpan(68), 900);
        BinaryPrimitives.WriteInt16BigEndian(font.AsSpan(70), -300);
        try
        {
            check(font);
        }
        finally
        {
            BrowserFontBank.DisposeFonts();
            BrowserFontBank.Rasterize = null;
            BrowserFontBank.ConfigureDensity(1);
            rootField.SetValue(null, previous);
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
