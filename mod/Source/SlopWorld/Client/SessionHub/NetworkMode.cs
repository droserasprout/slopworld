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
                case "none": return NetworkMode.None;
                case "host": return NetworkMode.Host;
                default: return NetworkMode.Private;
            }
        }

        public static string Name(NetworkMode mode) => mode == NetworkMode.None ? "none" :
            mode == NetworkMode.Host ? "host" : "private";

        public static string Label(NetworkMode mode) => mode == NetworkMode.None
            ? "No network"
            : mode == NetworkMode.Host
                ? "Host network (full local access)"
                : "Private network (Internet, no host loopback)";

        public static string ShortLabel(NetworkMode mode) => mode == NetworkMode.None
            ? "no network"
            : mode == NetworkMode.Host ? "host network" : "private network";

        public static bool Allowed(NetworkMode requested, NetworkMode ceiling) =>
            Rank(requested) <= Rank(ceiling);

        static int Rank(NetworkMode mode) => mode == NetworkMode.None ? 0 :
            mode == NetworkMode.Private ? 1 : 2;
    }
}
