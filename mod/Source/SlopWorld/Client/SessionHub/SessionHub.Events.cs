namespace SlopWorld
{
    public partial class SessionHub
    {
        // The coordinator's one job on the socket: route each pushed event to the store that
        // owns it. The stores apply their own updates; Usage and the jukebox are small enough
        // to land here.
        void Handle(JVal ev)
        {
            switch (ev["t"].AsString())
            {
                case "capabilities":
                    Capabilities = DaemonCapabilities.FromJson(ev["capabilities"]);
                    break;

                case "sessions":
                    _sessions.ApplySessions(ev);
                    break;

                case "projects":
                    _catalog.ApplyProjects(ev);
                    break;

                case "library":
                    _catalog.ApplyLibrary(ev);
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
                    _sessions.ApplyScreen(ev["screen"]);
                    break;
            }
        }
    }
}
