namespace SlopWorld
{
    public partial class SessionHub
    {
        void Handle(Wire.Event ev)
        {
            switch (ev.PayloadCase)
            {
                case Wire.Event.PayloadOneofCase.Capabilities:
                    Capabilities = DaemonCapabilities.FromWire(ev.Capabilities);
                    Radio.CapabilitiesChanged();
                    break;

                case Wire.Event.PayloadOneofCase.Sessions:
                    _sessions.ApplySessions(ev.Sessions);
                    break;

                case Wire.Event.PayloadOneofCase.Projects:
                    _catalog.ApplyProjects(ev.Projects);
                    break;

                case Wire.Event.PayloadOneofCase.Library:
                    _catalog.ApplyLibrary(ev.Library);
                    break;

                case Wire.Event.PayloadOneofCase.Jukebox:
                    Radio.SetStations(ev.Jukebox);
                    break;

                case Wire.Event.PayloadOneofCase.Usage:
                    Usage = UsageInfo.FromWire(ev.Usage, Usage);
                    break;

                case Wire.Event.PayloadOneofCase.Audio:
                    Radio.Report(ev.Audio.Playing,
                        ev.Audio.Error,
                        ev.Audio.Title, ev.Audio.Source, ev.Audio.Session);
                    break;

                case Wire.Event.PayloadOneofCase.Screen:
                    _sessions.ApplyScreen(ev.Screen);
                    break;
            }
        }
    }
}
