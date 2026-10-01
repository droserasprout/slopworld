using System;

namespace SlopWorld
{
    public enum NetworkMode { None, Private, Host }
    public static class NetworkModeText
    {
        public static NetworkMode Parse(string text)
        {
            switch ((text ?? "").Trim().ToLowerInvariant())
            {
                case WireProtocol.NetworkMode.None: return NetworkMode.None;
                case WireProtocol.NetworkMode.Host: return NetworkMode.Host;
                default: return NetworkMode.Private;
            }
        }

        public static string Name(NetworkMode mode) => mode == NetworkMode.None ? WireProtocol.NetworkMode.None :
            mode == NetworkMode.Host ? WireProtocol.NetworkMode.Host : WireProtocol.NetworkMode.Private;

        public static string Label(NetworkMode mode) => mode == NetworkMode.None
            ? "No network"
            : mode == NetworkMode.Host
                ? "Host network (daemon’s local network)"
                : "Private network (Internet, no host loopback)";

        public static string ShortLabel(NetworkMode mode) => mode == NetworkMode.None
            ? "no network"
            : mode == NetworkMode.Host ? "host network" : "private network";

    }
}
