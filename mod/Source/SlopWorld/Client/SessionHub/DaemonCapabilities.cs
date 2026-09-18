namespace SlopWorld
{
    // Runtime features announced before the rest of the WebSocket snapshot. Defaults describe
    // the native Linux daemon so an older daemon keeps the behavior it had before this event.
    public sealed class DaemonCapabilities
    {
        public static DaemonCapabilities Current = new DaemonCapabilities();
        public bool Known;
        public string Runtime = "native";
        public bool AudioPlayback = true;
        public bool Clipboard = true;
        public bool DesktopOpen = true;
        public bool PerSessionLimits = true;
        public bool HostNetworkIsContainer;
        public bool HostTerminalsAreContainer;
        public string TerminalName => HostTerminalsAreContainer
            ? "container terminal" : "host terminal";
        public string TerminalNames => HostTerminalsAreContainer
            ? "container terminals" : "host terminals";

        public static DaemonCapabilities FromWire(Wire.Capabilities j)
        {
            if (j == null) return Current = new DaemonCapabilities();
            return Current = new DaemonCapabilities
            {
                Known = true,
                Runtime = j.Runtime,
                AudioPlayback = j.AudioPlayback,
                Clipboard = j.Clipboard,
                DesktopOpen = j.DesktopOpen,
                PerSessionLimits = j.PerSessionLimits,
                HostNetworkIsContainer = j.HostNetworkIsContainer,
                HostTerminalsAreContainer = j.HostTerminalsAreContainer,
                Terminal = TerminalLimits.FromWire(j.Terminal),
            };
        }

        public TerminalLimits Terminal = new TerminalLimits();
    }
}
