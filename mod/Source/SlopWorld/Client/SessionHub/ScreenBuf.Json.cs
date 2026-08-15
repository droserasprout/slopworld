using System.Linq;

namespace SlopWorld
{
    public partial class ScreenBuf
    {
        public void FromJson(JVal s)
        {
            Seq = s["seq"].AsInt();
            Cols = s["cols"].AsInt(80);
            Rows = s["rows"].AsInt(24);
            Cx = s["cx"].AsInt();
            Cy = s["cy"].AsInt();
            Off = s["off"].AsInt(0);
            CursorShape = s["cursor_shape"].AsInt(0);
            CursorBlink = s["cursor_blink"].AsBool(true);
            AppMouse = s["app_mouse"].AsBool(false);
            AppDrag = s["app_drag"].AsBool(false);
            AltScreen = s["alt_screen"].AsBool(false);
            Title = s["title"].AsString();
            ScrollRequestId = (ulong)s["request_id"].AsLong(0);
            Lines = s["lines"].Items.Select(l => l.AsString()).ToArray();
            Runs = null; // force a re-parse on next draw
        }
    }
}
