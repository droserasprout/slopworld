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
                case WireContract.Events.Capabilities:
                    Capabilities = DaemonCapabilities.FromJson(ev["capabilities"]);
                    break;

                case WireContract.Events.Sessions:
                    _sessions.ApplySessions(ev);
                    break;

                case WireContract.Events.Projects:
                    _catalog.ApplyProjects(ev);
                    break;

                case WireContract.Events.Library:
                    _catalog.ApplyLibrary(ev);
                    break;

                case WireContract.Events.Jukebox:
                    Radio.SetStations(ev["jukebox"]);
                    break;

                case WireContract.Events.Usage:
                    Usage = UsageInfo.FromJson(ev["usage"], Usage);
                    break;

                case WireContract.Events.Audio:
                    Radio.Report(ev["audio"]["playing"].AsBool(false),
                        ev["audio"]["error"].AsString(null),
                        ev["audio"]["title"].AsString(null));
                    break;

                case WireContract.Events.Screen:
                    _sessions.ApplyScreen(ev["screen"]);
                    break;
            }
        }
    }
}
