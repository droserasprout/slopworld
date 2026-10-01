using System;
namespace SlopWorld
{
    internal sealed class ReceivedEvent
    {
        Wire.Event _value;
        byte[] _payload;
        public Wire.Event Value
        {
            get
            {
                if (_payload != null)
                {
                    _value = Wire.Event.Parser.ParseFrom(_payload);
                    _payload = null;
                }
                return _value;
            }
        }
        public readonly long ReceivedAt = TerminalLatency.Enabled ? TerminalLatency.Now() : 0;
        public readonly Exception Error;
        public readonly string LiveName;
        public ReceivedEvent(byte[] payload, bool deferLive = false)
        {
            try
            {
                // Only the fully validated, canonical screen subset takes the lazy path.
                // Unknown fields, duplicate fields and other envelopes use the generated parser.
                if (deferLive && ValidatedLiveScreen.TryName(payload, out var name))
                {
                    LiveName = name;
                    _payload = payload;
                    return;
                }
                _value = Wire.Event.Parser.ParseFrom(payload);
                if (Value.PayloadCase == Wire.Event.PayloadOneofCase.None)
                    throw new FormatException("Missing Protobuf event payload");
                var screen = Value.Screen;
                if (screen != null && screen.Off == 0 && screen.RequestId == 0 && screen.Name.Length != 0)
                    LiveName = screen.Name;
            }
            catch (Exception error) { _value = null; Error = error; }
        }
    }

    // A conservative validator for the flat ScreenView schema. No row strings are allocated.
    // Use the generated parser for any encoding outside this subset. It preserves wire
    // compatibility and its malformed-message behavior. Keep schema field numbers here explicit.
    internal static class ValidatedLiveScreen
    {
        // Event.screen and the canonical ScreenView subset in shared/slopworld.proto.
        const ulong EventScreenTag = (5 << 3) | 2;
        const int ScreenNameField = 1, ScreenSequenceField = 2, ScreenOffsetField = 7;
        const int ScreenTitleField = 14, ScreenRequestIdField = 15, ScreenLinesField = 16;
        const ulong LastScreenTag = (ScreenLinesField << 3) | 2;

        static readonly System.Text.UTF8Encoding Utf8 = new System.Text.UTF8Encoding(false, true);

        public static bool TryName(byte[] bytes, out string name)
        {
            name = null;
            int at = 0;
            if (!Varint(bytes, ref at, out ulong tag) || tag != EventScreenTag ||
                !Varint(bytes, ref at, out ulong size) || size != (ulong)(bytes.Length - at)) return false;
            int previous = 0, nameAt = 0, nameSize = 0;
            while (at < bytes.Length)
            {
                if (!Varint(bytes, ref at, out tag)) return false;
                if (tag > LastScreenTag) return false;
                int field = (int)(tag >> 3);
                if (field < ScreenNameField || field > ScreenLinesField || field < previous ||
                    (field == previous && field != ScreenLinesField)) return false;
                previous = field;
                if (field == ScreenNameField || field == ScreenTitleField || field == ScreenLinesField)
                {
                    if ((tag & 7) != 2 || !Varint(bytes, ref at, out size) ||
                        size > (ulong)(bytes.Length - at)) return false;
                    try { Utf8.GetCharCount(bytes, at, (int)size); }
                    catch (System.Text.DecoderFallbackException) { return false; }
                    if (field == ScreenNameField) { nameAt = at; nameSize = (int)size; }
                    at += (int)size;
                }
                else
                {
                    if ((tag & 7) != 0 || !Varint(bytes, ref at, out ulong value)) return false;
                    if ((field == ScreenOffsetField || field == ScreenRequestIdField) && value != 0) return false;
                    if (field != ScreenSequenceField && field != ScreenRequestIdField && value > uint.MaxValue) return false;
                }
            }
            if (nameSize == 0) return false;
            name = Utf8.GetString(bytes, nameAt, nameSize);
            return true;
        }

        static bool Varint(byte[] bytes, ref int at, out ulong value)
        {
            value = 0;
            for (int shift = 0; shift < 64 && at < bytes.Length; shift += 7)
            {
                byte b = bytes[at++];
                if (shift == 63 && b > 1) return false;
                value |= (ulong)(b & 127) << shift;
                if (b < 128) return true;
            }
            return false;
        }
    }
}
