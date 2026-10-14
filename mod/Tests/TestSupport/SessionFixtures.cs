namespace SlopWorld.Tests
{
    static class SessionFixtures
    {
        public static Wire.SessionView View(string name, bool alive = false, string state = "") =>
            new Wire.SessionView
            {
                Name = name,
                Launch = new Wire.SessionLaunchView(),
                Worker = new Wire.SessionWorkerView(),
                Reader = new Wire.SessionReaderView(),
                Runtime = new Wire.SessionRuntimeView { Alive = alive, State = state },
            };

        public static Wire.SessionsReply Snapshot(params Wire.SessionView[] sessions) =>
            new Wire.SessionsReply { Sessions = { sessions } };
    }
}
