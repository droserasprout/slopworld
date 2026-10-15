using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityStubs.Gui;

namespace SlopWorld.Tests
{
    [TestFixture]
    public class NativeFontsTests
    {
        static void U16(byte[] data, int at, ushort value) =>
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), value);

        static void U32(byte[] data, int at, uint value) =>
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(at), value);

        static byte[] Face(
            string family,
            bool mono = false,
            bool collection = false,
            ushort weight = 400,
            ushort width = 5,
            bool italic = false
        )
        {
            byte[] text = Encoding.BigEndianUnicode.GetBytes(family);
            int header = collection ? 16 : 0;
            int names = header + 92;
            int post = names + 18 + text.Length;
            int head = post + 16;
            int hhea = head + 54;
            int os2 = hhea + 36;
            var data = new byte[os2 + 64];
            if (collection)
            {
                U32(data, 0, 0x74746366);
                U32(data, 8, 1);
                U32(data, 12, (uint)header);
            }
            U32(data, header, 0x00010000);
            U16(data, header + 4, 5);
            U32(data, header + 12, 0x6e616d65);
            U32(data, header + 20, (uint)names);
            U32(data, header + 24, (uint)(18 + text.Length));
            U32(data, header + 28, 0x706f7374);
            U32(data, header + 36, (uint)post);
            U32(data, header + 40, 16);
            U32(data, header + 44, 0x68656164);
            U32(data, header + 52, (uint)head);
            U32(data, header + 56, 54);
            U32(data, header + 60, 0x68686561);
            U32(data, header + 68, (uint)hhea);
            U32(data, header + 72, 36);
            U32(data, header + 76, 0x4f532f32);
            U32(data, header + 84, (uint)os2);
            U32(data, header + 88, 64);
            U16(data, os2 + 4, weight);
            U16(data, os2 + 6, width);
            U16(data, os2 + 62, italic ? (ushort)1 : (ushort)0);
            U32(data, head + 8, 12345);
            U16(data, head + 18, 1000);
            U16(data, hhea + 4, 800);
            U16(data, hhea + 6, unchecked((ushort)-200));
            U16(data, hhea + 8, 100);
            U16(data, names + 2, 1);
            U16(data, names + 4, 18);
            U16(data, names + 6, 3);
            U16(data, names + 10, 0x409);
            U16(data, names + 12, 16);
            U16(data, names + 14, (ushort)text.Length);
            text.CopyTo(data, names + 18);
            U32(data, post + 12, mono ? 1u : 0u);
            return data;
        }

        static byte[] Collection(params byte[][] faces)
        {
            int offset = 12 + faces.Length * 4;
            var data = new byte[offset + System.Linq.Enumerable.Sum(faces, face => face.Length)];
            U32(data, 0, 0x74746366);
            U32(data, 4, 0x00010000);
            U32(data, 8, (uint)faces.Length);
            for (int i = 0; i < faces.Length; i++)
            {
                U32(data, 12 + i * 4, (uint)offset);
                faces[i].CopyTo(data, offset);
                // SFNT table offsets in a collection are absolute from the file start.
                int tables = BinaryPrimitives.ReadUInt16BigEndian(faces[i].AsSpan(4));
                for (int table = 0; table < tables; table++)
                {
                    int record = offset + 20 + table * 16;
                    U32(
                        data,
                        record,
                        BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record)) + (uint)offset
                    );
                }
                offset += faces[i].Length;
            }
            return data;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegularFaceWinsOverEarlierStyledFacesRegardlessOfFileName(bool collection)
        {
            var faces = new[]
            {
                Face("Example", weight: 300),
                Face("Example", width: 3),
                Face("Example", italic: true),
                Face("Example", weight: 700),
                Face("Example", mono: true),
            };
            string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var previous = GuiBackend.Current;
            var previousPaths = Font.Paths;
            try
            {
                var paths = new System.Collections.Generic.List<string>();
                for (int i = 0; i < (collection ? 1 : faces.Length); i++)
                {
                    // Neither filenames nor collection order identify the regular face.
                    string path = Path.Combine(directory, i + ".font");
                    File.WriteAllBytes(path, collection ? Collection(faces) : faces[i]);
                    paths.Add(path);
                }
                Font.Paths = paths.ToArray();
                NativeFonts.Rescan();
                GuiBackend.Current = new TextBackend
                {
                    Register = (_, data, _, _) =>
                    {
                        // Only the regular fixture is fixed-pitch, identifying the chosen face.
                        Assert.That(InstalledFontNames.Monospaced(data), Is.True);
                        return 100;
                    },
                };
                Assert.That(NativeFonts.Create("Example", 12).GameFontIndex, Is.EqualTo(100));
                Assert.That(NativeFonts.Names(monospaced: true), Is.EqualTo(new[] { "Example" }));
            }
            finally
            {
                GuiBackend.Current = previous;
                Font.Paths = previousPaths;
                NativeFonts.Rescan();
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void EveryCollectionFamilyIsSelectableAndTheBackendReceivesStandaloneFontData()
        {
            byte[] bytes = Collection(
                Face("Proportional"),
                Face("Collection Mono", mono: true),
                Face("Third")
            );
            byte[] original = (byte[])bytes.Clone();
            Assert.That(InstalledFontNames.Count(bytes), Is.EqualTo(3));
            Assert.That(InstalledFontNames.Read(bytes, 2), Is.EqualTo("Third"));
            Assert.That(InstalledFontNames.Read(bytes, 3), Is.Null);
            Assert.That(
                InstalledFontNames.Read(InstalledFontNames.Select(bytes, 0)),
                Is.EqualTo("Proportional")
            );
            string path = Path.GetTempFileName();
            var previous = GuiBackend.Current;
            var previousPaths = Font.Paths;
            try
            {
                File.WriteAllBytes(path, bytes);
                Font.Paths = new[] { path };
                NativeFonts.Rescan();
                Assert.That(
                    NativeFonts.Names(),
                    Is.EquivalentTo(new[] { "Proportional", "Collection Mono", "Third" })
                );
                Assert.That(
                    NativeFonts.Names(monospaced: true),
                    Is.EqualTo(new[] { "Collection Mono" })
                );
                GuiBackend.Current = new TextBackend
                {
                    Register = (name, data, size, mode) =>
                    {
                        Assert.That(InstalledFontNames.Read(data), Is.EqualTo("Collection Mono"));
                        Assert.That(InstalledFontNames.Monospaced(data), Is.True);
                        Assert.That(InstalledFontNames.Count(data), Is.EqualTo(1));
                        uint checksum = 0;
                        for (int at = 0; at < data.Length; at += 4)
                            checksum = unchecked(
                                checksum + BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at))
                            );
                        Assert.That(checksum, Is.EqualTo(0xb1b0afba), "standalone file checksum");
                        Assert.That(
                            BinaryPrimitives.ReadUInt32BigEndian(data),
                            Is.EqualTo(0x00010000)
                        );
                        return 101;
                    },
                };
                Assert.That(
                    NativeFonts.Create("Collection Mono", 12).GameFontIndex,
                    Is.EqualTo(101)
                );
                Assert.That(bytes, Is.EqualTo(original), "selection preserves source data");
                Assert.That(
                    File.ReadAllBytes(path),
                    Is.EqualTo(original),
                    "installed font is never rewritten"
                );
            }
            finally
            {
                File.Delete(path);
                GuiBackend.Current = previous;
                Font.Paths = previousPaths;
                NativeFonts.Rescan();
            }
        }

        [Test]
        public void TruncatedCollectionOffsetArraysAreRejected()
        {
            byte[] bytes = Collection(Face("One"), Face("Two"));
            Assert.That(InstalledFontNames.Count(bytes.AsSpan(0, 16).ToArray()), Is.Zero);
            U32(bytes, 8, uint.MaxValue);
            Assert.That(InstalledFontNames.Count(bytes), Is.Zero);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReadsUnicodeFamilyAndFixedPitchFromFontOrCollection(bool collection)
        {
            var data = Face("Mono 日本語", mono: true, collection: collection);
            Assert.That(InstalledFontNames.Read(data), Is.EqualTo("Mono 日本語"));
            Assert.That(InstalledFontNames.Monospaced(data), Is.True);
        }

        [Test]
        public void TruncatedFontsHaveNoFamilyOrFixedPitch()
        {
            var data = Face("Example");
            for (int size = 0; size < 62; size++)
            {
                byte[] truncated = data.AsSpan(0, size).ToArray();
                Assert.That(InstalledFontNames.Read(truncated), Is.Null);
                Assert.That(InstalledFontNames.Monospaced(truncated), Is.False);
                Assert.That(InstalledFontNames.StyleDistance(truncated), Is.EqualTo(int.MaxValue));
            }
        }

        [Test]
        public void MissingOrInvalidOs2StyleUsesHeadFlagsAndMalformedOffsetsAreRejected()
        {
            var data = Face("Example");
            int head = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(52)));
            int os2 = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(84)));
            U16(data, os2 + 4, 0); // Invalid weight cannot outrank valid regular metadata.
            Assert.That(InstalledFontNames.StyleDistance(data), Is.Zero);
            U16(data, head + 44, 1);
            Assert.That(InstalledFontNames.StyleDistance(data), Is.GreaterThan(0));
            U32(data, 76, 0); // No OS/2 table: head still describes the posture.
            U16(data, head + 44, 2);
            Assert.That(InstalledFontNames.StyleDistance(data), Is.GreaterThan(300));
            U32(data, 52, uint.MaxValue);
            Assert.That(InstalledFontNames.StyleDistance(data), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void RegistersRealBytesAtDistinctNativeSizesAndRecoversAfterMissingFile()
        {
            string path = Path.GetTempFileName();
            var previous = GuiBackend.Current;
            var previousPaths = Font.Paths;
            try
            {
                var bytes = Face("Example", mono: true);
                File.WriteAllBytes(path, bytes);
                Font.Paths = new[] { path };
                NativeFonts.Rescan();
                Assert.That(NativeFonts.Names(monospaced: true), Is.EqualTo(new[] { "Example" }));
                var registrations = new System.Collections.Generic.List<string>();
                GuiBackend.Current = new TextBackend
                {
                    Register = (name, data, size, mode) =>
                    {
                        Assert.That(data, Is.EqualTo(bytes));
                        Assert.That(size, Is.EqualTo(registrations.Count == 0 ? 12 : 20));
                        registrations.Add(name);
                        return 100 + registrations.Count;
                    },
                };
                Assert.That(
                    NativeFonts.Create(new[] { "Missing", "example" }, 12).GameFontIndex,
                    Is.EqualTo(101)
                );
                Assert.That(NativeFonts.Create("Example", 20).GameFontIndex, Is.EqualTo(102));
                Assert.That(registrations[0], Is.Not.EqualTo(registrations[1]));
                File.Delete(path);
                Assert.That(NativeFonts.Create("Example", 12), Is.Null);
                File.WriteAllBytes(path, bytes);
                Assert.That(NativeFonts.Create("Example", 20).GameFontIndex, Is.EqualTo(103));
            }
            finally
            {
                File.Delete(path);
                GuiBackend.Current = previous;
                Font.Paths = previousPaths;
                NativeFonts.Rescan();
            }
        }
    }
}
