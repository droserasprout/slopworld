namespace SlopWorld
{
    // Optional launch intent; SessionStore owns scope resolution and refresh ordering.
    public sealed class SessionRunOptions
    {
        public bool Shell = true;
        public string Text = "";
        public bool Host, Temp, Hold;
        public string Path = "";
        public string Like = "";
        public string AgentTemplate = "";
        public string Worktree = "";
        public string Intent = "";
        public ReaderLaunchOptions Reader = new ReaderLaunchOptions();

        internal Wire.RunReq ToWire() => new Wire.RunReq
        {
            Kind = Shell ? "shell" : "prompt",
            Text = Text ?? "",
            Host = Host,
            Temp = Temp,
            Hold = Hold,
            Path = Path ?? "",
            Like = Like ?? "",
            AgentTemplate = AgentTemplate ?? "",
            Intent = Intent ?? "",
            Reader = (Reader ?? new ReaderLaunchOptions()).ToWire(),
        };
    }

    public sealed class ReaderLaunchOptions
    {
        public string Path = "";
        public string Key = "";
        public string Scope = "";
        public long Line;
        public bool Pinned;

        internal Wire.RunReaderReq ToWire() => new Wire.RunReaderReq
        {
            Path = Path ?? "",
            Key = Key ?? "",
            Scope = Scope ?? "",
            Line = checked((uint)System.Math.Max(0, Line)),
            Pinned = Pinned,
        };
    }
}
