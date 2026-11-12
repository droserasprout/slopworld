// Mount authored mod UI content after Core bootstrap. Verse retains texture lookup
// and ModEntry content ownership; host-side PNG decoding avoids native image APIs.
using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;

namespace BrowserAdapters;

public static class BrowserModContent
{
    static ModContentPack? content;

    public static ModContentPack? Current() => content;

    public static int Install(byte[] archive)
    {
        WorkspaceDrawing.RequireEntry();
        if (!PlayDataLoader.Loaded || content != null)
            throw new InvalidOperationException("Install mod content once after Core loading");
        const string root = "/BrowserMod/SlopWorld/";
        using var zip = new ZipArchive(new MemoryStream(archive));
        foreach (
            var entry in zip.Entries.Where(e =>
                !e.FullName.EndsWith(".rgba", StringComparison.Ordinal)
            )
        )
        {
            var path = Path.GetFullPath(root + entry.FullName);
            if (
                !path.StartsWith(root, StringComparison.Ordinal)
                || !(
                    path.EndsWith(".xml", StringComparison.Ordinal)
                    || path.EndsWith(".toml", StringComparison.Ordinal)
                )
            )
                throw new InvalidOperationException("Unexpected mod content path");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = File.Create(path);
            input.CopyTo(output);
        }
        var pack = new ModContentPack(
            new DirectoryInfo(root),
            "slopworld.browser",
            "slopworld.browser",
            1,
            "SlopWorld",
            false
        );
        var holder = pack.GetContentHolder<Texture2D>();
        var trie = holder
            .GetType()
            .GetField("contentListTrie", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(holder)!;
        foreach (
            var entry in zip.Entries.Where(e =>
                e.FullName.StartsWith("Textures/", StringComparison.Ordinal)
                && e.FullName.EndsWith(".rgba", StringComparison.Ordinal)
            )
        )
        {
            using var stream = entry.Open();
            var header = new byte[8];
            stream.ReadExactly(header);
            int width = BinaryPrimitives.ReadInt32LittleEndian(header),
                height = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
            if (
                width <= 0
                || height <= 0
                || width > 8192
                || height > 8192
                || entry.Length != 8L + width * height * 4L
            )
                throw new InvalidOperationException("Invalid mod texture dimensions");
            var bytes = new byte[checked(width * height * 4)];
            stream.ReadExactly(bytes);
            var pixels = MemoryMarshal.Cast<byte, Color32>(bytes).ToArray();
            string key = entry.FullName["Textures/".Length..^".rgba".Length];
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = key };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            holder.contentList.Add(key, texture);
            trie.GetType()
                .GetMethod("Add", new[] { typeof(string) })!
                .Invoke(trie, new object[] { key });
        }
        LoadedModManager.RunningModsListForReading.Add(pack);
        content = pack;
        return holder.contentList.Count;
    }
}
