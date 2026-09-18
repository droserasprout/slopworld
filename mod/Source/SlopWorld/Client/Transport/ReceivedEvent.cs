using System;
namespace SlopWorld
{
    internal sealed class ReceivedEvent
    {
        public readonly Wire.Event Value;
        public readonly Exception Error;
        public readonly string LiveName;
        public ReceivedEvent(byte[] payload)
        {
            try
            {
                Value = Wire.Event.Parser.ParseFrom(payload);
                if (Value.PayloadCase == Wire.Event.PayloadOneofCase.None)
                    throw new FormatException("Missing Protobuf event payload");
                var screen = Value.Screen;
                if (screen != null && screen.Off == 0 && screen.RequestId == 0 && screen.Name.Length != 0)
                    LiveName = screen.Name;
            }
            catch (Exception error) { Value = null; Error = error; }
        }
    }
}
