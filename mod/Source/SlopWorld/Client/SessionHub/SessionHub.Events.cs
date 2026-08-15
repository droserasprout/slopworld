using System.Linq;

namespace SlopWorld
{
    public partial class SessionHub
    {
        void Handle(JVal ev)
        {
            switch (ev["t"].AsString())
            {
                case "sessions":
                    Sessions = ev["sessions"].Items.Select(SessionInfo.FromJson).ToList();
                    ForgetScreens();
                    break;

                case "projects":
                    Projects = ev["projects"].Items.Select(ProjectInfo.FromJson).ToList();
                    break;

                case "shortcuts":
                    Shortcuts = ev["shortcuts"].Items.Select(ShortcutInfo.FromJson).ToList();
                    break;

                case "jukebox":
                    Radio.SetStations(ev["jukebox"]);
                    break;

                case "usage":
                    Usage = UsageInfo.FromJson(ev["usage"], Usage);
                    break;

                case "audio":
                    Radio.Report(ev["audio"]["playing"].AsBool(false),
                        ev["audio"]["error"].AsString(null),
                        ev["audio"]["title"].AsString(null));
                    break;

                case "screen":
                    var s = ev["screen"];
                    string name = s["name"].AsString();
                    int off = s["off"].AsInt(0);
                    // Scrolled frames answer one wheel request; kept apart from the live view.
                    var store = off > 0 ? _scrolls : _screens;
                    if (!store.TryGetValue(name, out var buf))
                        store[name] = buf = new ScreenBuf();
                    buf.FromJson(s);
                    break;
            }
        }
    }
}
