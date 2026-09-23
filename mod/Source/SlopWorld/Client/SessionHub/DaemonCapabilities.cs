using System;

namespace SlopWorld
{
    // Runtime features are announced before the rest of the WebSocket snapshot.
    // Defaults describe the native Linux daemon while the announcement is pending.
    public sealed class DaemonCapabilities
    {
        public static DaemonCapabilities Current = new DaemonCapabilities();
        public bool Known;
        public string Runtime = "native";
        public bool AudioPlayback = true;
        public bool Ncspot = true;
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
            if (j == null) throw new ArgumentNullException(nameof(j));
            return Current = new DaemonCapabilities
            {
                Known = true,
                Runtime = j.Runtime,
                AudioPlayback = j.AudioPlayback,
                Ncspot = j.Ncspot,
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
