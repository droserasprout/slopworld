namespace SlopWorld
{
    // Runtime features announced before the rest of the WebSocket snapshot. Defaults describe
    // the native Linux daemon so an older daemon keeps the behavior it had before this event.
    public sealed class DaemonCapabilities
    {
        public bool Known;
        public string Runtime = "native";
        public bool AudioPlayback = true;
        public bool Clipboard = true;
        public bool DesktopOpen = true;
        public bool PerSessionLimits = true;
        public bool HostNetworkIsContainer;
        public bool HostTerminalsAreContainer;

        public static DaemonCapabilities FromJson(JVal j)
        {
            if (j == null || j.IsNull) return new DaemonCapabilities();
            return new DaemonCapabilities
            {
                Known = true,
                Runtime = j["runtime"].AsString("native"),
                AudioPlayback = j["audio_playback"].AsBool(true),
                Clipboard = j["clipboard"].AsBool(true),
                DesktopOpen = j["desktop_open"].AsBool(true),
                PerSessionLimits = j["per_session_limits"].AsBool(true),
                HostNetworkIsContainer = j["host_network_is_container"].AsBool(false),
                HostTerminalsAreContainer = j["host_terminals_are_container"].AsBool(false),
            };
        }
    }
}
