using System;
using System.Buffers.Binary;

namespace SlopWorld
{
    // SFNT/TTC layout and bounds-checked views. Offsets in collection table records
    // are absolute file offsets, not relative to the selected face's directory.
    readonly ref struct OpenTypeFace
    {
        const int HeaderSize = 12;
        const int TableRecordSize = 16;
        readonly ReadOnlySpan<byte> _file;
        readonly int _header;

        internal OpenTypeFace(ReadOnlySpan<byte> file, int face)
        {
            _file = file;
            IsCollection = OpenTypeBinary.ReadUInt32(file, 0) == OpenTypeTags.Collection;
            int count = FaceCount(file);
            if (face < 0 || face >= count)
                throw new ArgumentOutOfRangeException(nameof(face));
            _header = IsCollection
                ? checked((int)OpenTypeBinary.ReadUInt32(file, checked(HeaderSize + face * 4)))
                : 0;
        }

        internal bool IsCollection { get; }

        // SFNT offset table: scaler type (4 bytes), then numTables (2 bytes).
        internal int TableCount => OpenTypeBinary.ReadUInt16(_file, _header + 4);
        internal ReadOnlySpan<byte> Directory =>
            _file.Slice(_header, HeaderSize + TableCount * TableRecordSize);

        internal static int RecordOffset(int index) => HeaderSize + index * TableRecordSize;

        internal OpenTypeTable Table(int index) =>
            new OpenTypeTable(_file, _file.Slice(_header + RecordOffset(index), TableRecordSize));

        internal bool TryGetTable(uint tag, out OpenTypeTable table)
        {
            for (int index = 0; index < TableCount; index++)
            {
                table = Table(index);
                if (table.Tag == tag)
                    return true;
            }
            table = default;
            return false;
        }

        internal static int FaceCount(ReadOnlySpan<byte> file)
        {
            if (OpenTypeBinary.ReadUInt32(file, 0) != OpenTypeTags.Collection)
                return 1;
            // TTC header: signature, version, face count, then uint32 face offsets.
            int count = checked((int)OpenTypeBinary.ReadUInt32(file, 8));
            _ = file.Slice(HeaderSize, checked(count * 4));
            return count;
        }
    }

    readonly ref struct OpenTypeTable
    {
        internal const int OffsetField = 8;
        internal const int ChecksumAdjustmentField = 8;
        readonly ReadOnlySpan<byte> _file;
        readonly ReadOnlySpan<byte> _record;

        internal OpenTypeTable(ReadOnlySpan<byte> file, ReadOnlySpan<byte> record)
        {
            _file = file;
            _record = record;
        }

        // Directory record: tag, checksum, absolute offset, byte length (all uint32).
        internal uint Tag => OpenTypeBinary.ReadUInt32(_record, 0);
        internal int Length => checked((int)OpenTypeBinary.ReadUInt32(_record, 12));
        internal ReadOnlySpan<byte> Data =>
            _file.Slice(checked((int)OpenTypeBinary.ReadUInt32(_record, OffsetField)), Length);

        // Fields are relative to their named table, never to the file or directory.
        internal bool IsFixedPitch => OpenTypeBinary.ReadUInt32(Data, 12) != 0; // post.isFixedPitch
        internal bool HasSelection => Data.Length >= 64;
        internal int WeightClass => OpenTypeBinary.ReadUInt16(Data, 4); // OS/2.usWeightClass
        internal int WidthClass => OpenTypeBinary.ReadUInt16(Data, 6); // OS/2.usWidthClass

        // OS/2.fsSelection: italic is bit 0; oblique is bit 9.
        internal bool IsItalicOrOblique => (OpenTypeBinary.ReadUInt16(Data, 62) & 0x201) != 0;
        internal bool HasMacStyle => Data.Length >= 46;
        internal bool IsBold => (OpenTypeBinary.ReadUInt16(Data, 44) & 1) != 0; // head.macStyle
        internal bool IsItalic => (OpenTypeBinary.ReadUInt16(Data, 44) & 2) != 0;
    }

    readonly ref struct OpenTypeNames
    {
        readonly ReadOnlySpan<byte> _data;

        internal OpenTypeNames(ReadOnlySpan<byte> data) => _data = data;

        // name header: format, record count, string storage offset (all uint16).
        internal int Count => OpenTypeBinary.ReadUInt16(_data, 2);

        internal OpenTypeNameRecord Record(int index) =>
            new OpenTypeNameRecord(_data.Slice(6 + index * 12, 12));

        internal ReadOnlySpan<byte> Text(OpenTypeNameRecord record) =>
            _data.Slice(OpenTypeBinary.ReadUInt16(_data, 4) + record.Offset, record.Length);
    }

    readonly ref struct OpenTypeNameRecord
    {
        readonly ReadOnlySpan<byte> _data;

        internal OpenTypeNameRecord(ReadOnlySpan<byte> data) => _data = data;

        // name record: platform, encoding, language, name ID, length, offset (uint16).
        int Platform => OpenTypeBinary.ReadUInt16(_data, 0);
        int NameId => OpenTypeBinary.ReadUInt16(_data, 6);
        internal bool IsUnicode => Platform == 0 || Platform == 3;
        internal bool IsFamily => NameId == 1 || IsTypographicFamily;
        internal bool IsTypographicFamily => NameId == 16;
        internal bool IsEnglish => OpenTypeBinary.ReadUInt16(_data, 4) == 0x409;
        internal int Length => OpenTypeBinary.ReadUInt16(_data, 8);
        internal int Offset => OpenTypeBinary.ReadUInt16(_data, 10);
    }

    static class OpenTypeTags
    {
        internal const uint Collection = 0x74746366; // ttcf
        internal const uint Name = 0x6e616d65; // name
        internal const uint Post = 0x706f7374; // post
        internal const uint Head = 0x68656164; // head
        internal const uint Os2 = 0x4f532f32; // OS/2
    }

    static class OpenTypeBinary
    {
        internal static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

        internal static uint ReadUInt32(ReadOnlySpan<byte> data, int offset) =>
            BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }
}
