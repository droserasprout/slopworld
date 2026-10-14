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

        static byte[] Face(string family, bool mono = false, bool collection = false)
        {
            byte[] text = Encoding.BigEndianUnicode.GetBytes(family);
            int header = collection ? 16 : 0;
            int names = header + 44;
            int post = names + 18 + text.Length;
            var data = new byte[post + 16];
            if (collection)
            {
                U32(data, 0, 0x74746366);
                U32(data, 8, 1);
                U32(data, 12, (uint)header);
            }
            U32(data, header, 0x00010000);
            U16(data, header + 4, 2);
            U32(data, header + 12, 0x6e616d65);
            U32(data, header + 20, (uint)names);
            U32(data, header + 24, (uint)(18 + text.Length));
            U32(data, header + 28, 0x706f7374);
            U32(data, header + 36, (uint)post);
            U32(data, header + 40, 16);
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
            }
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
