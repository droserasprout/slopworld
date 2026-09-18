namespace SlopWorld
{
    public sealed class DaemonHealth
    {
        public bool Known;
        public string Version = "?";
        public string Hostname = "?";

        public static DaemonHealth FromWire(Wire.Health j)
        {
            if (j == null) return new DaemonHealth();
            return new DaemonHealth
            {
                Known = j.Ok,
                Version = j.Version,
                Hostname = j.Hostname,
            };
        }
    }
}
