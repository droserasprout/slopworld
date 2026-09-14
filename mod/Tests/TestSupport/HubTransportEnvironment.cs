namespace SlopWorld
{
    // The production tracer is game-bound; transport tests only need its call shape.
    static class PerfTrace
    {
        public static long Start() => 0L;
        public static void End(string name, long started, int work, int backlog = 0) { }
        public static void Count(string name, int work = 1) { }
    }
}
