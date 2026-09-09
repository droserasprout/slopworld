namespace SlopWorld
{
    public sealed class DaemonHealth
    {
        public bool Known;
        public string Version = "?";
        public string Hostname = "?";

        public static DaemonHealth FromJson(JVal j)
        {
            if (j == null || j.IsNull) return new DaemonHealth();
            return new DaemonHealth
            {
                Known = j["ok"].AsBool(false),
                Version = j["version"].AsString("?"),
                Hostname = j["hostname"].AsString("?"),
            };
        }
    }
}
